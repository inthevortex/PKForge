using Avalonia;
using Avalonia.Controls.Maui;
using PKForge.App.Services;

namespace PKForge.App.Platforms.Desktop;

public static class Program
{
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
            .LogToTrace();
}
