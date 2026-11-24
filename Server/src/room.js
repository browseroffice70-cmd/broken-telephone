'use strict';

const C = require('./config');
const { similarity } = require('./scoring');
const { getSentence, LANGS } = require('./news');

// Binary frame kinds (server -> client): [kind u8][index u16 BE][mimeLen u8][mime ascii][audio bytes]
const BIN_TURN_AUDIO = 1;
const BIN_RESULT_AUDIO = 2;

function shuffle(a) {
  for (let i = a.length - 1; i > 0; i--) {
    const j = (Math.random() * (i + 1)) | 0;
    const t = a[i]; a[i] = a[j]; a[j] = t;
  }
  return a;
}

function audioFrame(kind, index, audio) {
  const mime = Buffer.from(audio.mime, 'ascii');
  const head = Buffer.allocUnsafe(4);
  head[0] = kind;
  head.writeUInt16BE(index, 1);
  head[3] = mime.length;
  return Buffer.concat([head, mime, audio.data]);
}

/**
 * One game room. Fully server-authoritative: clients only send intents (begin, turnDone, ...),
 * the room owns turn order, timers, sentence secrecy, scoring and restarts.
 *
 * phase:     'lobby' | 'game' | 'results'
 * turnPhase: 'loading' | 'read' | 'listen' | 'record' | 'paused'
 */
class Room {
  constructor(code, onEmpty, lang) {
    this.code = code;
    this.lang = LANGS.includes(lang) ? lang : 'en'; // headline region + speech language for the whole room
    this.onEmpty = onEmpty;
    this.humans = new Map();   // id -> { id, name, conn, online }
    this.hostId = null;
    this.phase = 'lobby';
    this.lineup = [];          // player ids in turn order
    this.chain = [];           // { playerId, name, heard, said, audio }
    this.index = 0;
    this.sentence = '';
    this.round = 0;
    this.turnPhase = 'loading';
    this.turnEndsAt = 0;
    this.timer = null;
    this.epoch = 0;            // invalidates stale async callbacks / timers
    this.pendingAudio = null;
    this.results = null;
    this.listenPlayed = false; // play-once is enforced server-side: a refresh can't re-fetch the clip
  }

  // ---------- membership ----------

  get(id) { return this.humans.get(id); }

  onlineHumans() {
    const out = [];
    for (const h of this.humans.values()) if (h.online) out.push(h);
    return out;
  }

  join(conn) {
    let h = this.humans.get(conn.deviceId);
    if (h) {
      // Reconnect / second tab: newest socket wins.
      if (h.conn && h.conn !== conn) h.conn.room = null;
      h.conn = conn;
      h.online = true;
      h.name = conn.name;
    } else {
      if (this.humans.size >= C.MAX_PLAYERS) return 'Room is full';
      h = { id: conn.deviceId, name: conn.name, conn, online: true };
      this.humans.set(h.id, h);
    }
    conn.room = this;
    if (!this.hostId || !this.humans.get(this.hostId)?.online) this.hostId = h.id;
    this.broadcastRoom();
    this.syncLateJoiner(h);
    return null;
  }

  leave(conn, explicit) {
    const h = this.humans.get(conn.deviceId);
    if (!h || h.conn !== conn) return;
    conn.room = null;
    h.online = false;
    h.conn = null;
    if (explicit || this.phase === 'lobby') this.humans.delete(h.id);

    if (this.hostId === h.id) {
      const next = this.onlineHumans()[0];
      this.hostId = next ? next.id : null;
    }
    if (this.onlineHumans().length === 0) { this.destroy(); return; }

    this.broadcastRoom();
    // The player we are waiting on vanished: same as not responding.
    if (this.phase === 'game' && this.currentId() === h.id &&
        (this.turnPhase === 'read' || this.turnPhase === 'listen' || this.turnPhase === 'record')) {
      this.noResponse(h.id);
    }
  }

  rename(conn, name) {
    const h = this.humans.get(conn.deviceId);
    if (!h) return;
    h.name = name;
    for (const e of this.chain) if (e.playerId === h.id) e.name = name;
    this.broadcastRoom();
  }

  destroy() {
    this.epoch++;
    clearTimeout(this.timer);
    this.chain = [];
    this.onEmpty(this);
  }

  // ---------- messaging ----------

  send(h, obj) {
    if (h && h.online && h.conn) h.conn.sendJson(obj);
  }

  broadcast(obj) {
    const s = JSON.stringify(obj); // serialize once for everyone
    for (const h of this.humans.values()) if (h.online && h.conn) h.conn.sendRaw(s);
  }

