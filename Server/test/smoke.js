'use strict';
// End-to-end smoke test: 2 humans + 8 bots play a full chain, then a silent player triggers a restart.
Object.assign(process.env, {
  PORT: '0', RECORD_MS: '200', UPLOAD_GRACE_MS: '1500', NOTICE_MS: '200',
  BOT_BASE_MS: '20', BOT_PER_WORD_MS: '1', READ_MS: '3000', LISTEN_MS: '3000',
});
const assert = require('assert');
const WebSocket = require('ws');
const { server } = require('../server');

function client(deviceId, name, port) {
  const ws = new WebSocket(`ws://localhost:${port}/ws`);
  const c = { ws, deviceId, msgs: [], audioFrames: 0, silent: false, handlers: [] };
  c.send = o => ws.send(JSON.stringify(o));
  c.wait = (pred, ms = 20000) => new Promise((res, rej) => {
    const found = c.msgs.find(pred);
    if (found) return res(found);
    const to = setTimeout(() => rej(new Error(`${name}: timeout waiting`)), ms);
    c.handlers.push(m => { if (pred(m)) { clearTimeout(to); res(m); return true; } return false; });
  });
  ws.on('message', (data, bin) => {
    if (bin) { c.audioFrames++; assert.strictEqual(data[0], 1); return; }
    const m = JSON.parse(data);
    c.msgs.push(m);
    c.handlers = c.handlers.filter(h => !h(m));
    // Auto-play: act whenever it's our turn.
    if (m.t === 'turn' && m.playerId === deviceId) {
      if (m.phase === 'read') assert.ok(m.sentence.length > 0, 'reader must get the sentence');
      if (m.phase === 'read' || m.phase === 'listen') c.send({ t: 'begin' });
      if (m.phase === 'record') {
        const mime = Buffer.from('audio/webm', 'ascii');
        ws.send(Buffer.concat([Buffer.from([mime.length]), mime, Buffer.alloc(800, 7)]));
        c.send(c.silent ? { t: 'turnDone', transcript: '', voiced: false } : { t: 'turnDone', transcript: 'the quick brown fox jumps', voiced: true });
      }
    }
    if (m.t === 'turn' && m.playerId !== deviceId) assert.strictEqual(m.sentence, '', 'sentence leaked to non-reader');
  });
  return new Promise(res => ws.on('open', () => { c.send({ t: 'hello', deviceId, name }); res(c); }));
}

server.listen(0, async () => {
  const port = server.address().port;
  try {
    const a = await client('device-aaaaaaaa', 'Alice', port);
    const b = await client('device-bbbbbbbb', 'Bob', port);
    a.send({ t: 'create', lang: 'kn' });
    const room = await a.wait(m => m.t === 'room');
    assert.strictEqual(room.lang, 'kn', 'room language from create');
    b.send({ t: 'join', code: room.code });
    await b.wait(m => m.t === 'room' && m.players.length === 2);
    // Bots are off by default: 2 humans < 10 must be refused.
    a.send({ t: 'start' });
    const refused = await a.wait(m => m.t === 'error');
    assert.strictEqual(refused.msg, 'Not enough players');
    b.send({ t: 'setBots', on: true });   // non-host: ignored
    a.send({ t: 'setBots', on: true });   // host: allowed
    await a.wait(m => m.t === 'room' && m.bots === true);
    console.log('Bots toggle OK (off by default, host-only)');
    a.send({ t: 'start' });

    const res = await a.wait(m => m.t === 'results');
    assert.strictEqual(res.entries.length, 4, 'lineup should be filled to MIN_PLAYERS (4)');
    assert.strictEqual(res.entries.filter(e => e.bot).length, 2);
    assert.ok(res.ranking.filter(r => !r.bot).every(r => r.rank > 0));
    assert.ok(res.entries.every(e => Number.isInteger(e.score) && typeof e.hasAudio === 'boolean'), 'every entry has a score + hasAudio');
    const badBots = res.entries.filter(e => e.bot && /[ಀ-೿]/.test(e.heard) && !/[ಀ-೿]/.test(e.said));
    if (badBots.length) console.log('DEBUG bad bot entries:', JSON.stringify(badBots, null, 1));
    assert.ok(/[ಀ-೿]/.test(res.original), 'Kannada room must get a Kannada headline');
    assert.ok(res.entries.filter(e => e.bot && /[ಀ-೿]/.test(e.heard)).every(e => /[ಀ-೿]/.test(e.said)), 'bots mishear in Kannada');
    console.log('Full chain OK. Original:', JSON.stringify(res.original));
    for (const e of res.entries) console.log(`  ${e.index + 1}. ${e.name.padEnd(14)} ${String(e.score).padStart(3)}  ${e.said}`);
    console.log('  overall fidelity:', res.overall);

    // Audio on demand for the results screen.
    const humanEntry = res.entries.find(e => !e.bot);
    const before = a.audioFrames;
    a.ws.removeAllListeners('message');
    const gotBin = new Promise(r => a.ws.once('message', (d, bin) => r(bin && d[0] === 2)));
    a.send({ t: 'getAudio', index: humanEntry.index });
    assert.ok(await gotBin, 'result audio frame');
    void before;
    console.log('Result audio fetch OK');

    // Silent player -> notice + restart.
    b.silent = true;
    b.msgs.length = 0;
    a.ws.close();
    const a2 = await client('device-aaaaaaaa', 'Alice', port);
    a2.send({ t: 'join', code: room.code });
    await a2.wait(m => m.t === 'room');
    const r2 = await a2.wait(m => m.t === 'room');
    assert.strictEqual(r2.hostId, 'device-bbbbbbbb', 'host migrates to Bob when Alice drops');
    b.send({ t: 'lobby' });
    await a2.wait(m => m.t === 'room' && m.phase === 'lobby');
    b.send({ t: 'start' });
    const notice = await b.wait(m => m.t === 'notice');
    console.log('Silent player OK ->', notice.kind, '|', notice.msg);
    await b.wait(m => m.t === 'turn' && m.phase !== 'paused' && m.round >= 2);
    console.log('Restart OK');
    console.log('\nALL SMOKE TESTS PASSED');
    process.exit(0);
  } catch (e) {
    console.error('SMOKE TEST FAILED:', e);
    process.exit(1);
  }
});
