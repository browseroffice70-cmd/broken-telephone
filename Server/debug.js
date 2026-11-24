'use strict';
// Local debug server: same as `npm start`, but a game can start with just 2 players
// (e.g. two browser windows / a laptop + a phone). Override with MIN_PLAYERS=n if needed.
if (!process.env.MIN_PLAYERS) process.env.MIN_PLAYERS = '2';
const C = require('./src/config');
const { server } = require('./server');
server.listen(C.PORT, () => {
  console.log(`[DEBUG] Broken Telephone on http://localhost:${C.PORT}  (MIN_PLAYERS=${C.MIN_PLAYERS})`);
});
