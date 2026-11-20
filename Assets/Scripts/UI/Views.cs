using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BrokenTelephone
{
    /// <summary>A screen = its own nested Canvas. Hiding disables the canvas (no SetActive churn, no rebuild of other screens).</summary>
    public sealed class UIScreen
    {
        public readonly RectTransform Root;
        public readonly RectTransform Column;
        readonly Canvas canvas;
        readonly GraphicRaycaster raycaster;

        public UIScreen(Transform parent, string name, float spacing = 18)
        {
            Root = UIKit.Rect(parent, name).Stretch();
            canvas = UIKit.SubCanvas(Root, true);
            raycaster = Root.GetComponent<GraphicRaycaster>();
            Column = UIKit.Rect(Root, "Column");
            Column.anchorMin = new Vector2(0.5f, 0f);
            Column.anchorMax = new Vector2(0.5f, 1f);
            Column.sizeDelta = new Vector2(1000f, -130f);
            Column.anchoredPosition = new Vector2(0f, -35f); // 100 top (room for the language button), 30 bottom
            UIKit.VStack(Column, spacing);
            Show(false);
        }

        public bool Visible => canvas.enabled;
        public void Show(bool on)
        {
            canvas.enabled = on;
            raycaster.enabled = on;
        }
    }

    /// <summary>Countdown bar on its own canvas: per-frame changes re-batch only these 3 graphics.</summary>
    public sealed class TimerView : MonoBehaviour
    {
        RectTransform fill;
        Image fillImage;
        Text label; // digits only -> plain uGUI Text, no browser round-trip every second
        Canvas canvas;
        float endTime, duration;
        int lastSecs = -1;
        float lastFrac = -1f;

        static readonly string[] SecStrings = BuildSecs();
        static string[] BuildSecs()
        {
            var a = new string[121];
            for (int i = 0; i < a.Length; i++) a[i] = i + "s";
            return a;
        }

        public static TimerView Create(Transform parent)
        {
            var bg = UIKit.Panel(parent, "Timer", Palette.Panel2);
            UIKit.Layout(bg, 56);
            var tv = bg.gameObject.AddComponent<TimerView>();
            tv.canvas = UIKit.SubCanvas(bg, false);
            tv.fillImage = UIKit.Panel(bg.transform, "Fill", Palette.Accent);
            tv.fill = tv.fillImage.rectTransform;
            tv.fill.anchorMin = Vector2.zero;
            tv.fill.anchorMax = Vector2.one;
            tv.fill.offsetMin = tv.fill.offsetMax = Vector2.zero;
            tv.label = UIKit.LegacyLabel(bg.transform, "Secs", "", 30, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            tv.label.rectTransform.Stretch();
            tv.Hide();
            return tv;
        }

        public void Run(int ms, Color color)
        {
            duration = Mathf.Max(0.1f, ms / 1000f);
            endTime = Time.unscaledTime + duration;
            fillImage.color = color;
            lastSecs = -1;
            lastFrac = -1f;
            canvas.enabled = true;
            enabled = true;
            Update();
        }

        public void Hide()
        {
            canvas.enabled = false;
            enabled = false;
        }

        void Update()
        {
            float rem = Mathf.Max(0f, endTime - Time.unscaledTime);
            float frac = rem / duration;
            if (Mathf.Abs(frac - lastFrac) > 0.002f)
            {
                lastFrac = frac;
                fill.anchorMax = new Vector2(frac, 1f);
            }
            int secs = Mathf.CeilToInt(rem);
            if (secs != lastSecs)
            {
                lastSecs = secs;
                label.text = SecStrings[Mathf.Clamp(secs, 0, SecStrings.Length - 1)];
            }
            if (rem <= 0f) enabled = false; // stop ticking; bar stays empty until next Run/Hide
        }
    }

    /// <summary>Player lineup chips (pooled; grows only when the lineup grows).</summary>
    public sealed class LineupView
    {
        const int Columns = 5;
        const float CellH = 88f, Gap = 8f;

        sealed class Chip
        {
            public GameObject go;
            public Image border, bg;
            public UText num, name, tag;
        }

        readonly RectTransform grid;
        readonly LayoutElement layout;
        readonly List<Chip> chips = new List<Chip>(20);

        public LineupView(Transform parent)
        {
            grid = UIKit.Rect(parent, "Lineup");
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2((1000f - Gap * (Columns - 1)) / Columns, CellH);
            g.spacing = new Vector2(Gap, Gap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = Columns;
            g.childAlignment = TextAnchor.UpperCenter;
            layout = UIKit.Layout(grid, CellH);
        }

        Chip Get(int i)
        {
            while (chips.Count <= i)
            {
                var c = new Chip();
                c.border = UIKit.Panel(grid, "Chip", Palette.Panel);
                c.go = c.border.gameObject;
                c.bg = UIKit.Panel(c.border.transform, "Bg", Palette.Panel2);
                c.bg.rectTransform.Stretch(4, 4, 4, 4);
                var mask = c.bg.gameObject.AddComponent<RectMask2D>(); // clip long names cheaply (no stencil)
                mask.padding = new Vector4(6, 0, 6, 0);
                c.num = UIKit.Label(c.bg.transform, "Num", "", 22, Palette.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
                c.num.Wrap = false;
                c.num.rectTransform.Stretch(12, 8, 12, 8);
                c.tag = UIKit.Label(c.bg.transform, "Tag", "", 18, Palette.Muted, TextAnchor.UpperRight, FontStyle.Bold);
                c.tag.Wrap = false;
                c.tag.rectTransform.Stretch(12, 9, 12, 8);
                c.name = UIKit.Label(c.bg.transform, "Name", "", 25, Palette.Text, TextAnchor.LowerLeft, FontStyle.Bold);
                c.name.Wrap = false;
                c.name.rectTransform.Stretch(12, 8, 12, 9);
                chips.Add(c);
            }
            return chips[i];
        }

        /// <param name="current">index of the active player, or -1 (lobby)</param>
        public void Set(PlayerInfo[] players, int current, string myId, string hostId)
        {
            int n = players?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                var p = players[i];
                var c = Get(i);
                if (!c.go.activeSelf) c.go.SetActive(true);
                bool me = p.id == myId;
                bool isCurrent = i == current;
                bool done = current >= 0 && i < current;

                c.num.Text = current >= 0 ? UIKit.Num(i + 1) : "";
                c.name.Text = p.name;
                c.tag.Text = !p.online ? Loc.T("tag_offline") : me ? Loc.T("tag_you") : p.bot ? Loc.T("tag_bot")
                    : p.id == hostId ? Loc.T("tag_host") : done ? Loc.T("tag_done") : "";
                c.border.color = me ? Palette.Blue : isCurrent ? Palette.Accent : Palette.Panel;
                c.bg.color = isCurrent ? Palette.Accent : done ? Palette.Panel : Palette.Panel2;
                c.name.Color = isCurrent ? Palette.OnAccent : done || !p.online ? Palette.Muted : Palette.Text;
                c.num.Color = isCurrent ? Palette.OnAccent : Palette.Muted;
                c.tag.Color = isCurrent ? Palette.OnAccent : me ? Palette.Blue : !p.online ? Palette.Bad : Palette.Muted;
            }
            for (int i = n; i < chips.Count; i++) if (chips[i].go.activeSelf) chips[i].go.SetActive(false);
            int rows = Mathf.Max(1, (n + Columns - 1) / Columns);
            layout.preferredHeight = rows * CellH + (rows - 1) * Gap;
        }
    }

    /// <summary>Top-right language button + dropdown. Lives on its own top-most canvas, visible on every screen.</summary>
    public sealed class LanguagePicker
    {
        readonly UText current;
        readonly GameObject menu;
        readonly Image[] optionBgs = new Image[4];

        public LanguagePicker(Transform root, Action<Lang> onPick)
        {
            var layer = UIKit.Rect(root, "LanguageLayer").Stretch();
            var cv = UIKit.SubCanvas(layer, true);
            cv.overrideSorting = true;
            cv.sortingOrder = 100;

            var btn = UIKit.Button(layer, "LangButton", "", Palette.Panel2, Palette.Text, 28, null, out current);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-20f, -20f);
            rt.sizeDelta = new Vector2(260f, 68f);

            menu = UIKit.Rect(layer, "Menu").Stretch().gameObject;
            // Full-screen dim catcher: tapping outside closes the menu.
            var catcher = UIKit.Rect(menu.transform, "Catcher").Stretch().gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0.35f);
            catcher.gameObject.AddComponent<Button>().onClick.AddListener(() => menu.SetActive(false));

            var panel = UIKit.Panel(menu.transform, "Panel", Palette.Panel, true);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-20f, -98f);
            prt.sizeDelta = new Vector2(340f, 4 * 84f + 70f);
            UIKit.VStack(panel, 8, 14);
            UIKit.Layout(UIKit.LabelKey(panel.transform, "Title", "language", 22, Palette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold), 34);
            for (int i = 0; i < Loc.All.Length; i++)
            {
                var lang = Loc.All[i];
                var b = UIKit.Button(panel.transform, "Opt" + i, Loc.NativeNames[i], Palette.Panel2, Palette.Text, 32,
                    () => { menu.SetActive(false); onPick(lang); }, out _);
                UIKit.Layout(b, 76);
                optionBgs[i] = (Image)b.targetGraphic;
            }
            menu.SetActive(false);
            btn.onClick.AddListener(() => menu.SetActive(!menu.activeSelf));
            Refresh();
        }

        public void Refresh()
        {
            int c = (int)Loc.Current;
            current.Text = "A / अ   " + Loc.NativeNames[c];
            for (int i = 0; i < optionBgs.Length; i++) optionBgs[i].color = i == c ? Palette.Accent : Palette.Panel2;
        }
    }

    public sealed class HomeView
    {
        public readonly UIScreen Screen;
        public readonly InputField Name, Code;
        public readonly Button Create, Join;
        public readonly UText Status, Device;

        public HomeView(Transform root, Action onCreate, Action onJoin)
        {
            Screen = new UIScreen(root, "Home", 22);
            var col = Screen.Column;
            UIKit.Layout(UIKit.Rect(col, "Spacer"), -1, -1, 1);
            UIKit.LabelKey(col, "Title", "title", 80, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Layout(UIKit.LabelKey(col, "Sub", "subtitle", 30, Palette.Muted), -1, -1, -1, -1, 60);

            UIKit.Layout(UIKit.LabelKey(col, "NameLbl", "your_name", 24, Palette.Muted, TextAnchor.LowerLeft, FontStyle.Bold), 44);
            Name = UIKit.Input(col, "Name", "name_ph", 38, 18);
            UIKit.Layout(Name, 96);

            Create = UIKit.ButtonKey(col, "Create", "create_room", Palette.Accent, Palette.OnAccent, 40, () => onCreate(), out _);
            UIKit.Layout(Create, 120);

            UIKit.Layout(UIKit.LabelKey(col, "Or", "or_join", 26, Palette.Muted), 54);
            var row = UIKit.Rect(col, "JoinRow");
            UIKit.HStack(row, 16);
            UIKit.Layout(row, 104);
            Code = UIKit.Input(row, "Code", "code_ph", 48, 4);
            Code.onValidateInput = (text, i, ch) => char.IsLetter(ch) && ch < 128 ? char.ToUpperInvariant(ch) : '\0';
            UIKit.Layout(Code, -1, 0, -1, 1);
            Join = UIKit.ButtonKey(row, "Join", "join", Palette.Blue, Color.white, 40, () => onJoin(), out _);
            UIKit.Layout(Join, -1, 300);

            Status = UIKit.Label(col, "Status", "", 28, Palette.Warn);
            UIKit.Layout(Status, -1, -1, -1, -1, 110);
            UIKit.Layout(UIKit.Rect(col, "Spacer2"), -1, -1, 1);
            Device = UIKit.Label(col, "Device", "", 20, Palette.Muted);
            UIKit.Layout(Device, 40);
        }
    }

    public sealed class LobbyView
    {
        public readonly UIScreen Screen;
        public readonly UText Code, Info, Hint, Headlines;
        public readonly LineupView Players;
        public readonly Button Start, Copy, Leave, Bots;
        public readonly UText BotsLabel;

        public LobbyView(Transform root, Action onStart, Action onCopy, Action onLeave, Action onBots)
        {
            Screen = new UIScreen(root, "Lobby", 16);
            var col = Screen.Column;
            UIKit.Layout(UIKit.LabelKey(col, "RoomLbl", "room_code", 26, Palette.Muted, TextAnchor.LowerCenter, FontStyle.Bold), 50);
            Code = UIKit.Label(col, "Code", "----", 120, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            Code.Wrap = false;
            UIKit.Layout(Code, 140);
            Copy = UIKit.ButtonKey(col, "Copy", "copy_link", Palette.Panel2, Palette.Text, 28, () => onCopy(), out _);
            UIKit.Layout(Copy, 76);
            Headlines = UIKit.Label(col, "Headlines", "", 26, Palette.Good);
            UIKit.Layout(Headlines, -1, -1, -1, -1, 50);
            UIKit.Layout(UIKit.LabelKey(col, "PlayersLbl", "players", 26, Palette.Muted, TextAnchor.LowerLeft, FontStyle.Bold), 46);
            Players = new LineupView(col);
            Info = UIKit.Label(col, "Info", "", 28, Palette.Muted);
            UIKit.Layout(Info, -1, -1, -1, -1, 70);
            // Optional bots: host-only toggle, only shown before the game starts.
            Bots = UIKit.Button(col, "Bots", "", Palette.Panel2, Palette.Text, 30, () => onBots(), out BotsLabel);
            UIKit.Layout(Bots, 84);
            UIKit.Layout(UIKit.Rect(col, "Spacer"), -1, -1, 1);
            Hint = UIKit.Label(col, "Hint", "", 28, Palette.Muted);
            UIKit.Layout(Hint, -1, -1, -1, -1, 50);
            Start = UIKit.ButtonKey(col, "Start", "start_game", Palette.Accent, Palette.OnAccent, 42, () => onStart(), out _);
            UIKit.Layout(Start, 124);
            Leave = UIKit.ButtonKey(col, "Leave", "leave", Palette.Panel, Palette.Muted, 26, () => onLeave(), out _);
            UIKit.Layout(Leave, 70);
        }
    }

    public sealed class GameView
    {
        public readonly UIScreen Screen;
        public readonly UText Header, Title, Body, Hint, PrimaryLabel, Notice;
        public readonly LineupView Lineup;
        public readonly Image Card, NoticeBg;
        public readonly TimerView Timer;
        public readonly Button Primary, Leave;

        public GameView(Transform root, Action onPrimary, Action onLeave)
        {
            Screen = new UIScreen(root, "Game", 16);
            var col = Screen.Column;
            Header = UIKit.Label(col, "Header", "", 26, Palette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            Header.Wrap = false;
            UIKit.Layout(Header, 50);
            Lineup = new LineupView(col);

            NoticeBg = UIKit.Panel(col, "Notice", Palette.BadTint);
            UIKit.Layout(NoticeBg, 0);
            NoticeBg.enabled = false;
            Notice = UIKit.Label(NoticeBg.transform, "Text", "", 28, Palette.Bad, TextAnchor.MiddleCenter, FontStyle.Bold);
            Notice.rectTransform.Stretch(20, 8, 20, 8);

            Card = UIKit.Panel(col, "Card", Palette.Panel);
            UIKit.Layout(Card, -1, -1, 1, -1, 460);
            UIKit.VStack(Card, 18, 34);
            Title = UIKit.Label(Card.transform, "Title", "", 34, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Layout(Title, -1, -1, -1, -1, 60);
            Body = UIKit.Label(Card.transform, "Body", "", 46, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Layout(Body, -1, -1, 1, -1, 160);
            Timer = TimerView.Create(Card.transform);
            Primary = UIKit.Button(Card.transform, "Primary", "", Palette.Accent, Palette.OnAccent, 38, () => onPrimary(), out PrimaryLabel);
            UIKit.Layout(Primary, 116);
            Hint = UIKit.Label(Card.transform, "Hint", "", 26, Palette.Muted);
            UIKit.Layout(Hint, -1, -1, -1, -1, 44);

            Leave = UIKit.ButtonKey(col, "Leave", "leave_game", Palette.Panel, Palette.Muted, 24, () => onLeave(), out _);
            UIKit.Layout(Leave, 64);
        }

        public void ShowNotice(string msg)
        {
            bool on = !string.IsNullOrEmpty(msg);
            Notice.Text = msg ?? "";
            UIKit.Layout(NoticeBg, on ? 110 : 0);
            NoticeBg.enabled = on;
        }
    }

    public sealed class ResultsView
    {
        public readonly UIScreen Screen;
        public readonly Button Lobby, Leave;
        readonly RectTransform content;
        readonly UText overall, original, final;
        readonly RectTransform entriesRoot, rankingRoot;
        readonly List<Row> rows = new List<Row>(20);
        readonly List<RankRow> rankRows = new List<RankRow>(20);
        readonly Action<int> onPlay;
        readonly ScrollRect scroll;
        ResultsMsg last;
        string lastMyId;
        bool lastHost;

        sealed class Row
        {
            public GameObject go;
            public Image bg;
            public UText num, name, said, score, playLabel;
            public Button play;
            public int index;
            public bool bot;
        }

        sealed class RankRow { public GameObject go; public Image bg; public UText rank, name, score; }

        public ResultsView(Transform root, Action<int> onPlay, Action onLobby, Action onLeave)
        {
            this.onPlay = onPlay;
            Screen = new UIScreen(root, "Results", 14);
            var col = Screen.Column;
            UIKit.Layout(UIKit.LabelKey(col, "Title", "results_title", 56, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold), -1, -1, -1, -1, 80);
            overall = UIKit.Label(col, "Overall", "", 28, Palette.Muted);
            UIKit.Layout(overall, -1, -1, -1, -1, 44);

            // Scroll view (RectMask2D viewport: no stencil buffer, cheap culling of off-screen rows).
            var view = UIKit.Rect(col, "Scroll");
            UIKit.Layout(view, -1, -1, 1);
            scroll = view.gameObject.AddComponent<ScrollRect>();
            var viewport = UIKit.Rect(view, "Viewport").Stretch();
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); // drag target
            hit.color = Palette.Clear;
            content = UIKit.Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            UIKit.VStack(content, 12);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            original = HighlightCard(content, "original", Palette.GoodTint, Palette.Good);
            entriesRoot = UIKit.Rect(content, "Entries");
            UIKit.VStack(entriesRoot, 10);
            final = HighlightCard(content, "final", Palette.WarnTint, Palette.Warn);
            UIKit.Layout(UIKit.LabelKey(content, "RankTitle", "rank_title", 30, Palette.Accent, TextAnchor.LowerLeft, FontStyle.Bold), -1, -1, -1, -1, 70);
            UIKit.LabelKey(content, "RankHelp", "rank_help", 22, Palette.Muted, TextAnchor.UpperLeft);
            rankingRoot = UIKit.Rect(content, "Ranking");
            UIKit.VStack(rankingRoot, 8);

            var buttons = UIKit.Rect(col, "Buttons");
            UIKit.HStack(buttons, 16);
            UIKit.Layout(buttons, 100);
            Lobby = UIKit.ButtonKey(buttons, "Lobby", "play_again", Palette.Accent, Palette.OnAccent, 34, () => onLobby(), out _);
            UIKit.Layout(Lobby, -1, 0, -1, 2);
            Leave = UIKit.ButtonKey(buttons, "Leave", "leave", Palette.Panel, Palette.Muted, 28, () => onLeave(), out _);
            UIKit.Layout(Leave, -1, 0, -1, 1);
        }

        static UText HighlightCard(Transform parent, string labelKey, Color bg, Color fg)
        {
            var card = UIKit.Panel(parent, labelKey, bg);
            UIKit.VStack(card, 6, 24);
            UIKit.Layout(UIKit.LabelKey(card.transform, "Label", labelKey, 22, fg, TextAnchor.MiddleLeft, FontStyle.Bold), -1, -1, -1, -1, 30);
            return UIKit.Label(card.transform, "Text", "", 38, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        Row GetRow(int i)
        {
            while (rows.Count <= i)
            {
                var r = new Row();
                r.bg = UIKit.Panel(entriesRoot, "Row", Palette.Panel);
                r.go = r.bg.gameObject;
                var h = UIKit.HStack(r.bg, 16, 18);
                h.childForceExpandHeight = false;
                h.childAlignment = TextAnchor.MiddleLeft;
                r.num = UIKit.Label(r.bg.transform, "Num", "", 34, Palette.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Layout(r.num, 50, 54);
                var mid = UIKit.Rect(r.bg.transform, "Mid");
                UIKit.VStack(mid, 4);
                UIKit.Layout(mid, -1, 0, -1, 1);
                r.name = UIKit.Label(mid, "Name", "", 24, Palette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
                r.said = UIKit.Label(mid, "Said", "", 30, Palette.Text, TextAnchor.MiddleLeft);
                r.score = UIKit.Label(r.bg.transform, "Score", "", 40, Palette.Good, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Layout(r.score, 60, 100);
                int idx = rows.Count;
                r.play = UIKit.Button(r.bg.transform, "Play", "", Palette.Panel2, Palette.Text, 24, () => onPlay(rows[idx].index), out r.playLabel);
                UIKit.Layout(r.play, 70, 140);
                rows.Add(r);
            }
            return rows[i];
        }

        RankRow GetRank(int i)
        {
            while (rankRows.Count <= i)
            {
                var r = new RankRow();
                r.bg = UIKit.Panel(rankingRoot, "Rank", Palette.Panel);
                r.go = r.bg.gameObject;
                UIKit.Layout(r.bg, 70);
                UIKit.HStack(r.bg, 16, 14);
                r.rank = UIKit.Label(r.bg.transform, "Rank", "", 30, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                r.rank.Wrap = false;
                UIKit.Layout(r.rank, -1, 90);
                r.name = UIKit.Label(r.bg.transform, "Name", "", 30, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                r.name.Wrap = false;
                UIKit.Layout(r.name, -1, 0, -1, 1);
                r.score = UIKit.Label(r.bg.transform, "Score", "", 32, Palette.Good, TextAnchor.MiddleRight, FontStyle.Bold);
                r.score.Wrap = false;
                UIKit.Layout(r.score, -1, 140);
                rankRows.Add(r);
            }
            return rankRows[i];
        }

        static Color ScoreColor(int s) => s < 0 ? Palette.Muted : s >= 80 ? Palette.Good : s >= 50 ? Palette.Accent : Palette.Bad;
        static string ScoreText(int s) => s < 0 ? "-" : UIKit.Num(s);
        static string Said(string s) => string.IsNullOrEmpty(s) ? Loc.T("no_transcript") : "“" + s + "”";

        /// <summary>Re-render with the current language (called after a language switch).</summary>
        public void Relocalize() { if (last != null) Show(last, lastMyId, lastHost, false); }

        public void Show(ResultsMsg r, string myId, bool isHost, bool scrollToTop = true)
        {
            last = r; lastMyId = myId; lastHost = isHost;
            overall.Text = r.overall < 0 ? "" : Loc.F("survived", r.overall);
            original.Text = r.original;
            final.Text = string.IsNullOrEmpty(r.final) ? Loc.T("no_transcript") : r.final;

            int n = r.entries?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                var e = r.entries[i];
                var row = GetRow(i);
                row.go.SetActive(true);
                row.index = e.index;
                row.bot = e.bot;
                row.num.Text = UIKit.Num(i + 1);
                row.name.Text = e.bot ? e.name + "  ·  " + Loc.T("tag_bot") : e.playerId == myId ? e.name + "  ·  " + Loc.T("tag_you") : e.name;
                row.name.Color = e.playerId == myId ? Palette.Blue : Palette.Muted;
                row.said.Text = Said(e.said);
                row.score.Text = ScoreText(e.score);
                row.score.Color = ScoreColor(e.score);
                row.bg.color = i == n - 1 ? Palette.WarnTint : Palette.Panel; // last link = final version
                row.playLabel.Text = Loc.T(e.bot ? "voice" : "play");
                row.play.interactable = e.hasAudio || !string.IsNullOrEmpty(e.said);
            }
            for (int i = n; i < rows.Count; i++) rows[i].go.SetActive(false);

            int m = r.ranking?.Length ?? 0;
            for (int i = 0; i < m; i++)
            {
                var k = r.ranking[i];
                var row = GetRank(i);
                row.go.SetActive(true);
                row.rank.Text = k.rank > 0 ? "#" + k.rank : Loc.T("tag_bot");
                row.rank.Color = k.rank == 1 ? Palette.Accent : k.rank == 2 ? Palette.Text : k.rank == 3 ? Palette.Warn : Palette.Muted;
                row.name.Text = k.name;
                row.name.Color = k.bot ? Palette.Muted : k.playerId == myId ? Palette.Blue : Palette.Text;
                row.score.Text = ScoreText(k.score);
                row.score.Color = ScoreColor(k.score);
                row.bg.color = k.rank == 1 ? Palette.GoodTint : Palette.Panel;
            }
            for (int i = m; i < rankRows.Count; i++) rankRows[i].go.SetActive(false);

            Lobby.gameObject.SetActive(isHost);
            if (scrollToTop) scroll.verticalNormalizedPosition = 1f;
        }

        public void SetPlaying(int index, bool playing)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].go.activeSelf && rows[i].index == index)
                    rows[i].playLabel.Text = playing ? "…" : Loc.T(rows[i].bot ? "voice" : "play");
        }
    }
}
