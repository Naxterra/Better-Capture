using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace BetterCapture.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\BetterCapture.SingleInstance";
    private const string ActivationEventName = @"Local\BetterCapture.ActivateExisting";
    private MainWindow? _window;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCancellation;
    private volatile bool _activateWhenReady;
    private bool _ownsSingleInstanceMutex;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        if (args.Arguments.Contains("--restart", StringComparison.OrdinalIgnoreCase))
        {
            Thread.Sleep(650);
        }

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            if (!args.Arguments.Contains("--background", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                    activationEvent.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _activationCancellation = new CancellationTokenSource();
        _ = WatchForSecondaryLaunchesAsync(_activationCancellation.Token);
        Services.CaptureTrace.StartSession();

        _window = new MainWindow();
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
        if (args.Arguments.Contains("--background", StringComparison.OrdinalIgnoreCase))
        {
            _window.StartInTray();
        }

        if (_activateWhenReady)
        {
            _activateWhenReady = false;
            _window.ActivateFromSecondaryInstance();
        }
    }

    private Task WatchForSecondaryLaunchesAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var activationEvent = _activationEvent;
        if (activationEvent is null)
        {
            return;
        }

        var waits = new WaitHandle[] { activationEvent, cancellationToken.WaitHandle };
        while (WaitHandle.WaitAny(waits) == 0)
        {
            var window = _window;
            if (window is null)
            {
                _activateWhenReady = true;
                continue;
            }

            window.DispatcherQueue.TryEnqueue(window.ActivateFromSecondaryInstance);
        }
    }, cancellationToken);

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
        _activationCancellation = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
        }

        _ownsSingleInstanceMutex = false;
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }
}
