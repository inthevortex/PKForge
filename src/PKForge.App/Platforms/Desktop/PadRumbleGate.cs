namespace PKForge.App.Platforms.Desktop;

using PKForge.App.Services;

/// <summary>
/// Rumbles the gamepad for a handled press only when that press came from a gamepad and the
/// user has turned rumble on. Used on the UI thread only.
/// </summary>
internal sealed class PadRumbleGate(Func<bool> isEnabled, Action<HapticKind> rumble)
{
    private int _padDispatchDepth;

    /// <summary>Runs <paramref name="dispatch"/> as gamepad input, so any haptic it raises may rumble.</summary>
    public void FromPad(Action dispatch)
    {
        _padDispatchDepth++;
        try
        {
            dispatch();
        }
        finally
        {
            _padDispatchDepth--;
        }
    }

    /// <summary>A haptic request; rumbles only while a gamepad press is being dispatched.</summary>
    public void OnHaptic(HapticKind kind)
    {
        if (_padDispatchDepth > 0 && isEnabled())
            rumble(kind);
    }
}
