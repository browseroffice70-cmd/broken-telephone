using System;
using System.Collections.Generic;
using UnityEngine;

namespace BrokenTelephone
{
    public enum Lang { En = 0, Hi = 1, Kn = 2, Ml = 3 }

    /// <summary>
    /// UI strings for English / Hindi / Kannada / Malayalam. One flat table, looked up by key;
    /// array index = (int)Lang. No asset loading, no Localization package dependency.
    /// </summary>
    public static class Loc
    {
        public static readonly Lang[] All = { Lang.En, Lang.Hi, Lang.Kn, Lang.Ml };
        public static readonly string[] Codes = { "en", "hi", "kn", "ml" };
        public static readonly string[] NativeNames = { "English", "हिन्दी", "ಕನ್ನಡ", "മലയാളം" };
        /// <summary>BCP-47 tags for speech recognition / synthesis (Indian variants).</summary>
        public static readonly string[] SpeechTags = { "en-IN", "hi-IN", "kn-IN", "ml-IN" };
        /// <summary>Where the headlines come from, written in that language.</summary>
        public static readonly string[] Regions = { "India · English", "उत्तर भारत · हिन्दी", "ಕರ್ನಾಟಕ · ಕನ್ನಡ", "കേരളം · മലയാളം" };

        const string PrefKey = "bt_lang";
        public static Lang Current { get; private set; } = Lang.En;
        public static event Action Changed;

        public static string Code(Lang l) => Codes[(int)l];
        public static Lang FromCode(string code)
        {
            int i = Array.IndexOf(Codes, (code ?? "").ToLowerInvariant());
            return i >= 0 ? (Lang)i : Lang.En;
        }

        public static void Load(string browserLang)
        {
            var saved = PlayerPrefs.GetString(PrefKey, "");
            Current = FromCode(!string.IsNullOrEmpty(saved) ? saved : browserLang);
        }

        public static void Set(Lang l)
        {
            if (l == Current) return;
            Current = l;
            PlayerPrefs.SetString(PrefKey, Code(l));
            UText.RelocalizeAll();
            Changed?.Invoke();
        }

        public static string T(string key)
        {
            if (Table.TryGetValue(key, out var row)) return row[(int)Current] ?? row[0];
            return key;
        }

        public static string F(string key, object a) => string.Format(T(key), a);
        public static string F(string key, object a, object b) => string.Format(T(key), a, b);
        public static string F(string key, object a, object b, object c) => string.Format(T(key), a, b, c);

