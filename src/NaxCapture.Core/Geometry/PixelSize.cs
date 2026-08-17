namespace NaxCapture.Core.Geometry;

public readonly record struct PixelSize
{
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public bool IsEmpty => Width == 0 || Height == 0;
}
