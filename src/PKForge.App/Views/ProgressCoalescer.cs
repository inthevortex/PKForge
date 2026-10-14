namespace PKForge.App.Views;

/// <summary>
/// Coalesces progress reports from any thread into at most one pending update, which shows the
/// latest report when it runs, with updates at least <paramref name="intervalMs"/> apart.
/// </summary>
/// <param name="intervalMs">The shortest time between two updates.</param>
/// <param name="clockMs">A monotonic clock in milliseconds.</param>
internal sealed class ProgressCoalescer(long intervalMs, Func<long> clockMs)
{
    private readonly Lock _gate = new();
    private (string Text, double Fraction) _latest;
    private bool _pending;
    private long _shownAt = long.MinValue / 2;

    /// <summary>
    /// Records a report. Null when an update is already pending (it will show this report);
    /// otherwise the caller schedules one, after the returned milliseconds when positive.
    /// </summary>
    public long? Offer(string text, double fraction)
    {
        lock (_gate)
        {
            _latest = (text, fraction);
            if (_pending) return null;
            _pending = true;
            return _shownAt + intervalMs - clockMs();
        }
    }

    /// <summary>The latest report, for the pending update now showing it; the next report schedules another.</summary>
    public (string Text, double Fraction) Take()
    {
        lock (_gate)
        {
            _pending = false;
            _shownAt = clockMs();
            return _latest;
        }
    }
}
