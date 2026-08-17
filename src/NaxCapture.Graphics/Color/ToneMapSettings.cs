namespace NaxCapture.Graphics.Color;

public sealed record ToneMapSettings
{
    public const float ScRgbReferenceWhiteNits = 80f;

    public static ToneMapSettings Default { get; } = new();

    public float Exposure { get; init; } = 1.0f;

    public float SdrWhiteLevelNits { get; init; } = ScRgbReferenceWhiteNits;
}
