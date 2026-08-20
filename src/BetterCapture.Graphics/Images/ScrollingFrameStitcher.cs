using BetterCapture.Core.Capture;

namespace BetterCapture.Graphics.Images;

public sealed class ScrollingFrameStitcher
{
    private const double UnchangedErrorThreshold = 2.0;
    private const double MaximumOverlapError = 28.0;

    private readonly List<Segment> _segments = [];
    private readonly CaptureFrameMetadata _metadata;
    private readonly int _frameHeight;
    private Bgra8Image _lastPreview;

    public ScrollingFrameStitcher(ScRgbFrame firstFrame, Bgra8Image firstPreview)
    {
        ArgumentNullException.ThrowIfNull(firstFrame);
        ArgumentNullException.ThrowIfNull(firstPreview);
        ValidateDimensions(firstFrame, firstPreview);
        _segments.Add(new Segment(firstFrame.Pixels.ToArray(), firstFrame.Height));
        _metadata = firstFrame.Metadata;
        _frameHeight = firstFrame.Height;
        _lastPreview = firstPreview;
        Width = firstFrame.Width;
        Height = firstFrame.Height;
    }

    public int Width { get; }

    public int Height { get; private set; }

    public int FrameCount => _segments.Count;

    public bool TryAppend(
        ScRgbFrame frame,
        Bgra8Image preview,
        int maximumHeight,
        out int advance,
        int preferredAdvance = 0,
        int bottomTrim = 0,
        bool allowFallback = true)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(preview);
        advance = 0;
        ValidateDimensions(frame, preview);

        if (frame.Width != Width || frame.Height != _frameHeight)
        {
            throw new ArgumentException("Scrolling capture frames must have matching dimensions.", nameof(frame));
        }

        if (Height >= maximumHeight ||
            (preferredAdvance <= 0 && !TryFindVerticalAdvance(
                _lastPreview,
                preview,
                out advance,
                allowFallback)))
        {
            return false;
        }

        if (preferredAdvance > 0)
        {
            advance = Math.Clamp(preferredAdvance, 1, frame.Height - 1);
        }

        advance = Math.Min(advance, maximumHeight - Height);
        if (advance <= 0)
        {
            return false;
        }

