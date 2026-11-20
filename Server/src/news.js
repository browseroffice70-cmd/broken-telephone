'use strict';

// Region-specific trending headlines, fetched server-side (no browser CORS, one fetch per TTL shared by all rooms).
//   en -> India (English)       hi -> Hindi belt / North India
//   kn -> Karnataka (Kannada)    ml -> Kerala (Malayalam)
// Every language has a built-in regional fallback list so a game can always start.

const { NEWS_TTL_MS } = require('./config');

const LANGS = ['en', 'hi', 'kn', 'ml'];

const SOURCES = {
  // NATION topic = India-focused stories (the plain top-stories feed mixes in world/entertainment news).
  en: ['https://news.google.com/rss/headlines/section/topic/NATION?hl=en-IN&gl=IN&ceid=IN:en',
       'https://news.google.com/rss?hl=en-IN&gl=IN&ceid=IN:en'],
  hi: ['https://news.google.com/rss/headlines/section/topic/NATION?hl=hi&gl=IN&ceid=IN:hi',
       'https://news.google.com/rss?hl=hi&gl=IN&ceid=IN:hi'],
  // Google News has no Kannada edition (it serves Hindi), so use Karnataka's Kannada outlets directly.
  kn: ['https://www.prajavani.net/stories.rss', 'https://publictv.in/feed', 'https://kannada.oneindia.com/rss/feeds/kannada-news-fb.xml'],
  ml: ['https://news.google.com/rss?hl=ml&gl=IN&ceid=IN:ml'],
};

// Unicode block per language; a headline must be mostly in this script to count.
const SCRIPT = {
  en: /[A-Za-z]/g,
  hi: /[ऀ-ॿ]/g,
  kn: /[ಀ-೿]/g,
  ml: /[ഀ-ൿ]/g,
};

const FALLBACK = {
  en: [
    'Mumbai local trains will run extra services during the festive season',
    'Bengaluru techies are switching to cycles to beat the morning traffic',
    'Monsoon rains bring relief to farmers across Maharashtra and Gujarat',
    'Chennai beach clean up drive collects two tonnes of plastic in one day',
    'Kolkata book fair breaks attendance record on its very first weekend',
    'Indian cricket team arrives in Ahmedabad ahead of the final match',
    'Delhi metro to add new trains on the busy yellow line next month',
    'Hyderabad biryani named the most ordered dish of the year in India',
  ],
  hi: [
    'दिल्ली मेट्रो त्योहारों के मौसम में अतिरिक्त ट्रेनें चलाएगी',
    'वाराणसी के घाटों पर देव दीपावली की भव्य तैयारियां शुरू',
    'जयपुर में पतंग उत्सव देखने हजारों पर्यटक पहुंचे',
    'लखनऊ में नई इलेक्ट्रिक बसों का संचालन आज से शुरू',
    'बिहार के किसानों को मानसून की अच्छी बारिश से बड़ी राहत',
    'इंदौर लगातार आठवीं बार देश का सबसे स्वच्छ शहर बना',
    'शिमला में इस साल की पहली बर्फबारी से पर्यटक खुश',
    'भोपाल की बड़ी झील पर नौका दौड़ प्रतियोगिता का आयोजन',
  ],
  kn: [
    'ಬೆಂಗಳೂರಿನ ನಮ್ಮ ಮೆಟ್ರೋದಲ್ಲಿ ಹೊಸ ರೈಲುಗಳ ಸಂಚಾರ ಆರಂಭ',
    'ಮೈಸೂರು ದಸರಾ ಜಂಬೂ ಸವಾರಿ ನೋಡಲು ಲಕ್ಷಾಂತರ ಜನ ಸೇರಿದರು',
    'ಕೊಡಗಿನಲ್ಲಿ ಭಾರಿ ಮಳೆ ಕಾರಣ ಶಾಲೆಗಳಿಗೆ ರಜೆ ಘೋಷಣೆ',
    'ಹಂಪಿ ಉತ್ಸವಕ್ಕೆ ದೇಶ ವಿದೇಶಗಳಿಂದ ಪ್ರವಾಸಿಗರ ದಂಡು',
    'ಕೆಆರ್‌ಎಸ್ ಜಲಾಶಯ ಭರ್ತಿಯಾಗಿ ರೈತರ ಮುಖದಲ್ಲಿ ಮಂದಹಾಸ',
    'ಬೆಂಗಳೂರಿನ ಟ್ರಾಫಿಕ್ ಕಡಿಮೆ ಮಾಡಲು ಹೊಸ ಫ್ಲೈಓವರ್ ಯೋಜನೆ',
    'ಉಡುಪಿ ಕೃಷ್ಣ ಮಠದಲ್ಲಿ ಪರ್ಯಾಯ ಮಹೋತ್ಸವಕ್ಕೆ ಸಿದ್ಧತೆ',
    'ಹುಬ್ಬಳ್ಳಿ ಧಾರವಾಡದಲ್ಲಿ ಹೊಸ ಐಟಿ ಪಾರ್ಕ್ ನಿರ್ಮಾಣಕ್ಕೆ ಅನುಮೋದನೆ',
  ],
  ml: [
    'കൊച്ചി മെട്രോയിൽ യാത്രക്കാരുടെ എണ്ണത്തിൽ റെക്കോർഡ് വർധന',
    'ആലപ്പുഴയിൽ നെഹ്റു ട്രോഫി വള്ളംകളി കാണാൻ വൻ ജനക്കൂട്ടം',
    'തൃശൂർ പൂരത്തിന് ഒരുക്കങ്ങൾ അവസാന ഘട്ടത്തിൽ',
    'മൂന്നാറിൽ തണുപ്പ് കൂടി, സഞ്ചാരികളുടെ തിരക്ക് വർധിച്ചു',
    'സംസ്ഥാനത്ത് അടുത്ത മൂന്ന് ദിവസം കനത്ത മഴയ്ക്ക് സാധ്യത',
    'തിരുവനന്തപുരത്ത് പുതിയ ഇലക്ട്രിക് ബസുകൾ നിരത്തിലിറങ്ങി',
    'ഓണത്തിന് മുന്നോടിയായി പച്ചക്കറി വിലയിൽ നേരിയ കുറവ്',
    'വയനാട്ടിൽ കാപ്പി കർഷകർക്ക് ഇത്തവണ മികച്ച വിളവ്',
  ],
};

