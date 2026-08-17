using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;
using BetterCapture.Graphics.Images;
using BetterCapture.App.Services;
using static BetterCapture.App.Services.WindowCandidateService;
using Windows.Graphics;
using Windows.System;
using WinPoint = global::Windows.Foundation.Point;
using WinRect = global::Windows.Foundation.Rect;
using WinSize = global::Windows.Foundation.Size;

namespace BetterCapture.App.Windows;

internal sealed partial class SelectionOverlayWindow : Window
{
    private const int LoupeSampleSize = 21;
    private const double DragThreshold = 5;
    private readonly Bgra8Image _preview;
    private readonly DisplayTarget _target;
    private readonly IReadOnlyList<CaptureCandidate> _candidates;
    private readonly TaskCompletionSource<SmartCaptureSelection?> _selectionSource = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WriteableBitmap _loupeBitmap = new(LoupeSampleSize, LoupeSampleSize);
    private WinPoint? _dragStart;
    private CaptureCandidate? _pressedCandidate;
    private bool _isDragging;

    internal SelectionOverlayWindow(
        Bgra8Image preview,
        DisplayTarget target,
        IReadOnlyList<CaptureCandidate> candidates)
    {
        _preview = preview;
        _target = target;
        _candidates = candidates;
        InitializeComponent();
        Title = Localizer.Get("SelectorWindowTitle");

        PreviewImage.Source = CreateBitmap(preview.Width, preview.Height, preview.Pixels);
        LoupeImage.Source = _loupeBitmap;
        ConfigureWindow();
        InteractionCanvas.SizeChanged += OnCanvasSizeChanged;
        Closed += OnClosed;
    }

    internal Task<SmartCaptureSelection?> SelectAsync()
    {
        Activate();
        return _selectionSource.Task;
    }

