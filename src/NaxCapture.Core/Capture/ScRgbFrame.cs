using NaxCapture.Core.Geometry;

namespace NaxCapture.Core.Capture;

public sealed class ScRgbFrame
{
    public const int ChannelCount = 4;

    private readonly Half[] _pixels;

    public ScRgbFrame(
        int width,
        int height,
        Half[] pixels,
        CaptureFrameMetadata metadata)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(metadata);

        var expectedLength = checked(width * height * ChannelCount);
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} half-float channel values, received {pixels.Length}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
        Metadata = metadata;
    }

    public int Width { get; }

    public int Height { get; }

    public PixelSize Size => new(Width, Height);

    public CaptureFrameMetadata Metadata { get; }

    public ReadOnlyMemory<Half> Pixels => _pixels;

    public ScRgbFrame Crop(PixelRect region)
    {
        var clipped = region.ClampTo(Size);
        if (clipped.IsEmpty)
        {
            throw new ArgumentException("The crop region does not intersect the frame.", nameof(region));
        }

        var cropped = new Half[checked(clipped.Width * clipped.Height * ChannelCount)];
        var source = _pixels.AsSpan();
        var destination = cropped.AsSpan();
        var sourceRowLength = checked(Width * ChannelCount);
        var destinationRowLength = checked(clipped.Width * ChannelCount);

        for (var row = 0; row < clipped.Height; row++)
        {
            var sourceOffset = checked(((clipped.Y + row) * sourceRowLength) + (clipped.X * ChannelCount));
            var destinationOffset = checked(row * destinationRowLength);
            source.Slice(sourceOffset, destinationRowLength)
                .CopyTo(destination.Slice(destinationOffset, destinationRowLength));
        }

        return new ScRgbFrame(clipped.Width, clipped.Height, cropped, Metadata);
    }
}