        //                                     English, Hindi, Kannada, Malayalam
        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            ["title"] = new[] { "BROKEN TELEPHONE", "टूटा टेलीफ़ोन", "ಮುರಿದ ಟೆಲಿಫೋನ್", "പൊട്ടിയ ടെലിഫോൺ" },
            ["subtitle"] = new[] {
                "Whisper a headline down the line. See what comes out the other end.",
                "एक खबर को कान-दर-कान आगे बढ़ाइए, देखिए आखिर में क्या निकलता है।",
                "ಒಂದು ಸುದ್ದಿಯನ್ನು ಕಿವಿಯಿಂದ ಕಿವಿಗೆ ದಾಟಿಸಿ. ಕೊನೆಗೆ ಏನು ಉಳಿಯುತ್ತದೆ ನೋಡಿ.",
                "ഒരു വാർത്ത ചെവിയിൽ നിന്ന് ചെവിയിലേക്ക് കൈമാറൂ. അവസാനം എന്ത് ബാക്കിയാകുമെന്ന് കാണൂ." },
            ["language"] = new[] { "Language", "भाषा", "ಭಾಷೆ", "ഭാഷ" },
            ["your_name"] = new[] { "YOUR NAME", "आपका नाम", "ನಿಮ್ಮ ಹೆಸರು", "നിങ്ങളുടെ പേര്" },
            ["name_ph"] = new[] { "Enter your name", "अपना नाम लिखें", "ನಿಮ್ಮ ಹೆಸರು ನಮೂದಿಸಿ", "നിങ്ങളുടെ പേര് നൽകൂ" },
            ["create_room"] = new[] { "CREATE ROOM", "रूम बनाएँ", "ರೂಮ್ ರಚಿಸಿ", "റൂം ഉണ്ടാക്കൂ" },
            ["or_join"] = new[] { "or join a friend's room", "या किसी दोस्त के रूम में जुड़ें", "ಅಥವಾ ಸ್ನೇಹಿತರ ರೂಮ್‌ಗೆ ಸೇರಿ", "അല്ലെങ്കിൽ സുഹൃത്തിന്റെ റൂമിൽ ചേരൂ" },
            ["code_ph"] = new[] { "CODE", "कोड", "ಕೋಡ್", "കോഡ്" },
            ["join"] = new[] { "JOIN", "जुड़ें", "ಸೇರಿ", "ചേരൂ" },
            ["device"] = new[] { "Device ID", "डिवाइस ID", "ಸಾಧನ ID", "ഉപകരണ ID" },
            ["connecting"] = new[] { "Connecting…", "कनेक्ट हो रहा है…", "ಸಂಪರ್ಕಿಸಲಾಗುತ್ತಿದೆ…", "കണക്റ്റ് ചെയ്യുന്നു…" },
            ["still_connecting"] = new[] { "Still connecting…", "अभी कनेक्ट हो रहा है…", "ಇನ್ನೂ ಸಂಪರ್ಕಿಸಲಾಗುತ್ತಿದೆ…", "ഇപ്പോഴും കണക്റ്റ് ചെയ്യുന്നു…" },
            ["retrying"] = new[] {
                "Can't reach the server. Retrying…",
                "सर्वर से संपर्क नहीं हो पा रहा। फिर से कोशिश कर रहे हैं…",
                "ಸರ್ವರ್ ಸಿಗುತ್ತಿಲ್ಲ. ಮತ್ತೆ ಪ್ರಯತ್ನಿಸಲಾಗುತ್ತಿದೆ…",
                "സെർവറിൽ എത്താനാകുന്നില്ല. വീണ്ടും ശ്രമിക്കുന്നു…" },
            ["no_stt"] = new[] {
                "Heads up: this browser has no speech-to-text. Chrome, Edge or Safari work best.",
                "ध्यान दें: इस ब्राउज़र में आवाज़ पहचान नहीं है। Chrome, Edge या Safari बेहतर हैं।",
                "ಗಮನಿಸಿ: ಈ ಬ್ರೌಸರ್‌ನಲ್ಲಿ ಧ್ವನಿ ಗುರುತಿಸುವಿಕೆ ಇಲ್ಲ. Chrome, Edge ಅಥವಾ Safari ಬಳಸಿ.",
                "ശ്രദ്ധിക്കുക: ഈ ബ്രൗസറിൽ ശബ്ദം തിരിച്ചറിയൽ ഇല്ല. Chrome, Edge അല്ലെങ്കിൽ Safari ഉപയോഗിക്കൂ." },
            ["code_len"] = new[] { "Room codes are 4 letters.", "रूम कोड 4 अक्षरों का होता है।", "ರೂಮ್ ಕೋಡ್ 4 ಅಕ್ಷರಗಳದ್ದು.", "റൂം കോഡിൽ 4 അക്ഷരങ്ങളുണ്ട്." },
            ["mic_blocked"] = new[] {
                "Microphone blocked. Allow mic access in the address bar, or you'll be skipped on your turn.",
                "माइक्रोफ़ोन ब्लॉक है। एड्रेस बार में माइक की अनुमति दें, वरना आपकी बारी छूट जाएगी।",
                "ಮೈಕ್ರೋಫೋನ್ ನಿರ್ಬಂಧಿಸಲಾಗಿದೆ. ವಿಳಾಸ ಪಟ್ಟಿಯಲ್ಲಿ ಮೈಕ್ ಅನುಮತಿ ನೀಡಿ, ಇಲ್ಲದಿದ್ದರೆ ನಿಮ್ಮ ಸರದಿ ತಪ್ಪುತ್ತದೆ.",
                "മൈക്രോഫോൺ തടഞ്ഞിരിക്കുന്നു. അഡ്രസ് ബാറിൽ മൈക്ക് അനുമതി നൽകൂ, അല്ലെങ്കിൽ നിങ്ങളുടെ ഊഴം നഷ്ടമാകും." },
            ["room_not_found"] = new[] { "Room not found", "रूम नहीं मिला", "ರೂಮ್ ಸಿಗಲಿಲ್ಲ", "റൂം കണ്ടെത്തിയില്ല" },
            ["room_full"] = new[] { "Room is full", "रूम भर गया है", "ರೂಮ್ ತುಂಬಿದೆ", "റൂം നിറഞ്ഞു" },
            ["host_only"] = new[] { "Only the host can start", "सिर्फ़ होस्ट शुरू कर सकता है", "ಹೋಸ್ಟ್ ಮಾತ್ರ ಪ್ರಾರಂಭಿಸಬಹುದು", "ഹോസ്റ്റിന് മാത്രമേ തുടങ്ങാനാകൂ" },

