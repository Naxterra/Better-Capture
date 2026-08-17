namespace BetterCapture.Core.Geometry;

public readonly record struct PixelRect
{
    public PixelRect(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public bool IsEmpty => Width == 0 || Height == 0;

    public PixelPoint Location => new(X, Y);

    public PixelSize Size => new(Width, Height);

    public static PixelRect FromPoints(PixelPoint first, PixelPoint second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X, second.X);
        var bottom = Math.Max(first.Y, second.Y);

        return new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return right <= left || bottom <= top
            ? new PixelRect(left, top, 0, 0)
            : new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelRect ClampTo(PixelSize size) => Intersect(new PixelRect(0, 0, size.Width, size.Height));
}
