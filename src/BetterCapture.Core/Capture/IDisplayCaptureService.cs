namespace BetterCapture.Core.Capture;

public interface IDisplayCaptureService : IDisposable
{
    bool IsSupported { get; }

    IReadOnlyList<DisplayTarget> GetDisplays();

    DisplayTarget GetDisplayUnderCursor();

    Task<ScRgbFrame> CaptureAsync(
        DisplayTarget target,
        CaptureOptions options,
        CancellationToken cancellationToken = default);
}