const cache = {};     // lang -> { at, items }
const inflight = {};  // lang -> Promise
const recent = {};    // lang -> string[]

function decodeEntities(s) {
  return s
    .replace(/<!\[CDATA\[([\s\S]*?)\]\]>/g, '$1')
    .replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'")
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>')
    .replace(/&#(\d+);/g, (_, d) => String.fromCharCode(+d))
    .replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCharCode(parseInt(h, 16)));
}

function clean(title) {
  let t = decodeEntities(title).replace(/<[^>]+>/g, '');
  const dash = t.lastIndexOf(' - ');
  if (dash > 15) t = t.slice(0, dash); // strip " - Publisher"
  return t
    .replace(/​/g, '')            // zero-width space (keep ZWJ/ZWNJ: they matter in Malayalam/Kannada)
    .replace(/[‘’]/g, "'").replace(/[“”]/g, '"').replace(/[—–]/g, '-')
    .replace(/\s*\|\s*.*$/, '')         // "Headline | LIVE"
    .replace(/[:;,!?.]+$/, '')
    .replace(/\s+/g, ' ')
    .trim();
}

const MIN_WORDS = 5, MAX_WORDS = 12; // short enough to remember after one read

function usable(t, lang) {
  if (!t || t.length > 100) return false;
  const words = t.split(' ').length;
  if (words < MIN_WORDS || words > MAX_WORDS) return false;
  if (/\blive\b|updates|watch:|video|photos|\bLIVE\b/i.test(t)) return false;
  const letters = (t.match(/\p{L}|\p{M}/gu) || []).length;
  const native = (t.match(SCRIPT[lang]) || []).length;
  if (letters === 0 || native / letters < 0.75) return false; // mostly in the right script (allows "CM", "PM", "NCR")
  if (lang === 'en' && t === t.toUpperCase()) return false;
  return true;
}

/**
 * Headlines are often "Prefix: main part; extra detail". Use the whole headline if it is a good length,
 * otherwise the first clause that is (e.g. drops "Rice Rate:" before a Kannada headline, or the
 * "; ..." tail of a Malayalam one).
 */
function pickClause(t, lang) {
  const unquote = x => x.replace(/(^|\s)['"]+|['"]+(?=\s|$)/g, '$1').replace(/\s+/g, ' ').trim();
  t = unquote(t);
  if (lang !== 'en') t = t.replace(/^[A-Za-z0-9 .'&-]+:\s*/, ''); // English tag on an Indic headline ("Rice Rate: ...")
  // "Topic: main part\" / "Main part; extra detail" -> the first strong clause is usually the punchline.
  const first = unquote(t.split(/\s*[;:|]\s*|\s+-\s+/)[0]);
  if (first !== t && usable(first, lang)) return first;
  if (usable(t, lang)) return t;
  for (const p of t.split(/\s*[;:|]\s*|\s+-\s+|,\s+/)) {
    const c = unquote(p);
    if (usable(c, lang)) return c;
  }
  return null;
}

async function fetchText(url, ms) {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), ms);
  try {
    const res = await fetch(url, { signal: ctl.signal, headers: { 'User-Agent': 'Mozilla/5.0 BrokenTelephoneGame/1.0' } });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    return await res.text();
  } finally {
    clearTimeout(timer);
  }
}

async function refresh(lang) {
  for (const url of SOURCES[lang]) {
    try {
      const xml = await fetchText(url, 5000);
      const out = [];
      const re = /<item>[\s\S]*?<title>([\s\S]*?)<\/title>/g;
      let m;
      while ((m = re.exec(xml))) {
        const t = pickClause(clean(m[1]), lang);
        if (t && !out.includes(t)) out.push(t);
      }
      if (out.length >= 3) return out;
    } catch (e) {
      console.warn(`[news:${lang}] ${url} failed: ${e.message}`);
    }
  }
  return [];
}

/** Resolves quickly: serves cache, refreshes at most once per TTL per language, never rejects. */
async function getSentence(lang) {
  if (!LANGS.includes(lang)) lang = 'en';
  const c = cache[lang] || { at: 0, items: [] };
  if (Date.now() - c.at > NEWS_TTL_MS) {
    if (!inflight[lang]) {
      inflight[lang] = refresh(lang).then(items => {
        cache[lang] = { at: Date.now(), items: items.length ? items : c.items };
        inflight[lang] = null;
      });
    }
    await inflight[lang];
  }
  const pool = cache[lang] && cache[lang].items.length ? cache[lang].items : FALLBACK[lang];
  const used = recent[lang] || (recent[lang] = []);
  let candidates = pool.filter(s => !used.includes(s));
  if (candidates.length === 0) candidates = pool;
  const s = candidates[(Math.random() * candidates.length) | 0];
  used.push(s);
  if (used.length > 30) used.shift();
  return s;
}

module.exports = { getSentence, LANGS, usable, pickClause, FALLBACK };
