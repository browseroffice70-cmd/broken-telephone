'use strict';

// All timings in milliseconds. Every value can be overridden with an env var of the same name.
const num = (name, def) => {
  const v = Number(process.env[name]);
  return Number.isFinite(v) && v > 0 ? v : def;
};

module.exports = {
  PORT: num('PORT', 8080),
  MIN_PLAYERS: num('MIN_PLAYERS', 4),       // fewest players a game needs
  MAX_PLAYERS: num('MAX_PLAYERS', 20),
  READ_MS: num('READ_MS', 45000),           // first player: time to read + press Start
  LISTEN_MS: num('LISTEN_MS', 60000),       // later players: time to play audio + press Start
  RECORD_MS: num('RECORD_MS', 10000),       // fixed speaking window
  UPLOAD_GRACE_MS: num('UPLOAD_GRACE_MS', 6000), // extra time for the client to upload after the window
  NOTICE_MS: num('NOTICE_MS', 3500),        // pause showing "X is not responding" before restarting
  MAX_AUDIO_BYTES: num('MAX_AUDIO_BYTES', 600 * 1024),
  MAX_TRANSCRIPT: 300,
  NEWS_TTL_MS: num('NEWS_TTL_MS', 15 * 60 * 1000),
};
