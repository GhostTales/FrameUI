namespace FrameUI
{
    public static class FigmaExtensions
    {
        /// <summary>Sets the width and height sizing of a frame and returns it, for use inside an initializer chain.</summary>
        public static T Size<T>(this T frame, FigmaSize width, FigmaSize height) where T : FigmaFrame
        {
            frame.WidthSize = width;
            frame.HeightSize = height;
            return frame;
        }
    }
}
