using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace BrokenTelephone
{
    /// <summary>
    /// Client controller. Server is authoritative: this class renders server state, forwards intents
    /// (begin / done / play), and drives the browser plugin. No per-frame game logic.
    /// Receives browser callbacks via SendMessage, so the GameObject name must stay <see cref="ObjectName"/>.
    ///
    /// Two languages are in play:
    ///   - UI language (<see cref="Loc.Current"/>): per player, chosen with the language button.
    ///   - Room language (<see cref="RoomMsg.lang"/>): set by the host; decides the headline region
    ///     and the speech-to-text / text-to-speech language for everyone in the room.
    /// </summary>
    public sealed class GameClient : MonoBehaviour
    {
        public const string ObjectName = "BrokenTelephoneClient";
        const string PrefName = "bt_name", PrefRoom = "bt_room";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<GameClient>() != null) return;
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            go.AddComponent<GameClient>();
        }

        // --- state (mirrors the server) ---
        string deviceId, myName, serverUrl;
        bool connected, micOk, sttOk;
        RoomMsg room;
        TurnMsg turn;
        ResultsMsg results;
        string pendingJoin;
        float reconnectAt = -1f, reconnectDelay = 1f;
        string statusKey; // last home/lobby status, re-translated on language change
        string noticeKey, noticePlayer;

        // --- local per-turn state ---
        int turnKey = -1;
        bool mediaReady, playing, played, recording, sent;
        int playingResult = -1;

        // --- UI ---
        CanvasScaler scaler;
        int lastW, lastH;
        HomeView home;
        LobbyView lobby;
        GameView game;
        ResultsView resultsView;
        LanguagePicker picker;

        // ================= lifecycle =================

        void Awake()
        {
            gameObject.name = ObjectName;
            ConfigurePerformance();
            WebBridge.Init(gameObject);
            Loc.Load(WebBridge.BrowserLang());
            WebBridge.LoadFonts();
            EnsureFonts();
            BuildUI();
            deviceId = WebBridge.DeviceId();
            serverUrl = WebBridge.ServerUrl();
            sttOk = WebBridge.SttSupported;
            myName = PlayerPrefs.GetString(PrefName, "");
            if (string.IsNullOrEmpty(myName)) myName = "Player " + deviceId.Substring(deviceId.Length - 4).ToUpperInvariant();
            home.Name.SetTextWithoutNotify(myName);
            home.Device.Text = Loc.T("device") + " " + deviceId.Substring(0, Mathf.Min(8, deviceId.Length)) + "…";
            var urlRoom = WebBridge.RoomFromUrl();
            pendingJoin = !string.IsNullOrEmpty(urlRoom) ? urlRoom : PlayerPrefs.GetString(PrefRoom, "");
            if (!string.IsNullOrEmpty(urlRoom)) home.Code.SetTextWithoutNotify(urlRoom);
            Debug.Log($"[BT] boot device={deviceId} server={serverUrl} stt={sttOk} lang={Loc.Code(Loc.Current)} pendingJoin={pendingJoin}");
            ShowScreen();
            Connect();
        }

        static void ConfigurePerformance()
        {
            // Web: let the browser pace frames (requestAnimationFrame), but only render every 2nd frame —
            // this is a static UI game, 30 fps is plenty and halves GPU/battery cost. Input is still polled every frame.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            OnDemandRendering.renderFrameInterval = 2;
            Application.runInBackground = true; // keep the socket + timers alive when the tab loses focus

            // Strip the sample scene down to: one camera that clears to a solid colour. No post, no AA, no shadows.
            var cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Bg;
            cam.cullingMask = 0;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var urp = cam.GetUniversalAdditionalCameraData();
            urp.renderPostProcessing = false;
            urp.antialiasing = AntialiasingMode.None;
            urp.renderShadows = false;
            urp.requiresDepthTexture = false;
            urp.requiresColorTexture = false;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
                DontDestroyOnLoad(es);
            }
        }

        void BuildUI()
        {
            var root = new GameObject("UI", typeof(RectTransform)) { layer = 5 };
            DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1080);
            UpdateScaler();

            var t = root.transform;
            home = new HomeView(t, OnCreateClicked, OnJoinClicked);
            home.Name.onEndEdit.AddListener(OnNameEdited);
            lobby = new LobbyView(t, () => Send("{\"t\":\"start\"}"), OnCopyClicked, LeaveRoom, OnBotsClicked);
            game = new GameView(t, OnPrimaryClicked, LeaveRoom);
            resultsView = new ResultsView(t, OnPlayResult, () => Send("{\"t\":\"lobby\"}"), LeaveRoom);
            picker = new LanguagePicker(t, OnLanguagePicked);
        }

        void Update()
        {
            // The only per-frame work: an int compare for resize, and the reconnect timer.
            if (Screen.width != lastW || Screen.height != lastH) UpdateScaler();
            if (reconnectAt > 0f && Time.unscaledTime >= reconnectAt) { reconnectAt = -1f; Connect(); }
        }

        void UpdateScaler()
        {
            lastW = Screen.width;
            lastH = Screen.height;
            // Landscape: fit height; portrait: fit width. The 1000-unit column always fits.
            scaler.matchWidthOrHeight = lastW > lastH ? 1f : 0f;
        }

        // ================= language =================

        string RoomLang => room != null && !string.IsNullOrEmpty(room.lang) ? room.lang : Loc.Code(Loc.Current);
        string SpeechTag => Loc.SpeechTags[(int)Loc.FromCode(RoomLang)];

        void OnLanguagePicked(Lang lang)
        {
            Loc.Set(lang); // re-translates every keyed label
            picker.Refresh();
            EnsureFonts();
            // The host's choice also moves the room's headlines/speech to that region (lobby/results only).
            if (IsHost && room.phase != "game") Send("{\"t\":\"setLang\",\"lang\":\"" + Loc.Code(lang) + "\"}");
            home.Device.Text = Loc.T("device") + " " + deviceId.Substring(0, Mathf.Min(8, deviceId.Length)) + "…";
            SetStatus(statusKey);
            RenderLobby();
            RenderGame();
            if (noticeKey != null) game.ShowNotice(Loc.F(noticeKey, NameOf(noticePlayer)));
            resultsView.Relocalize();
            Debug.Log("[BT] UI language -> " + Loc.Code(lang) + (IsHost ? " (room headlines follow)" : ""));
        }

        /// <summary>Fetch the bundled Kannada font only when Kannada is on screen (UI or room language).</summary>
        void EnsureFonts()
        {
            if (Loc.Current == Lang.Kn || (room != null && room.lang == "kn")) WebBridge.LoadKannadaFont();
        }

        void OnFontsLoaded(string _)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            UTextSystem.InvalidateAll(); // re-shape labels drawn before the Indic web fonts arrived
