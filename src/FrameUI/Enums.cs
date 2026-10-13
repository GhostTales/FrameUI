namespace FrameUI
{
    /// <summary>How a frame arranges its children. None keeps the children's absolute Bounds.</summary>
    public enum FigmaLayout { None, Horizontal, Vertical, Grid }

    /// <summary>
    /// Figma child sizing. Fixed = Bounds size, Hug = size of the content, Fill = fill the container
    /// (stack: share the leftover main-axis space by FillWeight; grid: fill the cell).
    /// </summary>
    public enum FigmaSize { Fixed, Hug, Fill }

    /// <summary>Figma "Packing" (main axis) and "Alignment" (cross axis / grid cell alignment).</summary>
    public enum FigmaAlign { Start, Center, End, SpaceBetween }

    public enum FigmaTrackKind { Fixed, Hug, Fr }

    /// <summary>Pointer state of a frame, resolved on every repaint.</summary>
    public enum FigmaState { Idle, Hover, Pressed }
}
