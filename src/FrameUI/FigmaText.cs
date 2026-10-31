using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FrameUI
{
    /// <summary>A frame that draws a line of text on top of its own fill / border.</summary>
    public sealed class FigmaText : FigmaFrame
    {
        /// <summary>The font used by text instances that do not provide an explicit font.</summary>
        public static Font DefaultFont { get; set; }

        public string Text;

        // Frame trees are usually rebuilt on every IMGUI event, so everything expensive is shared between instances:
        // one GUIStyle per distinct look, and the measured size / letter-spacing pieces per (style, text).
        static readonly GUIContent Content = new GUIContent();
        static readonly Dictionary<StyleKey, GUIStyle> Styles = new Dictionary<StyleKey, GUIStyle>();
        static readonly Dictionary<(GUIStyle, string), Vector2> Sizes = new Dictionary<(GUIStyle, string), Vector2>();
        static readonly Dictionary<(GUIStyle, string), PieceSet> PieceSets = new Dictionary<(GUIStyle, string), PieceSet>();
        const int MaxCachedTexts = 4096;

        readonly struct StyleKey : IEquatable<StyleKey>
        {
            readonly GUIStyle _base;
            readonly Font _font;
            readonly int _size;
            readonly TextAnchor _align;
            readonly FontStyle _fontStyle;
            readonly bool _wrap, _rich;

            public StyleKey(GUIStyle baseStyle, Font font, int size, TextAnchor align, FontStyle fontStyle, bool wrap, bool rich)
            {
                _base = baseStyle; _font = font; _size = size; _align = align; _fontStyle = fontStyle; _wrap = wrap; _rich = rich;
            }

            public bool Equals(StyleKey o) =>
                ReferenceEquals(_base, o._base) && ReferenceEquals(_font, o._font) && _size == o._size && _align == o._align
                && _fontStyle == o._fontStyle && _wrap == o._wrap && _rich == o._rich;

            public override bool Equals(object obj) => obj is StyleKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _base != null ? _base.GetHashCode() : 0;
                    h = h * 31 + (_font != null ? _font.GetHashCode() : 0);
                    h = h * 31 + _size;
                    h = h * 31 + (int)_align;
                    h = h * 31 + (int)_fontStyle;
                    return (h * 31 + (_wrap ? 1 : 0)) * 31 + (_rich ? 1 : 0);
                }
            }
        }

        GUIStyle _baseStyle;               // the skin style the shared style derives from
        GUIStyle _owned;                   // private style when constructed from a GUIStyle (not cached or shared)
        GUIStyle _resolved;                // shared style for the current settings; null after a setting changed
        Font _font;
        int _fontSize;
        TextAnchor _align;
        FontStyle _fontStyle;
        bool _wordWrap, _richText;
        Color _color;

        public int FontSize { get => _fontSize; set { _fontSize = value; Invalidate(); } }
        public Font Font { get => _font; set { _font = value; Invalidate(); } }
        public Color Color { get => _color; set => _color = value; }
        public TextAnchor Align { get => _align; set { _align = value; Invalidate(); } }
        public FontStyle FontStyle { get => _fontStyle; set { _fontStyle = value; Invalidate(); } }
        public bool WordWrap { get => _wordWrap; set { _wordWrap = value; Invalidate(); } }
        /// <summary>Enables &lt;b&gt;, &lt;i&gt;, Gui.Colored(...) spans and &lt;cspace=0.3em&gt;...&lt;/cspace&gt; letter spacing (em or px) inside Text.</summary>
        public bool RichText { get => _richText; set { _richText = value; Invalidate(); } }

        void Invalidate() => _resolved = null;

        GUIStyle Style
        {
            get
            {
                if (_resolved != null) return _resolved;
                if (_owned != null) return _resolved = Apply(_owned);

                var key = new StyleKey(_baseStyle, _font, _fontSize, _align, _fontStyle, _wordWrap, _richText);
                if (!Styles.TryGetValue(key, out var style)) Styles[key] = style = Apply(new GUIStyle(_baseStyle));
                return _resolved = style;
            }
        }

        GUIStyle Apply(GUIStyle style)
        {
            style.font = _font;
            style.fontSize = _fontSize;
            style.alignment = _align;
            style.fontStyle = _fontStyle;
            style.wordWrap = _wordWrap;
            style.richText = _richText;
            return style;
        }

        /// <summary>Owns a private copy of the style, so changing FontSize/Color/Align never affects other texts.</summary>
        public FigmaText(Rect bounds, string text, GUIStyle style) : base(bounds)
        {
            Text = text;
            _owned = new GUIStyle(style);
            _font = style.font != null ? style.font : DefaultFont;
            _fontSize = style.fontSize;
            _align = style.alignment;
            _fontStyle = style.fontStyle;
            _wordWrap = style.wordWrap;
            _richText = style.richText;
            _color = style.normal.textColor;
        }

        public FigmaText(Rect bounds, string text, int fontSize, Color color, TextAnchor align = TextAnchor.UpperLeft) : base(bounds)
        {
            Text = text;
            _baseStyle = GUI.skin.label;
            _font = DefaultFont ?? _baseStyle.font;
            _fontSize = fontSize;
            _align = align;
            _color = color;
        }

        static readonly Regex TagRx = new Regex(@"<(/?)(\w+)(?:=([^>]*))?>", RegexOptions.Compiled);

        struct Piece { public string Markup; public float Width; public float Spacing; }

        sealed class PieceSet
        {
            public readonly List<Piece> Pieces = new List<Piece>();
            public float Height;
            public float Total;
        }

        GUIStyle _measuredStyle;
        string _measuredText;
        Vector2 _measuredSize;

        bool HasSpacing => _richText && Text != null && Text.IndexOf("<cspace", StringComparison.OrdinalIgnoreCase) >= 0;

        protected override Vector2 Measure()
        {
            var style = Style;
            if (!HasSpacing)
            {
                string text = Text ?? "";
                if (_owned != null)
                {
                    Content.text = text;
                    return style.CalcSize(Content);
                }

                // The same tree is measured more than once per event (ContentSize, then Render).
                if (ReferenceEquals(_measuredStyle, style) && ReferenceEquals(_measuredText, text)) return _measuredSize;

                var key = (style, text);
                if (!Sizes.TryGetValue(key, out var size))
                {
                    if (Sizes.Count >= MaxCachedTexts) Sizes.Clear();
                    Content.text = text;
                    Sizes[key] = size = style.CalcSize(Content);
                }
                _measuredStyle = style;
                _measuredText = text;
                return _measuredSize = size;
            }
            var set = BuildPieces(style);
            return new Vector2(set.Total, set.Height);
        }

        // Splits Text into runs; characters inside a cspace tag become single-glyph pieces so each can be pushed apart.
        PieceSet BuildPieces(GUIStyle style)
        {
            var cacheKey = (style, Text);
            bool cacheable = _owned == null;
            if (cacheable)
            {
                if (PieceSets.TryGetValue(cacheKey, out var cached)) return cached;
                if (PieceSets.Count >= MaxCachedTexts) PieceSets.Clear();
            }

            var set = new PieceSet();
            var pieces = set.Pieces;

            float em = style.fontSize > 0 ? style.fontSize : 14f;
            var open = new List<KeyValuePair<string, string>>();
            float spacing = 0f;
            var run = new StringBuilder();
            int pos = 0;

            void Flush()
            {
                if (run.Length == 0) return;
                string text = run.ToString();
                run.Length = 0;
                if (spacing <= 0f) AddPiece(style, set, open, text, 0f);
                else foreach (char c in text) AddPiece(style, set, open, c.ToString(), spacing);
            }

            foreach (Match m in TagRx.Matches(Text))
            {
                run.Append(Text, pos, m.Index - pos);
                pos = m.Index + m.Length;
                bool closing = m.Groups[1].Length > 0;
                string name = m.Groups[2].Value.ToLowerInvariant();

                if (name == "cspace")
                {
                    Flush();
                    spacing = closing ? 0f : ParseSpacing(m.Groups[3].Value, em);
                    continue;
                }

                // Unknown tags stay in the run so IMGUI treats them as it normally would.
                if (name != "b" && name != "i" && name != "color" && name != "size") { run.Append(m.Value); continue; }

                Flush();
                if (!closing) open.Add(new KeyValuePair<string, string>(name, m.Value));
                else for (int i = open.Count - 1; i >= 0; i--)
                    if (open[i].Key == name) { open.RemoveAt(i); break; }
            }
            run.Append(Text, pos, Text.Length - pos);
            Flush();

            foreach (var piece in pieces) set.Total += piece.Width + piece.Spacing;
            if (cacheable) PieceSets[cacheKey] = set;
            return set;
        }

        static void AddPiece(GUIStyle style, PieceSet set, List<KeyValuePair<string, string>> open, string text, float spacing)
        {
            var sb = new StringBuilder();
            foreach (var t in open) sb.Append(t.Value);
            sb.Append(text);
            for (int i = open.Count - 1; i >= 0; i--) sb.Append("</").Append(open[i].Key).Append('>');
            Content.text = sb.ToString();
            var size = style.CalcSize(Content);
            if (size.y > set.Height) set.Height = size.y;
            set.Pieces.Add(new Piece { Markup = Content.text, Width = size.x, Spacing = spacing });
        }

        static float ParseSpacing(string value, float em)
        {
            value = value.Trim().Trim('"');
            bool isEm = value.EndsWith("em");
            if (isEm || value.EndsWith("px")) value = value.Substring(0, value.Length - 2);
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0f;
            return isEm ? v * em : v;
        }

        protected override void Draw(Rect rect, Color tint)
        {
            base.Draw(rect, tint);
            var style = Style;
            style.normal.textColor = Gui.TextColor(_color) * tint;
            if (!HasSpacing)
            {
                Content.text = Text ?? "";
                GUI.Label(rect, Content, style);
                return;
            }

            var set = BuildPieces(style);
            var pieces = set.Pieces;
            float total = set.Total;

            var original = style.alignment;
            float x = rect.x;
            switch (original)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: x += (rect.width - total) * 0.5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x += rect.width - total; break;
            }

            // Each piece is drawn left-aligned at its own x, keeping the vertical alignment. The style is shared,
            // so the alignment is put back before returning.
            style.alignment = original <= TextAnchor.UpperRight ? TextAnchor.UpperLeft
                : original <= TextAnchor.MiddleRight ? TextAnchor.MiddleLeft : TextAnchor.LowerLeft;
            for (int i = 0; i < pieces.Count; i++)
            {
                Content.text = pieces[i].Markup;
                GUI.Label(new Rect(x, rect.y, pieces[i].Width + 1f, rect.height), Content, style);
                x += pieces[i].Width + pieces[i].Spacing;
            }
            style.alignment = original;
        }
    }
}
