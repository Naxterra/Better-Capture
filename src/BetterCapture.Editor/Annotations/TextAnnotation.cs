using SkiaSharp;

namespace BetterCapture.Editor.Annotations;

internal sealed class TextAnnotation : IEditorAnnotation
{
    internal TextAnnotation(
        Guid id,
        string text,
        SKPoint position,
        string fontFamily,
        float fontSize,
        float maxWidth,
        SKColor color,
        byte opacity)
    {
        Id = id;
        Text = text;
        Position = position;
        FontFamily = fontFamily;
        FontSize = fontSize;
        MaxWidth = maxWidth;
        Color = color;
        Opacity = opacity;
    }

    internal Guid Id { get; }

    internal string Text { get; }

    internal SKPoint Position { get; }

    internal string FontFamily { get; }

    internal float FontSize { get; }

    internal float MaxWidth { get; }

    internal SKColor Color { get; }

    internal byte Opacity { get; }

    internal SKRect Bounds
    {
        get
        {
            using var typeface = SKTypeface.FromFamilyName(FontFamily) ?? SKTypeface.Default;
            using var font = new SKFont(typeface, FontSize);
            var lines = GetLines(font);
            var width = lines.Max(line => font.MeasureText(line));
            var metrics = font.Metrics;
            var lineHeight = FontSize * 1.2f;
            return new SKRect(
                Position.X,
                Position.Y + metrics.Top,
                Position.X + Math.Max(1f, width),
                Position.Y + metrics.Bottom + ((lines.Length - 1) * lineHeight));
        }
    }

    internal EditorTextElement ToElement() => new(
        Id,
        Text,
        Position,
        FontFamily,
        FontSize,
        MaxWidth,
        Color,
        Opacity,
        Bounds);

    public void Draw(SKCanvas canvas)
    {
        using var typeface = SKTypeface.FromFamilyName(FontFamily) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, FontSize);
        using var paint = new SKPaint
        {
            Color = Color.WithAlpha(Opacity),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        var lineHeight = FontSize * 1.2f;
        var lines = GetLines(font);
        for (var index = 0; index < lines.Length; index++)
        {
            canvas.DrawText(
                lines[index],
                new SKPoint(Position.X, Position.Y + (index * lineHeight)),
                SKTextAlign.Left,
                font,
                paint);
        }
    }

    public IEditorAnnotation Clone() => new TextAnnotation(
        Id,
        Text,
        Position,
        FontFamily,
        FontSize,
        MaxWidth,
        Color,
        Opacity);

    public IEditorAnnotation Translate(float x, float y) => new TextAnnotation(
        Id,
        Text,
        new SKPoint(Position.X + x, Position.Y + y),
        FontFamily,
        FontSize,
        MaxWidth,
        Color,
        Opacity);

    public IEditorAnnotation Scale(float x, float y) => new TextAnnotation(
        Id,
        Text,
        new SKPoint(Position.X * x, Position.Y * y),
        FontFamily,
        FontSize * ((x + y) / 2f),
        MaxWidth * x,
        Color,
        Opacity);

    private string[] GetLines(SKFont font)
    {
        var paragraphs = Text.Replace("\r\n", "\n").Split('\n');
        if (MaxWidth <= 0)
        {
            return paragraphs;
        }

        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var current = words[0];
            for (var index = 1; index < words.Length; index++)
            {
                var candidate = current + " " + words[index];
                if (font.MeasureText(candidate) <= MaxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = words[index];
                }
            }

            lines.Add(current);
        }

        return lines.ToArray();
    }
}
