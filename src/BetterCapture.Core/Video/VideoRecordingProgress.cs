namespace BetterCapture.Core.Video;

public sealed record VideoRecordingProgress(
    TimeSpan Elapsed,
    long EncodedFrameCount,
    long DroppedFrameCount);
