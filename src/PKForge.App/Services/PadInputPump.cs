namespace PKForge.App.Services;

/// <summary>
/// Cross-platform input pump managing gamepad button presses, hold-repeat timing,
/// directional consumption, and stick/hat deflection.
/// </summary>
public sealed class PadInputPump : IDisposable
{
    public const int DefaultHatHoldDelayMs = 320;
    public const int DefaultHatRepeatMs = 80;
    public const int DefaultShoulderHoldDelayMs = 380;
    public const int DefaultShoulderRepeatMs = 140;

    public int HatHoldDelayMs { get; init; } = DefaultHatHoldDelayMs;
    public int HatRepeatMs { get; init; } = DefaultHatRepeatMs;
    public int ShoulderHoldDelayMs { get; init; } = DefaultShoulderHoldDelayMs;
    public int ShoulderRepeatMs { get; init; } = DefaultShoulderRepeatMs;

    private readonly GamepadRouter _router;
    public Action? OnHaptic { get; set; }
    private readonly Action<Action> _dispatchToUi;

    private PadButton? _currentRepeatButton;
    private PadButton? _currentHatDirection;

    private readonly Timer _keyTimer;
    private readonly Timer _hatTimer;
    private readonly Lock _gate = new();

    public PadButton? CurrentRepeatButton
    {
        get { lock (_gate) return _currentRepeatButton; }
    }

    public PadButton? CurrentHatDirection
    {
        get { lock (_gate) return _currentHatDirection; }
    }

    public PadInputPump(GamepadRouter router, Action? onHaptic = null, Action<Action>? dispatchToUi = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        OnHaptic = onHaptic;
        _dispatchToUi = dispatchToUi ?? (action => action());

        _keyTimer = new Timer(_ => OnKeyRepeatTick(), null, Timeout.Infinite, Timeout.Infinite);
        _hatTimer = new Timer(_ => OnHatRepeatTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Processes a button press. Returns true if handled by the router or if consumed as directional.
    /// </summary>
    public bool Press(PadButton button)
    {
        bool repeats;
        bool handled;

        lock (_gate)
        {
            repeats = IsDirectional(button) || (IsRepeatable(button) && _router.TopPagesWithShoulders);
            handled = _router.Dispatch(button);

            if (handled)
            {
                OnHaptic?.Invoke();
                if (repeats)
                {
                    _currentRepeatButton = button;
                    var delay = IsDirectional(button) ? HatHoldDelayMs : ShoulderHoldDelayMs;
                    var interval = IsDirectional(button) ? HatRepeatMs : ShoulderRepeatMs;
                    _keyTimer.Change(delay, interval);
                }
                return true;
            }

            // Directional buttons must never fall through to default platform focus navigation
            if (IsDirectional(button))
                return true;

            return false;
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
            {
                _currentRepeatButton = null;
                _keyTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            _router.DispatchRelease(button);
        }
    }

    /// <summary>
    /// Processes axis/hat motion deflection. Returns true if deflected beyond deadzone.
    /// </summary>
    public bool Motion(float x, float y)
    {
        var direction = EvaluateDirection(x, y);

        lock (_gate)
        {
            if (direction != _currentHatDirection)
            {
                _hatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _currentHatDirection = direction;

                if (direction is { } btn)
                {
                    if (_router.Dispatch(btn))
                    {
                        OnHaptic?.Invoke();
                    }
                    _hatTimer.Change(HatHoldDelayMs, HatRepeatMs);
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
            _currentRepeatButton = null;
            _currentHatDirection = null;
            _keyTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _hatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnKeyRepeatTick()
    {
        _dispatchToUi(() =>
        {
            lock (_gate)
            {
                if (_currentRepeatButton is not { } button) return;

                if (!IsDirectional(button) && !_router.TopPagesWithShoulders)
                {
                    _currentRepeatButton = null;
                    _keyTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                if (!_router.Dispatch(button))
                {
                    _currentRepeatButton = null;
                    _keyTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        });
    }

    private void OnHatRepeatTick()
    {
        _dispatchToUi(() =>
        {
            lock (_gate)
            {
                if (_currentHatDirection is not { } button) return;
                _router.Dispatch(button);
            }
        });
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
        Reset();
        _keyTimer.Dispose();
        _hatTimer.Dispose();
    }
}