        var trimmedRows = Math.Clamp(bottomTrim, 0, Math.Max(0, advance - 1));
        var rowCount = advance - trimmedRows;
        var channelsPerRow = checked(Width * ScRgbFrame.ChannelCount);
        var strip = new Half[checked(rowCount * channelsPerRow)];
        frame.Pixels.Span.Slice(
                checked((frame.Height - advance) * channelsPerRow),
                strip.Length)
            .CopyTo(strip);
        _segments.Add(new Segment(strip, rowCount));
        _lastPreview = preview;
        Height += rowCount;
        return true;
    }

    public ScRgbFrame Build()
    {
        var channelsPerRow = checked(Width * ScRgbFrame.ChannelCount);
        var pixels = new Half[checked(Height * channelsPerRow)];
        var destination = pixels.AsSpan();
        _segments[0].Pixels.AsSpan().CopyTo(destination);
        var destinationRow = _segments[0].RowCount;

        foreach (var segment in _segments.Skip(1))
        {
            var length = checked(segment.RowCount * channelsPerRow);
            segment.Pixels.AsSpan(0, length)
                .CopyTo(destination.Slice(destinationRow * channelsPerRow, length));
            destinationRow += segment.RowCount;
        }

        var metadata = _metadata with
        {
            CaptureBackend = $"{_metadata.CaptureBackend} / scrolling stitch ({FrameCount} frames)",
        };
        return new ScRgbFrame(Width, Height, pixels, metadata);
    }

    public static bool TryFindVerticalAdvance(
        Bgra8Image previous,
        Bgra8Image current,
        out int advance,
        bool allowFallback = true)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            throw new ArgumentException("Scrolling previews must have matching dimensions.", nameof(current));
        }

        var unchangedError = CalculateShiftError(previous, current, 0);
        if (unchangedError <= UnchangedErrorThreshold)
        {
            advance = 0;
            return false;
        }

        var minimumAdvance = Math.Max(8, previous.Height / 40);
        var maximumAdvance = Math.Max(minimumAdvance, Math.Min(previous.Height - 16, previous.Height * 3 / 5));
        var coarseStep = Math.Max(1, previous.Height / 240);
        var bestAdvance = 0;
        var bestError = double.MaxValue;

        for (var candidate = minimumAdvance; candidate <= maximumAdvance; candidate += coarseStep)
        {
            var error = CalculateShiftError(previous, current, candidate);
            if (error < bestError)
            {
                bestError = error;
                bestAdvance = candidate;
            }
        }

        var refinementStart = Math.Max(minimumAdvance, bestAdvance - coarseStep);
        var refinementEnd = Math.Min(maximumAdvance, bestAdvance + coarseStep);
        for (var candidate = refinementStart; candidate <= refinementEnd; candidate++)
        {
            var error = CalculateShiftError(previous, current, candidate);
            if (error < bestError)
            {
                bestError = error;
                bestAdvance = candidate;
            }
        }

        if (bestAdvance <= 0 ||
            bestError > MaximumOverlapError ||
            bestError >= unchangedError * 0.88)
        {
            if (!allowFallback)
            {
                advance = 0;
                return false;
            }

            advance = Math.Clamp(previous.Height * 3 / 4, minimumAdvance, maximumAdvance);
            return true;
        }

        advance = bestAdvance;
        return true;
    }

    private static double CalculateShiftError(Bgra8Image previous, Bgra8Image current, int advance)
    {
        var overlap = previous.Height - advance;
        if (overlap <= 8)
        {
            return double.MaxValue;
        }

        var verticalMargin = Math.Min(Math.Max(4, overlap / 14), Math.Max(0, (overlap - 4) / 3));
        var horizontalMargin = Math.Min(Math.Max(2, previous.Width / 24), Math.Max(0, (previous.Width - 4) / 3));
        var startY = verticalMargin;
        var endY = overlap - verticalMargin;
        var startX = horizontalMargin;
        var endX = previous.Width - horizontalMargin;
        var stepY = Math.Max(2, Math.Max(1, endY - startY) / 42);
        var stepX = Math.Max(2, Math.Max(1, endX - startX) / 64);
        var errors = new List<int>();

        for (var y = startY; y < endY; y += stepY)
        {
            var previousRow = checked((y + advance) * previous.Width * 4);
            var currentRow = checked(y * current.Width * 4);
            for (var x = startX; x < endX; x += stepX)
            {
                var previousOffset = previousRow + (x * 4);
                var currentOffset = currentRow + (x * 4);
                if (CalculateContrast(previous, previousOffset) < 18)
                {
                    continue;
                }

                var error = Math.Abs(previous.Pixels[previousOffset] - current.Pixels[currentOffset]) +
                    Math.Abs(previous.Pixels[previousOffset + 1] - current.Pixels[currentOffset + 1]) +
                    Math.Abs(previous.Pixels[previousOffset + 2] - current.Pixels[currentOffset + 2]);
                errors.Add(error / 3);
            }
        }

        if (errors.Count < 24)
        {
            return CalculateUnfilteredShiftError(previous, current, advance);
        }

        errors.Sort();
        var retainedCount = Math.Max(1, errors.Count * 2 / 3);
        long retainedError = 0;
        for (var index = 0; index < retainedCount; index++)
        {
            retainedError += errors[index];
        }

        return retainedError / (double)retainedCount;
    }

    private static int CalculateContrast(Bgra8Image image, int offset)
    {
        var rightOffset = offset + 8;
        var lowerOffset = offset + (image.Width * 8);
        var contrast = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            contrast += Math.Abs(image.Pixels[offset + channel] - image.Pixels[rightOffset + channel]);
            contrast += Math.Abs(image.Pixels[offset + channel] - image.Pixels[lowerOffset + channel]);
        }

        return contrast / 6;
    }

    private static double CalculateUnfilteredShiftError(
        Bgra8Image previous,
        Bgra8Image current,
        int advance)
    {
        var overlap = previous.Height - advance;
        var stepY = Math.Max(3, overlap / 40);
        var stepX = Math.Max(3, previous.Width / 56);
        long difference = 0;
        long channelCount = 0;
        for (var y = 4; y < overlap - 4; y += stepY)
        {
            var previousRow = ((y + advance) * previous.Width) * 4;
            var currentRow = (y * current.Width) * 4;
            for (var x = 4; x < previous.Width - 4; x += stepX)
            {
                var previousOffset = previousRow + (x * 4);
                var currentOffset = currentRow + (x * 4);
                difference += Math.Abs(previous.Pixels[previousOffset] - current.Pixels[currentOffset]);
                difference += Math.Abs(previous.Pixels[previousOffset + 1] - current.Pixels[currentOffset + 1]);
                difference += Math.Abs(previous.Pixels[previousOffset + 2] - current.Pixels[currentOffset + 2]);
                channelCount += 3;
            }
        }

        return channelCount == 0 ? double.MaxValue : difference / (double)channelCount;
    }

    private static void ValidateDimensions(ScRgbFrame frame, Bgra8Image preview)
    {
        if (frame.Width != preview.Width || frame.Height != preview.Height)
        {
            throw new ArgumentException("The FP16 frame and preview dimensions must match.", nameof(preview));
        }
    }

    private sealed record Segment(Half[] Pixels, int RowCount);
}
