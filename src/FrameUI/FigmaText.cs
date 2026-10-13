using UnityEngine;

namespace FrameUI
{
    /// <summary>A frame that draws a line of text on top of its own fill / border.</summary>
    public sealed class FigmaText : FigmaFrame
    {
        public string Text;
        readonly GUIStyle _style;
        readonly GUIContent _content = new GUIContent();
        Color _color;

        public int FontSize { get => _style.fontSize; set => _style.fontSize = value; }
        public Color Color { get => _color; set => _style.normal.textColor = Gui.TextColor(_color = value); }
        public TextAnchor Align { get => _style.alignment; set => _style.alignment = value; }
        public FontStyle FontStyle { get => _style.fontStyle; set => _style.fontStyle = value; }
        public bool WordWrap { get => _style.wordWrap; set => _style.wordWrap = value; }
        /// <summary>Enables &lt;b&gt;, &lt;i&gt; and Gui.Colored(...) spans inside Text.</summary>
        public bool RichText { get => _style.richText; set => _style.richText = value; }

        /// <summary>Owns a private copy of the style, so changing FontSize/Color/Align never affects other texts.</summary>
        public FigmaText(Rect bounds, string text, GUIStyle style) : base(bounds)
        {
            Text = text;
            _style = new GUIStyle(style);
            Color = style.normal.textColor;
        }

        public FigmaText(Rect bounds, string text, int fontSize, Color color, TextAnchor align = TextAnchor.UpperLeft) : base(bounds)
        {
            Text = text;
            _style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = align, wordWrap = false };
            Color = color;
        }

        protected override Vector2 Measure()
        {
            _content.text = Text ?? "";
            return _style.CalcSize(_content);
        }

        protected override void Draw(Rect rect, Color tint)
        {
            base.Draw(rect, tint);
            _content.text = Text ?? "";
            _style.normal.textColor = Gui.TextColor(_color) * tint;
            GUI.Label(rect, _content, _style);
        }
    }
}
