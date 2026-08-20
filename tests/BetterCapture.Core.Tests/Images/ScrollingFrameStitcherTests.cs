using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;
using BetterCapture.Graphics.Images;

namespace BetterCapture.Core.Tests.Images;

public sealed class ScrollingFrameStitcherTests
{
    [Fact]
    public void Append_DetectsOverlapAndAddsOnlyNewRows()
    {
        const int width = 80;
        const int height = 100;
        const int advance = 32;
        var firstPreview = CreatePreview(width, height, row => row);
        var secondPreview = CreatePreview(width, height, row => row < height - advance ? row + advance : 140 + row);
        var firstFrame = CreateFrame(firstPreview);
        var secondFrame = CreateFrame(secondPreview);
        var stitcher = new ScrollingFrameStitcher(firstFrame, firstPreview);

        var appended = stitcher.TryAppend(secondFrame, secondPreview, 1000, out var detectedAdvance);
        var result = stitcher.Build();

        Assert.True(appended);
        Assert.Equal(advance, detectedAdvance);
        Assert.Equal(height + advance, result.Height);
        Assert.Equal(width, result.Width);
    }

    [Fact]
    public void Append_StopsWhenThePageNoLongerMoves()
    {
        var preview = CreatePreview(80, 100, row => row);
        var frame = CreateFrame(preview);
        var stitcher = new ScrollingFrameStitcher(frame, preview);

        var appended = stitcher.TryAppend(frame, preview, 1000, out var advance);

        Assert.False(appended);
        Assert.Equal(0, advance);
        Assert.Equal(1, stitcher.FrameCount);
    }

    [Fact]
    public void Append_IgnoresAnAnimatedRegionWhileMatchingStaticContent()
    {
        const int width = 96;
        const int height = 120;
        const int expectedAdvance = 36;
        var firstPreview = CreatePreview(width, height, row => row);
        var secondPreview = CreatePreview(
            width,
            height,
            row => row < height - expectedAdvance ? row + expectedAdvance : 170 + row);
        for (var y = 12; y < 72; y++)
        {
            for (var x = 8; x < 36; x++)
            {
                var offset = ((y * width) + x) * 4;
                secondPreview.Pixels[offset] ^= 0xff;
                secondPreview.Pixels[offset + 1] ^= 0x7f;
                secondPreview.Pixels[offset + 2] ^= 0x3f;
            }
        }

        var stitcher = new ScrollingFrameStitcher(CreateFrame(firstPreview), firstPreview);

        var appended = stitcher.TryAppend(
            CreateFrame(secondPreview),
            secondPreview,
            1000,
            out var detectedAdvance);

        Assert.True(appended);
        Assert.Equal(expectedAdvance, detectedAdvance);
    }

    private static Bgra8Image CreatePreview(int width, int height, Func<int, int> rowValue)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var value = (byte)((rowValue(y) + (x * 3)) & 0xff);
                pixels[offset] = value;
                pixels[offset + 1] = (byte)(value ^ 0x55);
                pixels[offset + 2] = (byte)(value ^ 0xaa);
                pixels[offset + 3] = 255;
            }
        }

        return new Bgra8Image(width, height, pixels);
    }

    private static ScRgbFrame CreateFrame(Bgra8Image image)
    {
        var pixels = new Half[image.Width * image.Height * ScRgbFrame.ChannelCount];
        for (var index = 0; index < image.Width * image.Height; index++)
        {
            pixels[(index * 4)] = (Half)(image.Pixels[(index * 4) + 2] / 255f);
            pixels[(index * 4) + 1] = (Half)(image.Pixels[(index * 4) + 1] / 255f);
            pixels[(index * 4) + 2] = (Half)(image.Pixels[index * 4] / 255f);
            pixels[(index * 4) + 3] = (Half)1f;
        }

        return new ScRgbFrame(
            image.Width,
            image.Height,
            pixels,
            new CaptureFrameMetadata(
                DateTimeOffset.UtcNow,
                "display",
                "adapter",
                new PixelRect(0, 0, image.Width, image.Height),
                CaptureColorSpace.LinearScRgb,
                false,
                null,
                "test"));
    }
}