            ["room_code"] = new[] { "ROOM CODE", "रूम कोड", "ರೂಮ್ ಕೋಡ್", "റൂം കോഡ്" },
            ["copy_link"] = new[] { "COPY INVITE LINK", "आमंत्रण लिंक कॉपी करें", "ಆಹ್ವಾನ ಲಿಂಕ್ ನಕಲಿಸಿ", "ക്ഷണ ലിങ്ക് പകർത്തൂ" },
            ["link_copied"] = new[] { "Invite link copied!", "आमंत्रण लिंक कॉपी हो गया!", "ಆಹ್ವಾನ ಲಿಂಕ್ ನಕಲಿಸಲಾಗಿದೆ!", "ക്ഷണ ലിങ്ക് പകർത്തി!" },
            ["players"] = new[] { "PLAYERS", "खिलाड़ी", "ಆಟಗಾರರು", "കളിക്കാർ" },
            ["info_ready"] = new[] { "{0} players here. Ready!", "{0} खिलाड़ी मौजूद। तैयार!", "{0} ಆಟಗಾರರು ಇದ್ದಾರೆ. ಸಿದ್ಧ!", "{0} കളിക്കാർ ഉണ്ട്. തയ്യാർ!" },
            ["headlines"] = new[] { "Headlines: {0}", "खबरें: {0}", "ಸುದ್ದಿಗಳು: {0}", "വാർത്തകൾ: {0}" },
            ["headlines_host"] = new[] {
                "Headlines: {0}  (change with the language button)",
                "खबरें: {0}  (भाषा बटन से बदलें)",
                "ಸುದ್ದಿಗಳು: {0}  (ಭಾಷೆ ಬಟನ್‌ನಿಂದ ಬದಲಿಸಿ)",
                "വാർത്തകൾ: {0}  (ഭാഷാ ബട്ടൺ ഉപയോഗിച്ച് മാറ്റൂ)" },
            ["info_need"] = new[] {
                "{0} / {1} players · need {2} more",
                "{0} / {1} खिलाड़ी · {2} और चाहिए",
                "{0} / {1} ಆಟಗಾರರು · ಇನ್ನೂ {2} ಬೇಕು",
                "{0} / {1} കളിക്കാർ · ഇനിയും {2} പേർ വേണം" },
            ["not_enough"] = new[] {
                "Not enough players to start. Invite more friends.",
                "शुरू करने के लिए खिलाड़ी कम हैं। और दोस्तों को बुलाएँ।",
                "ಪ್ರಾರಂಭಿಸಲು ಆಟಗಾರರು ಸಾಲದು. ಇನ್ನಷ್ಟು ಸ್ನೇಹಿತರನ್ನು ಆಹ್ವಾನಿಸಿ.",
                "തുടങ്ങാൻ ആവശ്യത്തിന് കളിക്കാരില്ല. കൂടുതൽ സുഹൃത്തുക്കളെ ക്ഷണിക്കൂ." },
            ["notice_not_enough"] = new[] {
                "Not enough players to continue. Back to the lobby.",
                "आगे खेलने के लिए खिलाड़ी कम हैं। लॉबी में वापस।",
                "ಮುಂದುವರಿಸಲು ಆಟಗಾರರು ಸಾಲದು. ಲಾಬಿಗೆ ಹಿಂತಿರುಗಲಾಗುತ್ತಿದೆ.",
                "തുടരാൻ ആവശ്യത്തിന് കളിക്കാരില്ല. ലോബിയിലേക്ക് മടങ്ങുന്നു." },
            ["start_game"] = new[] { "START GAME", "खेल शुरू करें", "ಆಟ ಪ್ರಾರಂಭಿಸಿ", "കളി തുടങ്ങൂ" },
            ["leave"] = new[] { "LEAVE", "छोड़ें", "ಹೊರಡಿ", "പുറത്തുപോകൂ" },
            ["waiting_host"] = new[] { "Waiting for the host to start…", "होस्ट के शुरू करने का इंतज़ार…", "ಹೋಸ್ಟ್ ಪ್ರಾರಂಭಿಸಲು ಕಾಯಲಾಗುತ್ತಿದೆ…", "ഹോസ്റ്റ് തുടങ്ങുന്നതിനായി കാത്തിരിക്കുന്നു…" },

