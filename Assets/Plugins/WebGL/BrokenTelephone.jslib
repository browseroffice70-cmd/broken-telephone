// Browser side of Broken Telephone.
// Performance design: the WebSocket, microphone, MediaRecorder, speech-to-text and all audio
// playback live here in JS. Recordings are Opus blobs that go straight from MediaRecorder to the
// socket and from the socket to an <audio> element — they never cross into the Wasm heap.
// C# only receives small JSON strings via SendMessage.

var BrokenTelephonePlugin = {
  $BT: {
    go: null,
    ws: null,
    stream: null,        // live mic stream: open only while this player is recording
    micSrc: null,
    audioCtx: null,
    analyser: null,
    vadBuf: null,
    player: null,        // one reusable <audio> element (unlocked once for iOS/Safari)
    turnUrl: null,       // blob URL of the clip the local player must listen to (playable once)
    turnTts: null,       // or a transcript, spoken with speechSynthesis (when no recording was uploaded)
    turnLang: 'en-IN',
    resultUrls: {},
    rec: null,           // active recording session
    unlocked: false,
    tc: null, tx: null,  // offscreen canvas used to shape + rasterize UI text
    textW: 0, textH: 0,
    fontsRequested: false,
    kannadaFaces: null,
    // System fonts first (zero download on Windows/Android/iOS/macOS), Noto web fonts as the fallback.
    // 'BT Kannada' = bundled Google Sans subset (unicode-range limited to Kannada, so Latin etc. fall through).
    family: "'BT Kannada','Noto Sans','Noto Sans Devanagari','Noto Sans Malayalam','Nirmala UI','Segoe UI'," +
            "'Kohinoor Devanagari','Kannada Sangam MN','Malayalam Sangam MN',Roboto,'Helvetica Neue',Arial,sans-serif",

    call: function (method, arg) {
      if (!BT.go) return;
      var fn = (typeof SendMessage === 'function') ? SendMessage : Module.SendMessage;
      fn(BT.go, method, arg === undefined ? '' : String(arg));
    },
    str: function (s) {
      var n = lengthBytesUTF8(s) + 1, p = _malloc(n);
      stringToUTF8(s, p, n);
      return p;
    },
    unlock: function () {
      // iOS/Safari only allow media + speech that start inside a user gesture. Unity handles input
      // in its own frame loop (outside the gesture), so we prime everything on the first touch.
      if (BT.unlocked) return;
      BT.unlocked = true;
      try {
        if (!BT.player) BT.player = new Audio();
        BT.player.src = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=';
        var p = BT.player.play(); if (p && p.catch) p.catch(function () {});
      } catch (e) {}
      try { if (window.speechSynthesis) window.speechSynthesis.speak(new SpeechSynthesisUtterance('')); } catch (e) {}
      try { if (BT.audioCtx && BT.audioCtx.state === 'suspended') BT.audioCtx.resume(); } catch (e) {}
    },
    playUrl: function (url, onEnd) {
      if (!BT.player) BT.player = new Audio();
      var done = false;
      var finish = function () { if (done) return; done = true; BT.player.onended = BT.player.onerror = null; onEnd(); };
      BT.player.onended = finish;
      BT.player.onerror = finish;
      BT.player.src = url;
      var p = BT.player.play();
      if (p && p.catch) p.catch(finish);
    },
    speak: function (text, lang, onEnd) {
      var done = false;
      var finish = function () { if (done) return; done = true; clearTimeout(guard); onEnd(); };
      // Chrome sometimes never fires onend for long utterances — guard with an estimate.
      var guard = setTimeout(finish, 1500 + text.split(' ').length * 500);
      try {
        var synth = window.speechSynthesis;
        synth.cancel();
        var u = new SpeechSynthesisUtterance(text);
        u.lang = lang || 'en-IN';
        var prefix = u.lang.slice(0, 2).toLowerCase();
        var voices = synth.getVoices();
        for (var i = 0; i < voices.length; i++) {
          if ((voices[i].lang || '').replace('_', '-').toLowerCase().indexOf(prefix) === 0) { u.voice = voices[i]; break; }
        }
        u.rate = 1.0;
        u.onend = finish;
        u.onerror = finish;
        synth.speak(u);
      } catch (e) { finish(); }
    },
    sendJson: function (o) {
      if (BT.ws && BT.ws.readyState === 1) BT.ws.send(JSON.stringify(o));
    },
    onBinary: function (buf) {
      var u8 = new Uint8Array(buf);
      if (u8.length < 4) return;
      var kind = u8[0], index = (u8[1] << 8) | u8[2], ml = u8[3];
      var mime = '';
      for (var i = 0; i < ml; i++) mime += String.fromCharCode(u8[4 + i]);
      var blob = new Blob([buf.slice(4 + ml)], { type: mime.split(';')[0] });
      var url = URL.createObjectURL(blob);
      if (kind === 1) {
        if (BT.turnUrl) URL.revokeObjectURL(BT.turnUrl);
        BT.turnUrl = url; BT.turnTts = null;
        BT.call('OnTurnAudioReady', '');
      } else if (kind === 2) {
        if (BT.resultUrls[index]) URL.revokeObjectURL(BT.resultUrls[index]);
        BT.resultUrls[index] = url;
        BT.playUrl(url, function () { BT.call('OnResultPlaybackEnded', index); });
      }
    },
    pickMime: function () {
      if (typeof MediaRecorder === 'undefined') return null;
      var opts = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4', 'audio/webm'];
      for (var i = 0; i < opts.length; i++) if (MediaRecorder.isTypeSupported(opts[i])) return opts[i];
      return '';
    },
    // The mic is opened per turn and released right after, so the browser's "mic in use"
    // indicator is only on while the player is actually speaking.
    openMic: function (onOk, onFail) {
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) { onFail('unsupported'); return; }
      navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 } })
        .then(onOk, function () { onFail('denied'); });
    },
    stopTracks: function (stream) {
      var tracks = stream ? stream.getTracks() : [];
      for (var i = 0; i < tracks.length; i++) { try { tracks[i].stop(); } catch (e) {} }
    },
    attachMic: function (stream) {
      BT.stream = stream;
      try {
        if (!BT.audioCtx) {
          var Ctx = window.AudioContext || window.webkitAudioContext;
          BT.audioCtx = new Ctx();
          BT.analyser = BT.audioCtx.createAnalyser();
          BT.analyser.fftSize = 1024;
          BT.vadBuf = new Float32Array(BT.analyser.fftSize); // allocated once, reused by the VAD
        }
        BT.micSrc = BT.audioCtx.createMediaStreamSource(stream);
        BT.micSrc.connect(BT.analyser);
      } catch (e) {}
    },
    releaseMic: function () {
      try { if (BT.micSrc) BT.micSrc.disconnect(); } catch (e) {}
      BT.micSrc = null;
      BT.stopTracks(BT.stream);
      BT.stream = null;
    },
    // Starts recorder + speech-to-text + voice detection on the freshly opened mic.
    beginCapture: function (r, sttLang) {
      try { if (BT.audioCtx && BT.audioCtx.state === 'suspended') BT.audioCtx.resume(); } catch (e) {}
      // 1) Compressed recording (low bitrate Opus: ~12 KB for 10 s of speech).
      try {
        var opts = { audioBitsPerSecond: 24000 };
        if (r.mime) opts.mimeType = r.mime;
        r.recorder = new MediaRecorder(BT.stream, opts);
        r.mime = r.recorder.mimeType || r.mime || 'audio/webm';
        r.recorder.ondataavailable = function (e) { if (e.data && e.data.size) r.chunks.push(e.data); };
        r.recorder.start();
      } catch (e) { r.recorder = null; }

      // 2) Speech-to-text (Chrome/Edge/Safari). Restarts itself if the engine stops early.
      var SR = window.SpeechRecognition || window.webkitSpeechRecognition;
      if (SR) {
        try {
          var rc = new SR();
          rc.lang = sttLang; // room language: en-IN / hi-IN / kn-IN / ml-IN
          rc.continuous = true;
          rc.interimResults = true;
          rc.maxAlternatives = 1;
          rc.onresult = function (ev) {
            var fin = '', interim = '';
            for (var i = 0; i < ev.results.length; i++) {
              if (ev.results[i].isFinal) fin += ev.results[i][0].transcript + ' ';
              else interim += ev.results[i][0].transcript + ' ';
            }
            if (fin) r.finalText = (r.baseText || '') + fin;
            r.interimText = (r.finalText || r.baseText || '') + interim;
          };
          rc.onend = function () {
            if (!r.finishing) { r.baseText = r.finalText; try { rc.start(); } catch (e) {} }
            else if (r.onSttEnd) r.onSttEnd();
          };
          rc.onerror = function () {};
          rc.start();
          r.recognition = rc;
        } catch (e) { r.recognition = null; }
      }

      // 3) Voice-activity detection: >= 300 ms above an RMS threshold counts as "spoke".
      //    Covers browsers without STT and catches STT engines that return nothing.
      if (BT.analyser) {
        r.vadTimer = setInterval(function () {
          BT.analyser.getFloatTimeDomainData(BT.vadBuf);
          var sum = 0, b = BT.vadBuf;
          for (var i = 0; i < b.length; i++) sum += b[i] * b[i];
          if (Math.sqrt(sum / b.length) > 0.02) { r.voicedMs += 50; if (r.voicedMs >= 300) r.voiced = true; }
        }, 50);
      }
    },
    finishRecording: function () {
      var r = BT.rec;
      if (!r || r.finishing) return;
      r.finishing = true;
      clearTimeout(r.timer);
      clearInterval(r.vadTimer);
      try { if (r.recognition) r.recognition.stop(); } catch (e) {}
      var recorderDone = new Promise(function (res) {
        if (!r.recorder || r.recorder.state === 'inactive') return res();
        r.recorder.onstop = function () { res(); };
        try { r.recorder.stop(); } catch (e) { res(); }
      });
      // STT delivers its final result shortly after stop(); give it a moment.
      var sttDone = new Promise(function (res) {
        if (!r.recognition) return res();
        r.onSttEnd = res;
        setTimeout(res, 1200);
      });
      Promise.all([recorderDone, sttDone]).then(function () {
        BT.releaseMic(); // turn is over: turn the mic off before the upload
        var transcript = (r.finalText || r.interimText || '').replace(/\s+/g, ' ').trim();
        var mime = r.mime || 'audio/webm';
        var blob = new Blob(r.chunks, { type: mime });
        blob.arrayBuffer().then(function (ab) {
          // Upload frame: [mimeLen u8][mime ascii][audio]
          var head = new Uint8Array(1 + mime.length);
          head[0] = mime.length;
          for (var i = 0; i < mime.length; i++) head[1 + i] = mime.charCodeAt(i) & 0x7f;
          var frame = new Uint8Array(head.length + ab.byteLength);
          frame.set(head, 0);
          frame.set(new Uint8Array(ab), head.length);
          if (BT.ws && BT.ws.readyState === 1 && ab.byteLength > 0) BT.ws.send(frame.buffer);
          BT.sendJson({ t: 'turnDone', transcript: transcript, voiced: r.voiced });
          BT.rec = null;
          BT.call('OnRecordingFinished', JSON.stringify({ transcript: transcript, voiced: r.voiced }));
        });
      });
    }
  },

  BT_Init: function (goName) {
    BT.go = UTF8ToString(goName);
    var u = function () { BT.unlock(); };
    document.addEventListener('pointerdown', u, { once: true, capture: true });
    document.addEventListener('keydown', u, { once: true, capture: true });
    if (window.speechSynthesis) window.speechSynthesis.getVoices(); // warm the voice list
  },

  BT_GetDeviceId: function () {
    var id = null;
    try { id = localStorage.getItem('bt_device_id'); } catch (e) {}
    if (!id) {
      id = (window.crypto && crypto.randomUUID) ? crypto.randomUUID()
        : 'd-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 12);
      try { localStorage.setItem('bt_device_id', id); } catch (e) {}
    }
    return BT.str(id);
  },

  BT_GetServerUrl: function () {
    var q = new URLSearchParams(location.search).get('server');
    var url = q;
    if (!url) {
      if (location.protocol === 'http:' || location.protocol === 'https:') {
        url = (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws';
      } else {
        url = 'ws://localhost:8080/ws';
      }
    }
    return BT.str(url);
  },

  BT_GetRoomFromUrl: function () {
    var r = new URLSearchParams(location.search).get('room') || '';
    return BT.str(r.toUpperCase().replace(/[^A-Z]/g, '').slice(0, 4));
  },

  BT_GetShareLink: function (codePtr) {
    var code = UTF8ToString(codePtr);
    return BT.str(location.origin + location.pathname + '?room=' + code);
  },

  BT_CopyText: function (textPtr) {
    var t = UTF8ToString(textPtr);
    try { navigator.clipboard.writeText(t); } catch (e) {}
  },

  BT_Connect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    try { if (BT.ws) { BT.ws.onclose = null; BT.ws.close(); } } catch (e) {}
    var ws = new WebSocket(url);
    ws.binaryType = 'arraybuffer';
    ws.onopen = function () { BT.call('OnNetOpen', ''); };
    ws.onclose = function () { if (BT.ws === ws) BT.call('OnNetClosed', ''); };
    ws.onmessage = function (ev) {
      if (typeof ev.data === 'string') BT.call('OnNetMessage', ev.data);
      else BT.onBinary(ev.data);
    };
    BT.ws = ws;
  },

  BT_Send: function (jsonPtr) {
    if (BT.ws && BT.ws.readyState === 1) BT.ws.send(UTF8ToString(jsonPtr));
  },

  BT_IsSttSupported: function () {
    return (window.SpeechRecognition || window.webkitSpeechRecognition) ? 1 : 0;
  },

  // Asks for mic permission up front (on create/join) so the prompt doesn't eat into the
  // recording window, then releases the mic straight away.
  BT_RequestMic: function () {
    BT.openMic(function (stream) {
      BT.stopTracks(stream);
      BT.call('OnMicResult', 'ok');
    }, function (err) { BT.call('OnMicResult', err); });
  },

  BT_StartRecording: function (ms, langPtr) {
    var sttLang = UTF8ToString(langPtr) || 'en-IN';
    if (BT.rec) return;
    var r = { chunks: [], finalText: '', interimText: '', voiced: false, voicedMs: 0, finishing: false, mime: BT.pickMime() };
    BT.rec = r;
    // The window counts from now, including the few hundred ms it takes to open the mic.
    r.timer = setTimeout(BT.finishRecording, ms);
    BT.openMic(function (stream) {
      // Turn already ended (player pressed "I'm done" or time ran out) while the mic was opening.
      if (BT.rec !== r || r.finishing) { BT.stopTracks(stream); return; }
      BT.attachMic(stream);
      BT.beginCapture(r, sttLang);
    }, function () {
      if (BT.rec !== r || r.finishing) return;
      clearTimeout(r.timer);
      BT.rec = null;
      BT.sendJson({ t: 'turnDone', transcript: '', voiced: false });
      BT.call('OnRecordingFinished', JSON.stringify({ transcript: '', voiced: false }));
    });
  },

  BT_StopRecording: function () {
    BT.finishRecording();
  },

  BT_PlayTurnAudio: function () {
    // Single play: the clip is revoked as soon as it starts, so it cannot be replayed.
    if (BT.turnUrl) {
      var url = BT.turnUrl;
      BT.turnUrl = null;
      BT.playUrl(url, function () { URL.revokeObjectURL(url); BT.call('OnPlaybackEnded', ''); });
      return 1;
    }
    if (BT.turnTts) {
      var text = BT.turnTts;
      BT.turnTts = null;
      BT.speak(text, BT.turnLang, function () { BT.call('OnPlaybackEnded', ''); });
      return 1;
    }
    return 0;
  },

  BT_SetTurnTts: function (textPtr, langPtr) {
    if (BT.turnUrl) { URL.revokeObjectURL(BT.turnUrl); BT.turnUrl = null; }
    BT.turnTts = UTF8ToString(textPtr);
    BT.turnLang = UTF8ToString(langPtr) || 'en-IN';
  },

  BT_ClearTurnMedia: function () {
    if (BT.turnUrl) { URL.revokeObjectURL(BT.turnUrl); BT.turnUrl = null; }
    BT.turnTts = null;
  },

  BT_PlayResult: function (index) {
    var url = BT.resultUrls[index];
    if (!url) return 0; // caller requests it from the server; it auto-plays on arrival
    BT.playUrl(url, function () { BT.call('OnResultPlaybackEnded', index); });
    return 1;
  },

  BT_SpeakResult: function (index, textPtr, langPtr) {
    BT.speak(UTF8ToString(textPtr), UTF8ToString(langPtr), function () { BT.call('OnResultPlaybackEnded', index); });
  },

  BT_StopAudio: function () {
    try { if (BT.player) BT.player.pause(); } catch (e) {}
    try { if (window.speechSynthesis) window.speechSynthesis.cancel(); } catch (e) {}
  },

  BT_GetBrowserLang: function () {
    var l = ((navigator.languages && navigator.languages[0]) || navigator.language || 'en').slice(0, 2).toLowerCase();
    return BT.str(l);
  },

  // Lazily pulls Noto web fonts for Indic scripts (only if the OS lacks them it matters; Google's
  // unicode-range subsets mean only the scripts actually drawn get downloaded). Re-shapes UI when ready.
  BT_LoadFonts: function () {
    if (!BT.fontsRequested) {
      BT.fontsRequested = true;
      try {
        var link = document.createElement('link');
        link.rel = 'stylesheet';
        // Kannada is NOT fetched from Google: it uses the bundled Google Sans subset (BT_LoadKannadaFont).
        link.href = 'https://fonts.googleapis.com/css2?family=Noto+Sans:wght@400;700&family=Noto+Sans+Devanagari:wght@400;700' +
                    '&family=Noto+Sans+Malayalam:wght@400;700&display=swap';
        document.head.appendChild(link);
        if (document.fonts && document.fonts.addEventListener) {
          var pending = null;
          document.fonts.addEventListener('loadingdone', function () {
            clearTimeout(pending); // coalesce bursts of subset loads into one re-shape
            pending = setTimeout(function () { BT.call('OnFontsLoaded', ''); }, 150);
          });
        }
      } catch (e) {}
    }
  },

  // Bundled Kannada font (Google Sans, subset to Kannada, ~30 KB per weight as WOFF2, served from
  // StreamingAssets on our own origin). Downloaded only the first time Kannada is needed.
  BT_LoadKannadaFont: function () {
    if (BT.kannadaFaces || typeof FontFace === 'undefined' || !document.fonts) return;
    var base = (typeof Module !== 'undefined' && Module.streamingAssetsUrl) ? Module.streamingAssetsUrl : 'StreamingAssets';
    var range = 'U+0C80-0CFF, U+200C-200D, U+0964-0965, U+25CC';
    var faces = [
      new FontFace('BT Kannada', "url('" + new URL(base + '/Fonts/GoogleSans-Kannada-Regular.woff2', location.href).href + "') format('woff2')", { weight: '400', unicodeRange: range }),
      new FontFace('BT Kannada', "url('" + new URL(base + '/Fonts/GoogleSans-Kannada-Bold.woff2', location.href).href + "') format('woff2')", { weight: '700', unicodeRange: range })
    ];
    BT.kannadaFaces = faces;
    var loads = [];
    for (var i = 0; i < faces.length; i++) { document.fonts.add(faces[i]); loads.push(faces[i].load()); }
    Promise.all(loads).then(function () { BT.call('OnFontsLoaded', ''); })
      .catch(function (e) { console.warn('[BT] Kannada font failed to load, using system font', e); });
  },

  // Shape + rasterize one label. Returns (width << 16) | height in pixels; pixels stay in JS until BT_TextCopy.
  BT_TextRender: function (textPtr, fontPx, bold, maxW, wrap, align) {
    var text = UTF8ToString(textPtr);
    if (!BT.tc) {
      BT.tc = document.createElement('canvas');
      BT.tx = BT.tc.getContext('2d', { willReadFrequently: true });
    }
    var ctx = BT.tx;
    var font = (bold ? '700 ' : '400 ') + fontPx + 'px ' + BT.family;
    ctx.font = font;
    var lines = [];
    var paras = text.split(String.fromCharCode(10));
    for (var p = 0; p < paras.length; p++) {
      if (!wrap) { lines.push(paras[p]); continue; }
      var words = paras[p].split(' ');
      var line = '';
      for (var i = 0; i < words.length; i++) {
        var test = line ? line + ' ' + words[i] : words[i];
        if (line && ctx.measureText(test).width > maxW) { lines.push(line); line = words[i]; }
        else line = test;
      }
      lines.push(line);
    }
    var pad = Math.ceil(fontPx * 0.12);
    var lineH = Math.ceil(fontPx * 1.32); // Indic scripts need extra room above/below for vowel signs
    var w = 0;
    for (var j = 0; j < lines.length; j++) w = Math.max(w, ctx.measureText(lines[j]).width);
    w = Math.min(Math.ceil(w) + pad * 2, wrap ? maxW + pad * 2 : 4096);
    var h = Math.min(lines.length * lineH + pad * 2, 4096);
    if (w <= pad * 2 || h <= 0) { BT.textW = BT.textH = 0; return 0; }
    BT.tc.width = w;   // resizing also clears the canvas
    BT.tc.height = h;
    ctx.font = font;
    ctx.fillStyle = '#fff'; // white glyphs; Unity tints via the RawImage colour
    ctx.textBaseline = 'middle';
    ctx.textAlign = align === 0 ? 'left' : align === 2 ? 'right' : 'center';
    var x = align === 0 ? pad : align === 2 ? w - pad : w / 2;
    for (var k = 0; k < lines.length; k++) ctx.fillText(lines[k], x, pad + k * lineH + lineH / 2);
    BT.textW = w; BT.textH = h;
    return (w << 16) | h;
  },

  // Copy the last rendered label into a C# byte[] (bottom-up rows, as Texture2D expects).
  BT_TextCopy: function (dst) {
    var w = BT.textW, h = BT.textH;
    if (!w || !h) return;
    var data = BT.tx.getImageData(0, 0, w, h).data;
    var row = w * 4;
    for (var y = 0; y < h; y++) HEAPU8.set(data.subarray((h - 1 - y) * row, (h - y) * row), dst + y * row);
  },

  BT_ClearResults: function () {
    for (var k in BT.resultUrls) URL.revokeObjectURL(BT.resultUrls[k]);
    BT.resultUrls = {};
  }
};

autoAddDeps(BrokenTelephonePlugin, '$BT');
mergeInto(LibraryManager.library, BrokenTelephonePlugin);
