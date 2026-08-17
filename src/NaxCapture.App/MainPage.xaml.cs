using Microsoft.UI.Xaml.Controls;
using NaxCapture.App.Services;
using NaxCapture.Core.Video;

namespace NaxCapture.App;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        ToolTipService.SetToolTip(OpenFolderButton, Localizer.Get("OpenFolder"));
        ToolTipService.SetToolTip(ChangeFolderButton, Localizer.Get("ChangeLocation"));
        ToolTipService.SetToolTip(LibraryButton, Localizer.Get("OpenLibrary"));
    }

    internal Func<Task>? ScreenshotCaptureRequested { get; set; }

    internal Func<Task>? VideoCaptureRequested { get; set; }

    internal Func<Task>? OpenOutputFolderRequested { get; set; }

    internal Func<Task>? ChangeLibraryFolderRequested { get; set; }

    internal Func<Task>? OpenLibraryRequested { get; set; }

    internal bool IncludeCursor => CursorToggle.IsOn;

    internal void SetOutputFolder(string path) => OutputFolderText.Text = path;

    internal void SetBusy(string message)
    {
        BusyRing.IsActive = true;
        StatusText.Text = message;
        ScreenshotButton.IsEnabled = false;
        VideoButton.IsEnabled = false;
    }

    internal void SetReady()
    {
        BusyRing.IsActive = false;
        StatusText.Text = Localizer.Get("Ready");
        ScreenshotButton.IsEnabled = true;
        VideoButton.IsEnabled = true;
    }

    internal void SetHotkeyStatus(bool registered, int errorCode)
    {
        HotkeyInfo.Severity = registered ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        HotkeyInfo.Title = registered
            ? Localizer.Get("PrintScreenReadyTitle")
            : Localizer.Get("PrintScreenReservedTitle");
        HotkeyInfo.Message = registered
            ? Localizer.Get("PrintScreenReadyMessage")
            : Localizer.Format("PrintScreenReservedMessage", errorCode);
    }

    internal void ShowCapture(CaptureSaveResult result)
    {
        CaptureInfo.Title = result.Analysis.HasExtendedRange
            ? Localizer.Get("HdrCaptureSaved")
            : Localizer.Get("CaptureSaved");
        CaptureInfo.Message = Localizer.Format(
            "CaptureResultMessage",
            result.Source.Description,
            result.Width,
            result.Height,
            result.SdrWhiteLevelNits);
        CaptureInfo.IsOpen = true;
        StatusText.Text = Localizer.Format("SavedStatus", Path.GetFileName(result.PngPath));
    }

    internal void ShowVideo(VideoRecordingResult result)
    {
        CaptureInfo.Title = Localizer.Get("VideoSaved");
        CaptureInfo.Message = Localizer.Format(
            "VideoResultMessage",
            result.Source.Description,
            result.Width,
            result.Height,
            result.Duration);
        CaptureInfo.IsOpen = true;
        StatusText.Text = Localizer.Format("SavedStatus", Path.GetFileName(result.VideoPath));
    }

    internal async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = Localizer.Get("Close"),
        };
        await dialog.ShowAsync();
    }

    private async void ScreenshotButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ScreenshotCaptureRequested is not null)
        {
            await ScreenshotCaptureRequested();
        }
    }

    private async void VideoButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (VideoCaptureRequested is not null)
        {
            await VideoCaptureRequested();
        }
    }

    private async void OpenFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (OpenOutputFolderRequested is not null)
        {
            await OpenOutputFolderRequested();
        }
    }

    private async void ChangeFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ChangeLibraryFolderRequested is not null)
        {
            await ChangeLibraryFolderRequested();
        }
    }

    private async void LibraryButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (OpenLibraryRequested is not null)
        {
            await OpenLibraryRequested();
        }
    }
}
