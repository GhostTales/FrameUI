using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrameUI
{
    /// <summary>
    /// The only building block: a rectangle with optional fill, border, corner radii, auto layout and children.
    /// Buttons, progress bars, cards and so on are just frames that are configured and nested.
    /// Build the tree, then call Render(parent) from inside OnGUI / a GUI.Window function on every event.
    /// </summary>
    public partial class FigmaFrame
    {
        const int ControlHint = 0x46524D45;
        static readonly Color WireColor = new Color(0f, 1f, 1f, 0.9f);

        /// <summary>Debug view: every frame is drawn as an outline instead of its fill/border.</summary>
        public static bool Wireframe;

        public Rect Bounds; // left, top, width, height
        public Color? Fill;
        public Color? Border;
        /// <summary>Per-side colour overrides; a side left null uses Border.</summary>
        public Color? BorderLeft, BorderTop, BorderRight, BorderBottom;
        public Vector4 BorderWidths;       // left, top, right, bottom
        public Vector4 CornerRadii;        // top-left, top-right, bottom-right, bottom-left

        /// <summary>Uniform shorthand that sets all four border widths.</summary>
        public float BorderWidth { get => BorderWidths.x; set => BorderWidths = new Vector4(value, value, value, value); }
        /// <summary>Uniform shorthand that sets all four corner radii.</summary>
        public float CornerRadius { get => CornerRadii.x; set => CornerRadii = new Vector4(value, value, value, value); }

        // Auto layout (mirrors Figma's panel). Layout.None keeps absolute Bounds positioning.
        public FigmaLayout Layout;
        public Vector4 Padding;            // left, top, right, bottom
        public float Gap;                  // Horizontal / Vertical
        public FigmaAlign MainAlign;       // Horizontal / Vertical: distribution along the direction
        public FigmaAlign CrossAlign;      // Horizontal / Vertical: placement across (SpaceBetween = Start)

        // Grid. Tracks beyond the defined ones default to 1fr; if ColumnTracks is empty, Columns x 1fr is used.
        public int Columns = 1;
        public FigmaTrack[] ColumnTracks;
        public FigmaTrack[] RowTracks;
        public float ColumnGap;
        public float RowGap;

        // Used when this frame is a child of an auto layout / grid frame.
        public FigmaSize WidthSize;
        public FigmaSize HeightSize;
        public float FillWeight = 1f;      // stack: share among Fill siblings
        public int ColumnSpan = 1;         // grid
        public int RowSpan = 1;            // grid
        public FigmaAlign CellAlignX;      // grid: position inside the cell when not Fill
        public FigmaAlign CellAlignY;

        // Interaction. Any frame can be clickable / hoverable; the Hover*/Pressed* overrides fall back to the
        // previous state (Pressed -> Hover -> idle value). Tint multiplies the colours of this frame and all its
        // descendants, so a button can dim or brighten its whole content at once.
        public Action Clicked;
        public Action Hovered;             // called on every repaint while the pointer is over the frame
        public Color Tint = Color.white;
        public Color? HoverTint, PressedTint;
        public Color? HoverFill, PressedFill;
        public Color? HoverBorder, PressedBorder;

        /// <summary>Pointer state, updated on every repaint. Pressed requires the frame to be clickable.</summary>
        public FigmaState State { get; private set; }

        /// <summary>Position and size resolved by the last Render, in the coordinates of the surrounding GUI.</summary>
        public Rect LayoutRect { get; private set; }

        public readonly List<FigmaFrame> Children = new List<FigmaFrame>();

        int _controlId;

        public FigmaFrame(Rect bounds)
        {
            Bounds = bounds;
        }

        public T Add<T>(T child) where T : FigmaFrame
        {
            Children.Add(child);
            return child;
        }

        /// <summary>
        /// Lays the tree out inside parent, handles mouse input and paints. Call it on every GUI event
        /// (Layout, Repaint and mouse events) with a tree of the same shape.
        /// </summary>
        public void Render(Rect parent)
        {
            Arrange(parent);
            AssignControls();

            var e = Event.current;
            if (e.isMouse) Dispatch(e);
            if (e.type == EventType.Repaint) Paint(Color.white);
        }

        // Measures and places the whole tree without touching IMGUI.
        internal void Arrange(Rect parent)
        {
            MeasureTree();
            PlaceAt(new Rect(parent.x + Bounds.x, parent.y + Bounds.y, Bounds.width, Bounds.height));
        }

        // Control ids must be requested in the same order on every event, so they are handed out in tree order.
        void AssignControls()
        {
            _controlId = Clicked != null ? GUIUtility.GetControlID(ControlHint, FocusType.Passive) : 0;
            for (int i = 0; i < Children.Count; i++) Children[i].AssignControls();
        }

        // ---- Input ----

        // Top-most frames (drawn last) see the event first and use it up, so they win over what is beneath them.
        void Dispatch(Event e)
        {
            for (int i = Children.Count - 1; i >= 0; i--) Children[i].Dispatch(e);
            if (_controlId == 0) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && GUIUtility.hotControl == 0 && LayoutRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = _controlId;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _controlId)
                    {
                        GUIUtility.hotControl = 0;
                        bool inside = LayoutRect.Contains(e.mousePosition);
                        e.Use();
                        // A click is a press and a release on the same frame.
                        if (inside) Clicked();
                    }
                    break;
            }
        }

        bool OwnsControl(int id)
        {
            if (_controlId == id) return true;
            for (int i = 0; i < Children.Count; i++)
                if (Children[i].OwnsControl(id)) return true;
            return false;
        }

        void UpdateState()
        {
            int hot = GUIUtility.hotControl;
            bool inside = LayoutRect.Contains(Event.current.mousePosition);
            // While something else holds the mouse (window drag, scrollbar...) nothing else reacts to it.
            bool hover = inside && (hot == 0 || OwnsControl(hot));
            State = !hover ? FigmaState.Idle : _controlId != 0 && hot == _controlId ? FigmaState.Pressed : FigmaState.Hover;
        }

        // Picks the value for the current pointer state.
        T? ForState<T>(T? idle, T? hover, T? pressed) where T : struct
        {
            switch (State)
            {
                case FigmaState.Pressed: return pressed ?? hover ?? idle;
                case FigmaState.Hover: return hover ?? idle;
                default: return idle;
            }
        }

        // ---- Painting ----

        void Paint(Color inherited)
        {
            UpdateState();
            var tint = inherited * ForState<Color>(Tint, HoverTint, PressedTint).Value;

            Draw(LayoutRect, tint);
            if (Wireframe) Gui.Outline(LayoutRect, WireColor, 1f);
            for (int i = 0; i < Children.Count; i++) Children[i].Paint(tint);

            if (State != FigmaState.Idle) Hovered?.Invoke();
        }

        /// <summary>Draws this frame's own fill and border; subclasses add their content after calling the base.</summary>
        protected virtual void Draw(Rect rect, Color tint)
        {
            if (Wireframe) return;

            bool rounded = CornerRadii != Vector4.zero;
            var fill = ForState<Color>(Fill, HoverFill, PressedFill);
            var border = ForState<Color>(Border, HoverBorder, PressedBorder);
            var left = BorderLeft ?? border;
            var top = BorderTop ?? border;
            var right = BorderRight ?? border;
            var bottom = BorderBottom ?? border;
            var baseColor = border ?? left ?? top ?? right ?? bottom;
            var b = BorderWidths;

            if (baseColor.HasValue && !fill.HasValue && !rounded)
            {
                // No fill to punch a hole into, so draw only the sides that have a colour and a width.
                if (top.HasValue) Gui.Round(new Rect(rect.x, rect.y, rect.width, b.y), top.Value * tint, 0f);
                if (bottom.HasValue) Gui.Round(new Rect(rect.x, rect.yMax - b.w, rect.width, b.w), bottom.Value * tint, 0f);
                if (left.HasValue) Gui.Round(new Rect(rect.x, rect.y + b.y, b.x, rect.height - b.y - b.w), left.Value * tint, 0f);
                if (right.HasValue) Gui.Round(new Rect(rect.xMax - b.z, rect.y + b.y, b.z, rect.height - b.y - b.w), right.Value * tint, 0f);
                return;
            }

            if (baseColor.HasValue)
            {
                // The border is the whole rectangle; the fill is painted over it, inset by the border widths.
                // Without a fill only the ring is drawn, so the inside stays see-through.
                if (fill.HasValue) Gui.Round(rect, baseColor.Value * tint, CornerRadii);
                else Gui.RoundRing(rect, baseColor.Value * tint, CornerRadii, b);
                // Sides with their own colour are painted over the base, stopping short of the rounded corners.
                var r = CornerRadii;
                if (BorderTop.HasValue) Gui.Round(new Rect(rect.x + r.x, rect.y, rect.width - r.x - r.y, b.y), BorderTop.Value * tint, 0f);
                if (BorderBottom.HasValue) Gui.Round(new Rect(rect.x + r.w, rect.yMax - b.w, rect.width - r.w - r.z, b.w), BorderBottom.Value * tint, 0f);
                if (BorderLeft.HasValue) Gui.Round(new Rect(rect.x, rect.y + r.x, b.x, rect.height - r.x - r.w), BorderLeft.Value * tint, 0f);
                if (BorderRight.HasValue) Gui.Round(new Rect(rect.xMax - b.z, rect.y + r.y, b.z, rect.height - r.y - r.z), BorderRight.Value * tint, 0f);
            }

            if (!fill.HasValue) return;
            var w = baseColor.HasValue ? b : Vector4.zero;
            var fillRect = new Rect(rect.x + w.x, rect.y + w.y, rect.width - w.x - w.z, rect.height - w.y - w.w);
            // An inner corner shrinks by the thicker of its two adjoining borders.
            var inner = new Vector4(
                Mathf.Max(0f, CornerRadii.x - Mathf.Max(w.x, w.y)),
                Mathf.Max(0f, CornerRadii.y - Mathf.Max(w.z, w.y)),
                Mathf.Max(0f, CornerRadii.z - Mathf.Max(w.z, w.w)),
                Mathf.Max(0f, CornerRadii.w - Mathf.Max(w.x, w.w)));
            Gui.Round(fillRect, fill.Value * tint, inner);
        }
    }
}
