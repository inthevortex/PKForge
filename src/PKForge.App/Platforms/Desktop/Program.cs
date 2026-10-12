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
        _gamepadSource = new SdlGamepadSource(pump, () => window.IsActive);
        pump.OnHaptic = () => _gamepadSource.Rumble();
        _gamepadSource.Start();

        var suspendTimer = new System.Threading.Timer(_ =>
        {
            if (!_isSuspended)
            {
                _isSuspended = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    App.RaiseSuspended();
                    AppLog.Info("lifecycle", "Desktop window suspended (deactivated/minimized)");
                });
            }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        void ScheduleSuspend(int delayMs)
        {
            suspendTimer.Change(delayMs, System.Threading.Timeout.Infinite);
        }

        void CancelSuspend()
        {
            suspendTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            if (_isSuspended)
            {
                _isSuspended = false;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    App.RaiseResumed();
                    AppLog.Info("lifecycle", "Desktop window resumed");
                });
            }
        }

        window.Deactivated += (_, _) =>
        {
            pump.Reset();
            ScheduleSuspend(2000);
        };

        window.Activated += (_, _) =>
        {
            CancelSuspend();
        };

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.WindowStateProperty && e.NewValue is Avalonia.Controls.WindowState state)
            {
                if (state == Avalonia.Controls.WindowState.Minimized)
                {
                    ScheduleSuspend(0);
                }
                else if (window.IsActive)
                {
                    CancelSuspend();
                }
            }
        };

        window.Closed += (_, _) =>
        {
            suspendTimer.Dispose();
            _gamepadSource?.Dispose();
            pump.Dispose();
        };

        AppLog.Info("input", "Desktop gamepad, keyboard sources, and window lifecycle initialized");
    }
}
