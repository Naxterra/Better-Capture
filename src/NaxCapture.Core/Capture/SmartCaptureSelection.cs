using NaxCapture.Core.Geometry;

namespace NaxCapture.Core.Capture;

public sealed record SmartCaptureSelection(PixelRect Region, CaptureSourceInfo Source);
