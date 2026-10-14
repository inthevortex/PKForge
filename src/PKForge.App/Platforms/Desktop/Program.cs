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
        _gamepadSource = new SdlGamepadSource(pump, window.IsActive);
        var platform = IPlatformApplication.Current?.Services.GetService<IAppPlatform>();
        if (platform is DesktopAppPlatform desktopPlatform)
            desktopPlatform.HapticSink = _gamepadSource.OnHaptic;
        pump.OnHaptic = () => platform?.Haptic(HapticKind.Click);
        _gamepadSource.Start();

        window.Deactivated += (_, _) =>
        {
            _gamepadSource.SetWindowActive(false);
            pump.Reset();
        };

        window.Activated += (_, _) => _gamepadSource.SetWindowActive(true);

        window.Closed += (_, _) =>
        {
            _gamepadSource?.Dispose();
            pump.Dispose();
        };

        AppLog.Info("input", "Desktop gamepad and keyboard sources initialized");
    }
}
