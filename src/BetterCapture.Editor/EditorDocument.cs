using BetterCapture.Editor.Annotations;
using SkiaSharp;

namespace BetterCapture.Editor;

public sealed class EditorDocument : IDisposable
{
    private const int UndoLimit = 30;
    private readonly List<IEditorAnnotation> _annotations = [];
    private readonly Stack<DocumentSnapshot> _undo = new();
    private readonly Stack<DocumentSnapshot> _redo = new();
    private SKBitmap _bitmap;
    private bool _disposed;

    private EditorDocument(string sourcePath, SKBitmap bitmap)
    {
        SourcePath = Path.GetFullPath(sourcePath);
        _bitmap = bitmap;
    }

    public event EventHandler? Changed;

    public string SourcePath { get; }

    public int Width => _bitmap.Width;

    public int Height => _bitmap.Height;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public IReadOnlyList<EditorTextElement> TextElements => _annotations
        .OfType<TextAnnotation>()
        .Select(annotation => annotation.ToElement())
        .ToArray();

    public static EditorDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var decoded = SKBitmap.Decode(path) ?? throw new InvalidDataException("The image could not be decoded.");
        return new EditorDocument(path, CopyBitmap(decoded));
    }

    public void Render(SKCanvas canvas)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(_bitmap, new SKPoint(0, 0), SKSamplingOptions.Default, null);
        foreach (var annotation in _annotations)
        {
            annotation.Draw(canvas);
        }
    }

    public void AddShape(EditorShape shape, SKPoint start, SKPoint end, SKColor color, float strokeWidth)
    {
        SaveUndoState();
        _annotations.Add(new ShapeAnnotation(shape, start, end, color, Math.Max(1f, strokeWidth)));
        OnChanged();
    }

    public Guid AddText(
        string text,
        SKPoint position,
        string fontFamily,
        float fontSize,
        SKColor color,
        float maxWidth = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        SaveUndoState();
        var id = Guid.NewGuid();
        _annotations.Add(new TextAnnotation(
            id,
            text,
            position,
            fontFamily,
            Math.Max(6f, fontSize),
            Math.Max(0f, maxWidth),
            color,
            byte.MaxValue));
        OnChanged();
        return id;
    }

    public Guid AddWatermark(string text, string fontFamily, float fontSize, SKColor color, byte opacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        SaveUndoState();
        using var typeface = SKTypeface.FromFamilyName(fontFamily) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, Math.Max(8f, fontSize));
        var width = font.MeasureText(text);
        var id = Guid.NewGuid();
        _annotations.Add(new TextAnnotation(
            id,
            text,
            new SKPoint(Math.Max(12f, Width - width - 18f), Math.Max(fontSize + 12f, Height - 18f)),
            fontFamily,
            Math.Max(8f, fontSize),
            0,
            color,
            opacity));
        OnChanged();
        return id;
    }

    public EditorTextElement? HitTestText(SKPoint point) => _annotations
        .OfType<TextAnnotation>()
        .Reverse()
        .FirstOrDefault(annotation => annotation.Bounds.Contains(point))
        ?.ToElement();

    public void UpdateText(
        Guid id,
        string text,
        SKPoint position,
        string fontFamily,
        float fontSize,
        SKColor color,
        float maxWidth = 0)
    {
        var index = _annotations.FindIndex(annotation =>
            annotation is TextAnnotation textAnnotation && textAnnotation.Id == id);
        if (index < 0)
        {
            throw new ArgumentException("The text annotation does not exist.", nameof(id));
        }

        SaveUndoState();
        if (string.IsNullOrWhiteSpace(text))
        {
            _annotations.RemoveAt(index);
        }
        else
        {
            var previous = (TextAnnotation)_annotations[index];
            _annotations[index] = new TextAnnotation(
                id,
                text,
                position,
                fontFamily,
                Math.Max(6f, fontSize),
                Math.Max(0f, maxWidth),
                color,
                previous.Opacity);
        }

        OnChanged();
    }

    public void Crop(SKRectI region)
    {
        var clipped = Clip(region);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            throw new ArgumentException("The crop region does not intersect the image.", nameof(region));
        }

        SaveUndoState();
        var cropped = new SKBitmap(CreateImageInfo(clipped.Width, clipped.Height));
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                _bitmap,
                clipped,
                SKRect.Create(cropped.Width, cropped.Height),
                SKSamplingOptions.Default,
                null);
        }

        ReplaceBitmap(cropped);
        for (var index = 0; index < _annotations.Count; index++)
        {
            _annotations[index] = _annotations[index].Translate(-clipped.Left, -clipped.Top);
        }

        OnChanged();
    }

    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        SaveUndoState();
        var resized = new SKBitmap(CreateImageInfo(width, height));
        using (var canvas = new SKCanvas(resized))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                _bitmap,
                SKRect.Create(width, height),
                new SKSamplingOptions(SKFilterMode.Linear),
                paint);
        }

        var scaleX = (float)width / Width;
        var scaleY = (float)height / Height;
        ReplaceBitmap(resized);
        for (var index = 0; index < _annotations.Count; index++)
        {
            _annotations[index] = _annotations[index].Scale(scaleX, scaleY);
        }

        OnChanged();
    }

    public void Blur(SKRectI region, float sigma = 10f)
    {
        var clipped = Clip(region);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        SaveUndoState();
        var output = CopyBitmap(_bitmap);
        using (var canvas = new SKCanvas(output))
        using (var paint = new SKPaint
        {
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(Math.Max(0.1f, sigma), Math.Max(0.1f, sigma)),
        })
        {
            canvas.Save();
            canvas.ClipRect(clipped);
            canvas.DrawBitmap(
                _bitmap,
                new SKPoint(0, 0),
                SKSamplingOptions.Default,
                paint);
            canvas.Restore();
        }

        ReplaceBitmap(output);
        OnChanged();
    }

    public byte[] CopyRegion(SKRectI region)
    {
        using var flattened = CreateFlattenedBitmap();
        var clipped = Clip(region);
        using var subset = new SKBitmap(CreateImageInfo(clipped.Width, clipped.Height));
        using (var canvas = new SKCanvas(subset))
        {
            canvas.DrawBitmap(
                flattened,
                clipped,
                SKRect.Create(clipped.Width, clipped.Height),
                SKSamplingOptions.Default,
                null);
        }

        return EncodePng(subset);
    }

    public byte[] Cut(SKRectI region)
    {
        var data = CopyRegion(region);
        SaveUndoState();
        using (var canvas = new SKCanvas(_bitmap))
        using (var paint = new SKPaint { BlendMode = SKBlendMode.Clear })
        {
            canvas.DrawRect(Clip(region), paint);
        }

        OnChanged();
        return data;
    }

    public void Paste(ReadOnlySpan<byte> encodedImage, SKPoint position)
    {
        using var image = SKBitmap.Decode(encodedImage.ToArray()) ??
                          throw new InvalidDataException("The clipboard image could not be decoded.");
        SaveUndoState();
        using (var canvas = new SKCanvas(_bitmap))
        {
            canvas.DrawBitmap(image, position, SKSamplingOptions.Default, null);
        }

        OnChanged();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(CaptureState());
        RestoreState(_undo.Pop());
        OnChanged();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(CaptureState());
        RestoreState(_redo.Pop());
        OnChanged();
    }

    public void Save(string? path = null)
    {
        var destination = Path.GetFullPath(path ?? SourcePath);
        var temporaryPath = destination + ".partial";
        using var flattened = CreateFlattenedBitmap();
        File.WriteAllBytes(temporaryPath, EncodePng(flattened));
        File.Move(temporaryPath, destination, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bitmap.Dispose();
        foreach (var state in _undo)
        {
            state.Dispose();
        }

        foreach (var state in _redo)
        {
            state.Dispose();
        }
    }

    private void SaveUndoState()
    {
        _undo.Push(CaptureState());
        if (_undo.Count > UndoLimit)
        {
            var states = _undo.ToArray();
            _undo.Clear();
            for (var index = Math.Min(UndoLimit, states.Length) - 1; index >= 0; index--)
            {
                _undo.Push(states[index]);
            }

            for (var index = UndoLimit; index < states.Length; index++)
            {
                states[index].Dispose();
            }
        }

        foreach (var state in _redo)
        {
            state.Dispose();
        }
        _redo.Clear();
    }

    private DocumentSnapshot CaptureState() => new(
        CopyBitmap(_bitmap),
        _annotations.Select(annotation => annotation.Clone()).ToArray());

    private void RestoreState(DocumentSnapshot state)
    {
        ReplaceBitmap(state.DetachBitmap());
        _annotations.Clear();
        _annotations.AddRange(state.Annotations.Select(annotation => annotation.Clone()));
        state.Dispose();
    }

    private SKBitmap CreateFlattenedBitmap()
    {
        var bitmap = new SKBitmap(CreateImageInfo(Width, Height));
        using var canvas = new SKCanvas(bitmap);
        Render(canvas);
        return bitmap;
    }

    private SKRectI Clip(SKRectI region) => new(
        Math.Clamp(region.Left, 0, Width),
        Math.Clamp(region.Top, 0, Height),
        Math.Clamp(region.Right, 0, Width),
        Math.Clamp(region.Bottom, 0, Height));

    private void ReplaceBitmap(SKBitmap bitmap)
    {
        var previous = _bitmap;
        _bitmap = bitmap;
        previous.Dispose();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static SKImageInfo CreateImageInfo(int width, int height) => new(
        width,
        height,
        SKColorType.Bgra8888,
        SKAlphaType.Premul,
        SKColorSpace.CreateSrgb());

    private static SKBitmap CopyBitmap(SKBitmap source)
    {
        var copy = new SKBitmap(CreateImageInfo(source.Width, source.Height));
        using var canvas = new SKCanvas(copy);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKPoint(0, 0), SKSamplingOptions.Default, null);
        return copy;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class DocumentSnapshot : IDisposable
    {
        private SKBitmap? _bitmap;

        internal DocumentSnapshot(SKBitmap bitmap, IReadOnlyList<IEditorAnnotation> annotations)
        {
            _bitmap = bitmap;
            Annotations = annotations;
        }

        internal IReadOnlyList<IEditorAnnotation> Annotations { get; }

        internal SKBitmap DetachBitmap()
        {
            var bitmap = _bitmap ?? throw new ObjectDisposedException(nameof(DocumentSnapshot));
            _bitmap = null;
            return bitmap;
        }

        public void Dispose()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
