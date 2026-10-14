namespace PKForge.App.Platforms.Desktop;

using PKForge.App.Services;

internal enum StickAxis { X, Y }

/// <summary>
/// What every connected gamepad is doing, and which of it reaches the input pump.
/// </summary>
/// <remarks>
/// Button and stick state is tracked whether or not the window is active; only the posting of
/// new input is gated. A press posted while active always gets its release, and each pad keeps
/// its own left stick so an idle pad never cancels the one being used. Every activation change
/// starts a new epoch, so a stick posted before it can be recognised and dropped when it
/// arrives. Not thread-safe: the owner serialises calls.
/// </remarks>
internal sealed class GamepadInputState(bool windowActive)
{
    private readonly HeldButtons<(uint Pad, PadButton Button)> _held = new();
    private readonly Dictionary<uint, (float X, float Y)> _sticks = new();

    public bool IsWindowActive { get; private set; } = windowActive;

    /// <summary>Changes on every activation and deactivation; tag each posted stick with it.</summary>
    public int Epoch { get; private set; }

    /// <summary>True when a stick posted in <paramref name="epoch"/> may still reach the pump.</summary>
    public bool IsCurrent(int epoch) => IsWindowActive && epoch == Epoch;

    /// <summary>True when the press should be posted to the pump.</summary>
    public bool ButtonDown(uint pad, PadButton button) =>
        IsWindowActive && _held.Press((pad, button), button);

    /// <summary>The button to release, or null when nothing posted is held by it.</summary>
    public PadButton? ButtonUp(uint pad, PadButton button) => _held.Release((pad, button));

    /// <summary>
    /// Records a left-stick axis of <paramref name="pad"/> and returns the stick to post, or
    /// null while the window is inactive.
    /// </summary>
    public (float X, float Y)? StickMoved(uint pad, StickAxis axis, short raw)
    {
        var value = Math.Clamp(raw / 32767f, -1f, 1f);
        var stick = _sticks.GetValueOrDefault(pad);
        stick = axis == StickAxis.X ? (value, stick.Y) : (stick.X, value);
        _sticks[pad] = stick;
        return IsWindowActive ? Stick : null;
    }

    /// <summary>The most deflected stick across every pad.</summary>
    public (float X, float Y) Stick
    {
        get
        {
            (float X, float Y) strongest = (0f, 0f);
            foreach (var stick in _sticks.Values)
            {
                if (Deflection(stick) > Deflection(strongest))
                    strongest = stick;
            }
            return strongest;
        }
    }

    /// <summary>
    /// Forgets a disconnected pad. Returns the buttons it held that must be released and, while
    /// the window is active, the stick left by the remaining pads.
    /// </summary>
    public (IReadOnlyList<PadButton> Released, (float X, float Y)? Stick) RemovePad(uint pad)
    {
        var released = _held.ReleaseWhere(source => source.Pad == pad);
        var hadStick = _sticks.Remove(pad);
        return (released, hadStick && IsWindowActive ? Stick : null);
    }

    /// <summary>
    /// Stops posting input and returns every held button, which must be released before the
    /// stick is centred.
    /// </summary>
    public IReadOnlyList<PadButton> Deactivate()
    {
        IsWindowActive = false;
        Epoch++;
        return _held.ReleaseAll();
    }

    /// <summary>Resumes posting input and returns the stick as it is now, to post.</summary>
    public (float X, float Y) Activate()
    {
        IsWindowActive = true;
        Epoch++;
        return Stick;
    }

    private static float Deflection((float X, float Y) stick) => Math.Max(Math.Abs(stick.X), Math.Abs(stick.Y));
}
