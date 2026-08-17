using System.Text.Json;
using NaxCapture.Capture;
using NaxCapture.Core.Capture;
using NaxCapture.Core.Geometry;
using NaxCapture.Core.Video;
using NaxCapture.Video;
using Windows.Storage;

namespace NaxCapture.VideoProbe;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BetterCaptureVideoProbe");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "probe.mp4");
        var metadataPath = Path.Combine(directory, "probe.video.json");

        try
        {
            using var capture = new WindowsGraphicsCaptureService();
            var target = capture.GetDisplayUnderCursor();
            var width = Math.Min(640, target.DesktopBounds.Width) & ~1;
            var height = Math.Min(360, target.DesktopBounds.Height) & ~1;
            var region = new PixelRect(
                Math.Max(0, (target.DesktopBounds.Width - width) / 2),
                Math.Max(0, (target.DesktopBounds.Height - height) / 2),
                width,
                height);
            var selection = new SmartCaptureSelection(
                region,
                new CaptureSourceInfo(
                    CaptureSelectionKind.Region,
                    "BetterCapture Video Probe",
                    "Synthetic recording test",
                    new PixelRect(
                        target.DesktopBounds.X + region.X,
                        target.DesktopBounds.Y + region.Y,
                        region.Width,
                        region.Height)));
            var recorder = new WindowsGraphicsVideoRecorder();
            await using var session = recorder.Start(
                target,
                selection,
                path,
                metadataPath,
                new VideoRecordingOptions
                {
                    FramesPerSecond = 10,
                    BitRate = 2_000_000,
                    IncludeCursor = false,
                    SdrWhiteLevelNits = 240f,
                });

            await Task.Delay(TimeSpan.FromSeconds(3));
            var result = await session.StopAsync();
            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            var thumbnailPath = Path.Combine(directory, "probe-thumbnail.jpg");
            using (var thumbnail = await file.GetThumbnailAsync(
                       global::Windows.Storage.FileProperties.ThumbnailMode.VideosView,
                       640))
            using (var source = thumbnail.AsStreamForRead())
            using (var destination = File.Create(thumbnailPath))
            {
                await source.CopyToAsync(destination);
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result.VideoPath,
                result.Width,
                result.Height,
                result.Duration,
                result.EncodedFrameCount,
                result.DroppedFrameCount,
                FileSize = new FileInfo(path).Length,
                DecodedWidth = properties.Width,
                DecodedHeight = properties.Height,
                DecodedDuration = properties.Duration,
                properties.Bitrate,
                Thumbnail = thumbnailPath,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
