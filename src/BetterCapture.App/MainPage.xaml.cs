using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using BetterCapture.App.Services;
using BetterCapture.Core.Video;

namespace BetterCapture.App;

public sealed partial class MainPage : Page
{
    private bool _settingStartupToggle;
    private bool _settingTrayToggle;

    public MainPage()
    {
        InitializeComponent();
        ApplyLocalization();
        InitializeLanguageMenu();
        ToolTipService.SetToolTip(OpenFolderButton, Localizer.Get("OpenFolder"));
        ToolTipService.SetToolTip(ChangeFolderButton, Localizer.Get("ChangeLocation"));
        ToolTipService.SetToolTip(LibraryButton, Localizer.Get("OpenLibrary"));
        ToolTipService.SetToolTip(LanguageButton, Localizer.Get("Language"));
        ToolTipService.SetToolTip(SettingsButton, Localizer.Get("Settings"));
        ToolTipService.SetToolTip(AboutButton, Localizer.Get("About"));
        AutomationProperties.SetName(LanguageButton, Localizer.Get("Language"));
        AutomationProperties.SetName(SettingsButton, Localizer.Get("Settings"));
        AutomationProperties.SetName(AboutButton, Localizer.Get("About"));
    }

    internal Func<Task>? ScreenshotCaptureRequested { get; set; }

    internal Func<Task>? VideoCaptureRequested { get; set; }

    internal Func<Task>? OpenOutputFolderRequested { get; set; }

    internal Func<Task>? ChangeLibraryFolderRequested { get; set; }

    internal Func<Task>? OpenLibraryRequested { get; set; }

    internal Func<bool, Task<bool>>? StartupSettingChanged { get; set; }

    internal Func<bool, Task<bool>>? MinimizeToTraySettingChanged { get; set; }

    internal Func<string, Task>? LanguageSettingChanged { get; set; }

    internal Func<Task>? ChangeHotkeyRequested { get; set; }

    internal event EventHandler? ContentExtentChanged;

    internal bool IncludeCursor => CursorToggle.IsOn;

    internal double ContentExtentHeight => DashboardContent.DesiredSize.Height;

    internal void SetOutputFolder(string path) => OutputFolderText.Text = path;

