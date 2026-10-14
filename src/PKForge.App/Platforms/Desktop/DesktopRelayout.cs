using Avalonia.Controls;
using Avalonia.Threading;
using PKForge.App.Services;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Rebuilds the pages when the length of a mockup pixel changes, after a resize or a UI size
/// change: the zoom rescales what is on screen, but mockup-sized parts such as editor rows keep
/// the length they were built with. The rebuild waits for the window to settle and for Home to
/// be idle, so nothing the player has open is torn down.
/// </summary>
internal static class DesktopRelayout
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(1);

    private static AvaloniaWindow? _window;
    private static DispatcherTimer? _timer;
    private static double? _built;
    private static bool _rebuilding;

    /// <summary>
    /// Starts following <paramref name="window"/>. Call before its first metrics refresh, so
    /// pages built before the window had a size are compared with what it measures.
    /// </summary>
    public static void Attach(AvaloniaWindow window)
    {
        _window = window;
        _built = ScreenMetrics.LastDesign;
        _timer = new DispatcherTimer { Interval = SettleDelay };
        _timer.Tick += (_, _) => OnSettled();
        window.Closed += (_, _) => _timer?.Stop();
    }

    /// <summary>Call after each metrics refresh; schedules a rebuild while the pages are out of date.</summary>
    public static void Follow()
    {
        var now = ScreenMetrics.DesignScale;
        if (_built is not { } built)
        {
            _built = now;
            return;
        }
        if (DesignMetrics.IsSignificantChange(built, now)) Restart(SettleDelay);
        else _timer?.Stop();
    }

    /// <summary>Notes that the pages were just built again from the current metrics.</summary>
    public static void Rebuilt() => _built = ScreenMetrics.DesignScale;

    private static void Restart(TimeSpan delay)
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    private static async void OnSettled()
    {
        _timer?.Stop();
        if (_rebuilding || _built is not { } built) return;
        var now = ScreenMetrics.DesignScale;
        if (!DesignMetrics.IsSignificantChange(built, now)) return;
        if (Microsoft.Maui.Controls.Application.Current is not App app) return;
        if (_window?.WindowState == WindowState.Minimized || !app.IsHomeIdle())
        {
            Restart(BusyRetryDelay);
            return;
        }

        _rebuilding = true;
        try
        {
            await app.ReloadForThemeAsync();
            AppLog.Info("display", $"Rebuilt pages for a mockup pixel of {now:0.###} units (was {built:0.###})");
        }
        catch (Exception error)
        {
            AppLog.Error("display", "Rebuilding pages after a layout change failed", error);
        }
        finally
        {
            _rebuilding = false;
        }
    }
}
