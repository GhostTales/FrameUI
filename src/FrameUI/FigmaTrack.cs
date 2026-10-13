namespace FrameUI
{
    /// <summary>One grid row or column definition: Px(100), Hug or Fr(0.25).</summary>
    public readonly struct FigmaTrack
    {
        public readonly FigmaTrackKind Kind;
        public readonly float Value;

        FigmaTrack(FigmaTrackKind kind, float value)
        {
            Kind = kind;
            Value = value;
        }

        public static FigmaTrack Px(float value) => new FigmaTrack(FigmaTrackKind.Fixed, value);
        public static FigmaTrack Fr(float value) => new FigmaTrack(FigmaTrackKind.Fr, value);
        public static FigmaTrack Hug => new FigmaTrack(FigmaTrackKind.Hug, 0f);
    }
}
