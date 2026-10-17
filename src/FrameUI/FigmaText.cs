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
        readonly GUIStyle _style;
        readonly GUIContent _content = new GUIContent();
        Color _color;

        public int FontSize { get => _style.fontSize; set => _style.fontSize = value; }
        public Font Font { get => _style.font; set => _style.font = value; }
        public Color Color { get => _color; set => _style.normal.textColor = Gui.TextColor(_color = value); }
        public TextAnchor Align { get => _style.alignment; set => _style.alignment = value; }
        public FontStyle FontStyle { get => _style.fontStyle; set => _style.fontStyle = value; }
        public bool WordWrap { get => _style.wordWrap; set => _style.wordWrap = value; }
        /// <summary>Enables &lt;b&gt;, &lt;i&gt;, Gui.Colored(...) spans and &lt;cspace=0.3em&gt;...&lt;/cspace&gt; letter spacing (em or px) inside Text.</summary>
        public bool RichText { get => _style.richText; set => _style.richText = value; }

        /// <summary>Owns a private copy of the style, so changing FontSize/Color/Align never affects other texts.</summary>
        public FigmaText(Rect bounds, string text, GUIStyle style) : base(bounds)
        {
            Text = text;
            _style = new GUIStyle(style);
            if (_style.font == null) _style.font = DefaultFont;
            Color = style.normal.textColor;
        }

        public FigmaText(Rect bounds, string text, int fontSize, Color color, TextAnchor align = TextAnchor.UpperLeft) : base(bounds)
        {
            Text = text;
            _style = new GUIStyle(GUI.skin.label) { font = DefaultFont ?? GUI.skin.label.font, fontSize = fontSize, alignment = align, wordWrap = false };
            Color = color;
        }

        static readonly Regex TagRx = new Regex(@"<(/?)(\w+)(?:=([^>]*))?>", RegexOptions.Compiled);

        struct Piece { public string Markup; public float Width; public float Spacing; }

        readonly List<Piece> _pieces = new List<Piece>();
        string _cacheText;
        int _cacheSize;
        FontStyle _cacheFontStyle;
        float _cacheHeight;

        bool HasSpacing => RichText && Text != null && Text.IndexOf("<cspace", System.StringComparison.OrdinalIgnoreCase) >= 0;

        protected override Vector2 Measure()
        {
            if (!HasSpacing)
            {
                _content.text = Text ?? "";
                return _style.CalcSize(_content);
            }
            BuildPieces();
            float w = 0f;
            for (int i = 0; i < _pieces.Count; i++) w += _pieces[i].Width + _pieces[i].Spacing;
            return new Vector2(w, _cacheHeight);
        }

        // Splits Text into runs; characters inside a cspace tag become single-glyph pieces so each can be pushed apart.
        void BuildPieces()
        {
            if (_cacheText == Text && _cacheSize == _style.fontSize && _cacheFontStyle == _style.fontStyle) return;
            _cacheText = Text; _cacheSize = _style.fontSize; _cacheFontStyle = _style.fontStyle;
            _pieces.Clear();
            _cacheHeight = 0f;

            float em = _style.fontSize > 0 ? _style.fontSize : 14f;
            var open = new List<KeyValuePair<string, string>>();
            float spacing = 0f;
            var run = new StringBuilder();
            int pos = 0;

            void Flush()
            {
                if (run.Length == 0) return;
                string text = run.ToString();
                run.Length = 0;
                if (spacing <= 0f) AddPiece(open, text, 0f);
                else foreach (char c in text) AddPiece(open, c.ToString(), spacing);
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
        }

        void AddPiece(List<KeyValuePair<string, string>> open, string text, float spacing)
        {
            var sb = new StringBuilder();
            foreach (var t in open) sb.Append(t.Value);
            sb.Append(text);
            for (int i = open.Count - 1; i >= 0; i--) sb.Append("</").Append(open[i].Key).Append('>');
            _content.text = sb.ToString();
            var size = _style.CalcSize(_content);
            if (size.y > _cacheHeight) _cacheHeight = size.y;
            _pieces.Add(new Piece { Markup = _content.text, Width = size.x, Spacing = spacing });
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
            _content.text = Text ?? "";
            _style.normal.textColor = Gui.TextColor(_color) * tint;
            if (!HasSpacing)
            {
                GUI.Label(rect, _content, _style);
                return;
            }

            BuildPieces();
            float total = 0f;
            for (int i = 0; i < _pieces.Count; i++) total += _pieces[i].Width + _pieces[i].Spacing;

            var original = _style.alignment;
            float x = rect.x;
            switch (original)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: x += (rect.width - total) * 0.5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x += rect.width - total; break;
            }

            // Each piece is drawn left-aligned at its own x, keeping the vertical alignment.
            _style.alignment = original <= TextAnchor.UpperRight ? TextAnchor.UpperLeft
                : original <= TextAnchor.MiddleRight ? TextAnchor.MiddleLeft : TextAnchor.LowerLeft;
            for (int i = 0; i < _pieces.Count; i++)
            {
                _content.text = _pieces[i].Markup;
                GUI.Label(new Rect(x, rect.y, _pieces[i].Width + 1f, rect.height), _content, _style);
                x += _pieces[i].Width + _pieces[i].Spacing;
            }
            _style.alignment = original;
        }
    }
}