  roomMessage() {
    const pub = p => ({ id: p.id, name: p.name, online: p.online });
    return {
      t: 'room',
      code: this.code,
      hostId: this.hostId,
      phase: this.phase,
      lang: this.lang,
      minPlayers: C.MIN_PLAYERS,
      recordMs: C.RECORD_MS,
      players: [...this.humans.values()].map(pub),
      lineup: this.lineup.map(id => pub(this.get(id) || { id, name: '?', online: false })),
    };
  }

  broadcastRoom() { this.broadcast(this.roomMessage()); }

  currentId() { return this.lineup[this.index]; }

  turnMessage(forId) {
    const pid = this.currentId();
    let remaining = Math.max(0, this.turnEndsAt - Date.now());
    if (this.turnPhase === 'record') remaining = Math.max(0, remaining - C.UPLOAD_GRACE_MS);
    return {
      t: 'turn',
      phase: this.turnPhase,
      index: this.index,
      total: this.lineup.length,
      playerId: pid || '',
      round: this.round,
      remainingMs: remaining,
      // The secret sentence only ever goes to the first player, only while reading.
      sentence: this.turnPhase === 'read' && forId === pid ? this.sentence : '',
      played: this.turnPhase === 'listen' && forId === pid && this.listenPlayed,
    };
  }

  broadcastTurn() {
    for (const h of this.humans.values()) this.send(h, this.turnMessage(h.id));
  }

  syncLateJoiner(h) {
    if (this.phase === 'game') {
      this.send(h, this.turnMessage(h.id));
      if (h.id === this.currentId() && this.turnPhase === 'listen' && !this.listenPlayed) this.sendListenMedia(h);
    } else if (this.phase === 'results' && this.results) {
      this.send(h, this.results);
    }
  }

  // ---------- game flow ----------

  start(conn) {
    if (conn.deviceId !== this.hostId) return this.send(this.humans.get(conn.deviceId), { t: 'error', msg: 'Only the host can start' });
    if (this.phase === 'game') return;
    if (this.onlineHumans().length < C.MIN_PLAYERS) {
      return this.send(this.humans.get(conn.deviceId), { t: 'error', msg: 'Not enough players' });
    }
    this.phase = 'game';
    this.round = 0;
    this.buildLineup(true);
    this.startChain();
  }

  setLang(conn, lang) {
    if (conn.deviceId !== this.hostId || this.phase === 'game' || !LANGS.includes(lang)) return;
    this.lang = lang;
    this.broadcastRoom();
  }

  backToLobby(conn) {
    if (conn.deviceId !== this.hostId) return;
    this.epoch++;
    clearTimeout(this.timer);
    this.phase = 'lobby';
    this.chain = [];
    this.results = null;
    this.lineup = [];
    for (const [id, h] of this.humans) if (!h.online) this.humans.delete(id);
    this.broadcastRoom();
  }

  /** Online players, shuffled or keeping the previous order (newcomers appended). */
  buildLineup(doShuffle) {
    const ids = this.onlineHumans().map(h => h.id);
    this.lineup = doShuffle ? shuffle(ids) : this.lineup.filter(id => ids.includes(id)).concat(ids.filter(id => !this.lineup.includes(id)));
  }

  startChain() {
    const ep = ++this.epoch;
    clearTimeout(this.timer);
    this.chain = [];
    this.index = 0;
    this.round++;
    this.pendingAudio = null;
    this.turnPhase = 'loading';
    this.turnEndsAt = 0;
    this.broadcastRoom();
    this.broadcastTurn();
    getSentence(this.lang).then(s => {
      if (ep !== this.epoch) return;
      this.sentence = s;
      this.beginTurn();
    });
  }

  beginTurn() {
    const pid = this.currentId();
    const p = this.get(pid);
    if (!p) return this.advance();
    if (!p.online) return this.noResponse(pid);
    if (this.index === 0) {
      this.setTurn('read', C.READ_MS);
    } else {
      this.listenPlayed = false;
      this.setTurn('listen', C.LISTEN_MS);
      this.sendListenMedia(p);
    }
  }

  setTurn(phase, ms) {
    const ep = ++this.epoch;
    clearTimeout(this.timer);
    this.turnPhase = phase;
    this.turnEndsAt = Date.now() + ms;
    this.timer = setTimeout(() => { if (ep === this.epoch) this.noResponse(this.currentId()); }, ms);
    this.broadcastTurn();
  }

  /** Send the previous player's recording (or its transcript as TTS if the upload is missing) to the current listener only. */
  sendListenMedia(h) {
    const prev = this.chain[this.index - 1];
    if (!prev) return;
    if (prev.audio) h.conn.sendBinary(audioFrame(BIN_TURN_AUDIO, this.index - 1, prev.audio));
    else this.send(h, { t: 'listenTts', index: this.index - 1, text: prev.said });
  }

  onPlayed(conn) {
    if (this.phase === 'game' && this.turnPhase === 'listen' && conn.deviceId === this.currentId()) this.listenPlayed = true;
  }