    private void DashboardContent_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5)
        {
            ContentExtentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyLocalization()
    {
        CompactPromptText.Text = Localizer.Get("CompactPrompt/Text");
        CompactHintText.Text = Localizer.Get("CompactHint/Text");
        ScreenshotTitleText.Text = Localizer.Get("ScreenshotTitle/Text");
        ScreenshotHintText.Text = Localizer.Get("ScreenshotHint/Text");
        VideoTitleText.Text = Localizer.Get("VideoTitle/Text");
        VideoHintText.Text = Localizer.Get("VideoHint/Text");
        LibraryTitleText.Text = Localizer.Get("LibraryTitle/Text");
        LibraryHintText.Text = Localizer.Get("LibraryHint/Text");
        CursorLabelText.Text = Localizer.Get("CursorLabel/Text");
        StorageLocationLabelText.Text = Localizer.Get("StorageLocationLabel");
        StartupMenuItem.Text = Localizer.Get("StartupMenuItem");
        MinimizeToTrayMenuItem.Text = Localizer.Get("MinimizeToTrayMenuItem");
    }

    internal void SetStartupEnabled(bool enabled)
    {
        _settingStartupToggle = true;
        StartupMenuItem.IsChecked = enabled;
        _settingStartupToggle = false;
    }

    internal void SetMinimizeToTrayEnabled(bool enabled)
    {
        _settingTrayToggle = true;
        MinimizeToTrayMenuItem.IsChecked = enabled;
        _settingTrayToggle = false;
    }

    internal void SetBusy(string message)
    {
        CaptureInfo.Severity = InfoBarSeverity.Informational;
        CaptureInfo.Title = message;
        CaptureInfo.Message = string.Empty;
        CaptureInfo.IsOpen = true;
        ScreenshotButton.IsEnabled = false;
        VideoButton.IsEnabled = false;
        LibraryButton.IsEnabled = false;
    }

    internal void SetReady()
    {
        if (CaptureInfo.Severity == InfoBarSeverity.Informational)
        {
            CaptureInfo.IsOpen = false;
        }

        ScreenshotButton.IsEnabled = true;
        VideoButton.IsEnabled = true;
        LibraryButton.IsEnabled = true;
    }

    internal void SetHotkeyStatus(CaptureHotkey hotkey, bool registered, int errorCode)
    {
        var displayName = HotkeyPreferenceService.GetDisplayName(hotkey);
        ShortcutMenuItem.Text = registered
            ? Localizer.Format("CaptureShortcutMenu", displayName)
            : Localizer.Format("CaptureShortcutUnavailable", displayName, errorCode);
    }

    internal void ShowCapture(CaptureSaveResult result)
    {
        CaptureInfo.Severity = InfoBarSeverity.Success;
        CaptureInfo.Title = result.Source.Kind == BetterCapture.Core.Capture.CaptureSelectionKind.Scrolling
            ? Localizer.Get("ScrollingCaptureSaved")
            : result.Analysis.HasExtendedRange
                ? Localizer.Get("HdrCaptureSaved")
                : Localizer.Get("CaptureSaved");
        CaptureInfo.Message = Localizer.Format(
            "CaptureResultMessage",
            result.Source.Description,
            result.Width,
            result.Height,
            result.SdrWhiteLevelNits);
        CaptureInfo.IsOpen = true;
    }

    internal void ShowVideo(VideoRecordingResult result)
    {
        CaptureInfo.Severity = InfoBarSeverity.Success;
        CaptureInfo.Title = Localizer.Get("VideoSaved");
        CaptureInfo.Message = Localizer.Format(
            "VideoResultMessage",
            result.Source.Description,
            result.Width,
            result.Height,
            result.Duration);
        CaptureInfo.IsOpen = true;
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
        CaptureTrace.Write("Screenshot button clicked");
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

    private async void StartupMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_settingStartupToggle || StartupSettingChanged is null)
        {
            return;
        }

        StartupMenuItem.IsEnabled = false;
        try
        {
            SetStartupEnabled(await StartupSettingChanged(StartupMenuItem.IsChecked));
        }
        finally
        {
            StartupMenuItem.IsEnabled = true;
        }
    }

    private async void ShortcutMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ChangeHotkeyRequested is not null)
        {
            await ChangeHotkeyRequested();
        }
    }

    private async void MinimizeToTrayMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_settingTrayToggle || MinimizeToTraySettingChanged is null)
        {
            return;
        }

        MinimizeToTrayMenuItem.IsEnabled = false;
        try
        {
            SetMinimizeToTrayEnabled(
                await MinimizeToTraySettingChanged(MinimizeToTrayMenuItem.IsChecked));
        }
        finally
        {
            MinimizeToTrayMenuItem.IsEnabled = true;
        }
    }

    private void InitializeLanguageMenu()
    {
        var language = LanguagePreferenceService.CurrentLanguage;
        EnglishLanguageMenuItem.IsChecked = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        GermanLanguageMenuItem.IsChecked = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
    }

    private async void LanguageMenuItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (LanguageSettingChanged is null || sender is not ToggleMenuFlyoutItem { Tag: string language })
        {
            return;
        }

        await LanguageSettingChanged(language);
        InitializeLanguageMenu();
    }

    private async void AboutButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var version = typeof(MainPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Format("AppAboutTitle", version),
            Content = new TextBlock
            {
                Text = $"{Localizer.Get("AppAboutText")}\n\n{CreatorIdentity.AboutLine}",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            },
            CloseButtonText = Localizer.Get("Close")
        };
        await dialog.ShowAsync();
    }

    internal async Task<CaptureHotkey?> PromptHotkeyAsync(CaptureHotkey current)
    {
        var options = HotkeyPreferenceService.Supported
            .Select(hotkey => new HotkeyChoice(hotkey, HotkeyPreferenceService.GetDisplayName(hotkey)))
            .ToArray();
        var selector = new ComboBox
        {
            Header = Localizer.Get("CaptureShortcutLabel"),
            ItemsSource = options,
            DisplayMemberPath = nameof(HotkeyChoice.Display),
            SelectedItem = options.First(option => option.Hotkey == current),
            MinWidth = 280,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get("CaptureShortcutTitle"),
            Content = selector,
            PrimaryButtonText = Localizer.Get("Apply"),
            CloseButtonText = Localizer.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary &&
            selector.SelectedItem is HotkeyChoice choice
                ? choice.Hotkey
                : null;
    }

    internal async Task<bool> PromptLanguageRestartAsync(bool canRestart)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get("LanguageRestartTitle"),
            Content = Localizer.Get(canRestart
                ? "LanguageRestartMessage"
                : "LanguageRestartDeferredMessage"),
            CloseButtonText = Localizer.Get(canRestart ? "Later" : "Close"),
            DefaultButton = canRestart ? ContentDialogButton.Primary : ContentDialogButton.Close,
        };
        if (canRestart)
        {
            dialog.PrimaryButtonText = Localizer.Get("RestartNow");
        }

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private sealed record HotkeyChoice(CaptureHotkey Hotkey, string Display);
}
