using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using BetterCapture.App.Services;
using BetterCapture.Editor;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using WinPoint = global::Windows.Foundation.Point;

namespace BetterCapture.App.Windows;

internal sealed partial class EditorWindow : Window
{
    private readonly EditorDocument _document;
    private readonly Func<Task>? _openLibrary;
    private readonly List<EditorWindow> _childEditors = [];
    private EditorInteractionTool _tool = EditorInteractionTool.Select;
    private EditorShape _shape = EditorShape.Arrow;
    private WinPoint? _dragStart;
    private WinPoint? _dragCurrent;
    private SKRectI? _selection;
    private Guid? _editingTextId;
    private SKPoint _inlineTextBaseline;
    private float _inlineTextMaxWidth;
    private bool _ignoreInlineLostFocus;

    internal EditorWindow(string imagePath, Func<Task>? openLibrary = null)
    {
        _openLibrary = openLibrary;
        _document = EditorDocument.Load(imagePath);
        InitializeComponent();
        WindowAppearanceService.ApplyDarkTitleBar(this);
        Title = $"{Localizer.Get("EditorWindowTitle")} — {Path.GetFileName(imagePath)}";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1280, 820));
        CenterWindow();

        CanvasHost.Width = _document.Width;
        CanvasHost.Height = _document.Height;
        EditorCanvas.Width = _document.Width;
        EditorCanvas.Height = _document.Height;
        SelectionCanvas.Width = _document.Width;
        SelectionCanvas.Height = _document.Height;
        InlineEditorCanvas.Width = _document.Width;
        InlineEditorCanvas.Height = _document.Height;
        FontFamilyCombo.ItemsSource = SKFontManager.Default.FontFamilies.OrderBy(name => name).ToArray();
        using (var defaultTypeface = SKFontManager.Default.MatchCharacter('A'))
        {
            FontFamilyCombo.SelectedItem = defaultTypeface?.FamilyName ?? "Arial";
        }
        _document.Changed += OnDocumentChanged;
        Closed += OnClosed;
        UndoMenuItem.IsEnabled = _document.CanUndo;
        RedoMenuItem.IsEnabled = _document.CanRedo;
        SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectHint"));
    }

    private void CenterWindow()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (area is null)
        {
            return;
        }

        var workArea = area.WorkArea;
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - 1280) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - 820) / 2)));
    }

    private void EditorCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        var scaleX = args.Info.Width / (float)_document.Width;
        var scaleY = args.Info.Height / (float)_document.Height;
        canvas.Scale(scaleX, scaleY);
        _document.Render(canvas);

        if (_dragStart is not null && _dragCurrent is not null && _tool == EditorInteractionTool.Shape)
        {
            using var paint = new SKPaint
            {
                Color = SelectedColor.WithAlpha(180),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f,
            };
            canvas.DrawRect(Normalize(_dragStart.Value, _dragCurrent.Value), paint);
        }
    }

    private void EditorCanvas_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = Clamp(args.GetCurrentPoint(EditorCanvas).Position);
        if (!args.GetCurrentPoint(EditorCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_tool is EditorInteractionTool.Text or EditorInteractionTool.Select)
        {
            var textElement = _document.HitTestText(new SKPoint((float)point.X, (float)point.Y));
            if (textElement is not null)
            {
                BeginInlineTextEdit(textElement, textElement.Bounds);
                args.Handled = true;
                return;
            }
        }

        _dragStart = point;
        _dragCurrent = point;
        EditorCanvas.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void EditorCanvas_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragStart is null)
        {
            return;
        }

        _dragCurrent = Clamp(args.GetCurrentPoint(EditorCanvas).Position);
        ShowSelection(Normalize(_dragStart.Value, _dragCurrent.Value));
        EditorCanvas.Invalidate();
    }

    private void EditorCanvas_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_dragStart is null)
        {
            return;
        }

        _dragCurrent = Clamp(args.GetCurrentPoint(EditorCanvas).Position);
        EditorCanvas.ReleasePointerCapture(args.Pointer);
        var region = ToPixelRect(Normalize(_dragStart.Value, _dragCurrent.Value));
        var start = new SKPoint((float)_dragStart.Value.X, (float)_dragStart.Value.Y);
        var end = new SKPoint((float)_dragCurrent.Value.X, (float)_dragCurrent.Value.Y);
        _dragStart = null;
        _dragCurrent = null;

        if (_tool == EditorInteractionTool.Text)
        {
            var placement = Normalize(
                new WinPoint(start.X, start.Y),
                new WinPoint(end.X, end.Y));
            if (placement.Width < 2 || placement.Height < 2)
            {
                placement = SKRect.Create(
                    start.X,
                    start.Y,
                    Math.Min(300f, Math.Max(120f, _document.Width - start.X)),
                    Math.Min(
                        Math.Max(52f, SelectedFontSize * 2f),
                        Math.Max(42f, _document.Height - start.Y)));
            }

            BeginInlineTextEdit(element: null, placement: placement);
            ClearSelection();
            args.Handled = true;
            return;
        }

        if (region.Width > 1 && region.Height > 1)
        {
            switch (_tool)
            {
                case EditorInteractionTool.Crop:
                    _document.Crop(region);
                    ResizeCanvasToDocument();
                    ClearSelection();
                    SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectHint"));
                    break;
                case EditorInteractionTool.Blur:
                    _document.Blur(region);
                    ClearSelection();
                    SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectHint"));
                    break;
                case EditorInteractionTool.Shape:
                    _document.AddShape(_shape, start, end, SelectedColor, 4f);
                    ClearSelection();
                    break;
                default:
                    _selection = region;
                    ShowSelection(Normalize(
                        new WinPoint(region.Left, region.Top),
                        new WinPoint(region.Right, region.Bottom)));
                    break;
            }
        }

        EditorCanvas.Invalidate();
        args.Handled = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveDocument();
    }

    private void SaveDocument()
    {
        CommitInlineText();
        _document.Save();
        EditorStatusText.Text = Localizer.Get("EditorSaved");
    }

    private async void OpenImageMenuItem_Click(object sender, RoutedEventArgs e) =>
        await OpenImageAsync();

    private async Task OpenImageAsync()
    {
        var picker = new global::Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            ViewMode = global::Windows.Storage.Pickers.PickerViewMode.Thumbnail,
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var editor = new EditorWindow(file.Path, _openLibrary);
        _childEditors.Add(editor);
        editor.Closed += (_, _) => _childEditors.Remove(editor);
        editor.Activate();
    }

    private async void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CommitInlineText();
        var picker = new global::Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(_document.SourcePath),
        };
        picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            _document.Save(file.Path);
            EditorStatusText.Text = Localizer.Format("SavedStatus", file.Name);
        }
    }

    private async void OpenLibraryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_openLibrary is not null)
        {
            await _openLibrary();
        }
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        _document.Undo();
        ResizeCanvasToDocument();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        _document.Redo();
        ResizeCanvasToDocument();
    }

    private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _selection = new SKRectI(0, 0, _document.Width, _document.Height);
        ShowSelection(SKRect.Create(_document.Width, _document.Height));
        SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectionAll"));
    }

    private void DeselectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (InlineTextEditor.Visibility == Visibility.Visible)
        {
            CancelInlineText();
        }

        ClearSelection();
        SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectHint"));
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectHint"));

    private void CropButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(EditorInteractionTool.Crop, Localizer.Get("EditorCropHint"));

    private async void ResizeButton_Click(object sender, RoutedEventArgs e)
    {
        var width = new NumberBox { Header = Localizer.Get("Width"), Value = _document.Width, Minimum = 1 };
        var height = new NumberBox { Header = Localizer.Get("Height"), Value = _document.Height, Minimum = 1 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(width);
        panel.Children.Add(height);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Localizer.Get("EditorResize"),
            Content = panel,
            PrimaryButtonText = Localizer.Get("Apply"),
            CloseButtonText = Localizer.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _document.Resize(Math.Max(1, (int)width.Value), Math.Max(1, (int)height.Value));
            ResizeCanvasToDocument();
        }
    }

    private void BlurButton_Click(object sender, RoutedEventArgs e) =>
        SetTool(EditorInteractionTool.Blur, Localizer.Get("EditorBlurHint"));

    private async void CopyButton_Click(object sender, RoutedEventArgs e) => await CopySelectionAsync();

    private async Task CopySelectionAsync()
    {
        if (_selection is null)
        {
            EditorStatusText.Text = Localizer.Get("EditorSelectFirst");
            return;
        }

        await CopyBytesToClipboardAsync(_document.CopyRegion(_selection.Value));
        EditorStatusText.Text = Localizer.Get("EditorCopied");
    }

    private async void CutButton_Click(object sender, RoutedEventArgs e) => await CutSelectionAsync();

    private async Task CutSelectionAsync()
    {
        if (_selection is null)
        {
            EditorStatusText.Text = Localizer.Get("EditorSelectFirst");
            return;
        }

        await CopyBytesToClipboardAsync(_document.Cut(_selection.Value));
        ClearSelection();
    }

    private async void PasteButton_Click(object sender, RoutedEventArgs e) => await PasteFromClipboardAsync();

    private async Task PasteFromClipboardAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap))
        {
            EditorStatusText.Text = Localizer.Get("EditorClipboardNoImage");
            return;
        }

        var reference = await content.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        using var input = stream.AsStreamForRead();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory);
        var position = _selection is null
            ? new SKPoint(0, 0)
            : new SKPoint(_selection.Value.Left, _selection.Value.Top);
        _document.Paste(memory.ToArray(), position);
    }

    private void TextButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
        SetTool(EditorInteractionTool.Text, Localizer.Get("EditorTextPlacementHint"));
    }

    private void BeginInlineTextEdit(EditorTextElement? element, SKRect placement)
    {
        SetTool(EditorInteractionTool.Text, Localizer.Get("EditorTextPlacementHint"));
        SetDocumentAcceleratorsEnabled(false);
        _ignoreInlineLostFocus = true;
        InlineTextEditor.Visibility = Visibility.Collapsed;

        if (element is not null)
        {
            _editingTextId = element.Id;
            InlineTextEditor.Text = element.Text;
            _inlineTextBaseline = element.Position;
            _inlineTextMaxWidth = element.MaxWidth > 0 ? element.MaxWidth : Math.Max(120f, placement.Width);
            FontFamilyCombo.SelectedItem = element.FontFamily;
            FontSizeBox.Value = element.FontSize;
            SelectColor(element.Color);
            placement = new SKRect(
                element.Bounds.Left,
                element.Bounds.Top,
                element.Bounds.Left + Math.Max(120f, _inlineTextMaxWidth),
                element.Bounds.Top + Math.Max(48f, element.Bounds.Height + 14f));
        }
        else
        {
            _editingTextId = null;
            InlineTextEditor.Text = string.Empty;
            _inlineTextBaseline = new SKPoint(placement.Left, placement.Top + SelectedFontSize);
            _inlineTextMaxWidth = Math.Max(120f, placement.Width);
        }

        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(InlineTextEditor, placement.Left);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(InlineTextEditor, placement.Top);
        InlineTextEditor.Width = Math.Min(
            Math.Max(120f, placement.Width),
            Math.Max(120f, _document.Width - placement.Left));
        InlineTextEditor.Height = Math.Min(
            Math.Max(48f, placement.Height),
            Math.Max(48f, _document.Height - placement.Top));
        ApplyInlineTextStyle();
        InlineTextEditor.Visibility = Visibility.Visible;
        _ignoreInlineLostFocus = false;
        InlineTextEditor.Focus(FocusState.Programmatic);
        if (element is not null)
        {
            InlineTextEditor.SelectAll();
        }
    }

    private void CommitInlineText()
    {
        if (InlineTextEditor.Visibility != Visibility.Visible)
        {
            return;
        }

        var text = InlineTextEditor.Text;
        var id = _editingTextId;
        _ignoreInlineLostFocus = true;
        InlineTextEditor.Visibility = Visibility.Collapsed;
        _editingTextId = null;
        SetDocumentAcceleratorsEnabled(true);

        if (id is not null)
        {
            _document.UpdateText(
                id.Value,
                text,
                _inlineTextBaseline,
                SelectedFont,
                SelectedFontSize,
                SelectedColor,
                _inlineTextMaxWidth);
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            _document.AddText(
                text,
                _inlineTextBaseline,
                SelectedFont,
                SelectedFontSize,
                SelectedColor,
                _inlineTextMaxWidth);
        }

        _ignoreInlineLostFocus = false;
        SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectHint"));
        EditorCanvas.Invalidate();
    }

    private void CancelInlineText()
    {
        _ignoreInlineLostFocus = true;
        InlineTextEditor.Visibility = Visibility.Collapsed;
        InlineTextEditor.Text = string.Empty;
        _editingTextId = null;
        SetDocumentAcceleratorsEnabled(true);
        _ignoreInlineLostFocus = false;
        SetTool(EditorInteractionTool.Select, Localizer.Get("EditorSelectHint"));
    }

    private void InlineTextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_ignoreInlineLostFocus && InlineTextEditor.Visibility == Visibility.Visible)
        {
            CommitInlineText();
        }
    }

    private void InlineTextEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            CancelInlineText();
            e.Handled = true;
        }
    }

    private void InlineTextCommitAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        CommitInlineText();
        args.Handled = true;
    }

    private void TextStyle_Changed(object sender, SelectionChangedEventArgs e) => ApplyInlineTextStyle();

    private void FontSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        ApplyInlineTextStyle();

    private void ApplyInlineTextStyle()
    {
        if (InlineTextEditor is null || InlineTextEditor.Visibility != Visibility.Visible)
        {
            return;
        }

        InlineTextEditor.FontFamily = new FontFamily(SelectedFont);
        InlineTextEditor.FontSize = SelectedFontSize;
        var color = SelectedColor;
        InlineTextEditor.Foreground = new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
    }

    private void SelectColor(SKColor color)
    {
        var tag = color == SKColors.Yellow
            ? "Yellow"
            : color == SKColors.White
                ? "White"
                : color == SKColors.Black
                    ? "Black"
                    : color == SKColors.DodgerBlue
                        ? "Blue"
                        : "Red";
        ColorCombo.SelectedItem = ColorCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    private async void WatermarkButton_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { Text = "BetterCapture", MinWidth = 320 };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Localizer.Get("EditorWatermark"),
            Content = input,
            PrimaryButtonText = Localizer.Get("Apply"),
            CloseButtonText = Localizer.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            _document.AddWatermark(input.Text, SelectedFont, SelectedFontSize, SelectedColor, 115);
        }
    }

    private void ArrowButton_Click(object sender, RoutedEventArgs e) => SetShape(EditorShape.Arrow);
    private void RectangleButton_Click(object sender, RoutedEventArgs e) => SetShape(EditorShape.Rectangle);
    private void EllipseButton_Click(object sender, RoutedEventArgs e) => SetShape(EditorShape.Ellipse);
    private void TriangleButton_Click(object sender, RoutedEventArgs e) => SetShape(EditorShape.Triangle);

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ChangeZoom(EditorScrollViewer.ZoomFactor / 1.25f);
    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ChangeZoom(EditorScrollViewer.ZoomFactor * 1.25f);

    private void FitButton_Click(object sender, RoutedEventArgs e)
    {
        var widthFactor = (float)Math.Max(0.1, EditorScrollViewer.ActualWidth / _document.Width);
        var heightFactor = (float)Math.Max(0.1, EditorScrollViewer.ActualHeight / _document.Height);
        ChangeZoom(Math.Min(widthFactor, heightFactor));
    }

    private void ActualSizeMenuItem_Click(object sender, RoutedEventArgs e) => ChangeZoom(1f);

    private async void ShortcutsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Localizer.Get("EditorKeyboardShortcuts"),
            Content = Localizer.Get("EditorKeyboardShortcutsText"),
            CloseButtonText = Localizer.Get("Close"),
        };
        await dialog.ShowAsync();
    }

    private async void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Localizer.Get("EditorAboutTitle"),
            Content = Localizer.Get("EditorAboutText"),
            CloseButtonText = Localizer.Get("Close"),
        };
        await dialog.ShowAsync();
    }

    private void ChangeZoom(float value)
    {
        var zoom = Math.Clamp(value, EditorScrollViewer.MinZoomFactor, EditorScrollViewer.MaxZoomFactor);
        EditorScrollViewer.ChangeView(null, null, zoom);
        ZoomText.Text = $"{zoom:P0}";
    }

    private void PropertiesPanelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var isVisible = PropertiesPanelMenuItem.IsChecked;
        PropertiesPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        PropertiesColumn.Width = isVisible ? new GridLength(292) : new GridLength(0);
    }

    private void SetShape(EditorShape shape)
    {
        _shape = shape;
        SetTool(EditorInteractionTool.Shape, Localizer.Get("EditorShapeHint"));
    }

    private void SetTool(EditorInteractionTool tool, string status)
    {
        _tool = tool;
        SelectButton.IsChecked = tool == EditorInteractionTool.Select;
        CropButton.IsChecked = tool == EditorInteractionTool.Crop;
        BlurButton.IsChecked = tool == EditorInteractionTool.Blur;
        TextButton.IsChecked = tool == EditorInteractionTool.Text;
        ArrowButton.IsChecked = tool == EditorInteractionTool.Shape && _shape == EditorShape.Arrow;
        RectangleButton.IsChecked = tool == EditorInteractionTool.Shape && _shape == EditorShape.Rectangle;
        EllipseButton.IsChecked = tool == EditorInteractionTool.Shape && _shape == EditorShape.Ellipse;
        TriangleButton.IsChecked = tool == EditorInteractionTool.Shape && _shape == EditorShape.Triangle;
        ColorPropertyGroup.Visibility = tool is EditorInteractionTool.Shape or EditorInteractionTool.Text
            ? Visibility.Visible
            : Visibility.Collapsed;
        TextPropertyGroup.Visibility = tool == EditorInteractionTool.Text
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoPropertiesText.Visibility = tool is EditorInteractionTool.Shape or EditorInteractionTool.Text
            ? Visibility.Collapsed
            : Visibility.Visible;
        EditorStatusText.Text = status;
    }

    private void SetDocumentAcceleratorsEnabled(bool isEnabled)
    {
        DocumentUndoAccelerator.IsEnabled = isEnabled;
        DocumentRedoAccelerator.IsEnabled = isEnabled;
        DocumentCutAccelerator.IsEnabled = isEnabled;
        DocumentCopyAccelerator.IsEnabled = isEnabled;
        DocumentPasteAccelerator.IsEnabled = isEnabled;
        DocumentSelectAllAccelerator.IsEnabled = isEnabled;
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        UndoMenuItem.IsEnabled = _document.CanUndo;
        RedoMenuItem.IsEnabled = _document.CanRedo;
        EditorCanvas.Invalidate();
    }

    private void ResizeCanvasToDocument()
    {
        CanvasHost.Width = _document.Width;
        CanvasHost.Height = _document.Height;
        EditorCanvas.Width = _document.Width;
        EditorCanvas.Height = _document.Height;
        SelectionCanvas.Width = _document.Width;
        SelectionCanvas.Height = _document.Height;
        InlineEditorCanvas.Width = _document.Width;
        InlineEditorCanvas.Height = _document.Height;
        ClearSelection();
        EditorCanvas.Invalidate();
    }

    private void ShowSelection(SKRect rectangle)
    {
        SelectionRectangle.Visibility = Visibility.Visible;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(SelectionRectangle, rectangle.Left);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(SelectionRectangle, rectangle.Top);
        SelectionRectangle.Width = rectangle.Width;
        SelectionRectangle.Height = rectangle.Height;
    }

    private void ClearSelection()
    {
        _selection = null;
        SelectionRectangle.Visibility = Visibility.Collapsed;
    }

    private WinPoint Clamp(WinPoint point) => new(
        Math.Clamp(point.X, 0, _document.Width),
        Math.Clamp(point.Y, 0, _document.Height));

    private static SKRect Normalize(WinPoint first, WinPoint second) => new(
        (float)Math.Min(first.X, second.X),
        (float)Math.Min(first.Y, second.Y),
        (float)Math.Max(first.X, second.X),
        (float)Math.Max(first.Y, second.Y));

    private static SKRectI ToPixelRect(SKRect region) => new(
        (int)MathF.Floor(region.Left),
        (int)MathF.Floor(region.Top),
        (int)MathF.Ceiling(region.Right),
        (int)MathF.Ceiling(region.Bottom));

    private SKColor SelectedColor => (ColorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
    {
        "Yellow" => SKColors.Yellow,
        "White" => SKColors.White,
        "Black" => SKColors.Black,
        "Blue" => SKColors.DodgerBlue,
        _ => SKColors.Red,
    };

    private string SelectedFont => FontFamilyCombo.SelectedItem?.ToString() ?? "Arial";

    private float SelectedFontSize => (float)Math.Clamp(FontSizeBox.Value, 6, 300);

    private static async Task CopyBytesToClipboardAsync(byte[] png)
    {
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void SaveAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        SaveDocument();
        args.Handled = true;
    }

    private async void OpenImageAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await OpenImageAsync();
    }

    private void SaveAsAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        SaveAsMenuItem_Click(sender, new RoutedEventArgs());
        args.Handled = true;
    }

    private void UndoAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        _document.Undo();
        ResizeCanvasToDocument();
        args.Handled = true;
    }

    private void RedoAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        _document.Redo();
        ResizeCanvasToDocument();
        args.Handled = true;
    }

    private async void CutAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        await CutSelectionAsync();
        args.Handled = true;
    }

    private async void CopyAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        await CopySelectionAsync();
        args.Handled = true;
    }

    private async void PasteAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        await PasteFromClipboardAsync();
        args.Handled = true;
    }

    private void SelectAllAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectAllMenuItem_Click(sender, new RoutedEventArgs());
        args.Handled = true;
    }

    private void ActualSizeAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ChangeZoom(1f);
        args.Handled = true;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _document.Changed -= OnDocumentChanged;
        _document.Dispose();
    }

    private enum EditorInteractionTool
    {
        Select,
        Crop,
        Blur,
        Shape,
        Text,
    }
}
