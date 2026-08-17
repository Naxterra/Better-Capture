using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;
using BetterCapture.Graphics.Images;

namespace BetterCapture.Graphics.Color;

public static class ScRgbToneMapper
{
    private const float ScRgbReferenceWhiteNits = ToneMapSettings.ScRgbReferenceWhiteNits;
    private const float RedLuminance = 0.2126f;
    private const float GreenLuminance = 0.7152f;
    private const float BlueLuminance = 0.0722f;

    public static Bgra8Image ToBgra8(
        ScRgbFrame frame,
        PixelRect? region = null,
        ToneMapSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        settings ??= ToneMapSettings.Default;
        if (!float.IsFinite(settings.Exposure) || settings.Exposure <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Exposure must be finite and greater than zero.");
        }

        if (!float.IsFinite(settings.SdrWhiteLevelNits) || settings.SdrWhiteLevelNits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "The Windows SDR white level must be finite and greater than zero.");
        }

        var sourceRegion = (region ?? new PixelRect(0, 0, frame.Width, frame.Height)).ClampTo(frame.Size);
        if (sourceRegion.IsEmpty)
        {
            throw new ArgumentException("The output region does not intersect the frame.", nameof(region));
        }

        var destination = new byte[checked(sourceRegion.Width * sourceRegion.Height * 4)];
        var source = frame.Pixels.Span;
        var sourceStride = checked(frame.Width * ScRgbFrame.ChannelCount);
        var inverseWindowsSdrBoost = ScRgbReferenceWhiteNits / settings.SdrWhiteLevelNits;

        for (var y = 0; y < sourceRegion.Height; y++)
        {
            var sourceOffset = checked(((sourceRegion.Y + y) * sourceStride) + (sourceRegion.X * 4));
            var destinationOffset = checked(y * sourceRegion.Width * 4);

            for (var x = 0; x < sourceRegion.Width; x++)
            {
                var sourcePixel = sourceOffset + (x * 4);
                var destinationPixel = destinationOffset + (x * 4);

                var red = Sanitize((float)source[sourcePixel]) * inverseWindowsSdrBoost * settings.Exposure;
                var green = Sanitize((float)source[sourcePixel + 1]) * inverseWindowsSdrBoost * settings.Exposure;
                var blue = Sanitize((float)source[sourcePixel + 2]) * inverseWindowsSdrBoost * settings.Exposure;
                var alpha = Math.Clamp(SanitizeAlpha((float)source[sourcePixel + 3]), 0f, 1f);

                PreserveSdrAndCompressHighlights(ref red, ref green, ref blue);

                destination[destinationPixel] = ToByte(LinearToSrgb(blue));
                destination[destinationPixel + 1] = ToByte(LinearToSrgb(green));
                destination[destinationPixel + 2] = ToByte(LinearToSrgb(red));
                destination[destinationPixel + 3] = ToByte(alpha);
            }
        }

        return new Bgra8Image(sourceRegion.Width, sourceRegion.Height, destination);
    }

    public static HdrAnalysis Analyze(ScRgbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var maximumLuminance = 0f;
        var minimumChannel = 0f;
        var extended = false;
        var source = frame.Pixels.Span;

        for (var index = 0; index < source.Length; index += ScRgbFrame.ChannelCount)
        {
            var red = SanitizePreservingNegative((float)source[index]);
            var green = SanitizePreservingNegative((float)source[index + 1]);
            var blue = SanitizePreservingNegative((float)source[index + 2]);
            minimumChannel = Math.Min(minimumChannel, Math.Min(red, Math.Min(green, blue)));
            extended |= red > 1.0009766f || green > 1.0009766f || blue > 1.0009766f;

            var luminance = Math.Max(0f, (red * RedLuminance) + (green * GreenLuminance) + (blue * BlueLuminance));
            maximumLuminance = Math.Max(maximumLuminance, luminance);
        }

        return new HdrAnalysis(
            extended,
            maximumLuminance,
            maximumLuminance * ScRgbReferenceWhiteNits,
            minimumChannel);
    }

    private static void PreserveSdrAndCompressHighlights(ref float red, ref float green, ref float blue)
    {
        red = Math.Max(0f, red);
        green = Math.Max(0f, green);
        blue = Math.Max(0f, blue);

        // Pixels inside the SDR reference range pass through unchanged. That is
        // essential for UI, browser, and office content: Windows already scaled
        // those pixels up when composing the HDR desktop, and the scale above
        // has just reversed that operation.
        var maximum = Math.Max(red, Math.Max(green, blue));
        if (maximum > 1f)
        {
            // An 8-bit compatibility PNG has no luminance above reference white.
            // Preserve highlight hue by scaling the triplet together. The paired
            // EXR remains the unclipped HDR master.
            red /= maximum;
            green /= maximum;
            blue /= maximum;
        }
    }

    private static float LinearToSrgb(float value) => value <= 0.0031308f
        ? value * 12.92f
        : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;

    private static byte ToByte(float value) => (byte)Math.Clamp(
        (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f),
        0,
        255);

    private static float Sanitize(float value) => float.IsFinite(value) ? Math.Max(0f, value) : 0f;

    private static float SanitizeAlpha(float value) => float.IsFinite(value) ? value : 1f;

    private static float SanitizePreservingNegative(float value) => float.IsFinite(value) ? value : 0f;
}