    private void ConfigureWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        AppWindow.IsShownInSwitchers = false;
        AppWindow.MoveAndResize(new RectInt32(
            _target.DesktopBounds.X,
            _target.DesktopBounds.Y,
            _target.DesktopBounds.Width,
            _target.DesktopBounds.Height));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _selectionSource.TrySetResult(null);
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs args)
    {
        CanvasPosition(InstructionBar, Math.Max(12, (args.NewSize.Width - InstructionBar.ActualWidth) / 2), 18);
        ShowPixelRegion(
            new PixelRect(0, 0, _preview.Width, _preview.Height),
            _target.DeviceName);
    }

    private void InteractionCanvas_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(InteractionCanvas);
        if (point.Properties.IsRightButtonPressed)
        {
            Cancel();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = point.Position;
        _pressedCandidate = FindCandidate(point.Position);
        _isDragging = false;
        InteractionCanvas.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void InteractionCanvas_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        var position = args.GetCurrentPoint(InteractionCanvas).Position;
        UpdateLoupe(position);

        if (_dragStart is not null)
        {
            var deltaX = position.X - _dragStart.Value.X;
            var deltaY = position.Y - _dragStart.Value.Y;
            if (!_isDragging && Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) >= DragThreshold)
            {
                _isDragging = true;
                InstructionBar.Visibility = Visibility.Collapsed;
            }

            if (_isDragging)
            {
                UpdateSelection(position);
            }
        }
        else
        {
            UpdateHover(position);
        }
    }

    private void InteractionCanvas_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_dragStart is null)
        {
            return;
        }

        var end = args.GetCurrentPoint(InteractionCanvas).Position;
        InteractionCanvas.ReleasePointerCapture(args.Pointer);
        SmartCaptureSelection selection;
        if (_isDragging)
        {
            var region = ToPixelRegion(_dragStart.Value, end);
            if (region.Width < 2 || region.Height < 2)
            {
                ResetPointerState();
                InstructionBar.Visibility = Visibility.Visible;
                UpdateHover(end);
                return;
            }

            selection = new SmartCaptureSelection(region, CreateSourceForRegion(region));
        }
        else
        {
            var candidate = _pressedCandidate ?? FindCandidate(end);
            selection = candidate is null
                ? CreateFullScreenSelection()
                : new SmartCaptureSelection(candidate.Region, candidate.Source);
        }

        ResetPointerState();
        Complete(selection);
        args.Handled = true;
    }

    private void ResetPointerState()
    {
        _dragStart = null;
        _pressedCandidate = null;
        _isDragging = false;
    }

    private void CancelAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        Cancel();
        args.Handled = true;
    }

    private void Cancel()
    {
        AppWindow.Hide();
        _selectionSource.TrySetResult(null);
    }

    private void Complete(SmartCaptureSelection selection)
    {
        AppWindow.Hide();
        _selectionSource.TrySetResult(selection);
    }

    private void UpdateHover(WinPoint position)
    {
        var candidate = FindCandidate(position);
        if (candidate is null)
        {
            ShowPixelRegion(
                new PixelRect(0, 0, _preview.Width, _preview.Height),
                _target.DeviceName);
            return;
        }

        ShowPixelRegion(candidate.Region, candidate.Source.Description);
    }

    private CaptureCandidate? FindCandidate(WinPoint position)
    {
        if (InteractionCanvas.ActualWidth <= 0 || InteractionCanvas.ActualHeight <= 0)
        {
            return null;
        }

        var pixelPoint = new PixelPoint(
            Math.Clamp((int)Math.Floor(position.X * _preview.Width / InteractionCanvas.ActualWidth), 0, _preview.Width - 1),
            Math.Clamp((int)Math.Floor(position.Y * _preview.Height / InteractionCanvas.ActualHeight), 0, _preview.Height - 1));
        return WindowCandidateService.FindAt(_candidates, pixelPoint);
    }

    private CaptureSourceInfo CreateSourceForRegion(PixelRect region)
    {
        var candidate = WindowCandidateService.FindBestOverlap(_candidates, region);
        if (candidate is not null)
        {
            return candidate.Source with { Kind = CaptureSelectionKind.Region };
        }

        return new CaptureSourceInfo(
            CaptureSelectionKind.Region,
            Localizer.Get("WindowsDesktop"),
            _target.DeviceName,
            ToDesktopBounds(region));
    }

    private SmartCaptureSelection CreateFullScreenSelection()
    {
        var region = new PixelRect(0, 0, _preview.Width, _preview.Height);
        return new SmartCaptureSelection(
            region,
            new CaptureSourceInfo(
                CaptureSelectionKind.FullScreen,
                Localizer.Get("WindowsDesktop"),
                _target.DeviceName,
                _target.DesktopBounds));
    }

    private PixelRect ToDesktopBounds(PixelRect region) => new(
        region.X + _target.DesktopBounds.X,
        region.Y + _target.DesktopBounds.Y,
        region.Width,
        region.Height);

    private void UpdateSelection(WinPoint current)
    {
        var start = _dragStart!.Value;
        var left = Math.Clamp(Math.Min(start.X, current.X), 0, InteractionCanvas.ActualWidth);
        var top = Math.Clamp(Math.Min(start.Y, current.Y), 0, InteractionCanvas.ActualHeight);
        var right = Math.Clamp(Math.Max(start.X, current.X), 0, InteractionCanvas.ActualWidth);
        var bottom = Math.Clamp(Math.Max(start.Y, current.Y), 0, InteractionCanvas.ActualHeight);
        var selection = new WinRect(left, top, right - left, bottom - top);

        CanvasPosition(SelectionRectangle, selection.X, selection.Y);
        SelectionRectangle.Width = selection.Width;
        SelectionRectangle.Height = selection.Height;
        UpdateShade(selection);

        var pixelRegion = ToPixelRegion(start, current);
        DimensionText.Text = $"{pixelRegion.Width} × {pixelRegion.Height}";
        DimensionBadge.Measure(new WinSize(double.PositiveInfinity, double.PositiveInfinity));
        var badgeY = selection.Y >= 42
            ? selection.Y - DimensionBadge.DesiredSize.Height - 7
            : selection.Bottom + 7;
        CanvasPosition(
            DimensionBadge,
            Math.Clamp(selection.X, 8, Math.Max(8, InteractionCanvas.ActualWidth - DimensionBadge.DesiredSize.Width - 8)),
            Math.Clamp(badgeY, 8, Math.Max(8, InteractionCanvas.ActualHeight - DimensionBadge.DesiredSize.Height - 8)));
    }

    private void ShowPixelRegion(PixelRect region, string label)
    {
        if (InteractionCanvas.ActualWidth <= 0 || InteractionCanvas.ActualHeight <= 0)
        {
            return;
        }

        var scaleX = InteractionCanvas.ActualWidth / _preview.Width;
        var scaleY = InteractionCanvas.ActualHeight / _preview.Height;
        var selection = new WinRect(
            region.X * scaleX,
            region.Y * scaleY,
            region.Width * scaleX,
            region.Height * scaleY);
        SelectionRectangle.Visibility = Visibility.Visible;
        DimensionBadge.Visibility = Visibility.Visible;
        CanvasPosition(SelectionRectangle, selection.X, selection.Y);
        SelectionRectangle.Width = selection.Width;
        SelectionRectangle.Height = selection.Height;
        UpdateShade(selection);

        DimensionText.Text = $"{region.Width} × {region.Height}  ·  {label}";
        DimensionBadge.Measure(new WinSize(double.PositiveInfinity, double.PositiveInfinity));
        var badgeY = selection.Y >= 42
            ? selection.Y - DimensionBadge.DesiredSize.Height - 7
            : selection.Bottom + 7;
        CanvasPosition(
            DimensionBadge,
            Math.Clamp(selection.X, 8, Math.Max(8, InteractionCanvas.ActualWidth - DimensionBadge.DesiredSize.Width - 8)),
            Math.Clamp(badgeY, 8, Math.Max(8, InteractionCanvas.ActualHeight - DimensionBadge.DesiredSize.Height - 8)));
    }

    private void UpdateShade(WinRect? selection)
    {
        var width = InteractionCanvas.ActualWidth;
        var height = InteractionCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (selection is null)
        {
            SetRectangle(TopShade, 0, 0, width, height);
            SetRectangle(BottomShade, 0, 0, 0, 0);
            SetRectangle(LeftShade, 0, 0, 0, 0);
            SetRectangle(RightShade, 0, 0, 0, 0);
            return;
        }

        var value = selection.Value;
        SetRectangle(TopShade, 0, 0, width, value.Top);
        SetRectangle(BottomShade, 0, value.Bottom, width, Math.Max(0, height - value.Bottom));
        SetRectangle(LeftShade, 0, value.Top, value.Left, value.Height);
        SetRectangle(RightShade, value.Right, value.Top, Math.Max(0, width - value.Right), value.Height);
    }

    private void UpdateLoupe(WinPoint position)
    {
        if (InteractionCanvas.ActualWidth <= 0 || InteractionCanvas.ActualHeight <= 0)
        {
            return;
        }

        var pixelX = Math.Clamp(
            (int)Math.Floor(position.X * _preview.Width / InteractionCanvas.ActualWidth),
            0,
            _preview.Width - 1);
        var pixelY = Math.Clamp(
            (int)Math.Floor(position.Y * _preview.Height / InteractionCanvas.ActualHeight),
            0,
            _preview.Height - 1);
        var sample = new byte[LoupeSampleSize * LoupeSampleSize * 4];
        var radius = LoupeSampleSize / 2;

        for (var y = 0; y < LoupeSampleSize; y++)
        {
            var sourceY = Math.Clamp(pixelY + y - radius, 0, _preview.Height - 1);
            for (var x = 0; x < LoupeSampleSize; x++)
            {
                var sourceX = Math.Clamp(pixelX + x - radius, 0, _preview.Width - 1);
                var sourceOffset = ((sourceY * _preview.Width) + sourceX) * 4;
                var destinationOffset = ((y * LoupeSampleSize) + x) * 4;
                _preview.Pixels.AsSpan(sourceOffset, 4).CopyTo(sample.AsSpan(destinationOffset, 4));
            }
        }

        WriteBitmap(_loupeBitmap, sample);
        CoordinateText.Text = $"{pixelX}, {pixelY}  ·  8×";

        var left = position.X + 26;
        var top = position.Y + 26;
        if (left + Loupe.Width > InteractionCanvas.ActualWidth - 8)
        {
            left = position.X - Loupe.Width - 26;
        }

        if (top + Loupe.Height > InteractionCanvas.ActualHeight - 8)
        {
            top = position.Y - Loupe.Height - 26;
        }

        CanvasPosition(Loupe, Math.Max(8, left), Math.Max(8, top));
    }

    private PixelRect ToPixelRegion(WinPoint first, WinPoint second)
    {
        var scaleX = _preview.Width / InteractionCanvas.ActualWidth;
        var scaleY = _preview.Height / InteractionCanvas.ActualHeight;
        var left = (int)Math.Floor(Math.Min(first.X, second.X) * scaleX);
        var top = (int)Math.Floor(Math.Min(first.Y, second.Y) * scaleY);
        var right = (int)Math.Ceiling(Math.Max(first.X, second.X) * scaleX);
        var bottom = (int)Math.Ceiling(Math.Max(first.Y, second.Y) * scaleY);

        left = Math.Clamp(left, 0, _preview.Width);
        top = Math.Clamp(top, 0, _preview.Height);
        right = Math.Clamp(right, left, _preview.Width);
        bottom = Math.Clamp(bottom, top, _preview.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static WriteableBitmap CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = new WriteableBitmap(width, height);
        WriteBitmap(bitmap, pixels);
        return bitmap;
    }

    private static void WriteBitmap(WriteableBitmap bitmap, byte[] pixels)
    {
        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
    }

    private static void SetRectangle(FrameworkElement element, double x, double y, double width, double height)
    {
        CanvasPosition(element, x, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private static void CanvasPosition(UIElement element, double x, double y)
    {
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(element, x);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(element, y);
    }
}
