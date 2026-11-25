using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BrokenTelephone
{
    public enum StageMode { Hidden, Reading, Whisper, Thinking, Celebrate, Confused }

    /// <summary>
    /// Little 2D cartoon stage on the game card: blob characters (one per player, coloured by device id)
    /// reading the secret, whispering into each other's ears (words visibly garble mid-flight), scratching
    /// their heads, celebrating. Everything is procedural uGUI on its own nested canvas, so the per-frame
    /// transform tweaks re-batch only these ~40 graphics. Sleeps when hidden or when the game screen is off.
    /// </summary>
    public sealed class WhisperStage : MonoBehaviour
    {
        const float BoxW = 600f, BoxH = 260f, Ground = -95f;

        static readonly Color32[] Skins =
        {
            new Color32(255, 138, 101, 255), new Color32(110, 190, 255, 255), new Color32(170, 130, 255, 255),
            new Color32(90, 210, 150, 255), new Color32(255, 196, 70, 255), new Color32(255, 125, 185, 255),
            new Color32(110, 220, 220, 255), new Color32(215, 170, 120, 255),
        };
        static readonly Color Dark = new Color32(34, 26, 46, 255);
        static readonly Color EyeWhite = new Color32(250, 250, 255, 255);
        static readonly Color Blush = new Color32(255, 110, 140, 120);
        static readonly Color EarPink = new Color32(255, 150, 160, 255);
        static readonly Color PaperCol = new Color32(242, 237, 222, 255);
        static readonly Color InkCol = new Color32(150, 148, 165, 255);
        static readonly Color NeutralSkin = new Color32(146, 149, 176, 255);

        // What was said -> what the listener heard. Swapped halfway to the ear.
        static readonly string[][] Garble =
        {
            new[] { "psst", "pssh" }, new[] { "cat", "hat" }, new[] { "news", "shoes" }, new[] { "pizza", "Pisa" },
            new[] { "duck", "truck" }, new[] { "banana", "bandana" }, new[] { "hello", "jello" },
            new[] { "tomato", "potato" }, new[] { "secret", "biscuit" }, new[] { "vote", "goat" },
            new[] { "rain", "train" }, new[] { "minister", "monster" }, new[] { "budget", "bucket" },
            new[] { "market", "carpet" }, new[] { "police", "polish" },
        };

        sealed class Puppet
        {
            public RectTransform root, mirror, head, hand, paper, earFront, eyeL, eyeR, pupilL, pupilR, mouth;
            public Image skull, body, earF, earB, handImg, tuft;
            public UText name;
            public float phase, blinkAt, blinkEnd, eyeScale = 1f;
        }

        sealed class Particle
        {
            public RectTransform rt;
            public Graphic g;
            public Text text;
            public bool alive;
            public float born, life, lift, spin, baseAngle;
            public Vector2 from, to;
            public string alt;
            public Color color;
        }

        UIScreen screen;
        Canvas canvas;
        LayoutElement layout;
        RectTransform self, box, bubble;
        Image bub1, bub2, bubBig;
        Text bubText;
        Puppet a, b;
        readonly List<Particle> arcs = new List<Particle>(8), glyphs = new List<Particle>(8);
        int nextArcSlot, nextGlyphSlot;

        StageMode mode = (StageMode)(-1);
        string idA, idB;
        bool active;
        float modeStart, arcAt, glyphAt;
        int wordIdx, bubbleIdx = -1;
        Vector2 lastSize;

        public static WhisperStage Create(Transform parent, UIScreen screen)
        {
            var rt = UIKit.Rect(parent, "Stage");
            var st = rt.gameObject.AddComponent<WhisperStage>();
            st.self = rt;
            st.screen = screen;
            st.canvas = UIKit.SubCanvas(rt, false);
            st.layout = UIKit.Layout(rt, 0);
            st.box = Zero(rt, "Box");
            st.b = BuildPuppet(st.box, "B");
            st.a = BuildPuppet(st.box, "A");
            for (int i = 0; i < 6; i++) st.arcs.Add(MakeArc(st.box));
            for (int i = 0; i < 6; i++) st.glyphs.Add(MakeGlyph(st.box));
            st.BuildBubble();
            st.Set(StageMode.Hidden, null, null, false);
            return st;
        }

        // ================= public API =================

        /// <summary>Cheap to call every render: restarts the scene only when the mode or the cast changes.</summary>
        public void Set(StageMode m, PlayerInfo pa, PlayerInfo pb, bool isActive)
        {
            active = isActive;
            string ia = pa?.id, ib = pb?.id;
            if (m == mode && ia == idA && ib == idB) return;
            mode = m; idA = ia; idB = ib;
            modeStart = Time.unscaledTime;
            arcAt = glyphAt = modeStart + 0.25f;
            bubbleIdx = -1;

            bool on = m != StageMode.Hidden;
            canvas.enabled = on;
            enabled = on;
            layout.minHeight = on ? 130f : 0f;
            layout.preferredHeight = on ? 230f : 0f;
            layout.flexibleHeight = on ? 1f : 0f;
            foreach (var p in arcs) Kill(p);
            foreach (var p in glyphs) Kill(p);
            bubble.gameObject.SetActive(m == StageMode.Thinking);
            if (!on) return;

            bool two = pb != null && (m == StageMode.Whisper || m == StageMode.Confused);
            float ax = two ? -140f : m == StageMode.Whisper ? -120f : m == StageMode.Thinking ? -70f : 0f;
            Dress(a, pa, ax, 1f);
            if (two) Dress(b, pb, 140f, -1f);
            if (b.root.gameObject.activeSelf != two) b.root.gameObject.SetActive(two);
            a.paper.gameObject.SetActive(m == StageMode.Reading);
            lastSize = Vector2.zero; // refit
        }

        // ================= per-frame =================

        void Update()
        {
            if (!screen.Visible) return;
            var size = self.rect.size;
            if (size != lastSize)
            {
                lastSize = size;
                float s = Mathf.Max(0.05f, Mathf.Min(size.x / BoxW, size.y / BoxH));
                box.localScale = new Vector3(s, s, 1f);
            }

            float now = Time.unscaledTime, t = now - modeStart;
            bool two = b.root.gameObject.activeSelf;
            Idle(a, now, t);
            if (two) Idle(b, now, t);

            switch (mode)
            {
                case StageMode.Whisper: AnimWhisper(now, t, two); break;
                case StageMode.Reading: AnimReading(now, t); break;
                case StageMode.Thinking: AnimThinking(now, t); break;
                case StageMode.Celebrate: AnimCelebrate(now, t); break;
                case StageMode.Confused: AnimConfused(now, t, two); break;
            }
            Tick(arcs, now);
            Tick(glyphs, now);
        }

        void Idle(Puppet p, float now, float t)
        {
            p.root.anchoredPosition = new Vector2(p.root.anchoredPosition.x, Ground + Mathf.Sin(t * 2.2f + p.phase) * 3f);
            if (now >= p.blinkAt) { p.blinkEnd = now + 0.12f; p.blinkAt = now + Random.Range(2f, 4.5f); }
            float ey = now < p.blinkEnd ? 0.1f : p.eyeScale;
            p.eyeL.localScale = p.eyeR.localScale = new Vector3(1f, ey, 1f);
        }

        void AnimWhisper(float now, float t, bool two)
        {
            // Whisperer leans in, cups a hand to their mouth, eyes sneakily on the listener.
            Rot(a.head, -12f + Mathf.Sin(t * 3f) * 2f);
            a.hand.anchoredPosition = new Vector2(62f, 128f + Mathf.Sin(t * 6f) * 2f);
            float talk = active ? 0.35f + 0.9f * Mathf.Abs(Mathf.Sin(t * 13f) * Mathf.Sin(t * 5.3f)) : 0.3f;
            a.mouth.localScale = new Vector3(0.8f, talk, 1f);
            Look(a, 6f, 1f);
            a.eyeScale = 0.75f;

            var mouthPos = Local(a.mouth) + new Vector2(14f, 0f);
            if (two)
            {
                // Listener leans back in, ear swells, eyes go wide; a little giggle-shiver while it tickles.
                Rot(b.head, -7f + (active ? Mathf.Sin(t * 22f) * 1.5f : 0f));
                float ear = active ? 1.35f + 0.12f * Mathf.Sin(t * 9f) : 1.1f;
                b.earFront.localScale = new Vector3(ear, ear, 1f);
                Look(b, 5f, Mathf.Sin(t * 1.3f) * 2f);
                b.eyeScale = active ? 1.2f : 1f;
                bool gasp = active && Mathf.Repeat(t, 3.2f) > 2.4f;
                b.mouth.localScale = gasp ? new Vector3(0.45f, 1.3f, 1f) : new Vector3(1f, 0.5f, 1f);
            }
            var earPos = two ? Local(b.earFront) : mouthPos + new Vector2(300f, 30f);

            if (active)
            {
                if (now >= arcAt) { arcAt = now + 0.3f; SpawnArc(mouthPos, earPos, 1.0f); }
                if (now >= glyphAt)
                {
                    glyphAt = now + 0.85f;
                    var w = Garble[wordIdx = (wordIdx + 1 + Random.Range(0, 4)) % Garble.Length];
                    SpawnGlyph(w[0], w[1], mouthPos + new Vector2(0f, 10f), earPos + new Vector2(0f, 14f), 1.5f, 50f, Palette.Accent, 30);
                }
            }
            else if (now >= glyphAt)
            {
                glyphAt = now + 1.5f;
                SpawnGlyph("psst...", null, mouthPos + new Vector2(10f, 20f), mouthPos + new Vector2(50f, 80f), 1.3f, 10f, Palette.Muted, 26);
            }
        }

        void AnimReading(float now, float t)
        {
            Rot(a.head, Mathf.Sin(t * 1.2f) * 3f - 4f);
            a.hand.anchoredPosition = new Vector2(70f, 82f + Mathf.Sin(t * 1.2f) * 2f);
            Look(a, Mathf.Sin(t * 2.4f) * 6f, -4f); // eyes scanning the lines
            a.eyeScale = 0.9f;
            a.paper.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.2f) * 2f);
            if (now >= glyphAt)
            {
                glyphAt = now + 2.2f;
                var top = Local(a.head) + new Vector2(0f, 150f);
                bool bang = Random.value < 0.5f;
                SpawnGlyph(bang ? "!" : "hmm", null, top, top + new Vector2(Random.Range(-30f, 30f), 50f), 1.4f, 0f,
                    bang ? Palette.Accent : Palette.Muted, bang ? 40 : 26);
            }
        }

        void AnimThinking(float now, float t)
        {
            Rot(a.head, Mathf.Sin(t * 1.6f) * 7f);
            a.hand.anchoredPosition = new Vector2(42f + Mathf.Sin(t * 8f) * 3f, 104f); // rubbing chin
            Look(a, 3f, 6f);
            a.eyeScale = 1f;
            a.mouth.localScale = new Vector3(0.55f, 0.3f, 1f);

            var origin = (Vector2)a.root.anchoredPosition;
            bubble.anchoredPosition = new Vector2(origin.x, Ground);
            Pop(bub1.rectTransform, t * 4f);
            Pop(bub2.rectTransform, t * 4f - 1f);
            Pop(bubBig.rectTransform, t * 4f - 2f);
            int idx = (int)(t / 1.1f) % Garble.Length;
            if (idx != bubbleIdx) { bubbleIdx = idx; bubText.text = Garble[(idx * 7 + 3) % Garble.Length][1] + "?"; }
        }

        void AnimCelebrate(float now, float t)
        {
            float jump = Mathf.Abs(Mathf.Sin(t * 5f)) * 22f;
            a.root.anchoredPosition = new Vector2(a.root.anchoredPosition.x, Ground + jump);
            Rot(a.head, Mathf.Sin(t * 5f) * 6f);
            a.hand.anchoredPosition = new Vector2(70f, 175f);
            a.hand.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 14f) * 25f);
            a.hand.localScale = Vector3.one;
            Look(a, 0f, 2f);
            a.eyeScale = 0.45f; // happy squint
            a.mouth.localScale = new Vector3(1.3f, 1.2f, 1f);
            if (now >= glyphAt)
            {
                glyphAt = now + 0.22f;
                var top = Local(a.head) + new Vector2(0f, 110f);
                SpawnGlyph("*", null, top, top + new Vector2(Random.Range(-150f, 150f), Random.Range(30f, 90f)), 0.9f, 20f,
                    Random.value < 0.5f ? Palette.Accent : Palette.Good, Random.Range(28, 44));
            }
        }

        void AnimConfused(float now, float t, bool two)
        {
            Confused(a, t, 0f);
            if (two) Confused(b, t, 1.7f);
            if (now >= glyphAt)
            {
                glyphAt = now + 0.9f;
                var p = two && Random.value < 0.5f ? b : a;
                var top = Local(p.head) + new Vector2(0f, 140f);
                SpawnGlyph("?", null, top, top + new Vector2(Random.Range(-40f, 40f), 50f), 1.2f, 0f, Palette.Warn, 40);
            }
        }

        static void Confused(Puppet p, float t, float off)
        {
            Rot(p.head, Mathf.Sin(t * 1.7f + off) * 14f);
            Look(p, Mathf.Sin(t * 2.3f + off) * 6f, Mathf.Cos(t * 1.9f + off) * 3f);
            p.mouth.localScale = new Vector3(0.8f, 0.25f, 1f);
            p.hand.anchoredPosition = new Vector2(60f, 165f + Mathf.Sin(t * 7f) * 3f); // scratching head
        }

        // ================= particles =================

        void SpawnArc(Vector2 from, Vector2 to, float life)
        {
            var p = arcs[nextArcSlot = (nextArcSlot + 1) % arcs.Count];
            var d = to - from;
            Launch(p, from, to, life, 18f, Color.white);
            p.baseAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 45f; // filled quarter ring faces the target
        }

        void SpawnGlyph(string text, string alt, Vector2 from, Vector2 to, float life, float lift, Color color, int size)
        {
            var p = glyphs[nextGlyphSlot = (nextGlyphSlot + 1) % glyphs.Count];
            p.text.text = text;
            p.text.fontSize = size;
            p.alt = alt;
            p.spin = Random.Range(6f, 16f) * (Random.value < 0.5f ? -1f : 1f);
            Launch(p, from, to, life, lift, color);
        }

        void Launch(Particle p, Vector2 from, Vector2 to, float life, float lift, Color color)
        {
            p.alive = true;
            p.born = Time.unscaledTime;
            p.life = life;
            p.from = from;
            p.to = to;
            p.lift = lift;
            p.color = color;
            p.rt.SetAsLastSibling();
            if (!p.rt.gameObject.activeSelf) p.rt.gameObject.SetActive(true);
        }

        static void Tick(List<Particle> list, float now)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (!p.alive) continue;
                float u = (now - p.born) / p.life;
                if (u >= 1f) { Kill(p); continue; }
                float e = 1f - (1f - u) * (1f - u); // ease-out
                p.rt.anchoredPosition = Vector2.LerpUnclamped(p.from, p.to, e) + new Vector2(0f, Mathf.Sin(u * Mathf.PI) * p.lift);
                float alpha = Mathf.Clamp01(u * 6f) * Mathf.Clamp01((1f - u) * 4f);
                var c = p.color; c.a *= alpha;
                p.g.color = c;
                if (p.text == null)
                {
                    float s = 0.5f + u * 0.9f;
                    p.rt.localScale = new Vector3(s, s, 1f);
                    p.rt.localRotation = Quaternion.Euler(0f, 0f, p.baseAngle);
                }
                else
                {
                    if (p.alt != null && u > 0.5f) { p.text.text = p.alt; p.alt = null; } // the "broken" part
                    p.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(u * 12f) * p.spin);
                    float s = 0.7f + Mathf.Sin(Mathf.Min(u * 4f, 1f) * Mathf.PI * 0.5f) * 0.3f;
                    p.rt.localScale = new Vector3(s, s, 1f);
                }
            }
        }

        static void Kill(Particle p)
        {
            p.alive = false;
            if (p.rt.gameObject.activeSelf) p.rt.gameObject.SetActive(false);
        }

        static Particle MakeArc(RectTransform parent)
        {
            var img = Blob(parent, "Arc", UIKit.Ring, Color.white, 0, 0, 60, 60);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Right;
            img.fillClockwise = false;
            img.fillAmount = 0.25f;
            img.gameObject.SetActive(false);
            return new Particle { rt = img.rectTransform, g = img };
        }

        static Particle MakeGlyph(RectTransform parent)
        {
            var t = UIKit.LegacyLabel(parent, "Glyph", "", 30, Palette.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200f, 50f);
            t.gameObject.SetActive(false);
            return new Particle { rt = rt, g = t, text = t };
        }

        // ================= building =================

        void BuildBubble()
        {
            bubble = Zero(box, "Bubble");
            bub1 = Blob(bubble, "B1", UIKit.Circle, Color.white, 95f, 165f, 14f, 14f);
            bub2 = Blob(bubble, "B2", UIKit.Circle, Color.white, 118f, 184f, 24f, 24f);
            bubBig = Blob(bubble, "Big", UIKit.Rounded, Color.white, 205f, 196f, 170f, 66f);
            bubText = UIKit.LegacyLabel(bubBig.transform, "Text", "", 28, Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            bubText.horizontalOverflow = HorizontalWrapMode.Overflow;
            bubText.rectTransform.Stretch();
            bubble.gameObject.SetActive(false);
        }

        static Puppet BuildPuppet(RectTransform parent, string n)
        {
            var p = new Puppet();
            p.root = Zero(parent, n);
            p.name = UIKit.Label(p.root, "Name", "", 24, Palette.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            p.name.Wrap = false;
            var nrt = p.name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
            nrt.sizeDelta = new Vector2(260f, 32f);
            nrt.anchoredPosition = new Vector2(0f, -20f);

            p.mirror = Zero(p.root, "Mirror");
            p.body = Blob(p.mirror, "Body", UIKit.Rounded, Color.white, 0f, 38f, 96f, 76f);
            p.head = Zero(p.mirror, "Head");
            p.head.anchoredPosition = new Vector2(0f, 92f); // neck = rotation pivot
            p.tuft = Blob(p.head, "Tuft", UIKit.Circle, Color.white, -8f, 122f, 28f, 28f);
            p.earB = Blob(p.head, "EarB", UIKit.Circle, Color.white, -64f, 60f, 40f, 40f);
            Blob(p.earB.transform, "In", UIKit.Circle, EarPink, -4f, 0f, 20f, 20f);
            p.earF = Blob(p.head, "EarF", UIKit.Circle, Color.white, 64f, 60f, 40f, 40f);
            p.earFront = p.earF.rectTransform;
            Blob(p.earFront, "In", UIKit.Circle, EarPink, 4f, 0f, 20f, 20f);
            p.skull = Blob(p.head, "Skull", UIKit.Circle, Color.white, 0f, 60f, 124f, 120f);
            p.eyeL = Blob(p.head, "EyeL", UIKit.Circle, EyeWhite, -8f, 72f, 30f, 34f).rectTransform;
            p.pupilL = Blob(p.eyeL, "P", UIKit.Circle, Dark, 3f, 0f, 14f, 16f).rectTransform;
            p.eyeR = Blob(p.head, "EyeR", UIKit.Circle, EyeWhite, 30f, 72f, 30f, 34f).rectTransform;
            p.pupilR = Blob(p.eyeR, "P", UIKit.Circle, Dark, 3f, 0f, 14f, 16f).rectTransform;
            Blob(p.head, "Cheek", UIKit.Circle, Blush, 44f, 46f, 22f, 14f);
            p.mouth = Blob(p.head, "Mouth", UIKit.Circle, Dark, 28f, 34f, 26f, 12f).rectTransform;

            var paper = Blob(p.mirror, "Paper", UIKit.Rounded, PaperCol, 22f, 98f, 118f, 76f);
            p.paper = paper.rectTransform;
            Blob(p.paper, "Head", null, Dark, 0f, 22f, 84f, 9f);
            for (int i = 0; i < 3; i++) Blob(p.paper, "Line", null, InkCol, i == 2 ? -10f : 0f, 4f - i * 13f, i == 2 ? 64f : 84f, 6f);
            p.paper.gameObject.SetActive(false);

            p.handImg = Blob(p.mirror, "Hand", UIKit.Circle, Color.white, 46f, 40f, 30f, 30f);
            p.hand = p.handImg.rectTransform;
            return p;
        }

        void Dress(Puppet p, PlayerInfo info, float x, float facing)
        {
            uint h = Hash(info?.id);
            Color skin = info == null ? NeutralSkin : (Color)Skins[h % (uint)Skins.Length];
            p.skull.color = p.earB.color = p.earF.color = skin;
            p.body.color = Shade(skin, 0.75f);
            p.handImg.color = Shade(skin, 0.88f);
            p.tuft.color = Shade(skin, 0.7f);
            p.tuft.enabled = (h >> 4 & 1) == 0;
            float w = 116f + (h >> 6) % 16; // a bit of face variety per player
            p.skull.rectTransform.sizeDelta = new Vector2(w, 120f);
            p.name.Text = info?.name ?? "";
            p.name.Color = info == null ? Palette.Muted : skin;
            p.phase = (h >> 10) % 628 / 100f;
            p.blinkAt = Time.unscaledTime + Random.Range(0.5f, 3f);
            p.root.anchoredPosition = new Vector2(x, Ground);
            p.mirror.localScale = new Vector3(facing, 1f, 1f);
            // neutral pose
            p.head.localRotation = Quaternion.identity;
            p.hand.localRotation = Quaternion.identity;
            p.hand.localScale = Vector3.one;
            p.hand.anchoredPosition = new Vector2(46f, 40f);
            p.earFront.localScale = Vector3.one;
            p.mouth.localScale = new Vector3(1f, 0.5f, 1f);
            p.eyeScale = 1f;
            Look(p, 3f, 0f);
        }

        // ================= helpers =================

        Vector2 Local(RectTransform rt) => box.InverseTransformPoint(rt.position);

        static void Rot(RectTransform rt, float z) => rt.localRotation = Quaternion.Euler(0f, 0f, z);

        static void Look(Puppet p, float x, float y) => p.pupilL.anchoredPosition = p.pupilR.anchoredPosition = new Vector2(x, y);

        static void Pop(RectTransform rt, float k)
        {
            k = Mathf.Clamp01(k);
            float s = k < 1f ? k * 1.15f : 1f; // tiny overshoot while popping in
            rt.localScale = new Vector3(s, s, 1f);
        }

        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        static uint Hash(string s)
        {
            uint h = 2166136261;
            if (s != null) foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h;
        }

        static RectTransform Zero(Transform parent, string n)
        {
            var rt = UIKit.Rect(parent, n);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        static Image Blob(Transform parent, string n, Sprite sprite, Color c, float x, float y, float w, float h)
        {
            var img = UIKit.Rect(parent, n).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            if (sprite == UIKit.Rounded) img.type = Image.Type.Sliced;
            img.color = c;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            return img;
        }
    }
}
