using BetterCapture.Editor;
using SkiaSharp;

namespace BetterCapture.Core.Tests.Editor;

public sealed class EditorDocumentTests
{
    [Fact]
    public void CropResizeAnnotateAndSave_ProducesDecodablePng()
    {
        var sourcePath = TemporaryPath();
        var outputPath = TemporaryPath();
        try
        {
            CreateSource(sourcePath);
            using var document = EditorDocument.Load(sourcePath);
            document.Crop(new SKRectI(2, 2, 18, 10));
            document.Resize(64, 32);
            document.Blur(new SKRectI(0, 0, 16, 16), sigma: 3f);
            document.AddShape(
                EditorShape.Arrow,
                new SKPoint(2, 2),
                new SKPoint(40, 20),
                SKColors.Red,
                3f);
            document.AddText("Test", new SKPoint(4, 28), "Arial", 12f, SKColors.White);
            document.AddWatermark("BetterCapture", "Arial", 9f, SKColors.White, 128);
            document.Save(outputPath);

            using var decoded = SKBitmap.Decode(outputPath);
            Assert.NotNull(decoded);
            Assert.Equal(64, decoded.Width);
            Assert.Equal(32, decoded.Height);
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void UndoAndRedo_RestoreDimensions()
    {
        var sourcePath = TemporaryPath();
        try
        {
            CreateSource(sourcePath);
            using var document = EditorDocument.Load(sourcePath);
            document.Resize(40, 24);

            document.Undo();
            Assert.Equal(20, document.Width);
            Assert.Equal(12, document.Height);

            document.Redo();
            Assert.Equal(40, document.Width);
            Assert.Equal(24, document.Height);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public void CopyAndCut_ReturnPngAndSupportUndo()
    {
        var sourcePath = TemporaryPath();
        try
        {
            CreateSource(sourcePath);
            using var document = EditorDocument.Load(sourcePath);

            var copied = document.CopyRegion(new SKRectI(0, 0, 5, 5));
            var cut = document.Cut(new SKRectI(0, 0, 5, 5));

            Assert.Equal(new byte[] { 137, 80, 78, 71 }, copied[..4]);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, cut[..4]);
            Assert.True(document.CanUndo);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public void Text_RemainsAddressableAndEditableAfterPlacement()
    {
        var sourcePath = TemporaryPath();
        try
        {
            CreateSource(sourcePath);
            using var document = EditorDocument.Load(sourcePath);
            var id = document.AddText(
                "Original editable text",
                new SKPoint(2, 10),
                "Arial",
                8f,
                SKColors.Red,
                maxWidth: 15f);

            var element = document.TextElements.Single(item => item.Id == id);
            Assert.True(element.Bounds.Width > 0);
            Assert.True(element.Bounds.Height > 8f);
            var hitPoint = new SKPoint(
                (element.Bounds.Left + element.Bounds.Right) / 2f,
                (element.Bounds.Top + element.Bounds.Bottom) / 2f);
            Assert.Equal(id, document.HitTestText(hitPoint)?.Id);

            document.UpdateText(
                id,
                "Changed text",
                element.Position,
                "Arial",
                9f,
                SKColors.Blue,
                element.MaxWidth);

            var changed = document.TextElements.Single(item => item.Id == id);
            Assert.Equal("Changed text", changed.Text);
            Assert.Equal(SKColors.Blue, changed.Color);
            Assert.True(document.CanUndo);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    private static void CreateSource(string path)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(20, 12, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkSlateGray);
        using var paint = new SKPaint { Color = SKColors.CornflowerBlue };
        canvas.DrawRect(new SKRect(2, 2, 18, 10), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static string TemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"bettercapture-editor-{Guid.NewGuid():N}.png");
}
