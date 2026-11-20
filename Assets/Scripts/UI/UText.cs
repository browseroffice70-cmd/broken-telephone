using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BrokenTelephone
{
    /// <summary>
    /// Text label that renders correctly for complex scripts (Devanagari, Kannada, Malayalam).
    ///
    /// Unity's uGUI Text / TextMeshPro do no glyph shaping, so conjuncts and vowel signs in Indic
    /// scripts come out broken. In the WebGL player this component asks the *browser* to shape and
    /// rasterize the string (Canvas2D fillText, system/Noto fonts) and shows it as a RawImage texture.
    ///   - Renders only when text / size / width / DPI change, never per frame.
    ///   - Ships zero font files in the build (fonts come from the OS or lazily from Google Fonts,
    ///     and Google's unicode-range subsets mean a browser only downloads scripts it actually shows).
    /// In the Editor (and non-web players) it falls back to a legacy Text so Play mode still works.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UText : MonoBehaviour, ILayoutElement
    {
        string text = "";
        int fontSize = 30;
        bool bold;
        bool wrap = true;
        TextAnchor alignment = TextAnchor.MiddleCenter;
        Color color = Color.white;
        string key; // localization key: when set, text follows the current language

        public RectTransform rectTransform => (RectTransform)transform;

        public string Text
        {
            get => text;
            set
            {
                value ??= "";
                if (text == value) return;
                text = value;
                Changed();
            }
        }

        public Color Color
        {
            get => color;
            set { if (color == value) return; color = value; if (graphic != null) graphic.color = value; }
        }

        public int FontSize { get => fontSize; set { if (fontSize == value) return; fontSize = value; Changed(); } }
        public bool Bold { get => bold; set { if (bold == value) return; bold = value; Changed(); } }
        public bool Wrap { get => wrap; set { if (wrap == value) return; wrap = value; Changed(); } }
        public TextAnchor Alignment { get => alignment; set { if (alignment == value) return; alignment = value; Changed(); } }

        /// <summary>The visible graphic (RawImage on web, Text elsewhere). Usable as an InputField placeholder.</summary>
        public Graphic Graphic => graphic;

        Graphic graphic;
        static readonly List<UText> Keyed = new List<UText>(128);

        public static UText Create(Transform parent, string name)
        {
            var rt = UIKit.Rect(parent, name);
            var t = rt.gameObject.AddComponent<UText>();
            t.Init();
            return t;
        }

        /// <summary>Re-applies every keyed label after a language switch.</summary>
        public static void RelocalizeAll()
        {
            for (int i = 0; i < Keyed.Count; i++) if (Keyed[i] != null) Keyed[i].Text = Loc.T(Keyed[i].key);
        }

        public void SetKey(string k)
        {
            key = k;
            if (!Keyed.Contains(this)) Keyed.Add(this);
            Text = Loc.T(k);
        }

        void OnDestroy()
        {
            Keyed.Remove(this);
            OnDestroyTexture();
        }

        void Changed()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MarkDirty();
#else
            if (legacy == null) return;
            legacy.text = text;
            legacy.fontSize = fontSize;
            legacy.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            legacy.alignment = alignment;
            legacy.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // ---------------- browser-rendered path ----------------
        RawImage image;
        RectTransform imageRt;
        Texture2D texture;
        Canvas rootCanvas;
        bool dirty = true;
        float renderedWidth = -1f, renderedScale = -1f;
        float measuredW, measuredH;
        static byte[] pixelBuffer = new byte[0];

        void Init()
        {
            imageRt = UIKit.Rect(transform, "Glyphs");
            image = imageRt.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.color = color;
            graphic = image;
            UTextSystem.Ensure();
        }

        void OnEnable() { if (dirty) UTextSystem.Enqueue(this); }

        void OnRectTransformDimensionsChange()
        {
            float w = rectTransform.rect.width;
            if (Mathf.Abs(w - renderedWidth) > 0.5f) MarkDirty();
            else AlignImage();
        }

        internal void MarkDirty()
        {
            dirty = true;
            UTextSystem.Enqueue(this);
        }

        internal void OnScaleChanged(float scale)
        {
            if (!Mathf.Approximately(scale, renderedScale)) MarkDirty();
        }

        /// <summary>Called by UTextSystem in LateUpdate (after scripts, before canvas layout/render).</summary>
        internal void Render()
        {
            if (!dirty || !isActiveAndEnabled) return;
            float width = rectTransform.rect.width;
            if (wrap && width < 1f) { UTextSystem.Enqueue(this); return; } // not laid out yet; retry next frame
            if (rootCanvas == null) { var c = GetComponentInParent<Canvas>(); rootCanvas = c != null ? c.rootCanvas : null; }
            float scale = rootCanvas != null ? Mathf.Max(0.1f, rootCanvas.scaleFactor) : 1f;
            dirty = false;
            renderedWidth = width;
            renderedScale = scale;

            float oldH = measuredH, oldW = measuredW;
            if (string.IsNullOrEmpty(text))
            {
                measuredW = measuredH = 0f;
                image.texture = null;
                imageRt.sizeDelta = Vector2.zero;
            }
            else
            {
                int px = Mathf.Max(8, Mathf.RoundToInt(fontSize * scale));
                int maxW = wrap ? Mathf.Max(8, Mathf.CeilToInt(width * scale)) : 4096;
                int packed = WebBridge.TextRender(text, px, bold, maxW, wrap, HAlign(alignment));
                int w = packed >> 16, h = packed & 0xFFFF;
                if (w <= 0 || h <= 0) { measuredW = measuredH = 0f; image.texture = null; }
                else
                {
                    int bytes = w * h * 4;
                    if (pixelBuffer.Length < bytes) pixelBuffer = new byte[Mathf.NextPowerOfTwo(bytes)];
                    WebBridge.TextCopy(pixelBuffer);
                    if (texture != null) Destroy(texture);
                    texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    texture.SetPixelData(pixelBuffer, 0, 0);
                    texture.Apply(false, true); // upload, then free the CPU-side copy
                    image.texture = texture;
                    measuredW = w / scale;
                    measuredH = h / scale;
                }
                imageRt.sizeDelta = new Vector2(measuredW, measuredH);
            }
            AlignImage();
            if (!Mathf.Approximately(oldH, measuredH) || (!wrap && !Mathf.Approximately(oldW, measuredW)))
                LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
        }

        void AlignImage()
        {
            var a = Anchor(alignment);
            imageRt.anchorMin = imageRt.anchorMax = imageRt.pivot = a;
            imageRt.anchoredPosition = Vector2.zero;
        }

        static int HAlign(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: return 0;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: return 2;
                default: return 1;
            }
        }

        static Vector2 Anchor(TextAnchor a)
        {
            float x = HAlign(a) * 0.5f;
            float y;
            switch (a)
            {
                case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: y = 1f; break;
                case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: y = 0f; break;
                default: y = 0.5f; break;
            }
            return new Vector2(x, y);
        }

        void OnDestroyTexture() { if (texture != null) Destroy(texture); }

        // ILayoutElement: report the browser-measured size so layout groups can size rows to wrapped text.
        public float preferredWidth => wrap ? -1f : measuredW;
        public float preferredHeight => measuredH;
#else
        // ---------------- legacy fallback (Editor / non-web) ----------------
        Text legacy;
        static Font editorFont;

        void Init()
        {
            legacy = gameObject.AddComponent<Text>();
            if (editorFont == null)
            {
                // OS font with Indic coverage so the Editor shows *something* (unshaped) for hi/kn/ml.
                editorFont = Font.CreateDynamicFontFromOSFont(new[] { "Nirmala UI", "Arial" }, 32);
                if (editorFont == null) editorFont = UIKit.Font;
            }
            legacy.font = editorFont;
            legacy.raycastTarget = false;
            legacy.supportRichText = false;
            legacy.verticalOverflow = VerticalWrapMode.Overflow;
            legacy.color = color;
            graphic = legacy;
            Changed();
        }

        void OnDestroyTexture() { }

        public float preferredWidth => -1f;
        public float preferredHeight => -1f;
#endif

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
        public float minWidth => -1f;
        public float flexibleWidth => -1f;
        public float minHeight => -1f;
        public float flexibleHeight => -1f;
        public int layoutPriority => 0;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>Batches UText renders once per frame, and re-renders everything on DPI / font-load changes.</summary>
    public sealed class UTextSystem : MonoBehaviour
    {
        static UTextSystem instance;
        static readonly List<UText> queue = new List<UText>(64);
        static readonly List<UText> work = new List<UText>(64);
        float lastScale = -1f;
        Canvas probe;

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("UTextSystem");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<UTextSystem>();
        }

        public static void Enqueue(UText t) { if (!queue.Contains(t)) queue.Add(t); }

        /// <summary>Web fonts finished loading (or language changed): re-shape every label.</summary>
        public static void InvalidateAll()
        {
            foreach (var t in FindObjectsByType<UText>(FindObjectsInactive.Include, FindObjectsSortMode.None)) t.MarkDirty();
        }

        void LateUpdate()
        {
            if (probe == null) { var c = FindAnyObjectByType<CanvasScaler>(); if (c != null) probe = c.GetComponent<Canvas>(); }
            if (probe != null && !Mathf.Approximately(probe.scaleFactor, lastScale))
            {
                lastScale = probe.scaleFactor;
                foreach (var t in FindObjectsByType<UText>(FindObjectsInactive.Include, FindObjectsSortMode.None)) t.OnScaleChanged(lastScale);
            }
            if (queue.Count == 0) return;
            work.Clear();
            work.AddRange(queue);
            queue.Clear();
            for (int i = 0; i < work.Count; i++) if (work[i] != null) work[i].Render();
        }
    }
#endif
}
