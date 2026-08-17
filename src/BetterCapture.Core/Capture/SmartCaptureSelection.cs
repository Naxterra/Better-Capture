using BetterCapture.Core.Geometry;

namespace BetterCapture.Core.Capture;

public sealed record SmartCaptureSelection(PixelRect Region, CaptureSourceInfo Source);
