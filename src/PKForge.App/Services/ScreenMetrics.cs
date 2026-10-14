using Microsoft.Maui.Devices;
using SkiaSharp;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Services;

/// <summary>
/// Manages display metrics (density, physical dimensions, and mockup scaling). Values are
/// measured once and kept until <see cref="Refresh"/> or, on Android, a configuration change,
/// so every page built in between shares the same sizes.
/// </summary>
public static class ScreenMetrics
{
    private static readonly DesignMetricsCache Cache = new(MeasureDisplay);

#if ANDROID
    private static readonly ConfigurationWatcher Watcher = new();

    static ScreenMetrics() => Android.App.Application.Context.RegisterComponentCallbacks(Watcher);

    /// <summary>
    /// A display resolution or density switch, or a foldable opening, keeps the activity (see
    /// MainActivity's ConfigurationChanges) and arrives here; pages built afterwards measure again.
    /// </summary>
    private sealed class ConfigurationWatcher : Java.Lang.Object, Android.Content.IComponentCallbacks
    {
        public void OnConfigurationChanged(Android.Content.Res.Configuration newConfig) => Cache.Invalidate();

        public void OnLowMemory()
        {
        }
    }
#endif

    /// <summary>Display density (scaling factor). Guaranteed to be greater than zero.</summary>
    public static double Density
    {
        get
        {
            var density = Cache.Current.Density;
            return density > 0 ? density : 1.0;
        }
    }

    /// <summary>Physical pixel length of the longer edge of the display.</summary>
    public static double LongSidePx
    {
        get
        {
            var longSide = Cache.Current.LongSidePx;
            return longSide > 0 ? longSide : DesignMetrics.MockupLongSide;
        }
    }

    /// <summary>Overall design scaling factor converting 1920x1080 mockup pixels to DIPs.</summary>
    public static double DesignScale => (LongSidePx / DesignMetrics.MockupLongSide) / Density;

    /// <summary>
    /// Scales a length in mockup pixels (1920x1080 design) into device-independent units.
    /// </summary>
    public static double Design(double pixels) => DesignMetrics.Design(pixels, Cache.Current);

    /// <summary>Measures again; pages built afterwards follow the new size.</summary>
    internal static DisplaySnapshot Refresh() => Cache.Refresh();

    /// <summary>
    /// Computes the exact per-paint pixel-to-dp ratio from an SKCanvasView paint callback,
    /// falling back to the current display density.
    /// </summary>
    public static float CanvasDensity(object? sender, SKImageInfo info)
    {
        if (sender is SKCanvasView view && view.Width > 0)
        {
            var ratio = (float)(info.Width / view.Width);
            if (ratio > 0) return ratio;
        }
        return (float)Density;
    }

    /// <summary>
    /// The pixel-to-dp ratio an SKCanvasView last painted at, so its touches map back onto
    /// what it drew; the display density before its first paint.
    /// </summary>
    public static float PaintedDensity(object? sender)
    {
        if (sender is SKCanvasView view && view.Width > 0 && view.CanvasSize.Width > 0)
        {
            var ratio = (float)(view.CanvasSize.Width / view.Width);
            if (ratio > 0) return ratio;
        }
        return (float)Density;
    }

    private static DisplaySnapshot MeasureDisplay()
    {
        var display = DeviceDisplay.MainDisplayInfo;
        var longSide = Math.Max(display.Width, display.Height);
        return new DisplaySnapshot(longSide, display.Density);
    }
}
