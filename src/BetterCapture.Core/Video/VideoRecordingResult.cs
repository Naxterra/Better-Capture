using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;

namespace BetterCapture.Core.Video;

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
