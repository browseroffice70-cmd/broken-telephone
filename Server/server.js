'use strict';

// Broken Telephone server: static host for the WebGL build + WebSocket game server on one port.
//   node server.js                         -> http://localhost:8080  (ws at /ws)
//   WEB_ROOT=../WebBuild PORT=8080 node server.js
// Mic + speech recognition need a secure context: use localhost, or put this behind HTTPS (nginx/Caddy/Cloudflare).

const http = require('http');
const fs = require('fs');
const path = require('path');
const { WebSocketServer } = require('ws');
const C = require('./src/config');
const { Room } = require('./src/room');

const WEB_ROOT = path.resolve(process.env.WEB_ROOT || path.join(__dirname, '..', 'WebBuild'));

// ---------- static hosting (handles Unity's pre-compressed .br / .gz files) ----------

const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.wasm': 'application/wasm',
  '.data': 'application/octet-stream', '.json': 'application/json', '.css': 'text/css',
  '.woff2': 'font/woff2', '.png': 'image/png', '.ico': 'image/x-icon', '.svg': 'image/svg+xml', '.symbols.json': 'application/json',
};

function serveStatic(req, res) {
  let urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
  if (urlPath.endsWith('/')) urlPath += 'index.html';
  const file = path.resolve(WEB_ROOT, '.' + urlPath);
  if (!file.startsWith(WEB_ROOT + path.sep)) { res.writeHead(403); return res.end(); }

  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      return res.end(fs.existsSync(WEB_ROOT) ? 'Not found' : `No WebGL build at ${WEB_ROOT}. In Unity use Broken Telephone > Build WebGL.`);
    }
    let name = file;
    const headers = { 'Content-Length': st.size };
    if (name.endsWith('.br')) { headers['Content-Encoding'] = 'br'; name = name.slice(0, -3); }
    else if (name.endsWith('.gz')) { headers['Content-Encoding'] = 'gzip'; name = name.slice(0, -3); }
    headers['Content-Type'] = TYPES[path.extname(name)] || 'application/octet-stream';
    headers['Cache-Control'] = name.endsWith('.html') ? 'no-cache' : 'public, max-age=600';
    res.writeHead(200, headers);
    fs.createReadStream(file).pipe(res);
  });
}

const server = http.createServer((req, res) => {
  if (req.url === '/health') { res.writeHead(200); return res.end('ok'); }
  serveStatic(req, res);
});

// ---------- rooms ----------

const rooms = new Map();
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ'; // no I/O to avoid confusion

function newCode() {
  for (;;) {
    let c = '';
    for (let i = 0; i < 4; i++) c += CODE_CHARS[(Math.random() * CODE_CHARS.length) | 0];
    if (!rooms.has(c)) return c;
  }
}

function createRoom(lang) {
  const room = new Room(newCode(), r => rooms.delete(r.code), lang);
  rooms.set(room.code, room);
  return room;
}

const cleanName = n => String(n || '').replace(/[\u0000-\u001f<>]/g, '').trim().slice(0, 18) || 'Player';
const cleanId = id => (/^[\w-]{8,64}$/.test(String(id)) ? String(id) : null);

// ---------- websocket ----------

// perMessageDeflate off: audio is already Opus-compressed and JSON frames are tiny, so deflate only costs CPU.
const wss = new WebSocketServer({ server, path: '/ws', maxPayload: C.MAX_AUDIO_BYTES + 1024, perMessageDeflate: false });

wss.on('connection', ws => {
  const conn = {
    ws, deviceId: null, name: 'Player', room: null, alive: true, budget: 40,
    sendRaw: s => { if (ws.readyState === 1) ws.send(s); },
    sendJson: o => { if (ws.readyState === 1) ws.send(JSON.stringify(o)); },
    sendBinary: b => { if (ws.readyState === 1) ws.send(b, { binary: true }); },
  };
  ws._btConn = conn;
  ws.on('pong', () => { conn.alive = true; });

  ws.on('message', (data, isBinary) => {
    if (--conn.budget < 0) return; // simple flood guard, refilled every second
    if (isBinary) { if (conn.room) conn.room.onAudio(conn, data); return; }
    let msg;
    try { msg = JSON.parse(data); } catch { return; }
    if (!msg || typeof msg.t !== 'string') return;
    handle(conn, msg);
  });

  ws.on('close', () => { if (conn.room) conn.room.leave(conn, false); });
});

function handle(conn, msg) {
  if (msg.t === 'hello') {
    conn.deviceId = cleanId(msg.deviceId);
    conn.name = cleanName(msg.name);
    if (!conn.deviceId) return conn.sendJson({ t: 'error', msg: 'Bad device id' });
    return conn.sendJson({ t: 'welcome', deviceId: conn.deviceId, name: conn.name });
  }
  if (!conn.deviceId) return conn.sendJson({ t: 'error', msg: 'Say hello first' });

  switch (msg.t) {
    case 'create': {
      if (conn.room) conn.room.leave(conn, true);
      createRoom(String(msg.lang || 'en')).join(conn);
      break;
    }
    case 'join': {
      const room = rooms.get(String(msg.code || '').toUpperCase().trim());
      if (!room) return conn.sendJson({ t: 'error', msg: 'Room not found' });
      if (conn.room && conn.room !== room) conn.room.leave(conn, true);
      const err = room.join(conn);
      if (err) conn.sendJson({ t: 'error', msg: err });
      break;
    }
    case 'rename':
      conn.name = cleanName(msg.name);
      if (conn.room) conn.room.rename(conn, conn.name);
      conn.sendJson({ t: 'welcome', deviceId: conn.deviceId, name: conn.name });
      break;
    case 'leave': if (conn.room) conn.room.leave(conn, true); break;
    case 'start': if (conn.room) conn.room.start(conn); break;
    case 'lobby': if (conn.room) conn.room.backToLobby(conn); break;
    case 'setBots': if (conn.room) conn.room.setBots(conn, !!msg.on); break;
    case 'setLang': if (conn.room) conn.room.setLang(conn, String(msg.lang || '')); break;
    case 'played': if (conn.room) conn.room.onPlayed(conn); break;
    case 'begin': if (conn.room) conn.room.onBegin(conn); break;
    case 'turnDone': if (conn.room) conn.room.onTurnDone(conn, msg); break;
    case 'getAudio': if (conn.room) conn.room.onGetAudio(conn, msg); break;
  }
}

// Heartbeat (drops dead sockets so a vanished player is detected) + flood-budget refill.
const conns = new Set();
wss.on('connection', ws => {
  const c = ws._btConn;
  conns.add(c);
  ws.on('close', () => conns.delete(c));
});
setInterval(() => { for (const c of conns) c.budget = 40; }, 1000).unref();
setInterval(() => {
  for (const c of conns) {
    if (!c.alive) { c.ws.terminate(); continue; }
    c.alive = false;
    c.ws.ping();
  }
}, 20000).unref();

if (require.main === module) {
  server.listen(C.PORT, () => {
    console.log(`Broken Telephone server on http://localhost:${C.PORT}  (ws: /ws, web root: ${WEB_ROOT})`);
  });
}

module.exports = { server, rooms };
