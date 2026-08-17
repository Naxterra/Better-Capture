namespace NaxCapture.Core.Capture;

public sealed record CaptureOptions
{
    public static CaptureOptions Default { get; } = new();

    public bool IncludeCursor { get; init; } = true;

    public bool RequestBorderlessCapture { get; init; } = true;

    public TimeSpan FrameTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
