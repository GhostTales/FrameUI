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
        public float MinWidth;             // Hug / Fill: content size never goes below this (Bounds is ignored when not Fixed)
        public float MinHeight;
        public float FillWeight = 1f;      // stack: share among Fill siblings
        public int ColumnSpan = 1;         // grid
        public int RowSpan = 1;            // grid
        public FigmaAlign CellAlignX;      // grid: position inside the cell when not Fill
        public FigmaAlign CellAlignY;

        // Interaction. Any frame can be clickable / hoverable; the Hover*/Pressed* overrides fall back to the
        // previous state (Pressed -> Hover -> idle value). Tint multiplies the colours of this frame and all its
        // descendants, so a button can dim or brighten its whole content at once.
        /// <summary>Clips the children to this frame's rectangle (they, and their input, are cut off outside it).</summary>
        public bool ClipContent;
        /// <summary>Moves the children up by this many units; together with ClipContent it makes a vertical scroll viewport.</summary>
        public float ScrollY;

        public Action Clicked;
        public Action Hovered;             // called on every repaint while the pointer is over the frame
        public Color Tint = Color.white;

        // Rarely used colour overrides live in a separate object that is only created when one is set, so the
        // thousands of plain frames a big list creates per event stay small.
        sealed class Overrides
        {
            public Color? BorderLeft, BorderTop, BorderRight, BorderBottom;
            public Color? HoverTint, PressedTint, HoverFill, PressedFill, HoverBorder, PressedBorder;
        }

        Overrides _overrides;
        Overrides Ov => _overrides ?? (_overrides = new Overrides());

        /// <summary>Per-side colour overrides; a side left null uses Border.</summary>
        public Color? BorderLeft { get => _overrides?.BorderLeft; set { if (value.HasValue || _overrides != null) Ov.BorderLeft = value; } }
        public Color? BorderTop { get => _overrides?.BorderTop; set { if (value.HasValue || _overrides != null) Ov.BorderTop = value; } }
        public Color? BorderRight { get => _overrides?.BorderRight; set { if (value.HasValue || _overrides != null) Ov.BorderRight = value; } }
        public Color? BorderBottom { get => _overrides?.BorderBottom; set { if (value.HasValue || _overrides != null) Ov.BorderBottom = value; } }
        public Color? HoverTint { get => _overrides?.HoverTint; set { if (value.HasValue || _overrides != null) Ov.HoverTint = value; } }
        public Color? PressedTint { get => _overrides?.PressedTint; set { if (value.HasValue || _overrides != null) Ov.PressedTint = value; } }
        public Color? HoverFill { get => _overrides?.HoverFill; set { if (value.HasValue || _overrides != null) Ov.HoverFill = value; } }
        public Color? PressedFill { get => _overrides?.PressedFill; set { if (value.HasValue || _overrides != null) Ov.PressedFill = value; } }
        public Color? HoverBorder { get => _overrides?.HoverBorder; set { if (value.HasValue || _overrides != null) Ov.HoverBorder = value; } }
        public Color? PressedBorder { get => _overrides?.PressedBorder; set { if (value.HasValue || _overrides != null) Ov.PressedBorder = value; } }

        /// <summary>Pointer state, updated on every repaint. Pressed requires the frame to be clickable.</summary>
        public FigmaState State { get; private set; }

        /// <summary>Position and size resolved by the last Render, in the coordinates of the surrounding GUI.</summary>
        public Rect LayoutRect { get; private set; }

        static readonly List<FigmaFrame> NoChildren = new List<FigmaFrame>();
        List<FigmaFrame> _children;

        /// <summary>The child frames. The list is created on first use, so leaf frames don't pay for one.</summary>
        public List<FigmaFrame> Children => _children ?? (_children = new List<FigmaFrame>());

        // Read-only view for the engine itself; never hand it out or add to it.
        List<FigmaFrame> Kids => _children ?? NoChildren;

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
            // Other events (scroll wheel, keys, Used...) neither paint nor hit-test frames, so skip the layout work.
            var e = Event.current;
            if (e.type != EventType.Layout && e.type != EventType.Repaint && !e.isMouse) return;

            Arrange(parent);
            AssignControls();

            var outerView = _view;
            _view = CullOffscreen ? parent : (Rect?)null;
            try
            {
                if (e.isMouse)
                {
                    // While something holds the mouse, its release must still reach it even if it was scrolled out of view.
                    _cullInput = e.type == EventType.MouseDown || GUIUtility.hotControl == 0;
                    Dispatch(e);
                }
                if (e.type != EventType.Repaint) return;

                _mouse = e.mousePosition;
                _hot = GUIUtility.hotControl;
                Paint(Color.white);
            }
            finally { _view = outerView; }
        }

        static bool _cullInput;
        static Vector2 _mouse;
        static int _hot;

        /// <summary>
        /// On the frame passed to Render: skips painting descendants that lie completely outside parent (which is
        /// normally the window, so they would be clipped anyway). Keeps long lists cheap. Skipped frames don't
        /// update their State or call Hovered. Descendants of a ClipContent frame are likewise skipped outside it.
        /// </summary>
        public bool CullOffscreen = true;

        static Rect? _view;

        // Measures and places the whole tree without touching IMGUI.
        internal void Arrange(Rect parent)
        {
            MeasureTree();
            _placeView = CullOffscreen ? parent : (Rect?)null;
            try { PlaceAt(new Rect(parent.x + Bounds.x, parent.y + Bounds.y, Bounds.width, Bounds.height)); }
            finally { _placeView = null; }
        }

        // Control ids must be requested in the same order on every event, so they are handed out in tree order.
        void AssignControls()
        {
            _controlId = Clicked != null ? GUIUtility.GetControlID(ControlHint, FocusType.Passive) : 0;
            for (int i = 0; i < Kids.Count; i++) Kids[i].AssignControls();
        }

        // ---- Input ----

        // Top-most frames (drawn last) see the event first and use it up, so they win over what is beneath them.
        void Dispatch(Event e)
        {
            var outerClip = _clip;
            if (ClipContent) _clip = Intersect(outerClip, LayoutRect);
            for (int i = Kids.Count - 1; i >= 0; i--)
            {
                var child = Kids[i];
                if (_cullInput && !child.IsVisible()) continue;
                child.Dispatch(e);
            }
            _clip = outerClip;
            if (_controlId == 0) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && GUIUtility.hotControl == 0 && LayoutRect.Contains(e.mousePosition) && InsideClip(e.mousePosition))
                    {
                        GUIUtility.hotControl = _controlId;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _controlId)
                    {
                        GUIUtility.hotControl = 0;
                        bool inside = LayoutRect.Contains(e.mousePosition) && InsideClip(e.mousePosition);
                        e.Use();
                        // A click is a press and a release on the same frame.
                        if (inside) Clicked();
                    }
                    break;
            }
        }

        // Visible area imposed by the clipping ancestors while dispatching / painting.
        static Rect? _clip;

        static bool InsideClip(Vector2 point) => !_clip.HasValue || _clip.Value.Contains(point);

        static Rect Intersect(Rect? outer, Rect inner)
        {
            if (!outer.HasValue) return inner;
            var o = outer.Value;
            float xMin = Mathf.Max(o.xMin, inner.xMin), yMin = Mathf.Max(o.yMin, inner.yMin);
            float xMax = Mathf.Min(o.xMax, inner.xMax), yMax = Mathf.Min(o.yMax, inner.yMax);
            return new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
        }

        bool OwnsControl(int id)
        {
            if (_controlId == id) return true;
            for (int i = 0; i < Kids.Count; i++)
                if (Kids[i].OwnsControl(id)) return true;
            return false;
        }

        void UpdateState()
        {
            int hot = _hot;
            bool inside = LayoutRect.Contains(_mouse) && InsideClip(_mouse);
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

        // False when nothing of this subtree can show inside the clipping ancestors / the rendered area.
        bool IsVisible()
        {
            if (_view.HasValue && !Overlaps(_view.Value, _visual)) return false;
            return !_clip.HasValue || Overlaps(_clip.Value, _visual);
        }

        void Paint(Color inherited)
        {
            UpdateState();
            var tint = inherited * ForState<Color>(Tint, HoverTint, PressedTint).Value;

            Draw(LayoutRect, tint);
            if (Wireframe) Gui.Outline(LayoutRect, WireColor, 1f);

            var outerClip = _clip;
            bool clipped = ClipContent && Gui.BeginClip(LayoutRect);
            if (ClipContent) _clip = Intersect(outerClip, LayoutRect);
            for (int i = 0; i < Kids.Count; i++)
            {
                var child = Kids[i];
                if (!child.IsVisible()) continue;
                child.Paint(tint);
            }
            if (ClipContent) _clip = outerClip;
            if (clipped) Gui.EndClip();

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
