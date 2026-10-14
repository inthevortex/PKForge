namespace PKForge.App.Views;

/// <summary>
/// Turns wheel deltas into whole steps for views that move a page or a row per step: a mouse
/// notch is one step, and a touchpad's small deltas add up to one instead of each taking a step.
/// </summary>
internal sealed class WheelSteps
{
    /// <summary>The wheel delta of one mouse notch.</summary>
    public const int Notch = 120;

    private int _pending;

    /// <summary>Adds a wheel delta (positive scrolls up); returns the steps it completes, negative for up.</summary>
    public int Add(int delta)
    {
        if (delta == 0) return 0;
        if (Math.Sign(delta) != Math.Sign(_pending)) _pending = 0;
        _pending += delta;
        var steps = _pending / Notch;
        _pending -= steps * Notch;
        return -steps;
    }
}
