using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using BetterCapture.App.Services;
using BetterCapture.App.Windows;
using BetterCapture.Capture;
using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;
using BetterCapture.Core.Video;
using BetterCapture.Graphics.Color;
using BetterCapture.Graphics.Images;
using Windows.Graphics;

namespace BetterCapture.App;

public sealed partial class MainWindow : Window
{
    private const double PreferredWidthDip = 640;
    private const double MinimumHeightDip = 380;
    private const double WorkAreaMarginDip = 24;

    private readonly WindowsGraphicsCaptureService _captureService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly CaptureWorkflow _workflow;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly List<EditorWindow> _editorWindows = [];
    private LibraryWindow? _libraryWindow;
    private GlobalHotkeyService? _hotkeyService;
    private TrayIconService? _trayIconService;
    private CaptureHotkey _captureHotkey = HotkeyPreferenceService.Load();
    private bool _minimizeToTray = TrayPreferenceService.Load();
    private bool _hotkeyRegistered;
    private int _hotkeyErrorCode;
    private bool _isExiting;
    private bool _contentResizePending;
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
        WindowAppearanceService.ApplyAppIcon(this);
        RootFrame.Navigate(typeof(MainPage));
        RootFrame.Loaded += RootFrame_Loaded;
        ResizeAndCenter();

        AttachMainPage((MainPage)RootFrame.Content);

        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void ResizeAndCenter(bool center = true)
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
        var contentHeightDip = _page?.ContentExtentHeight ?? 0;
        var titleBarHeightDip = Math.Max(48, AppTitleBar.ActualHeight);
        var requestedHeightDip = Math.Max(
            MinimumHeightDip,
            contentHeightDip + titleBarHeightDip);
        var height = Math.Min(availableHeight, (int)Math.Ceiling(requestedHeightDip * scale));

