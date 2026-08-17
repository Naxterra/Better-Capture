using NaxCapture.Core.Geometry;

namespace NaxCapture.Core.Tests.Geometry;

public sealed class PixelRectTests
{
    [Fact]
    public void FromPoints_NormalizesDragDirection()
    {
        var region = PixelRect.FromPoints(new PixelPoint(90, 70), new PixelPoint(10, 20));

        Assert.Equal(new PixelRect(10, 20, 80, 50), region);
    }

    [Fact]
    public void Intersect_ClipsToFrame()
    {
        var region = new PixelRect(-10, 25, 40, 100);

        Assert.Equal(new PixelRect(0, 25, 30, 55), region.ClampTo(new PixelSize(100, 80)));
    }
}
