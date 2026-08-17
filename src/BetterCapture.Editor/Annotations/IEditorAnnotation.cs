using SkiaSharp;

namespace BetterCapture.Editor.Annotations;

internal interface IEditorAnnotation
{
    void Draw(SKCanvas canvas);

    IEditorAnnotation Clone();

    IEditorAnnotation Translate(float x, float y);

    IEditorAnnotation Scale(float x, float y);
}
