using SkiaSharp;

namespace NaxCapture.Editor.Annotations;

internal sealed class ShapeAnnotation : IEditorAnnotation
{
    internal ShapeAnnotation(
        EditorShape shape,
        SKPoint start,
        SKPoint end,
        SKColor color,
        float strokeWidth)
    {
        Shape = shape;
        Start = start;
        End = end;
        Color = color;
        StrokeWidth = strokeWidth;
    }

    internal EditorShape Shape { get; }

    internal SKPoint Start { get; }

    internal SKPoint End { get; }

    internal SKColor Color { get; }

    internal float StrokeWidth { get; }

    public void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        var rectangle = SKRect.Create(
            Math.Min(Start.X, End.X),
            Math.Min(Start.Y, End.Y),
            Math.Abs(End.X - Start.X),
            Math.Abs(End.Y - Start.Y));

        switch (Shape)
        {
            case EditorShape.Line:
                canvas.DrawLine(Start, End, paint);
                break;
            case EditorShape.Arrow:
                DrawArrow(canvas, paint);
                break;
            case EditorShape.Rectangle:
                canvas.DrawRect(rectangle, paint);
                break;
            case EditorShape.Ellipse:
                canvas.DrawOval(rectangle, paint);
                break;
            case EditorShape.Triangle:
                using (var builder = new SKPathBuilder())
                {
                    builder.MoveTo((Start.X + End.X) / 2f, Math.Min(Start.Y, End.Y));
                    builder.LineTo(Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));
                    builder.LineTo(Math.Min(Start.X, End.X), Math.Max(Start.Y, End.Y));
                    builder.Close();
                    using var path = builder.Detach();
                    canvas.DrawPath(path, paint);
                }
                break;
        }
    }

    public IEditorAnnotation Clone() => new ShapeAnnotation(Shape, Start, End, Color, StrokeWidth);

    public IEditorAnnotation Translate(float x, float y) => new ShapeAnnotation(
        Shape,
        new SKPoint(Start.X + x, Start.Y + y),
        new SKPoint(End.X + x, End.Y + y),
        Color,
        StrokeWidth);

    public IEditorAnnotation Scale(float x, float y) => new ShapeAnnotation(
        Shape,
        new SKPoint(Start.X * x, Start.Y * y),
        new SKPoint(End.X * x, End.Y * y),
        Color,
        StrokeWidth * ((x + y) / 2f));

    private void DrawArrow(SKCanvas canvas, SKPaint paint)
    {
        canvas.DrawLine(Start, End, paint);
        var angle = MathF.Atan2(End.Y - Start.Y, End.X - Start.X);
        var headLength = Math.Max(12f, StrokeWidth * 4f);
        const float spread = MathF.PI / 7f;
        var first = new SKPoint(
            End.X - (headLength * MathF.Cos(angle - spread)),
            End.Y - (headLength * MathF.Sin(angle - spread)));
        var second = new SKPoint(
            End.X - (headLength * MathF.Cos(angle + spread)),
            End.Y - (headLength * MathF.Sin(angle + spread)));
        canvas.DrawLine(End, first, paint);
        canvas.DrawLine(End, second, paint);
    }
}
