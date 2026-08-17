using SkiaSharp;

namespace BetterCapture.Editor;

public sealed record EditorTextElement(
    Guid Id,
    string Text,
    SKPoint Position,
    string FontFamily,
    float FontSize,
    float MaxWidth,
    SKColor Color,
    byte Opacity,
    SKRect Bounds);