        var x = center
            ? workArea.X + Math.Max(0, (workArea.Width - width) / 2)
            : Math.Clamp(AppWindow.Position.X, workArea.X, workArea.X + workArea.Width - width);
        var y = center
            ? workArea.Y + Math.Max(0, (workArea.Height - height) / 2)
            : Math.Clamp(AppWindow.Position.Y, workArea.Y, workArea.Y + workArea.Height - height);
        AppWindow.MoveAndResize(new RectInt32(
            x,
            y,
            width,
            height));
    }

    private void RootFrame_Loaded(object sender, RoutedEventArgs e)
    {
        RootFrame.Loaded -= RootFrame_Loaded;
        ResizeAndCenter();
    }

    private void OnContentExtentChanged(object? sender, EventArgs e)
    {
        if (_contentResizePending)
        {
            return;
        }

        _contentResizePending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _contentResizePending = false;
            ResizeAndCenter(center: false);
        });
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_hotkeyService is null)
        {
            _hotkeyService = new GlobalHotkeyService(this);
            _hotkeyService.Pressed += OnPrintScreenPressed;
            _hotkeyRegistered = _hotkeyService.TryRegister(_captureHotkey, out _hotkeyErrorCode);
            _page?.SetHotkeyStatus(_captureHotkey, _hotkeyRegistered, _hotkeyErrorCode);
        }

        if (_trayIconService is null)
        {
            _trayIconService = new TrayIconService(this);
            _trayIconService.OpenRequested += OnTrayOpenRequested;
            _trayIconService.CaptureRequested += OnTrayCaptureRequested;
            _trayIconService.ExitRequested += OnTrayExitRequested;
            _trayIconService.Minimized += OnTrayMinimized;
            _trayIconService.SetEnabled(_minimizeToTray);
        }
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
        CaptureTrace.Write($"capture requested · operation {operation} · gate {_captureGate.CurrentCount}");
        if (!await _captureGate.WaitAsync(0))
        {
            CaptureTrace.Write("capture ignored · gate already held");
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
            CaptureTrace.Write($"display selected · {target.DeviceName} · {target.DesktopBounds} · candidates {candidates.Count}");
            AppWindow.Hide();
            await Task.Delay(140);

            var prepared = await _workflow.PrepareAsync(
                target,
                new CaptureOptions
                {
                    IncludeCursor = _page?.IncludeCursor ?? false,
                    RequestBorderlessCapture = true,
                });

            overlay = new SelectionOverlayWindow(prepared.Preview, target, candidates);
            var selection = await overlay.SelectAsync();
            if (selection is null)
            {
                CaptureTrace.Write("selector cancelled");
                return;
            }

            CaptureTrace.Write(
                $"selector completed · kind {selection.Source.Kind} · app {selection.Source.ApplicationName} · " +
                $"region {selection.Region} · hwnd 0x{selection.WindowHandle:X}");

            if (operation == CaptureOperation.Video)
            {
                var session = _workflow.StartVideoRecording(
                    prepared,
                    selection,
                    includeCursor: _page?.IncludeCursor ?? false);
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
                var saved = selection.Source.Kind == CaptureSelectionKind.Scrolling
                    ? await CaptureScrollingSelectionAsync(prepared, selection)
                    : await _workflow.SaveAsync(prepared, selection, saveHdrMaster: true);
                await HandleSavedCaptureAsync(saved);
            }
        }
        catch (Exception exception)
        {
            CaptureTrace.Write($"capture failed · {exception.GetType().Name} · {exception.Message}");
            ShowDashboard();
            if (_page is not null)
            {
                await _page.ShowErrorAsync(Localizer.Get("CaptureFailedTitle"), exception.Message);
            }
        }
        finally
        {
            CaptureTrace.Write("capture workflow finished");
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

    private async Task<CaptureSaveResult> CaptureScrollingSelectionAsync(
        PreparedCapture prepared,
        SmartCaptureSelection selection)
    {
        const int maximumFrames = 160;
        const int maximumHeight = 30_000;
        if (selection.WindowHandle == 0 || selection.Source.Kind != CaptureSelectionKind.Scrolling)
        {
            throw new InvalidOperationException(Localizer.Get("ScrollingCaptureUnavailable"));
        }

        var toneMapSettings = new ToneMapSettings
        {
            SdrWhiteLevelNits = prepared.SdrWhiteLevelNits,
        };
        using var parkedCursor = CursorParkingScope.ParkAboveCaptureViewport(selection.Source.DesktopBounds);
        await Task.Delay(350);
        if (!WindowScrollService.TryScrollToStart(
                selection.WindowHandle,
                selection.Source.DesktopBounds,
                out var resetDetails))
        {
            CaptureTrace.Write($"scroll capture could not reset to start · {resetDetails}");
            throw new InvalidOperationException(Localizer.Get("ScrollingCaptureResetFailed"));
        }
        CaptureTrace.Write(resetDetails);

        await using var continuousCapture = _captureService.StartContinuousCapture(
            prepared.Target,
            selection.Region,
            includeCursor: false);
        var firstFrame = await continuousCapture.ReadLatestAsync();
        var firstPreview = await Task.Run(() => ScRgbToneMapper.ToBgra8(firstFrame, settings: toneMapSettings));
        var stitcher = new ScrollingFrameStitcher(firstFrame, firstPreview);
        CaptureTrace.Write($"scroll probe started · frame {firstFrame.Width}x{firstFrame.Height}");
        for (var frameIndex = 1; frameIndex < maximumFrames && stitcher.Height < maximumHeight; frameIndex++)
        {
            continuousCapture.DiscardPendingFrames();
            if (!WindowScrollService.TryScrollDown(
                    selection.WindowHandle,
                    selection.Source.DesktopBounds,
                    out var expectedAdvance,
                    out var reachedEnd,
                    out var scrollDetails))
            {
                CaptureTrace.Write($"scroll stopped · {scrollDetails}");
                break;
            }
            CaptureTrace.Write(scrollDetails);
            if (expectedAdvance == 0 && reachedEnd)
            {
                CaptureTrace.Write($"scroll stopped · UI Automation reached the end · frames {stitcher.FrameCount}");
                break;
            }

            var appended = false;
            var advance = 0;
            for (var settleAttempt = 0; settleAttempt < 10 && !appended; settleAttempt++)
            {
                await Task.Delay(settleAttempt == 0 ? 180 : 160);
                var nextFrame = await continuousCapture.ReadLatestAsync();
                var nextPreview = await Task.Run(() => ScRgbToneMapper.ToBgra8(nextFrame, settings: toneMapSettings));
                appended = stitcher.TryAppend(
                    nextFrame,
                    nextPreview,
                    maximumHeight,
                    out advance,
                    bottomTrim: 0,
                    allowFallback: false);
            }

            if (!appended)
            {
                CaptureTrace.Write(
                    $"scroll stopped · no movement or reliable image overlap after settling · " +
                    $"UIA expected {expectedAdvance}px · " +
                    $"frames {stitcher.FrameCount}");
                break;
            }
            CaptureTrace.Write(
                $"frame appended · measured advance {advance}px · UIA expected {expectedAdvance}px · " +
                $"height {stitcher.Height}");
        }

        if (stitcher.FrameCount == 1)
        {
            throw new InvalidOperationException(Localizer.Get("ScrollingCaptureNoMovement"));
        }

        var stitchedFrame = await Task.Run(stitcher.Build);
        var scrollingSource = selection.Source with { Kind = CaptureSelectionKind.Scrolling };
        CaptureTrace.Write($"scroll capture completed · frames {stitcher.FrameCount} · size {stitcher.Width}x{stitcher.Height}");
        return await _workflow.SaveFrameAsync(
            stitchedFrame,
            scrollingSource,
            selection.Source.DesktopBounds,
            prepared.SdrWhiteLevelNits,
            saveHdrMaster: true);
    }

    private async Task HandleSavedCaptureAsync(CaptureSaveResult saved)
    {
        await ClipboardService.CopyPngAsync(saved.PngPath);
        if (_libraryWindow is not null)
        {
            await _libraryWindow.RefreshAsync();
        }

        ShowDashboard();
        _page?.ShowCapture(saved);
        OpenEditor(saved.PngPath);
    }

    private void ShowDashboard()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        Activate();
    }

    internal void ActivateFromSecondaryInstance() => ShowDashboard();

    internal void StartInTray()
    {
        if (_minimizeToTray)
        {
            AppWindow.Hide();
        }
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
        _page.ContentExtentChanged += OnContentExtentChanged;
        _page.ScreenshotCaptureRequested = CaptureScreenshotAsync;
        _page.VideoCaptureRequested = CaptureVideoAsync;
        _page.OpenOutputFolderRequested = _workflow.OpenOutputFolderAsync;
        _page.ChangeLibraryFolderRequested = ChangeLibraryFolderAsync;
        _page.OpenLibraryRequested = OpenLibraryAsync;
        _page.StartupSettingChanged = ChangeStartupSettingAsync;
        _page.MinimizeToTraySettingChanged = ChangeMinimizeToTraySettingAsync;
        _page.LanguageSettingChanged = ChangeLanguageAsync;
        _page.ChangeHotkeyRequested = ChangeHotkeyAsync;
        _page.SetOutputFolder(_workflow.OutputRoot);
        _page.SetStartupEnabled(StartupRegistrationService.IsEnabled);
        _page.SetMinimizeToTrayEnabled(_minimizeToTray);
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

    private async Task<bool> ChangeMinimizeToTraySettingAsync(bool enabled)
    {
        try
        {
            TrayPreferenceService.Save(enabled);
            _minimizeToTray = enabled;
            _trayIconService?.SetEnabled(enabled);
        }
        catch (Exception exception)
        {
            if (_page is not null)
            {
                await _page.ShowErrorAsync(Localizer.Get("TraySettingFailedTitle"), exception.Message);
            }
        }

        return _minimizeToTray;
    }

    private void OnTrayOpenRequested(object? sender, EventArgs args) => ShowDashboard();

    private void OnTrayCaptureRequested(object? sender, EventArgs args) =>
        _ = CaptureScreenshotAsync();

    private void OnTrayMinimized(object? sender, EventArgs args)
    {
        if (_minimizeToTray && !_isExiting)
        {
            AppWindow.Hide();
        }
    }

    private void OnTrayExitRequested(object? sender, EventArgs args) => ExitApplication();

    private void ExitApplication()
    {
        _isExiting = true;
        _recordingControl?.Close();
        _libraryWindow?.Close();
        foreach (var editor in _editorWindows.ToArray())
        {
            editor.Close();
        }

        Close();
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
        _isExiting = true;
        if (_page is not null)
        {
            _page.ContentExtentChanged -= OnContentExtentChanged;
        }
        if (_hotkeyService is not null)
        {
            _hotkeyService.Pressed -= OnPrintScreenPressed;
            _hotkeyService.Dispose();
        }

        if (_trayIconService is not null)
        {
            _trayIconService.OpenRequested -= OnTrayOpenRequested;
            _trayIconService.CaptureRequested -= OnTrayCaptureRequested;
            _trayIconService.ExitRequested -= OnTrayExitRequested;
            _trayIconService.Minimized -= OnTrayMinimized;
            _trayIconService.Dispose();
        }

        _captureService.Dispose();
        _captureGate.Dispose();
    }
}
