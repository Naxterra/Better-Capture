using BetterCapture.Core.Geometry;

namespace BetterCapture.Core.Capture;

public sealed record DisplayTarget(
    nint MonitorHandle,
    string DeviceName,
    PixelRect DesktopBounds,
    bool IsPrimary,
    uint DpiX,
    uint DpiY)
{
    public double DpiScaleX => DpiX / 96d;

    public double DpiScaleY => DpiY / 96d;
}
