using NaxCapture.Core.Capture;
using NaxCapture.Core.Geometry;
using NaxCapture.Graphics.Color;

namespace NaxCapture.Core.Tests.Color;

public sealed class ScRgbToneMapperTests
{
    [Fact]
    public void Analyze_RecognizesExtendedHdrRange()
    {
        var frame = Frame([ (Half)12.5f, (Half)12.5f, (Half)12.5f, (Half)1f ]);

        var analysis = ScRgbToneMapper.Analyze(frame);

        Assert.True(analysis.HasExtendedRange);
        Assert.InRange(analysis.MaximumLuminanceNits, 999f, 1001f);
    }

    [Fact]
    public void ToneMap_ProducesOpaqueBgraWithoutClippingToBlack()
    {
        var frame = Frame([ (Half)4f, (Half)2f, (Half)1f, (Half)1f ]);

        var image = ScRgbToneMapper.ToBgra8(frame);

        Assert.Equal(4, image.Pixels.Length);
        Assert.Equal(255, image.Pixels[3]);
        Assert.True(image.Pixels[2] > image.Pixels[1]);
        Assert.True(image.Pixels[1] > image.Pixels[0]);
        Assert.True(image.Pixels[0] > 0);
    }

    [Fact]
    public void ToneMap_ReversesWindowsSdrWhiteBoostBeforeEncoding()
    {
        const float middleGrayLinear = 0.21404114f;
        const float windowsSdrBoost = 3f;
        var boostedGray = (Half)(middleGrayLinear * windowsSdrBoost);
        var frame = Frame([boostedGray, boostedGray, boostedGray, (Half)1f]);

        var image = ScRgbToneMapper.ToBgra8(
            frame,
            settings: new ToneMapSettings { SdrWhiteLevelNits = 240f });

        Assert.InRange(image.Pixels[0], 126, 129);
        Assert.Equal(image.Pixels[0], image.Pixels[1]);
        Assert.Equal(image.Pixels[1], image.Pixels[2]);
        Assert.Equal(255, image.Pixels[3]);
    }

    private static ScRgbFrame Frame(Half[] pixels) => new(
        1,
        1,
        pixels,
        new CaptureFrameMetadata(
            DateTimeOffset.UnixEpoch,
            "display",
            "adapter",
            new PixelRect(0, 0, 1, 1),
            CaptureColorSpace.LinearScRgb,
            true,
            1000f,
            "test"));
}