            ["header"] = new[] { "ROUND {0}  ·  TURN {1} / {2}", "राउंड {0}  ·  बारी {1} / {2}", "ಸುತ್ತು {0}  ·  ಸರದಿ {1} / {2}", "റൗണ്ട് {0}  ·  ഊഴം {1} / {2}" },
            ["get_ready"] = new[] { "GET READY", "तैयार हो जाइए", "ಸಿದ್ಧರಾಗಿ", "തയ്യാറാകൂ" },
            ["new_round"] = new[] { "NEW ROUND", "नया राउंड", "ಹೊಸ ಸುತ್ತು", "പുതിയ റൗണ്ട്" },
            ["fetching"] = new[] { "Grabbing a trending headline…", "ताज़ा खबर लाई जा रही है…", "ಹೊಸ ಸುದ್ದಿ ತರಲಾಗುತ್ತಿದೆ…", "പുതിയ വാർത്ത കൊണ്ടുവരുന്നു…" },
            ["you_start"] = new[] { "YOU START! MEMORIZE THIS", "आप शुरू करेंगे! इसे याद करें", "ನೀವು ಪ್ರಾರಂಭಿಸಿ! ಇದನ್ನು ನೆನಪಿಡಿ", "നിങ്ങൾ തുടങ്ങൂ! ഇത് ഓർത്തുവയ്ക്കൂ" },
            ["ready_start"] = new[] { "I'M READY - START SPEAKING", "मैं तैयार हूँ - बोलना शुरू करें", "ನಾನು ಸಿದ್ಧ - ಮಾತನಾಡಲು ಪ್ರಾರಂಭಿಸಿ", "ഞാൻ തയ്യാർ - സംസാരിച്ചു തുടങ്ങൂ" },
            ["disappears"] = new[] {
                "The sentence disappears when you press start.",
                "शुरू दबाते ही वाक्य गायब हो जाएगा।",
                "ಪ್ರಾರಂಭ ಒತ್ತಿದಾಗ ವಾಕ್ಯ ಮಾಯವಾಗುತ್ತದೆ.",
                "തുടങ്ങുക അമർത്തുമ്പോൾ വാചകം മാഞ്ഞുപോകും." },
            ["x_memorizing"] = new[] { "{0} IS MEMORIZING", "{0} याद कर रहे हैं", "{0} ನೆನಪಿಟ್ಟುಕೊಳ್ಳುತ್ತಿದ್ದಾರೆ", "{0} ഓർത്തുവയ്ക്കുന്നു" },
            ["secret"] = new[] {
                "A secret headline is being memorized…",
                "एक गुप्त खबर याद की जा रही है…",
                "ಒಂದು ರಹಸ್ಯ ಸುದ್ದಿಯನ್ನು ನೆನಪಿಟ್ಟುಕೊಳ್ಳಲಾಗುತ್ತಿದೆ…",
                "ഒരു രഹസ്യ വാർത്ത ഓർത്തുവയ്ക്കുന്നു…" },
            ["your_turn_listen"] = new[] { "YOUR TURN - LISTEN CAREFULLY", "आपकी बारी - ध्यान से सुनें", "ನಿಮ್ಮ ಸರದಿ - ಗಮನವಿಟ್ಟು ಕೇಳಿ", "നിങ്ങളുടെ ഊഴം - ശ്രദ്ധിച്ചു കേൾക്കൂ" },
            ["hear_prev"] = new[] {
                "Hear what {0} said. It plays only ONCE.",
                "सुनिए {0} ने क्या कहा। यह सिर्फ़ एक बार चलेगा।",
                "{0} ಏನು ಹೇಳಿದರು ಕೇಳಿ. ಇದು ಒಂದೇ ಬಾರಿ ಪ್ಲೇ ಆಗುತ್ತದೆ.",
                "{0} എന്താണ് പറഞ്ഞതെന്ന് കേൾക്കൂ. ഇത് ഒരു തവണ മാത്രം പ്ലേ ചെയ്യും." },
            ["playing"] = new[] { "PLAYING…", "चल रहा है…", "ಪ್ಲೇ ಆಗುತ್ತಿದೆ…", "പ്ലേ ചെയ്യുന്നു…" },
            ["play_once"] = new[] { "PLAY (ONCE)", "चलाएँ (एक बार)", "ಪ್ಲೇ (ಒಂದೇ ಬಾರಿ)", "പ്ലേ (ഒരു തവണ)" },
            ["loading_audio"] = new[] { "LOADING AUDIO…", "ऑडियो लोड हो रहा है…", "ಆಡಿಯೋ ಲೋಡ್ ಆಗುತ್ತಿದೆ…", "ഓഡിയോ ലോഡ് ചെയ്യുന്നു…" },
            ["then_repeat"] = new[] {
                "Then repeat it as best you can.",
                "फिर जितना हो सके उतना सही दोहराइए।",
                "ನಂತರ ಸಾಧ್ಯವಾದಷ್ಟು ಸರಿಯಾಗಿ ಪುನರಾವರ್ತಿಸಿ.",
                "പിന്നെ കഴിയുന്നത്ര കൃത്യമായി ആവർത്തിക്കൂ." },
            ["say_heard"] = new[] { "NOW SAY WHAT YOU HEARD", "अब जो सुना वो बोलिए", "ಈಗ ನೀವು ಕೇಳಿದ್ದನ್ನು ಹೇಳಿ", "ഇനി കേട്ടത് പറയൂ" },
            ["ready_press"] = new[] {
                "Ready? Press start and repeat it out loud.",
                "तैयार? शुरू दबाइए और ज़ोर से दोहराइए।",
                "ಸಿದ್ಧರೇ? ಪ್ರಾರಂಭ ಒತ್ತಿ ಜೋರಾಗಿ ಹೇಳಿ.",
                "തയ്യാറാണോ? തുടങ്ങുക അമർത്തി ഉറക്കെ പറയൂ." },
            ["start_speaking"] = new[] { "START SPEAKING", "बोलना शुरू करें", "ಮಾತನಾಡಲು ಪ್ರಾರಂಭಿಸಿ", "സംസാരിച്ചു തുടങ്ങൂ" },
            ["you_have"] = new[] {
                "You have {0} seconds. No retries!",
                "आपके पास {0} सेकंड हैं। दोबारा मौका नहीं!",
                "ನಿಮಗೆ {0} ಸೆಕೆಂಡುಗಳಿವೆ. ಮರುಪ್ರಯತ್ನವಿಲ್ಲ!",
                "നിങ്ങൾക്ക് {0} സെക്കൻഡ് ഉണ്ട്. വീണ്ടും ശ്രമിക്കാനാവില്ല!" },
            ["x_listening"] = new[] { "{0} IS LISTENING", "{0} सुन रहे हैं", "{0} ಕೇಳುತ್ತಿದ್ದಾರೆ", "{0} കേൾക്കുന്നു" },
            ["shh"] = new[] { "Shhh…", "श्श्श…", "ಶ್ಶ್ಶ್…", "ശ്ശ്ശ്…" },
            ["sent"] = new[] { "SENT!", "भेज दिया!", "ಕಳುಹಿಸಲಾಗಿದೆ!", "അയച്ചു!" },
            ["speak_now"] = new[] { "SPEAK NOW!", "अब बोलिए!", "ಈಗ ಮಾತನಾಡಿ!", "ഇപ്പോൾ സംസാരിക്കൂ!" },
            ["wait_others"] = new[] { "Nice. Now wait for the others.", "बढ़िया। अब बाकी लोगों का इंतज़ार करें।", "ಚೆನ್ನಾಗಿದೆ. ಈಗ ಇತರರಿಗಾಗಿ ಕಾಯಿರಿ.", "കൊള്ളാം. ഇനി മറ്റുള്ളവർക്കായി കാത്തിരിക്കൂ." },
            ["say_aloud"] = new[] { "Say the sentence out loud.", "वाक्य ज़ोर से बोलिए।", "ವಾಕ್ಯವನ್ನು ಜೋರಾಗಿ ಹೇಳಿ.", "വാചകം ഉറക്കെ പറയൂ." },
            ["im_done"] = new[] { "I'M DONE", "हो गया", "ಮುಗಿಯಿತು", "കഴിഞ്ഞു" },
            ["mic_silent"] = new[] {
                "Microphone is blocked: you'll be marked silent.",
                "माइक ब्लॉक है: आपको चुप माना जाएगा।",
                "ಮೈಕ್ ನಿರ್ಬಂಧಿಸಲಾಗಿದೆ: ನಿಮ್ಮನ್ನು ಮೌನ ಎಂದು ಗುರುತಿಸಲಾಗುತ್ತದೆ.",
                "മൈക്ക് തടഞ്ഞിരിക്കുന്നു: നിങ്ങളെ നിശബ്ദനായി കണക്കാക്കും." },
            ["x_speaking"] = new[] { "{0} IS SPEAKING", "{0} बोल रहे हैं", "{0} ಮಾತನಾಡುತ್ತಿದ್ದಾರೆ", "{0} സംസാരിക്കുന്നു" },
            ["hold_on"] = new[] { "HOLD ON", "रुकिए", "ಸ್ವಲ್ಪ ತಡೆಯಿರಿ", "ഒന്ന് നിൽക്കൂ" },
            ["spectating"] = new[] {
                "You're watching this round. You'll be in the next one.",
                "आप यह राउंड देख रहे हैं। अगले राउंड में आप खेलेंगे।",
                "ನೀವು ಈ ಸುತ್ತನ್ನು ವೀಕ್ಷಿಸುತ್ತಿದ್ದೀರಿ. ಮುಂದಿನ ಸುತ್ತಿನಲ್ಲಿ ಆಡುತ್ತೀರಿ.",
                "നിങ്ങൾ ഈ റൗണ്ട് കാണുകയാണ്. അടുത്ത റൗണ്ടിൽ കളിക്കാം." },
            ["leave_game"] = new[] { "LEAVE GAME", "खेल छोड़ें", "ಆಟ ಬಿಡಿ", "കളി വിടൂ" },
            ["notice_new"] = new[] {
                "{0} didn't say anything. Starting again with a new sentence.",
                "{0} ने कुछ नहीं बोला। नए वाक्य के साथ फिर से शुरू।",
                "{0} ಏನೂ ಹೇಳಲಿಲ್ಲ. ಹೊಸ ವಾಕ್ಯದೊಂದಿಗೆ ಮತ್ತೆ ಪ್ರಾರಂಭ.",
                "{0} ഒന്നും പറഞ്ഞില്ല. പുതിയ വാചകവുമായി വീണ്ടും തുടങ്ങുന്നു." },
            ["notice_noresp"] = new[] {
                "{0} is not responding. Restarting from the beginning with a shuffled lineup.",
                "{0} जवाब नहीं दे रहे। नए क्रम के साथ शुरू से फिर शुरू।",
                "{0} ಪ್ರತಿಕ್ರಿಯಿಸುತ್ತಿಲ್ಲ. ಹೊಸ ಕ್ರಮದೊಂದಿಗೆ ಮೊದಲಿನಿಂದ ಮತ್ತೆ ಪ್ರಾರಂಭ.",
                "{0} പ്രതികരിക്കുന്നില്ല. പുതിയ ക്രമത്തിൽ ആദ്യം മുതൽ വീണ്ടും തുടങ്ങുന്നു." },
            ["conn_lost"] = new[] { "Connection lost. Reconnecting…", "कनेक्शन टूट गया। फिर से जुड़ रहे हैं…", "ಸಂಪರ್ಕ ಕಡಿತಗೊಂಡಿದೆ. ಮರುಸಂಪರ್ಕಿಸಲಾಗುತ್ತಿದೆ…", "കണക്ഷൻ നഷ്ടപ്പെട്ടു. വീണ്ടും ബന്ധിപ്പിക്കുന്നു…" },

