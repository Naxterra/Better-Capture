using System.Globalization;

namespace NaxCapture.Core.Storage;

public static class CapturePathPlanner
{
    public static CapturePaths Create(string libraryRoot, DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        var localTime = capturedAt.ToLocalTime();
        var directory = Path.Combine(
            Path.GetFullPath(libraryRoot),
            localTime.ToString("yyyy", CultureInfo.InvariantCulture),
            localTime.ToString("MM", CultureInfo.InvariantCulture));
        var stem = localTime.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);

        return new CapturePaths(
            directory,
            Path.Combine(directory, stem + ".png"),
            Path.Combine(directory, stem + ".exr"),
            Path.Combine(directory, stem + ".json"));
    }

    public static VideoCapturePaths CreateVideo(string libraryRoot, DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);

        var localTime = capturedAt.ToLocalTime();
        var directory = Path.Combine(
            Path.GetFullPath(libraryRoot),
            localTime.ToString("yyyy", CultureInfo.InvariantCulture),
            localTime.ToString("MM", CultureInfo.InvariantCulture));
        var stem = localTime.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);

        return new VideoCapturePaths(
            directory,
            Path.Combine(directory, stem + ".mp4"),
            Path.Combine(directory, stem + ".video.json"));
    }
}

public sealed record CapturePaths(
    string Directory,
    string SdrPngPath,
    string HdrExrPath,
    string MetadataPath);

public sealed record VideoCapturePaths(
    string Directory,
    string VideoPath,
    string MetadataPath);
