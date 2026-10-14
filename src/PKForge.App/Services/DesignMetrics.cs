namespace PKForge.App.Services;

/// <summary>
/// One reading of the surface the UI is laid out on: the long side in physical pixels and
/// the density. Either value is zero when the platform has not reported it yet.
/// </summary>
internal sealed record DisplaySnapshot(double LongSidePx, double Density)
{
    public bool IsValid => LongSidePx > 0 && Density > 0;

    /// <summary>The long side in device-independent units, which is what layout sizes follow.</summary>
    public double LongSideDip => IsValid ? LongSidePx / Density : 0;
}

/// <summary>The arithmetic that maps the 1920×1080 mockup onto a display.</summary>
internal static class DesignMetrics
{
    public const double MockupLongSide = 1920.0;

    /// <summary>
    /// A length in mockup pixels as device-independent units; half the mockup length while the
    /// display has not reported its size or density.
    /// </summary>
    public static double Design(double pixels, DisplaySnapshot display) =>
        !display.IsValid ? pixels / 2 : pixels * (display.LongSidePx / MockupLongSide) / display.Density;

    /// <summary>
    /// The smallest relative change of a mockup pixel's length that makes pages worth rebuilding:
    /// below it an editor row moves by under a unit.
    /// </summary>
    public const double SignificantChange = 0.01;

    /// <summary>True when pages built with a mockup pixel <paramref name="built"/> units long look wrong at <paramref name="now"/>.</summary>
    public static bool IsSignificantChange(double built, double now)
    {
        if (!(now > 0) || !double.IsFinite(now)) return false;
        if (!(built > 0) || !double.IsFinite(built)) return true;
        return Math.Abs(now - built) / built >= SignificantChange;
    }
}

/// <summary>
/// Holds the display reading that layouts are built from, so every page built between two
/// refreshes uses the same sizes. An invalid reading is never kept, so the next call measures again.
/// </summary>
internal sealed class DesignMetricsCache(Func<DisplaySnapshot> measure)
{
    private DisplaySnapshot? _current;
    private volatile bool _stale;

    /// <summary>The reading <see cref="Current"/> holds, without measuring; null before the first valid one.</summary>
    public DisplaySnapshot? Last => _current;

    public DisplaySnapshot Current
    {
        get
        {
            if (_current is { } cached && !_stale) return cached;
            var measured = measure();
            if (!measured.IsValid) return _current ?? measured;
            _current = measured;
            _stale = false;
            return measured;
        }
    }

    /// <summary>
    /// Makes the next <see cref="Current"/> measure again, keeping the last valid reading
    /// until a valid one replaces it.
    /// </summary>
    public void Invalidate() => _stale = true;

    /// <summary>Measures again and keeps the reading when it is valid; returns what <see cref="Current"/> now holds.</summary>
    public DisplaySnapshot Refresh()
    {
        var measured = measure();
        if (measured.IsValid)
        {
            _current = measured;
            _stale = false;
        }
        return Current;
    }
}
