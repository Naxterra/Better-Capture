using NaxCapture.Core.Storage;

namespace NaxCapture.Core.Tests.Storage;

public sealed class CapturePathPlannerTests
{
    [Fact]
    public void Create_UsesYearAndMonthWithoutDailyFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "NaxCaptureLibrary");
        var localDate = new DateTime(2026, 8, 17, 21, 14, 32, 456, DateTimeKind.Unspecified);
        var capturedAt = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));

        var paths = CapturePathPlanner.Create(root, capturedAt);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "2026", "08"), paths.Directory);
        Assert.Equal("2026-08-17_21-14-32-456.png", Path.GetFileName(paths.SdrPngPath));
        Assert.Equal("2026-08-17_21-14-32-456.exr", Path.GetFileName(paths.HdrExrPath));
        Assert.Equal("2026-08-17_21-14-32-456.json", Path.GetFileName(paths.MetadataPath));
    }

    [Fact]
    public void CreateVideo_UsesSameMonthlyLibraryLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "NaxCaptureLibrary");
        var localDate = new DateTime(2026, 8, 17, 22, 30, 1, 125, DateTimeKind.Unspecified);
        var capturedAt = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));

        var paths = CapturePathPlanner.CreateVideo(root, capturedAt);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "2026", "08"), paths.Directory);
        Assert.Equal("2026-08-17_22-30-01-125.mp4", Path.GetFileName(paths.VideoPath));
        Assert.Equal("2026-08-17_22-30-01-125.video.json", Path.GetFileName(paths.MetadataPath));
    }
}
