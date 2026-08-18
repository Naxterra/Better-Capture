using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using BetterCapture.App.Services;
using BetterCapture.App.Windows;
using BetterCapture.Capture;
using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;
using BetterCapture.Core.Video;
using Windows.Graphics;

namespace BetterCapture.App;

public sealed partial class MainWindow : Window
{
    private const double PreferredWidthDip = 640;
    private const double PreferredHeightDip = 550;
    private const double WorkAreaMarginDip = 24;

    private readonly WindowsGraphicsCaptureService _captureService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly CaptureWorkflow _workflow;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly List<EditorWindow> _editorWindows = [];
    private LibraryWindow? _libraryWindow;
    private GlobalHotkeyService? _hotkeyService;
    private CaptureHotkey _captureHotkey = HotkeyPreferenceService.Load();
    private bool _hotkeyRegistered;
    private int _hotkeyErrorCode;
    private MainPage? _page;
    private RecordingControlWindow? _recordingControl;

    public MainWindow()
    {
        InitializeComponent();
        AppTitleBar.Title = Localizer.Get("AppTitleBar/Title");
        AppTitleBar.Subtitle = Localizer.Get("AppTitleBar/Subtitle");
        WindowAppearanceService.ApplyDarkTitleBar(this);
        _workflow = new CaptureWorkflow(_captureService, _settingsService);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        RootFrame.Navigate(typeof(MainPage));
        RootFrame.Loaded += RootFrame_Loaded;
        ResizeAndCenter();

        AttachMainPage((MainPage)RootFrame.Content);

        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void ResizeAndCenter()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (area is null)
        {
            return;
        }

        var scale = Math.Max(1d, RootFrame.XamlRoot?.RasterizationScale ?? 1d);
        var margin = (int)Math.Ceiling(WorkAreaMarginDip * scale);
        var workArea = area.WorkArea;
        var availableWidth = Math.Max(1, workArea.Width - (margin * 2));
        var availableHeight = Math.Max(1, workArea.Height - (margin * 2));
        var width = Math.Min(availableWidth, (int)Math.Ceiling(PreferredWidthDip * scale));
        var height = Math.Min(availableHeight, (int)Math.Ceiling(PreferredHeightDip * scale));

        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height));
    }

    private void RootFrame_Loaded(object sender, RoutedEventArgs e)
    {
        RootFrame.Loaded -= RootFrame_Loaded;
        ResizeAndCenter();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_hotkeyService is not null)
        {
            return;
        }

        _hotkeyService = new GlobalHotkeyService(this);
        _hotkeyService.Pressed += OnPrintScreenPressed;
        _hotkeyRegistered = _hotkeyService.TryRegister(_captureHotkey, out _hotkeyErrorCode);
        _page?.SetHotkeyStatus(_captureHotkey, _hotkeyRegistered, _hotkeyErrorCode);
    }

    private void OnPrintScreenPressed(object? sender, EventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_recordingControl is not null)
            {
                _ = _recordingControl.StopAsync();
            }
            else
            {
                _ = CaptureScreenshotAsync();
            }
        });
    }

    private Task CaptureScreenshotAsync() => RunSmartCaptureAsync(CaptureOperation.Screenshot);

    private Task CaptureVideoAsync() => RunSmartCaptureAsync(CaptureOperation.Video);

    private async Task RunSmartCaptureAsync(CaptureOperation operation)
    {
        if (!await _captureGate.WaitAsync(0))
        {
            return;
        }

        SelectionOverlayWindow? overlay = null;
        try
        {
            _page?.SetBusy(Localizer.Get(
                operation == CaptureOperation.Video
                    ? "PreparingVideoCapture"
                    : "PreparingSmartCapture"));
            var target = _captureService.GetDisplayUnderCursor();
            var candidates = WindowCandidateService.GetCandidates(target);
            AppWindow.Hide();
            await Task.Delay(140);

            var prepared = await _workflow.PrepareAsync(
                target,
                new CaptureOptions
                {
                    IncludeCursor = _page?.IncludeCursor ?? true,
                    RequestBorderlessCapture = true,
                });

            overlay = new SelectionOverlayWindow(prepared.Preview, target, candidates);
            var selection = await overlay.SelectAsync();
            if (selection is null)
            {
                return;
            }

            if (operation == CaptureOperation.Video)
            {
                var session = _workflow.StartVideoRecording(
                    prepared,
                    selection,
                    includeCursor: _page?.IncludeCursor ?? true);
                _recordingControl = new RecordingControlWindow(
                    session,
                    target,
                    selection.Source.Description);
                var recordingResult = await _recordingControl.ShowUntilStoppedAsync();
                await _workflow.WriteVideoMetadataAsync(recordingResult);
                if (_libraryWindow is not null)
                {
                    await _libraryWindow.RefreshAsync();
                }
                _recordingControl = null;
                ShowDashboard();
                _page?.ShowVideo(recordingResult);
            }
            else
            {
                var saved = await _workflow.SaveAsync(prepared, selection, saveHdrMaster: true);
                await ClipboardService.CopyPngAsync(saved.PngPath);
                if (_libraryWindow is not null)
                {
                    await _libraryWindow.RefreshAsync();
                }
                ShowDashboard();
                _page?.ShowCapture(saved);
                OpenEditor(saved.PngPath);
            }
        }
        catch (Exception exception)
        {
            ShowDashboard();
            if (_page is not null)
            {
                await _page.ShowErrorAsync(Localizer.Get("CaptureFailedTitle"), exception.Message);
            }
        }
        finally
        {
            overlay?.Close();
            _recordingControl = null;
            ShowDashboard();
            _page?.SetReady();
            _captureGate.Release();
        }
    }

    private enum CaptureOperation
    {
        Screenshot,
        Video,
    }

    private void ShowDashboard()
    {
        AppWindow.Show();
        Activate();
    }

    private void OpenEditor(string imagePath)
    {
        var editor = new EditorWindow(imagePath, OpenLibraryAsync);
        _editorWindows.Add(editor);
        editor.Closed += (_, _) => _editorWindows.Remove(editor);
        editor.Activate();
    }

    private async Task OpenLibraryAsync()
    {
        if (_libraryWindow is not null)
        {
            _libraryWindow.Activate();
            await _libraryWindow.RefreshAsync();
            return;
        }

        var library = new LibraryWindow(_workflow.OutputRoot, OpenEditor);
        _libraryWindow = library;
        library.Closed += (_, _) =>
        {
            if (ReferenceEquals(_libraryWindow, library))
            {
                _libraryWindow = null;
            }
        };
        library.Activate();
    }

    private async Task ChangeLibraryFolderAsync()
    {
        var picker = new global::Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            CommitButtonText = Localizer.Get("UseThisFolder"),
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        await _settingsService.SetLibraryRootAsync(folder.Path);
        _page?.SetOutputFolder(_workflow.OutputRoot);
        if (_libraryWindow is not null)
        {
            await _libraryWindow.SetRootAndRefreshAsync(_workflow.OutputRoot);
        }
    }

    private async Task<bool> ChangeStartupSettingAsync(bool enabled)
    {
        try
        {
            StartupRegistrationService.SetEnabled(enabled);
        }
        catch (Exception exception)
        {
            if (_page is not null)
            {
                await _page.ShowErrorAsync(Localizer.Get("StartupSettingFailedTitle"), exception.Message);
            }
        }

        return StartupRegistrationService.IsEnabled;
    }

    private void AttachMainPage(MainPage page)
    {
        _page = page;
        _page.ScreenshotCaptureRequested = CaptureScreenshotAsync;
        _page.VideoCaptureRequested = CaptureVideoAsync;
        _page.OpenOutputFolderRequested = _workflow.OpenOutputFolderAsync;
        _page.ChangeLibraryFolderRequested = ChangeLibraryFolderAsync;
        _page.OpenLibraryRequested = OpenLibraryAsync;
        _page.StartupSettingChanged = ChangeStartupSettingAsync;
        _page.LanguageSettingChanged = ChangeLanguageAsync;
        _page.ChangeHotkeyRequested = ChangeHotkeyAsync;
        _page.SetOutputFolder(_workflow.OutputRoot);
        _page.SetStartupEnabled(StartupRegistrationService.IsEnabled);
        if (_hotkeyService is not null)
        {
            _page.SetHotkeyStatus(_captureHotkey, _hotkeyRegistered, _hotkeyErrorCode);
        }
    }

    private async Task ChangeHotkeyAsync()
    {
        if (_page is null || _hotkeyService is null)
        {
            return;
        }

        var selected = await _page.PromptHotkeyAsync(_captureHotkey);
        if (selected is null || selected.Value == _captureHotkey)
        {
            return;
        }

        var previous = _captureHotkey;
        if (_hotkeyService.TryRegister(selected.Value, out var errorCode))
        {
            _captureHotkey = selected.Value;
            HotkeyPreferenceService.Save(_captureHotkey);
            _hotkeyRegistered = true;
            _hotkeyErrorCode = 0;
            _page.SetHotkeyStatus(_captureHotkey, registered: true, errorCode: 0);
            return;
        }

        _hotkeyRegistered = _hotkeyService.TryRegister(previous, out _hotkeyErrorCode);
        _page.SetHotkeyStatus(previous, _hotkeyRegistered, _hotkeyErrorCode);
        await _page.ShowErrorAsync(
            Localizer.Get("CaptureShortcutFailedTitle"),
            Localizer.Format(
                "CaptureShortcutFailedMessage",
                HotkeyPreferenceService.GetDisplayName(selected.Value),
                errorCode));
    }

    private async Task ChangeLanguageAsync(string language)
    {
        if (string.Equals(
                LanguagePreferenceService.CurrentLanguage,
                language,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LanguagePreferenceService.Save(language);
        var canRestart = _editorWindows.Count == 0 &&
            _recordingControl is null &&
            _captureGate.CurrentCount > 0;
        if (_page is null || !await _page.PromptLanguageRestartAsync(canRestart))
        {
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            await _page.ShowErrorAsync(
                Localizer.Get("LanguageRestartTitle"),
                Localizer.Get("RestartFailedMessage"));
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            executablePath,
            "--restart")
        {
            UseShellExecute = true,
        });
        _libraryWindow?.Close();
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_hotkeyService is not null)
        {
            _hotkeyService.Pressed -= OnPrintScreenPressed;
            _hotkeyService.Dispose();
        }

        _captureService.Dispose();
        _captureGate.Dispose();
    }
}
