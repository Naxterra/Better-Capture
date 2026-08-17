namespace BetterCapture.Graphics.Color;

public sealed record HdrAnalysis(
    bool HasExtendedRange,
    float MaximumLinearLuminance,
    float MaximumLuminanceNits,
    float MinimumChannelValue);
