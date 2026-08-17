namespace NaxCapture.Graphics.Color;

public sealed record HdrAnalysis(
    bool HasExtendedRange,
    float MaximumLinearLuminance,
    float MaximumLuminanceNits,
    float MinimumChannelValue);