#endif
        }

        void SetStatus(string key)
        {
            statusKey = key;
            var msg = string.IsNullOrEmpty(key) ? "" : Loc.T(key);
            home.Status.Text = msg;
            if (room != null && !string.IsNullOrEmpty(key)) lobby.Hint.Text = msg;
        }

        // ================= networking =================

        void Connect()
        {
            if (room == null) SetStatus("connecting");
            WebBridge.Connect(serverUrl);
        }

        void Send(string json) { if (connected) WebBridge.Send(json); }

        // --- callbacks from the browser plugin (SendMessage) ---

        void OnNetOpen(string _)
        {
            connected = true;
            reconnectDelay = 1f;
            Send(JsonUtility.ToJson(new HelloOut { deviceId = deviceId, name = myName }));
            var code = room != null ? room.code : pendingJoin;
            pendingJoin = null;
            if (!string.IsNullOrEmpty(code))
            {
                RequestMicOnce();
                Send(JsonUtility.ToJson(new JoinOut { code = code }));
            }
            else SetStatus(sttOk ? null : "no_stt");
        }

        void OnNetClosed(string _)
        {
            connected = false;
            reconnectAt = Time.unscaledTime + reconnectDelay;
            reconnectDelay = Mathf.Min(reconnectDelay * 2f, 10f);
            SetStatus("retrying");
            if (game.Screen.Visible) game.ShowNotice(Loc.T("conn_lost"));
        }

        void OnNetMessage(string json)
        {
            var t = JsonUtility.FromJson<Envelope>(json).t;
            switch (t)
            {
                case "welcome":
                    myName = JsonUtility.FromJson<WelcomeMsg>(json).name;
                    break;
                case "error":
                    var err = JsonUtility.FromJson<ErrorMsg>(json).msg;
                    var key = err == "Room not found" ? "room_not_found" : err == "Room is full" ? "room_full"
                        : err == "Only the host can start" ? "host_only" : err == "Not enough players" ? "not_enough" : null;
                    if (key != null) SetStatus(key); else { home.Status.Text = err; lobby.Hint.Text = err; }
                    if (key == "room_not_found") { PlayerPrefs.DeleteKey(PrefRoom); room = null; ShowScreen(); }
                    break;
                case "room":
                    OnRoom(JsonUtility.FromJson<RoomMsg>(json));
                    break;
                case "turn":
                    OnTurn(JsonUtility.FromJson<TurnMsg>(json));
                    break;
                case "listenTts":
                    WebBridge.SetTurnTts(JsonUtility.FromJson<TextAtIndexMsg>(json).text, SpeechTag);
                    mediaReady = true;
                    RenderGame();
                    break;
                case "notice":
                    var n = JsonUtility.FromJson<NoticeMsg>(json);
                    WebBridge.StopAudio();
                    if (n.kind == "notEnough") { SetStatus("notice_not_enough"); break; } // room msg moves us to the lobby
                    noticeKey = n.kind == "newSentence" ? "notice_new" : "notice_noresp";
                    noticePlayer = n.playerId;
                    game.ShowNotice(Loc.F(noticeKey, NameOf(n.playerId)));
                    Debug.Log("[BT] notice: " + n.msg);
                    break;
                case "results":
                    results = JsonUtility.FromJson<ResultsMsg>(json);
                    playingResult = -1;
                    WebBridge.ClearResults();
                    resultsView.Show(results, deviceId, IsHost);
                    ShowScreen();
                    break;
                case "resultTts":
                    var r = JsonUtility.FromJson<TextAtIndexMsg>(json);
                    WebBridge.SpeakResult(r.index, r.text, SpeechTag);
                    break;
            }
        }

        void OnTurnAudioReady(string _) { mediaReady = true; RenderGame(); }

        void OnPlaybackEnded(string _)
        {
            playing = false;
            played = true;
            RenderGame();
        }

        void OnRecordingFinished(string json)
        {
            var r = JsonUtility.FromJson<RecordingResult>(json);
            recording = false;
            sent = true;
            Debug.Log($"[BT] recording sent lang={SpeechTag} voiced={r.voiced} transcript=\"{r.transcript}\"");
            RenderGame();
        }

        void OnResultPlaybackEnded(string idx)
        {
            if (int.TryParse(idx, out var i)) resultsView.SetPlaying(i, false);
            if (playingResult.ToString() == idx) playingResult = -1;
        }

        void OnMicResult(string r)
        {
            micOk = r == "ok";
            if (!micOk) SetStatus("mic_blocked");
        }

        // ================= state handlers =================

        bool IsHost => room != null && room.hostId == deviceId;
        bool MyTurn => turn != null && turn.playerId == deviceId;

        void OnRoom(RoomMsg msg)
        {
            bool phaseChanged = room == null || room.phase != msg.phase || room.lang != msg.lang;
            room = msg;
            EnsureFonts();
            PlayerPrefs.SetString(PrefRoom, room.code);
            if (room.phase != "results") results = null;
            if (room.phase == "lobby") { turn = null; noticeKey = null; game.ShowNotice(null); }
            if (phaseChanged) Debug.Log($"[BT] room {room.code} phase={room.phase} lang={room.lang} players={room.players.Length} lineup={room.lineup.Length}");
            RenderLobby();
            RenderGame();
            ShowScreen();
        }

        void OnTurn(TurnMsg msg)
        {
            turn = msg;
            int key = msg.round * 1000 + msg.index;
            if (key != turnKey)
            {
                turnKey = key;
                mediaReady = playing = played = recording = sent = false;
                WebBridge.ClearTurnMedia();
                WebBridge.StopAudio();
            }
            if (msg.played && MyTurn) played = true;
            if (msg.phase != "paused") { noticeKey = null; game.ShowNotice(null); }

            // Server opened our speaking window -> start the mic (in the room's language) for exactly that long.
            if (msg.phase == "record" && MyTurn && !recording && !sent && msg.remainingMs > 300)
            {
                recording = true;
                WebBridge.StartRecording(msg.remainingMs, SpeechTag);
            }

            switch (msg.phase)
            {
                case "read":
                case "listen":
                    game.Timer.Run(msg.remainingMs, Palette.Blue); break;
                case "record":
                    game.Timer.Run(msg.remainingMs, Palette.Bad); break;
                case "bot":
                case "paused":
                    game.Timer.Run(msg.remainingMs, Palette.Muted); break;
                default:
                    game.Timer.Hide(); break;
            }
            RenderGame();
            ShowScreen();
        }

        // ================= rendering =================

        void ShowScreen()
        {
            bool inRoom = room != null;
            bool showResults = inRoom && room.phase == "results" && results != null;
            bool showGame = inRoom && (room.phase == "game" || (room.phase == "results" && results == null));
            home.Screen.Show(!inRoom);
            lobby.Screen.Show(inRoom && room.phase == "lobby");
            game.Screen.Show(showGame);
            resultsView.Screen.Show(showResults);
        }

        void RenderLobby()
        {
            if (room == null) return;
            lobby.Code.Text = room.code;
            lobby.Players.Set(room.players, -1, deviceId, room.hostId);
            int humans = 0;
            foreach (var p in room.players) if (p.online) humans++;
            int bots = Mathf.Max(0, room.minPlayers - humans);
            bool canStart = room.bots || humans >= room.minPlayers;
            lobby.Info.Text = humans >= room.minPlayers ? Loc.F("info_ready", humans)
                : room.bots ? Loc.F("info_bots", humans, bots, room.minPlayers)
                : Loc.F("info_need", humans, room.minPlayers, room.minPlayers - humans);
            lobby.Info.Color = canStart ? Palette.Muted : Palette.Warn;
            lobby.BotsLabel.Text = Loc.T(room.bots ? "bots_on" : "bots_off");
            lobby.BotsLabel.Color = room.bots ? Palette.OnAccent : Palette.Text;
            ((Image)lobby.Bots.targetGraphic).color = room.bots ? Palette.Good : Palette.Panel2;
            lobby.Bots.interactable = IsHost; // everyone sees the setting, only the host can flip it
            lobby.Start.interactable = canStart;
            var region = Loc.Regions[(int)Loc.FromCode(RoomLang)];
            lobby.Headlines.Text = Loc.F(IsHost ? "headlines_host" : "headlines", region);
            lobby.Start.gameObject.SetActive(IsHost);
            if (string.IsNullOrEmpty(statusKey) || statusKey == "waiting_host" || statusKey == "connecting" || statusKey == "retrying")
            {
                statusKey = IsHost ? null : "waiting_host";
                lobby.Hint.Text = statusKey == null ? "" : Loc.T(statusKey);
            }
        }

        string NameOf(string id)
        {
            if (room?.lineup != null) foreach (var p in room.lineup) if (p.id == id) return p.name;
            if (room?.players != null) foreach (var p in room.players) if (p.id == id) return p.name;
            return "?";
        }

        int LineupIndexOf(string id)
        {
            if (room?.lineup != null) for (int i = 0; i < room.lineup.Length; i++) if (room.lineup[i].id == id) return i;
            return -1;
        }

        void RenderGame()
        {
            if (room == null || room.phase != "game") return;
            game.Lineup.Set(room.lineup, turn != null ? turn.index : -1, deviceId, room.hostId);
            if (turn == null) { SetCard(Loc.T("get_ready"), "", "", false, ""); return; }

            game.Header.Text = Loc.F("header", turn.round, turn.index + 1, turn.total);
            bool spectator = LineupIndexOf(deviceId) < 0;
            string who = NameOf(turn.playerId);
            string prev = turn.index > 0 && room.lineup.Length > turn.index - 1 ? room.lineup[turn.index - 1].name : "";
            string spectate = spectator ? Loc.T("spectating") : "";

            switch (turn.phase)
            {
                case "loading":
                    SetCard(Loc.T("new_round"), Loc.T("fetching"), "", false, spectate);
                    break;
                case "read":
                    if (MyTurn) SetCard(Loc.T("you_start"), turn.sentence, Loc.T("ready_start"), true, Loc.T("disappears"));
                    else SetCard(Loc.F("x_memorizing", who), Loc.T("secret"), "", false, spectate);
                    break;
                case "listen":
                    if (MyTurn)
                    {
                        if (!played)
                            SetCard(Loc.T("your_turn_listen"), Loc.F("hear_prev", prev),
                                Loc.T(playing ? "playing" : mediaReady ? "play_once" : "loading_audio"),
                                mediaReady && !playing, Loc.T("then_repeat"));
                        else
                            SetCard(Loc.T("say_heard"), Loc.T("ready_press"), Loc.T("start_speaking"), true,
                                Loc.F("you_have", room.recordMs / 1000));
                    }
                    else SetCard(Loc.F("x_listening", who), Loc.T("shh"), "", false, spectate);
                    break;
                case "record":
                    if (MyTurn)
                        SetCard(Loc.T(sent ? "sent" : "speak_now"), Loc.T(sent ? "wait_others" : "say_aloud"),
                            sent ? "" : Loc.T("im_done"), recording, micOk ? "" : Loc.T("mic_silent"));
                    else SetCard(Loc.F("x_speaking", who), "…", "", false, spectate);
                    break;
                case "bot":
                    SetCard(Loc.F("x_whispering", who), Loc.T("bot_player"), "", false, spectate);
                    break;
                case "paused":
                    SetCard(Loc.T("hold_on"), "", "", false, "");
                    break;
            }
        }

        void SetCard(string title, string body, string primary, bool primaryEnabled, string hint)
        {
            game.Title.Text = title;
            game.Body.Text = body;
            game.Hint.Text = hint;
            bool show = !string.IsNullOrEmpty(primary);
            if (game.Primary.gameObject.activeSelf != show) game.Primary.gameObject.SetActive(show);
            game.PrimaryLabel.Text = primary;
            game.Primary.interactable = primaryEnabled;
        }

        // ================= UI actions =================

        void RequestMicOnce() { if (!micOk) WebBridge.RequestMic(); }

        void OnCreateClicked()
        {
            if (!connected) { SetStatus("still_connecting"); return; }
            OnNameEdited(home.Name.text);
            RequestMicOnce();
            // The creator's UI language picks the room's headline region / speech language.
            Send("{\"t\":\"create\",\"lang\":\"" + Loc.Code(Loc.Current) + "\"}");
        }

        void OnJoinClicked()
        {
            var code = home.Code.text.Trim().ToUpperInvariant();
            if (code.Length != 4) { SetStatus("code_len"); return; }
            if (!connected) { SetStatus("still_connecting"); return; }
            OnNameEdited(home.Name.text);
            RequestMicOnce();
            Send(JsonUtility.ToJson(new JoinOut { code = code }));
        }

        void OnNameEdited(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value == myName) return;
            myName = value;
            PlayerPrefs.SetString(PrefName, myName);
            Send(JsonUtility.ToJson(new RenameOut { name = myName }));
        }

        void OnBotsClicked()
        {
            if (!IsHost || room.phase == "game") return;
            Send(room.bots ? "{\"t\":\"setBots\",\"on\":false}" : "{\"t\":\"setBots\",\"on\":true}");
        }

        void OnCopyClicked()
        {
            if (room == null) return;
            WebBridge.Copy(WebBridge.ShareLink(room.code));
            statusKey = "link_copied";
            lobby.Hint.Text = Loc.T(statusKey);
        }

        void OnPrimaryClicked()
        {
            if (turn == null || !MyTurn) return;
            switch (turn.phase)
            {
                case "read":
                    Send("{\"t\":\"begin\"}");
                    game.Primary.interactable = false;
                    break;
                case "listen":
                    if (!played)
                    {
                        if (!mediaReady || playing) return;
                        if (WebBridge.PlayTurnAudio()) { playing = true; Send("{\"t\":\"played\"}"); RenderGame(); }
                    }
                    else
                    {
                        Send("{\"t\":\"begin\"}");
                        game.Primary.interactable = false;
                    }
                    break;
                case "record":
                    if (recording) WebBridge.StopRecording();
                    break;
            }
        }

        void OnPlayResult(int index)
        {
            WebBridge.StopAudio();
            if (playingResult >= 0) resultsView.SetPlaying(playingResult, false);
            playingResult = index;
            resultsView.SetPlaying(index, true);
            if (!WebBridge.PlayResult(index)) Send(JsonUtility.ToJson(new IndexOut { t = "getAudio", index = index }));
        }

        void LeaveRoom()
        {
            Send("{\"t\":\"leave\"}");
            WebBridge.StopAudio();
            PlayerPrefs.DeleteKey(PrefRoom);
            room = null;
            turn = null;
            results = null;
            noticeKey = null;
            game.Timer.Hide();
            game.ShowNotice(null);
            SetStatus(null);
            ShowScreen();
        }
    }
}
