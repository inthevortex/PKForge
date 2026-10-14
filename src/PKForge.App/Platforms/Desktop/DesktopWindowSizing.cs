using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Maui.LifecycleEvents;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Maui.LifecycleEvents;
using SkiaSharp.Views.Maui.Controls;
using Dispatcher = Avalonia.Threading.Dispatcher;
using PKForge.App.Services;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Puts the main window in the mode the command line asked for and zooms the whole window:
/// pages are laid out on a surface the UI size sets and scaled uniformly to fill it (see <see cref="DesktopZoom"/>).
/// </summary>
public static class DesktopWindowSizing
{
    private static DesktopLaunchOptions _options = DesktopLaunchOptions.Default;
    private static AvaloniaWindow? _window;

    /// <summary>Keeps the parsed command line for the window and reports what it cannot honour.</summary>
    public static void Configure(DesktopLaunchOptions options)
    {
        _options = options;
        foreach (var warning in options.Warnings)
            AppLog.Warn("launch", warning);
        if (options.Wayland)
            AppLog.Warn("launch", "--wayland needs the Avalonia Wayland backend, which this build does not include; running through X11/XWayland.");
    }

    /// <summary>Zooms the main window as soon as it is created and paints Skia canvases and bitmaps at its real pixel size.</summary>
    public static MauiAppBuilder UseDesktopZoom(this MauiAppBuilder builder)
    {
        DesktopImages.Register();
        return builder
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<SKCanvasView, ZoomedSKCanvasViewHandler>())
            .ConfigureLifecycleEvents(events => events.AddWindows(window => window.OnWindowCreated(InstallZoom)));
    }

    /// <summary>Waits for the main window, then applies the launch mode.</summary>
    public static void AttachWhenReady()
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
        {
            Dispatcher.UIThread.Post(AttachWhenReady, DispatcherPriority.Background);
            return;
        }
        if (ReferenceEquals(window, _window)) return;

        _window = window;
        Apply(window, _options);
    }

    private static void Apply(AvaloniaWindow window, DesktopLaunchOptions options)
    {
        if (options.Size is { } size)
        {
            var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1.0;
            window.WindowState = WindowState.Normal;
            window.Width = size.Width / scaling;
            window.Height = size.Height / scaling;
        }
        window.WindowState = options.Mode == DesktopWindowMode.Fullscreen ? WindowState.FullScreen : WindowState.Normal;
        AppLog.Info("launch", options.Size is { } requested
            ? $"Window mode {options.Mode}, size {requested}"
            : $"Window mode {options.Mode}");
    }

    /// <summary>
    /// Moves the window's content into a <see cref="LayoutTransformControl"/> that follows the
    /// window size and the UI size. Runs before the window is shown, so nothing has loaded yet.
    /// </summary>
    private static void InstallZoom(AvaloniaWindow window)
    {
        if (window.Content is not Control root || root is LayoutTransformControl) return;
        window.Content = null;
        var host = new LayoutTransformControl { Child = root };
        window.Content = host;

        void Follow()
        {
            var size = window.ClientSize;
            var uiSize = UiSizeSetting.Current;
            var factor = DesktopZoom.Factor(size.Width, size.Height, uiSize);
            // A fixed surface size keeps measure and arrange equal. Left to the transform, the
            // available size comes out a hair over it (the control rounds its matrix to 8
            // decimals), layout rounding then arranges the page a pixel wider than it measured,
            // and text can stay laid out for its measure width: shifted and clipped.
            (root.Width, root.Height) = DesktopZoom.Surface(size.Width, size.Height, window.RenderScaling, uiSize);
            host.LayoutTransform = new ScaleTransform(factor, factor);
            ScreenMetrics.Refresh(window);
            DesktopZoom.Set(factor);
            DesktopRelayout.Follow();
        }

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == TopLevel.ClientSizeProperty) Follow();
        };
        window.ScalingChanged += (_, _) => Follow();
        UiSizeSetting.Changed += Follow;
        window.Closed += (_, _) => UiSizeSetting.Changed -= Follow;
        DesktopRelayout.Attach(window);
        Follow();
    }
}
