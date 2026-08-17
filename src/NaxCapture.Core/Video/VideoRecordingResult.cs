using NaxCapture.Core.Capture;
using NaxCapture.Core.Geometry;

namespace NaxCapture.Core.Video;

public sealed record VideoRecordingResult(
    string VideoPath,
    string MetadataPath,
    int Width,
    int Height,
    TimeSpan Duration,
    long EncodedFrameCount,
    long DroppedFrameCount,
    CaptureSourceInfo Source,
    PixelRect CapturedDesktopBounds,
    int FramesPerSecond,
    int BitRate,
    float SdrWhiteLevelNits);