  onBegin(conn) {
    if (this.phase !== 'game' || conn.deviceId !== this.currentId()) return;
    if (this.turnPhase !== 'read' && this.turnPhase !== 'listen') return;
    this.pendingAudio = null;
    this.setTurn('record', C.RECORD_MS + C.UPLOAD_GRACE_MS);
  }

  onAudio(conn, buf) {
    if (this.phase !== 'game' || this.turnPhase !== 'record' || conn.deviceId !== this.currentId()) return;
    if (buf.length < 2 || buf.length > C.MAX_AUDIO_BYTES) return;
    const ml = buf[0];
    if (buf.length <= 1 + ml) return;
    const mime = buf.subarray(1, 1 + ml).toString('ascii');
    if (!/^[\w/.;=+-]*$/.test(mime)) return;
    this.pendingAudio = { mime, data: Buffer.from(buf.subarray(1 + ml)) };
  }

  onTurnDone(conn, msg) {
    if (this.phase !== 'game' || this.turnPhase !== 'record' || conn.deviceId !== this.currentId()) return;
    const h = this.humans.get(conn.deviceId);
    const transcript = String(msg.transcript || '').replace(/\s+/g, ' ').trim().slice(0, C.MAX_TRANSCRIPT);
    const voiced = !!msg.voiced;
    // "Spoke" = speech-to-text heard words, or (browsers without STT) the voice-activity detector fired.
    const spoke = transcript.length > 0 || (voiced && this.pendingAudio !== null);
    if (!spoke) return this.noResponse(h.id);
    this.chain.push({
      playerId: h.id, name: h.name,
      heard: this.heardText(), said: transcript, audio: this.pendingAudio,
    });
    this.pendingAudio = null;
    this.advance();
  }

  /** What the current player heard = last non-empty transcript (or the original sentence). */
  heardText() {
    for (let i = this.chain.length - 1; i >= 0; i--) if (this.chain[i].said) return this.chain[i].said;
    return this.sentence;
  }

  advance() {
    this.index++;
    if (this.index >= this.lineup.length) this.finish();
    else this.beginTurn();
  }

  noResponse(pid) {
    const ep = ++this.epoch;
    clearTimeout(this.timer);
    const p = this.get(pid);
    const name = p ? p.name : 'A player';
    const first = this.index === 0;
    this.turnPhase = 'paused';
    this.turnEndsAt = Date.now() + C.NOTICE_MS;
    this.broadcast({
      t: 'notice',
      kind: first ? 'newSentence' : 'noResponse',
      playerId: pid || '',
      msg: first
        ? `${name} didn't say anything. Starting again with a new sentence.`
        : `${name} is not responding. Restarting from the beginning with a shuffled lineup.`,
    });
    this.broadcastTurn();
    this.timer = setTimeout(() => {
      if (ep !== this.epoch) return;
      if (this.onlineHumans().length === 0) return this.destroy();
      // Players dropped below the minimum: the line can't continue -> back to the lobby.
      if (this.onlineHumans().length < C.MIN_PLAYERS) {
        this.phase = 'lobby';
        this.lineup = [];
        this.chain = [];
        this.broadcast({ t: 'notice', kind: 'notEnough', playerId: '', msg: 'Not enough players to continue.' });
        return this.broadcastRoom();
      }
      // First player silent -> same lineup, new sentence. Anyone else -> shuffled lineup.
      // Offline players are dropped from the lineup.
      this.buildLineup(!first);
      this.startChain();
    }, C.NOTICE_MS);
  }

  finish() {
    this.epoch++;
    clearTimeout(this.timer);
    this.phase = 'results';
    this.turnPhase = 'loading';
    const entries = this.chain.map((e, i) => ({
      index: i, playerId: e.playerId, name: e.name,
      heard: e.heard, said: e.said || '', score: similarity(e.heard, e.said), hasAudio: !!e.audio, // empty said -> client shows localized "(no transcript)"
    }));
    const finalSaid = this.heardText();
    const ranked = entries.slice().sort((a, b) => b.score - a.score);
    const ranking = ranked.map((e, i) => ({ playerId: e.playerId, name: e.name, score: e.score, rank: i + 1 }));
    this.results = {
      t: 'results', original: this.sentence, final: finalSaid,
      overall: similarity(this.sentence, finalSaid), entries, ranking,
    };
    this.broadcastRoom();
    this.broadcast(this.results);
  }

  onGetAudio(conn, msg) {
    if (this.phase !== 'results') return;
    const i = msg.index | 0;
    const e = this.chain[i];
    if (!e) return;
    if (e.audio) conn.sendBinary(audioFrame(BIN_RESULT_AUDIO, i, e.audio));
    else conn.sendJson({ t: 'resultTts', index: i, text: e.said });
  }
}

module.exports = { Room };
