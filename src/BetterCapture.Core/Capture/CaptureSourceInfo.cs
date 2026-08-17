using BetterCapture.Core.Geometry;

namespace BetterCapture.Core.Capture;

public sealed record CaptureSourceInfo(
    CaptureSelectionKind Kind,
    string ApplicationName,
    string WindowTitle,
    PixelRect DesktopBounds)
{
    public string Description => Kind switch
    {
        CaptureSelectionKind.FullScreen => string.IsNullOrWhiteSpace(WindowTitle)
            ? ApplicationName
            : WindowTitle,
        _ when string.IsNullOrWhiteSpace(WindowTitle) => ApplicationName,
        _ when string.Equals(ApplicationName, WindowTitle, StringComparison.OrdinalIgnoreCase) => ApplicationName,
        _ => $"{ApplicationName} — {WindowTitle}",
    };
}
