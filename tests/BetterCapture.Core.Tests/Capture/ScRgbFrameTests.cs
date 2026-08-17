using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;

namespace BetterCapture.Core.Tests.Capture;

public sealed class ScRgbFrameTests
{
    [Fact]
    public void Crop_CopiesRequestedPixelsInRgbaOrder()
    {
        var pixels = Enumerable.Range(0, 4 * 3 * ScRgbFrame.ChannelCount)
            .Select(value => (Half)value)
            .ToArray();
        var frame = new ScRgbFrame(4, 3, pixels, Metadata());

        var cropped = frame.Crop(new PixelRect(1, 1, 2, 2));

        Assert.Equal(2, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(
            new Half[] { (Half)20, (Half)21, (Half)22, (Half)23, (Half)24, (Half)25, (Half)26, (Half)27,
                         (Half)36, (Half)37, (Half)38, (Half)39, (Half)40, (Half)41, (Half)42, (Half)43 },
            cropped.Pixels.ToArray());
    }

    private static CaptureFrameMetadata Metadata() => new(
        DateTimeOffset.UnixEpoch,
        "display",
        "adapter",
        new PixelRect(0, 0, 4, 3),
        CaptureColorSpace.LinearScRgb,
        true,
        1000f,
        "test");
}
