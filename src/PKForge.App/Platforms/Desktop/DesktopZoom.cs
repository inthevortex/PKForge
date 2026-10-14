using PKForge.App.Services;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// How large the desktop window draws the interface. Each size lays pages out on a logical
/// surface no longer than <see cref="DesktopZoom.LongSide"/>, so a shorter surface draws
/// everything larger.
/// </summary>
public enum UiSize
{
    /// <summary>The mockup's own size: a 1920 px window draws one unit per pixel.</summary>
    Compact,

    /// <summary>Text and controls half as large again as <see cref="Compact"/> in a 1920 px window.</summary>
    Standard,

    /// <summary>Text and controls twice as large as <see cref="Compact"/> in a 1920 px window.</summary>
    Large,
}

/// <summary>
/// The desktop head's whole-window zoom: pages are laid out on a logical surface with the
/// window's aspect ratio and scaled uniformly to fill the window. A window longer than the
/// UI size's <see cref="LongSide"/> enlarges a surface that long; a shorter one down to
/// <see cref="MinLongSide"/> lays out at its own size, one unit per pixel, so text keeps its
/// size instead of shrinking with the window.
/// </summary>
public static class DesktopZoom
{
    /// <summary>
    /// The shortest long side pages lay out on, near an Android handheld's (an 891 dp S25), which
    /// the mockup's layouts fit; windows smaller still shrink a surface this long.
    /// </summary>
    public const double MinLongSide = 960;

    /// <summary>The longest logical surface pages lay out on at <paramref name="size"/>.</summary>
    public static double LongSide(UiSize size) => size switch
    {
        UiSize.Standard => 1280,
        UiSize.Large => MinLongSide,
        _ => DesignMetrics.MockupLongSide,
    };

    /// <summary>The scale from the logical surface to the window, in device-independent units; 1 while the window size is unknown.</summary>
    public static double Factor(double windowWidth, double windowHeight, UiSize size)
    {
        var longSide = Math.Max(windowWidth, windowHeight);
        return longSide > 0 && double.IsFinite(longSide)
            ? longSide / Math.Clamp(longSide, MinLongSide, LongSide(size))
            : 1.0;
    }

    /// <summary>
    /// What layout sizes follow for a window <paramref name="windowLongSide"/> device-independent
    /// units long: its physical long side at a density of the scaling times the zoom. A mockup
    /// pixel takes the same share of the logical surface as of the mockup, so mockup lengths keep
    /// their share of the window while plain sizes follow the zoom, as on Android.
    /// </summary>
    internal static DisplaySnapshot Display(double windowLongSide, double renderScaling, UiSize size)
    {
        var scaling = renderScaling > 0 && double.IsFinite(renderScaling) ? renderScaling : 1.0;
        return new DisplaySnapshot(windowLongSide * scaling, scaling * Factor(windowLongSide, windowLongSide, size));
    }

    /// <summary>
    /// The logical surface for a window, in device-independent units: the window unzoomed by
    /// <see cref="Factor"/>, each side rounded down to a whole device pixel so layout rounding
    /// keeps it as is. Both are NaN, meaning no fixed size, while the window size is unknown.
    /// </summary>
    public static (double Width, double Height) Surface(double windowWidth, double windowHeight, double renderScaling, UiSize size)
    {
        if (!(windowWidth > 0) || !(windowHeight > 0) || !double.IsFinite(windowWidth) || !double.IsFinite(windowHeight))
            return (double.NaN, double.NaN);
        var scaling = renderScaling > 0 && double.IsFinite(renderScaling) ? renderScaling : 1.0;
        var factor = Factor(windowWidth, windowHeight, size);
        return (WholePixels(windowWidth / factor, scaling), WholePixels(windowHeight / factor, scaling));
    }

    private static double WholePixels(double length, double scaling) => Math.Floor(length * scaling + 1e-6) / scaling;

    /// <summary>
    /// Moves a touch from the unzoomed pixels a canvas control reports into the pixels of its
    /// surface painted at <paramref name="zoom"/>; unchanged when the canvas ignores pixel
    /// scaling, since it then reports and paints in device-independent units.
    /// </summary>
    public static (float X, float Y) TouchToSurface(float x, float y, double zoom, bool ignorePixelScaling)
    {
        if (ignorePixelScaling || !(zoom > 0) || !double.IsFinite(zoom)) return (x, y);
        var scale = (float)zoom;
        return (x * scale, y * scale);
    }

    /// <summary>The zoom the window shows its pages at.</summary>
    public static double Current { get; private set; } = 1.0;

    /// <summary>Raised on the UI thread after <see cref="Current"/> changes.</summary>
    public static event Action? Changed;

    /// <summary>Makes <paramref name="factor"/> current; false, with nothing raised, when it is invalid or already current.</summary>
    public static bool Set(double factor)
    {
        if (!(factor > 0) || !double.IsFinite(factor) || factor == Current) return false;
        Current = factor;
        Changed?.Invoke();
        return true;
    }
}
