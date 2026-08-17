using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NaxCapture.App.Services;
using NaxCapture.Core.Capture;
using NaxCapture.Core.Video;
using NaxCapture.Video;
using Windows.Graphics;

namespace NaxCapture.App.Windows;

internal sealed partial class RecordingControlWindow : Window
{
    private const uint ExcludeFromCapture = 0x00000011;
    private readonly VideoRecordingSession _session;
    private readonly TaskCompletionSource<VideoRecordingResult> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _stopping;

    internal RecordingControlWindow(VideoRecordingSession session, DisplayTarget target, string sourceDescription)
    {
        _session = session;
        InitializeComponent();
        Title = Localizer.Get("RecordingWindowTitle");
        SourceText.Text = sourceDescription;
        ConfigureWindow(target);
        _timer.Tick += OnTimerTick;
        Closed += OnClosed;
    }

    internal Task<VideoRecordingResult> ShowUntilStoppedAsync()
    {
        _timer.Start();
        Activate();
        return _completion.Task;
    }

    internal async Task StopAsync()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        StopButton.IsEnabled = false;
        _timer.Stop();
        try
        {
            var result = await _session.StopAsync();
            _completion.TrySetResult(result);
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
        finally
        {
            Close();
        }
    }

    private void ConfigureWindow(DisplayTarget target)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        const int width = 390;
        const int height = 82;
        AppWindow.IsShownInSwitchers = false;
        AppWindow.MoveAndResize(new RectInt32(
            target.DesktopBounds.Right - width - 24,
            target.DesktopBounds.Y + 24,
            width,
            height));

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = SetWindowDisplayAffinity(windowHandle, ExcludeFromCapture);
    }

    private void OnTimerTick(object? sender, object e)
    {
        var progress = _session.GetProgress();
        ElapsedText.Text = progress.Elapsed.TotalHours >= 1
            ? progress.Elapsed.ToString(@"hh\:mm\:ss")
            : progress.Elapsed.ToString(@"mm\:ss");
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e) => await StopAsync();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _timer.Stop();
        if (!_stopping)
        {
            _ = StopAsync();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
