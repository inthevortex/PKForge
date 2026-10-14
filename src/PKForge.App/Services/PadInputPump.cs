namespace PKForge.App.Services;

/// <summary>
/// Cross-platform input pump managing gamepad button presses, hold-repeat timing,
/// directional consumption, and stick/hat deflection.
/// </summary>
/// <remarks>
/// Each repeat stream (held key, deflected hat) has at most one tick pending: its timer is
/// one-shot and re-armed only after a tick has run on the UI thread, so a stalled UI thread
/// never releases a burst of queued steps. Every start or stop bumps the stream's generation
/// and replaces its timer, and a posted tick from an older generation is dropped. Within one
/// hold the same timer is re-armed for every step.
/// </remarks>
public sealed class PadInputPump : IDisposable
{
    public const int DefaultHatHoldDelayMs = 320;
    public const int DefaultHatRepeatMs = 80;
    // A whole box or page changes per step: a slower cadence keeps each one readable.
    public const int DefaultShoulderHoldDelayMs = 380;
    public const int DefaultShoulderRepeatMs = 140;

    public int HatHoldDelayMs { get; init; } = DefaultHatHoldDelayMs;
    public int HatRepeatMs { get; init; } = DefaultHatRepeatMs;
    public int ShoulderHoldDelayMs { get; init; } = DefaultShoulderHoldDelayMs;
    public int ShoulderRepeatMs { get; init; } = DefaultShoulderRepeatMs;

    private readonly GamepadRouter _router;
    public Action? OnHaptic { get; set; }
    private readonly Action<Action> _dispatchToUi;
    private readonly TimeProvider _time;

    private PadButton? _currentRepeatButton;
    private PadButton? _currentHatDirection;
    private int _keyGeneration;
    private int _hatGeneration;
    private ITimer? _keyTimer;
    private ITimer? _hatTimer;
    private bool _disposed;
    private readonly Lock _gate = new();

    public PadButton? CurrentRepeatButton
    {
        get { lock (_gate) return _currentRepeatButton; }
    }

    public PadButton? CurrentHatDirection
    {
        get { lock (_gate) return _currentHatDirection; }
    }

    public PadInputPump(
        GamepadRouter router,
        Action? onHaptic = null,
        Action<Action>? dispatchToUi = null,
        TimeProvider? timeProvider = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        OnHaptic = onHaptic;
        _dispatchToUi = dispatchToUi ?? (action => action());
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Processes a button press. Returns true if handled by the router or if consumed as directional.
    /// </summary>
    public bool Press(PadButton button)
    {
        lock (_gate)
        {
            // Asked before dispatching: the press itself may open another screen.
            var repeats = IsDirectional(button) || (IsRepeatable(button) && _router.TopPagesWithShoulders);

            if (_router.Dispatch(button))
            {
                OnHaptic?.Invoke();
                if (repeats)
                    StartKeyRepeat(button);
                return true;
            }

            // Directional keys must never fall through to the platform's own focus search:
            // on Android it draws grey selection rectangles on the home shelf.
            return IsDirectional(button);
        }
    }

    /// <summary>
    /// Processes a button release. Stops repeats if the released button was repeating.
    /// </summary>
    public void Release(PadButton button)
    {
        lock (_gate)
        {
            if (_currentRepeatButton == button)
                StopKeyRepeat();
            _router.DispatchRelease(button);
        }
    }

    /// <summary>
    /// Processes hat deflection. Returns true if deflected beyond the deadzone.
    /// </summary>
    /// <remarks>
    /// D-pad-emulating hats (the AYN Thor's controller) only report changes: a statically held
    /// direction produces no further events, so the repeat runs on its own timer. It steps once
    /// on deflection, then keeps stepping after a hold delay until release. A hat has no
    /// release event of its own, so the router is never told about one.
    /// </remarks>
    public bool Motion(float x, float y)
    {
        var direction = EvaluateDirection(x, y);

        lock (_gate)
        {
            if (direction != _currentHatDirection)
            {
                StopHatRepeat();
                _currentHatDirection = direction;

                if (direction is { } button)
                {
                    // Haptic once per press: a tick on every repeat step would buzz.
                    if (_router.Dispatch(button))
                        OnHaptic?.Invoke();
                    StartHatTimer(button);
                }
            }

            return direction is not null;
        }
    }

    /// <summary>
    /// Resets all held state and active repeat timers.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            StopKeyRepeat();
            StopHatRepeat();
            _currentHatDirection = null;
        }
    }

    private void StartKeyRepeat(PadButton button)
    {
        StopKeyRepeat();
        _currentRepeatButton = button;
        if (_disposed) return;
        var generation = _keyGeneration;
        _keyTimer = CreateRepeatTimer(
            () => OnKeyRepeatTick(button, generation),
            IsDirectional(button) ? HatHoldDelayMs : ShoulderHoldDelayMs);
    }

    private void StopKeyRepeat()
    {
        _keyGeneration++;
        _currentRepeatButton = null;
        _keyTimer?.Dispose();
        _keyTimer = null;
    }

    private void StopHatRepeat()
    {
        _hatGeneration++;
        _hatTimer?.Dispose();
        _hatTimer = null;
    }

    private void StartHatTimer(PadButton button)
    {
        if (_disposed) return;
        var generation = _hatGeneration;
        _hatTimer = CreateRepeatTimer(() => OnHatRepeatTick(button, generation), HatHoldDelayMs);
    }

    private ITimer CreateRepeatTimer(Action tick, int delayMs) =>
        _time.CreateTimer(_ => _dispatchToUi(tick), null, TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);

    private static void Rearm(ITimer? timer, int delayMs) =>
        timer?.Change(TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);

    private void OnKeyRepeatTick(PadButton button, int generation)
    {
        lock (_gate)
        {
            if (generation != _keyGeneration || _currentRepeatButton != button) return;

            if (!IsDirectional(button) && !_router.TopPagesWithShoulders)
            {
                StopKeyRepeat();
                return;
            }

            if (!_router.Dispatch(button))
            {
                StopKeyRepeat();
                return;
            }

            // The handler may have reset or replaced the repeat while dispatching.
            if (generation == _keyGeneration)
                Rearm(_keyTimer, IsDirectional(button) ? HatRepeatMs : ShoulderRepeatMs);
        }
    }

    private void OnHatRepeatTick(PadButton button, int generation)
    {
        lock (_gate)
        {
            if (generation != _hatGeneration || _currentHatDirection != button) return;

            // Unhandled steps keep repeating: the hat stays deflected until it is released.
            _router.Dispatch(button);

            if (generation == _hatGeneration)
                Rearm(_hatTimer, HatRepeatMs);
        }
    }

    public static PadButton? EvaluateDirection(float x, float y)
    {
        if (Math.Abs(x) < 0.5f && Math.Abs(y) < 0.5f) return null;
        if (Math.Abs(y) >= Math.Abs(x))
            return y < 0 ? PadButton.Up : PadButton.Down;
        return x < 0 ? PadButton.Left : PadButton.Right;
    }

    public static bool IsDirectional(PadButton button) =>
        button is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right;

    public static bool IsRepeatable(PadButton button) =>
        IsDirectional(button) || button is PadButton.L or PadButton.R;

    public void Dispose()
    {
        lock (_gate)
        {
            Reset();
            _disposed = true;
        }
    }
}
