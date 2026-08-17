using NaxCapture.Core.Geometry;

namespace NaxCapture.Core.Capture;

public sealed record CaptureFrameMetadata(
    DateTimeOffset CapturedAt,
    string DisplayName,
    string AdapterName,
    PixelRect DesktopBounds,
    CaptureColorSpace ColorSpace,
    bool AdvancedColorEnabled,
    float? DisplayMaxLuminanceNits,
    string CaptureBackend);
