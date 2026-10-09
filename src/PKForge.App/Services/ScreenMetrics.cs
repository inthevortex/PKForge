using Microsoft.Maui.Devices;
using SkiaSharp;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Services;

/// <summary>
/// Manages display and window metrics (density, physical dimensions, and mockup scaling)
/// across mobile and desktop environments.
/// </summary>
public static class ScreenMetrics
{
    /// <summary>Display density (scaling factor). Guaranteed to be greater than zero.</summary>
    public static double Density
    {
        get
        {
            var density = DeviceDisplay.MainDisplayInfo.Density;
            return density > 0 ? density : 1.0;
        }
    }

    /// <summary>Physical pixel length of the longer edge of the display or window.</summary>
    public static double LongSidePx
    {
        get
        {
#if PKFORGE_DESKTOP
            var window = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (window != null && window.ClientSize.Width > 0 && window.ClientSize.Height > 0)
            {
                var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1.0;
                var wPx = window.ClientSize.Width * scaling;
                var hPx = window.ClientSize.Height * scaling;
                var maxPx = Math.Max(wPx, hPx);
                if (maxPx > 0) return maxPx;
            }
#endif
            var display = DeviceDisplay.MainDisplayInfo;
            var max = Math.Max(display.Width, display.Height);
            return max > 0 ? max : 1920.0;
        }
    }

    /// <summary>Overall design scaling factor converting 1920x1080 mockup pixels to DIPs.</summary>
    public static double DesignScale
    {
        get
        {
            var longSide = LongSidePx;
            var density = Density;
            return (longSide / 1920.0) / density;
        }
    }

    /// <summary>
    /// Scales a length in mockup pixels (1920x1080 design) into device-independent units.
    /// </summary>
    public static double Design(double pixels)
    {
        var longSide = LongSidePx;
        var density = Density;
        if (longSide <= 0 || density <= 0) return pixels / 2;
        return pixels * (longSide / 1920.0) / density;
    }

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
}