            ["results_title"] = new[] { "THE LINE IS BROKEN!", "लाइन टूट गई!", "ಸಾಲು ಮುರಿದಿದೆ!", "വരി പൊട്ടി!" },
            ["survived"] = new[] {
                "{0}% of the original survived the line",
                "मूल वाक्य का {0}% अंत तक बचा",
                "ಮೂಲ ವಾಕ್ಯದ {0}% ಕೊನೆಯವರೆಗೆ ಉಳಿಯಿತು",
                "യഥാർത്ഥ വാചകത്തിന്റെ {0}% അവസാനം വരെ നിലനിന്നു" },
            ["original"] = new[] { "ORIGINAL HEADLINE", "मूल खबर", "ಮೂಲ ಸುದ್ದಿ", "യഥാർത്ഥ വാർത്ത" },
            ["final"] = new[] { "WHAT CAME OUT THE OTHER END", "आखिर में क्या निकला", "ಕೊನೆಗೆ ಏನು ಬಂತು", "അവസാനം എന്ത് പുറത്തുവന്നു" },
            ["rank_title"] = new[] { "PERCEIVING SKILL RANKING", "सुनने की क्षमता रैंकिंग", "ಗ್ರಹಿಕೆ ಕೌಶಲ ಶ್ರೇಯಾಂಕ", "ഗ്രഹണശേഷി റാങ്കിംഗ്" },
            ["rank_help"] = new[] {
                "Score = how closely each player repeated what they heard (0-100).",
                "स्कोर = हर खिलाड़ी ने सुनी बात कितनी सही दोहराई (0-100)।",
                "ಸ್ಕೋರ್ = ಪ್ರತಿ ಆಟಗಾರ ಕೇಳಿದ್ದನ್ನು ಎಷ್ಟು ನಿಖರವಾಗಿ ಪುನರಾವರ್ತಿಸಿದರು (0-100).",
                "സ്കോർ = ഓരോ കളിക്കാരനും കേട്ടത് എത്ര കൃത്യമായി ആവർത്തിച്ചു (0-100)." },
            ["play_again"] = new[] { "PLAY AGAIN", "फिर से खेलें", "ಮತ್ತೆ ಆಡಿ", "വീണ്ടും കളിക്കൂ" },
            ["play"] = new[] { "PLAY", "चलाएँ", "ಪ್ಲೇ", "പ്ലേ" },
            ["no_transcript"] = new[] { "(no transcript)", "(कोई टेक्स्ट नहीं)", "(ಪಠ್ಯ ಲಭ್ಯವಿಲ್ಲ)", "(ടെക്സ്റ്റ് ലഭ്യമല്ല)" },

            ["tag_you"] = new[] { "YOU", "आप", "ನೀವು", "നിങ്ങൾ" },
            ["tag_host"] = new[] { "HOST", "होस्ट", "ಹೋಸ್ಟ್", "ഹോസ്റ്റ്" },
            ["tag_offline"] = new[] { "OFFLINE", "ऑफ़लाइन", "ಆಫ್‌ಲೈನ್", "ഓഫ്‌ലൈൻ" },
            ["tag_done"] = new[] { "DONE", "हो गया", "ಮುಗಿದಿದೆ", "കഴിഞ്ഞു" },
        };
    }
}
