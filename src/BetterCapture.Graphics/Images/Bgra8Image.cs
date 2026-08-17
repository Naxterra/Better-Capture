namespace BetterCapture.Graphics.Images;

public sealed class Bgra8Image
{
    public Bgra8Image(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        var expectedLength = checked(width * height * 4);
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException($"Expected {expectedLength} bytes, received {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }
}
