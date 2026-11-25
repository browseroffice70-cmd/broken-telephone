using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BrokenTelephone
{
    public static class Palette
    {
        public static readonly Color Bg = new Color32(17, 17, 27, 255);
        public static readonly Color Panel = new Color32(29, 30, 45, 255);
        public static readonly Color Panel2 = new Color32(41, 43, 64, 255);
        public static readonly Color Accent = new Color32(255, 199, 44, 255);
        public static readonly Color OnAccent = new Color32(30, 24, 0, 255);
        public static readonly Color Text = new Color32(237, 237, 246, 255);
        public static readonly Color Muted = new Color32(146, 149, 176, 255);
        public static readonly Color Good = new Color32(70, 205, 128, 255);
        public static readonly Color GoodTint = new Color32(26, 64, 46, 255);
        public static readonly Color Bad = new Color32(240, 86, 86, 255);
        public static readonly Color BadTint = new Color32(78, 30, 34, 255);
        public static readonly Color Warn = new Color32(255, 143, 64, 255);
        public static readonly Color WarnTint = new Color32(80, 46, 22, 255);
        public static readonly Color Blue = new Color32(88, 146, 255, 255);
        public static readonly Color Clear = new Color(0, 0, 0, 0);
    }

    /// <summary>Tiny code-only uGUI builder. One shared font + one shared 9-slice sprite = everything batches.</summary>
    public static class UIKit
    {
        static Font font;
        static Sprite rounded;

        public static Font Font => font ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        public static Sprite Rounded => rounded ? rounded : (rounded = MakeRounded(48, 18));

        static Sprite circle, ring;
        /// <summary>Anti-aliased filled disc (64px). Scales to any ellipse.</summary>
        public static Sprite Circle => circle ? circle : (circle = MakeRadial(64, d => Mathf.Clamp01(31.5f - d + 0.5f), "CircleUI"));
        /// <summary>Anti-aliased ring (64px, ~7px thick). Use Image.Type.Filled to draw arcs.</summary>
        public static Sprite Ring => ring ? ring : (ring = MakeRadial(64, d => Mathf.Clamp01(31.5f - d + 0.5f) * Mathf.Clamp01(d - 24f + 0.5f), "RingUI"));

        static Sprite MakeRadial(int size, System.Func<float, float> alphaAt, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
            var px = new Color32[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(alphaAt(d) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RoundedUI" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true); // upload and drop the CPU copy
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, bool raycast = false)
        {
            var img = Rect(parent, name).gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Shaped (Indic-safe) label. See <see cref="UText"/>.</summary>
        public static UText Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var t = UText.Create(parent, name);
            t.FontSize = size;
            t.Bold = style == FontStyle.Bold;
            t.Alignment = align;
            t.Color = color;
            t.Text = text;
            return t;
        }

        /// <summary>Localized label: follows the current language automatically.</summary>
        public static UText LabelKey(Transform parent, string name, string key, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var t = Label(parent, name, "", size, color, align, style);
            t.SetKey(key);
            return t;
        }

        /// <summary>Plain uGUI Text: only for ASCII-only content (digits) and InputField text.</summary>
        public static Text LegacyLabel(Transform parent, string name, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var t = Rect(parent, name).gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button ButtonKey(Transform parent, string name, string key, Color bg, Color fg, int size, UnityAction onClick, out UText text)
        {
            var b = Button(parent, name, "", bg, fg, size, onClick, out text);
            text.SetKey(key);
            return b;
        }

        public static Button Button(Transform parent, string name, string label, Color bg, Color fg, int size, UnityAction onClick, out UText text)
        {
            var img = Panel(parent, name, bg, true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0.3f);
            cb.fadeDuration = 0.06f;
            b.colors = cb;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            text = Label(img.transform, "Label", label, size, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.Wrap = false;
            text.rectTransform.Stretch(12, 0, 12, 0);
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        public static InputField Input(Transform parent, string name, string placeholderKey, int size, int limit)
        {
            var bg = Panel(parent, name, Palette.Panel2, true);
            // Typed text stays a uGUI Text (InputField needs it for caret/selection). The placeholder is shaped.
            var t = LegacyLabel(bg.transform, "Text", "", size, Palette.Text, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.rectTransform.Stretch(22, 0, 22, 0);
            var ph = LabelKey(bg.transform, "Placeholder", placeholderKey, size, Palette.Muted, TextAnchor.MiddleLeft);
            ph.Wrap = false;
            ph.rectTransform.Stretch(22, 0, 22, 0);
            var f = bg.gameObject.AddComponent<InputField>();
            f.targetGraphic = bg;
            f.textComponent = t;
            f.placeholder = ph.Graphic;
            f.characterLimit = limit;
            f.lineType = InputField.LineType.SingleLine;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            return f;
        }

        public static LayoutElement Layout(Component c, float prefHeight = -1, float prefWidth = -1, float flexHeight = -1, float flexWidth = -1, float minHeight = -1)
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = prefHeight;
            le.preferredWidth = prefWidth;
            le.flexibleHeight = flexHeight;
            le.flexibleWidth = flexWidth;
            le.minHeight = minHeight;
            return le;
        }

        public static VerticalLayoutGroup VStack(Component c, float spacing, int pad = 0, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(Component c, float spacing, int pad = 0, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return h;
        }

        /// <summary>Nested canvas: content under it re-batches without dirtying the parent canvas.</summary>
        public static Canvas SubCanvas(Component c, bool raycast)
        {
            var cv = c.gameObject.AddComponent<Canvas>();
            if (raycast) c.gameObject.AddComponent<GraphicRaycaster>();
            return cv;
        }

        // Pre-built "0".."99" strings so countdowns never allocate.
        static readonly string[] Numbers = BuildNumbers();
        static string[] BuildNumbers()
        {
            var a = new string[100];
            for (int i = 0; i < a.Length; i++) a[i] = i.ToString();
            return a;
        }
        public static string Num(int n) => n >= 0 && n < Numbers.Length ? Numbers[n] : n.ToString();
    }
}
