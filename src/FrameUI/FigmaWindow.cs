using UnityEngine;

namespace FrameUI
{
    /// <summary>
    /// A draggable, optionally resizable and content-sized GUI.Window that hosts a frame tree.
    /// Call Render from OnGUI; inside the window function build/render the frames and finish with GUI.DragWindow().
    /// </summary>
    public sealed class FigmaWindow
    {
        readonly int _id;
        readonly GUI.WindowFunction _draw;
        readonly Vector2 _minimumSize;
        int _resizeCorner;
        Vector2 _resizeStartMouse;
        Rect _resizeStartRect;
        Vector2? _pendingFit;

        internal int LastRenderFrame { get; private set; } = -1;

        public Rect Rect { get; private set; }
        /// <summary>Lets the window be resized by dragging within ResizeGrip of any of its corners.</summary>
        public bool Resizable { get; set; }
        public float ResizeGrip { get; set; } = 14f;
        /// <summary>How much of the window must stay on screen when it is dragged away.</summary>
        public float ScreenMargin { get; set; } = 40f;
        /// <summary>Makes the window follow its content size on the enabled axes (see FitTo).</summary>
        public bool AutoWidth { get; set; }
        public bool AutoHeight { get; set; }

        public FigmaWindow(int id, Rect initialRect, Vector2 minimumSize, GUI.WindowFunction draw)
        {
            _id = id;
            Rect = initialRect;
            _minimumSize = minimumSize;
            _draw = draw;
            ClickThrough.Register(this);
        }

        /// <summary>Stops the window from blocking clicks. Call when the window is no longer used.</summary>
        public void Destroy() => ClickThrough.Unregister(this);

        public void SetSize(float width, float height)
        {
            Rect = new Rect(Rect.x, Rect.y, Mathf.Max(_minimumSize.x, width), Mathf.Max(_minimumSize.y, height));
        }

        /// <summary>
        /// Requests the window to take the size of the frame tree on the AutoWidth / AutoHeight axes. Call it while
        /// drawing; the size is applied after GUI.Window returns (so it isn't overwritten) and shows on the next repaint.
        /// </summary>
        public void FitTo(FigmaFrame root)
        {
            if (AutoWidth || AutoHeight) _pendingFit = root.ContentSize();
        }

        /// <summary>True if a point in screen GUI coordinates (origin top-left) is over the window.</summary>
        public bool Contains(Vector2 position) => Rect.Contains(position);

        public void Render()
        {
            LastRenderFrame = Time.frameCount;

            // Everything below works in design units; the matrix maps them to the screen and also converts the
            // mouse position back, so dragging, resizing and clicks need no extra handling.
            float scale = Gui.Scale;
            var previousMatrix = GUI.matrix;
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                RenderScaled(Screen.width / scale, Screen.height / scale);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        void RenderScaled(float screenWidth, float screenHeight)
        {
            Resize();

            float maxWidth = Mathf.Max(_minimumSize.x, screenWidth);
            float maxHeight = Mathf.Max(_minimumSize.y, screenHeight);
            Rect = new Rect(
                Rect.x,
                Rect.y,
                Mathf.Clamp(Rect.width, Mathf.Min(_minimumSize.x, maxWidth), maxWidth),
                Mathf.Clamp(Rect.height, Mathf.Min(_minimumSize.y, maxHeight), maxHeight));
            Rect = GUI.Window(_id, Rect, _draw, "", GUIStyle.none);
            if (_pendingFit is Vector2 fit)
            {
                _pendingFit = null;
                SetSize(AutoWidth ? fit.x : Rect.width, AutoHeight ? fit.y : Rect.height);
            }
            Rect = new Rect(
                Mathf.Clamp(Rect.x, 0f, Mathf.Max(0f, screenWidth - ScreenMargin)),
                Mathf.Clamp(Rect.y, 0f, Mathf.Max(0f, screenHeight - ScreenMargin)),
                Rect.width,
                Rect.height);
        }

        // Runs before GUI.Window so a press near a corner is used up before the window (or its frames) can see it.
        void Resize()
        {
            // Requested on every event to keep the control ids of the following IMGUI calls stable.
            int id = GUIUtility.GetControlID(_id, FocusType.Passive);
            var e = Event.current;
            if (!Resizable && GUIUtility.hotControl != id) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && GUIUtility.hotControl == 0 && TryGrabCorner(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        ResizeTo(e.mousePosition - _resizeStartMouse);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        // _resizeCorner bits: 1 = right edge moves, 2 = bottom edge moves.
        bool TryGrabCorner(Vector2 mouse)
        {
            bool left = Mathf.Abs(mouse.x - Rect.xMin) <= ResizeGrip;
            bool right = Mathf.Abs(mouse.x - Rect.xMax) <= ResizeGrip;
            bool top = Mathf.Abs(mouse.y - Rect.yMin) <= ResizeGrip;
            bool bottom = Mathf.Abs(mouse.y - Rect.yMax) <= ResizeGrip;
            if (!(left || right) || !(top || bottom)) return false;

            _resizeCorner = (right ? 1 : 0) + (bottom ? 2 : 0);
            _resizeStartMouse = mouse;
            _resizeStartRect = Rect;
            return true;
        }

        void ResizeTo(Vector2 delta)
        {
            float xMin = _resizeStartRect.xMin, xMax = _resizeStartRect.xMax;
            float yMin = _resizeStartRect.yMin, yMax = _resizeStartRect.yMax;
            if ((_resizeCorner & 1) == 0) xMin = Mathf.Min(xMin + delta.x, xMax - _minimumSize.x);
            else xMax = Mathf.Max(xMax + delta.x, xMin + _minimumSize.x);
            if ((_resizeCorner & 2) == 0) yMin = Mathf.Min(yMin + delta.y, yMax - _minimumSize.y);
            else yMax = Mathf.Max(yMax + delta.y, yMin + _minimumSize.y);
            Rect = UnityEngine.Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
