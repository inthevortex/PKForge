using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using PKForge.App.Services;

namespace PKForge.App.Platforms.Desktop;

public static class Program
{
    private static KeyboardSource? _keyboardSource;
    private static SdlGamepadSource? _gamepadSource;
    private static bool _isSuspended;

    [STAThread]
    public static int Main(string[] args)
    {
        AppLog.Install();
        App.Trace("Desktop Program.Main enter");
        DesktopWindowSizing.Configure(DesktopLaunchOptions.Parse(args));

        var builder = BuildAvaloniaApp();
        return builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AvaloniaApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(InitializeDesktopInput);
                Avalonia.Threading.Dispatcher.UIThread.Post(DesktopWindowSizing.AttachWhenReady);
            });

    private static void InitializeDesktopInput()
    {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var window = desktop?.MainWindow;
        if (window == null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(InitializeDesktopInput, Avalonia.Threading.DispatcherPriority.Background);
            return;
        }

        var pump = IPlatformApplication.Current?.Services.GetService<PadInputPump>();
        if (pump == null)
        {
            AppLog.Warn("input", "PadInputPump service not found; input sources not initialized");
            return;
        }

        _keyboardSource = new KeyboardSource(window, pump);
        _gamepadSource = new SdlGamepadSource(pump, window.IsActive);
        var platform = IPlatformApplication.Current?.Services.GetService<IAppPlatform>();
        if (platform is DesktopAppPlatform desktopPlatform)
            desktopPlatform.HapticSink = _gamepadSource.OnHaptic;
        pump.OnHaptic = () => platform?.Haptic(HapticKind.Click);
        _gamepadSource.Start();

        // Lifecycle state is touched only on the UI thread, so a pending suspend can never
        // land after the window has been reactivated.
        var suspendTimer = new Avalonia.Threading.DispatcherTimer();
        suspendTimer.Tick += (_, _) =>
        {
            suspendTimer.Stop();
            if (!window.IsActive || window.WindowState == Avalonia.Controls.WindowState.Minimized)
                Suspend();
        };

        void Suspend()
        {
            suspendTimer.Stop();
            if (_isSuspended) return;
            _isSuspended = true;
            App.RaiseSuspended();
            AppLog.Info("lifecycle", "Desktop window suspended (deactivated/minimized)");
        }

        void ScheduleSuspend(TimeSpan delay)
        {
            suspendTimer.Stop();
            suspendTimer.Interval = delay;
            suspendTimer.Start();
        }

        void Resume()
        {
            suspendTimer.Stop();
            if (!_isSuspended) return;
            _isSuspended = false;
            App.RaiseResumed();
            AppLog.Info("lifecycle", "Desktop window resumed");
        }

        window.Deactivated += (_, _) =>
        {
            _gamepadSource.SetWindowActive(false);
            pump.Reset();
            ScheduleSuspend(TimeSpan.FromSeconds(2));
        };

        window.Activated += (_, _) =>
        {
            _gamepadSource.SetWindowActive(true);
            Resume();
        };

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.WindowStateProperty && e.NewValue is Avalonia.Controls.WindowState state)
            {
                if (state == Avalonia.Controls.WindowState.Minimized)
                {
                    Suspend();
                }
                else if (window.IsActive)
                {
                    Resume();
                }
            }
        };

        window.Closed += (_, _) =>
        {
            suspendTimer.Stop();
            _gamepadSource?.Dispose();
            pump.Dispose();
        };

        AppLog.Info("input", "Desktop gamepad, keyboard sources, and window lifecycle initialized");
    }
}
