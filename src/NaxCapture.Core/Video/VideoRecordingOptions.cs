namespace NaxCapture.Core.Video;

public sealed record VideoRecordingOptions
{
    public static VideoRecordingOptions Default { get; } = new();

    public int FramesPerSecond { get; init; } = 30;

    public int BitRate { get; init; } = 12_000_000;

    public bool IncludeCursor { get; init; } = true;

    public float SdrWhiteLevelNits { get; init; } = 80f;
}
