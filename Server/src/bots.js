'use strict';

const BOT_NAMES = [
  'Echo', 'Static', 'Murmur', 'Whisper', 'Mumbles', 'Parrot', 'Crackle',
  'Buzz', 'Fuzz', 'Reverb', 'Dial Tone', 'Hiss', 'Ringo', 'Morse',
];

// Words that a listener plausibly mishears. Bidirectional lookups are built below.
const SOUND_ALIKES = [
  ['their', 'there'], ['to', 'two'], ['for', 'four'], ['new', 'knew'], ['right', 'write'],
  ['week', 'weak'], ['sea', 'see'], ['by', 'buy'], ['one', 'won'], ['hour', 'our'],
  ['plane', 'plain'], ['rain', 'reign'], ['sun', 'son'], ['meet', 'meat'], ['bear', 'bare'],
  ['peace', 'piece'], ['road', 'rode'], ['tale', 'tail'], ['wait', 'weight'], ['whole', 'hole'],
  ['cell', 'sell'], ['flower', 'flour'], ['night', 'knight'], ['made', 'maid'], ['side', 'sighed'],
  ['price', 'prize'], ['bank', 'tank'], ['vote', 'boat'], ['law', 'raw'], ['deal', 'meal'],
  ['court', 'caught'], ['fight', 'flight'], ['star', 'car'], ['city', 'kitty'], ['film', 'farm'],
  ['team', 'steam'], ['game', 'name'], ['market', 'basket'], ['budget', 'bucket'], ['police', 'please'],
  ['storm', 'store'], ['world', 'word'], ['record', 'recall'], ['leader', 'ladder'], ['climate', 'climb it'],
  ['million', 'billion'], ['report', 'resort'], ['country', 'county'], ['health', 'wealth'], ['trade', 'trait'],
];
const ALIKE = new Map();
for (const [a, b] of SOUND_ALIKES) { ALIKE.set(a, b); ALIKE.set(b, a); }

const FILLERS = {
  en: ['the', 'a', 'so', 'like', 'just', 'um'],
  hi: ['तो', 'और', 'भी', 'वो'],
  kn: ['ಅದು', 'ಮತ್ತು', 'ಸರಿ', 'ಆ'],
  ml: ['അത്', 'പിന്നെ', 'ഒരു', 'ആ'],
};

// Dependent vowel signs (matras) per script. Swapping/dropping one is the typical mishearing
// in Indic speech ("ಕೊಡಗು" -> "ಕುಡಗು", "दिल्ली" -> "देल्ली").
const MATRAS = {
  hi: [...'ािीुूेैोौ'],
  kn: [...'ಾಿೀುೂೆೇೈೊೋೌ'],
  ml: [...'ാിീുൂെേൈൊോൌ'],
};

function mutateIndicWord(w, lang) {
  const set = MATRAS[lang];
  const chars = [...w];
  const idx = [];
  for (let i = 0; i < chars.length; i++) if (set.includes(chars[i])) idx.push(i);
  if (idx.length === 0) return null;
  const k = idx[(Math.random() * idx.length) | 0];
  if (Math.random() < 0.3 && chars.length > 3) chars.splice(k, 1);          // drop the vowel sign
  else chars[k] = pick(set.filter(m => m !== chars[k]));                      // swap to another vowel sign
  return chars.join('');
}

function pick(arr) { return arr[(Math.random() * arr.length) | 0]; }

function makeBot(i) {
  return {
    id: 'bot-' + i + '-' + Math.random().toString(36).slice(2, 7),
    name: 'Bot ' + BOT_NAMES[i % BOT_NAMES.length],
    bot: true,
    online: true,
    // Per-bot "hearing" quality: probability that any given word gets garbled.
    skill: 0.08 + Math.random() * 0.22,
  };
}

function mutateWord(w) {
  const lower = w.toLowerCase();
  if (ALIKE.has(lower)) return ALIKE.get(lower);
  const r = Math.random();
  if (r < 0.3 && lower.length > 3) {
    // Suffix confusion: plural / tense slips.
    if (lower.endsWith('s')) return lower.slice(0, -1);
    if (lower.endsWith('ed')) return lower.slice(0, -2) + 's';
    if (lower.endsWith('ing')) return lower.slice(0, -3) + 'ed';
    return lower + 's';
  }
  if (r < 0.55 && lower.length > 4) {
    // Swap a vowel — the classic mishearing.
    const vowels = 'aeiou';
    const idx = [...lower].findIndex((c, k) => k > 0 && vowels.includes(c));
    if (idx > 0) return lower.slice(0, idx) + pick(vowels.replace(lower[idx], '')) + lower.slice(idx + 1);
  }
  return null; // let the caller decide (drop / keep)
}

/** Returns the bot's (mis)heard version of `text`. Always changes at least one thing for flavour. */
const SCRIPT_OF = { hi: /[ऀ-ॿ]/, kn: /[ಀ-೿]/, ml: /[ഀ-ൿ]/ };

function mutate(text, skill, lang = 'en') {
  // Mishear in the script actually heard (a player may answer in English inside a Kannada room).
  if (SCRIPT_OF[lang] && !SCRIPT_OF[lang].test(text || '')) lang = 'en';
  const indic = MATRAS[lang] !== undefined;
  const mut = w => (indic ? mutateIndicWord(w, lang) : mutateWord(w));
  const fillers = FILLERS[lang] || FILLERS.en;
  const words = String(text || '').normalize('NFC').replace(/[^\p{L}\p{M}\p{N}' -]+/gu, ' ').replace(/(^|\s)'+|'+(?=\s|$)/g, ' ').split(/\s+/).filter(Boolean);
  if (words.length === 0) return '...';
  const out = [];
  let changed = false;
  for (const w of words) {
    if (Math.random() < skill) {
      const m = mut(w);
      if (m) { out.push(m); changed = true; continue; }
      if (words.length > 5 && Math.random() < 0.5) { changed = true; continue; } // dropped word
      if (Math.random() < 0.3) { out.push(pick(fillers)); changed = true; }
    }
    out.push(indic ? w : w.toLowerCase());
  }
  if (!changed) {
    // Garble one random word that has a sound-alike or a suffix slip.
    for (let tries = 0; tries < 4 && !changed; tries++) {
      const k = (Math.random() * out.length) | 0;
      const m = mut(out[k]);
      if (m) { out[k] = m; changed = true; }
    }
  }
  const s = out.join(' ');
  return indic ? s : s.charAt(0).toUpperCase() + s.slice(1);
}

module.exports = { makeBot, mutate };
