'use strict';

// "Perceiving skill" = how faithfully a player reproduced what they heard.
// Blend of word-level and char-level edit distance so small STT slips (plural, tense)
// cost less than dropped/replaced words. O(n*m) on ~15 words / ~100 chars — negligible.

// Unicode-aware: keeps letters + combining marks (Devanagari/Kannada/Malayalam vowel signs) + digits.
function normalize(s) {
  return String(s || '')
    .normalize('NFC')
    .toLowerCase()
    .replace(/[​-‍⁠]/g, '')   // zero-width chars: STT and headlines disagree on these
    .replace(/[’‘]/g, "'")
    .replace(/[^\p{L}\p{M}\p{N}' ]+/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

// Single-row Levenshtein over arrays or strings (indexable).
function levenshtein(a, b) {
  const n = a.length, m = b.length;
  if (n === 0) return m;
  if (m === 0) return n;
  let prev = new Array(m + 1);
  let cur = new Array(m + 1);
  for (let j = 0; j <= m; j++) prev[j] = j;
  for (let i = 1; i <= n; i++) {
    cur[0] = i;
    const ai = a[i - 1];
    for (let j = 1; j <= m; j++) {
      const cost = ai === b[j - 1] ? 0 : 1;
      const del = prev[j] + 1, ins = cur[j - 1] + 1, sub = prev[j - 1] + cost;
      cur[j] = del < ins ? (del < sub ? del : sub) : (ins < sub ? ins : sub);
    }
    const t = prev; prev = cur; cur = t;
  }
  return prev[m];
}

/** 0..100, or -1 when either side is missing (e.g. browser without speech-to-text). */
function similarity(heard, said) {
  const a = normalize(heard), b = normalize(said);
  if (!a || !b) return -1;
  const wa = a.split(' '), wb = b.split(' ');
  const wordSim = 1 - levenshtein(wa, wb) / Math.max(wa.length, wb.length);
  const charSim = 1 - levenshtein(a, b) / Math.max(a.length, b.length);
  return Math.max(0, Math.round((0.7 * wordSim + 0.3 * charSim) * 100));
}

module.exports = { similarity, normalize, levenshtein };
