using System.Text.Json;
using NaxCapture.Capture;
using NaxCapture.Core.Capture;
using NaxCapture.Graphics.Color;
using NaxCapture.Graphics.Encoding;

namespace NaxCapture.CaptureProbe;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main()
    {
        try
        {
            using var capture = new WindowsGraphicsCaptureService();
            var display = capture.GetDisplayUnderCursor();
            var frame = await capture.CaptureAsync(
                display,
                CaptureOptions.Default with
                {
                    IncludeCursor = false,
                    RequestBorderlessCapture = false,
                });
            var analysis = ScRgbToneMapper.Analyze(frame);
            var assumedSdrWhiteNits = frame.Metadata.AdvancedColorEnabled ? 203f : 80f;
            var preview = ScRgbToneMapper.ToBgra8(
                frame,
                settings: new ToneMapSettings { SdrWhiteLevelNits = assumedSdrWhiteNits });
            var directory = Path.Combine(Path.GetTempPath(), "BetterCaptureProbe");
            Directory.CreateDirectory(directory);
            var pngPath = Path.Combine(directory, "capture.png");
            var exrPath = Path.Combine(directory, "capture.exr");
            PngWriter.Write(pngPath, preview);
            OpenExrWriter.Write(exrPath, frame);

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                display.DeviceName,
                display.DesktopBounds,
                Frame = new { frame.Width, frame.Height },
                frame.Metadata.AdapterName,
                frame.Metadata.AdvancedColorEnabled,
                frame.Metadata.DisplayMaxLuminanceNits,
                AssumedSdrWhiteLevelNits = assumedSdrWhiteNits,
                analysis.HasExtendedRange,
                analysis.MaximumLuminanceNits,
                Png = pngPath,
                Exr = exrPath,
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
