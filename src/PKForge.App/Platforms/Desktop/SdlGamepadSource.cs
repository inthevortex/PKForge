namespace PKForge.App.Platforms.Desktop;

using SDL;
using PKForge.App.Services;

/// <summary>
/// Gamepad connection, button input, left stick deflection and rumble, handled on the SDL
/// thread that <see cref="SdlRuntime"/> owns.
/// </summary>
public sealed class SdlGamepadSource : IDisposable
{
    /// <summary>Preference that turns gamepad rumble on; off unless the user enables it.</summary>
    public const string RumblePreferenceKey = "pad_rumble";

    private readonly PadInputPump _pump;
    private readonly PadRumbleGate _rumble;
    private readonly Dictionary<uint, IntPtr> _gamepads = new();
    private readonly Lock _gamepadGate = new();
    private readonly GamepadInputState _state;
    private readonly Lock _stateGate = new();

    private bool _started;
    // Touched on the SDL thread only.
    private bool _acquired;
    private bool _stopped;

    public SdlGamepadSource(PadInputPump pump, bool windowActive)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _state = new GamepadInputState(windowActive);
        // The input pump raises only click haptics, so every rumble is the same short pulse.
        _rumble = new PadRumbleGate(() => RumbleEnabled, _ => Rumble());
    }

    /// <summary>Whether handled gamepad presses rumble the pad, stored under <see cref="RumblePreferenceKey"/>.</summary>
    public static bool RumbleEnabled
    {
        get => Preferences.Default.Get(RumblePreferenceKey, false);
        set => Preferences.Default.Set(RumblePreferenceKey, value);
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            await SdlRuntime.RunAsync(StartOnSdlThread).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            AppLog.Error("input", "Gamepad input failed to start", error);
        }
    }

    private void StartOnSdlThread()
    {
        if (_stopped) return;
        // SDL has no window to judge focus by; whether input reaches the app is decided here.
        SDL3.SDL_SetHint(SDL3.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
        if (!SdlRuntime.Acquire(SDL_InitFlags.SDL_INIT_GAMEPAD))
            return;
        _acquired = true;
        SdlRuntime.SetEventHandler(HandleEvent);
    }

    private void StopOnSdlThread()
    {
        _stopped = true;
        if (!_acquired) return;
        _acquired = false;
        SdlRuntime.SetEventHandler(null);
        CloseAllGamepads();
        SdlRuntime.Release(SDL_InitFlags.SDL_INIT_GAMEPAD);
    }

    private unsafe void HandleEvent(SDL_Event e)
    {
        switch (e.Type)
        {
            case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
            {
                var which = e.gdevice.which;
                var pad = SDL3.SDL_OpenGamepad(which);
                if (pad != null)
                {
                    lock (_gamepadGate)
                    {
                        _gamepads[(uint)which] = (IntPtr)pad;
                    }
                    AppLog.Info("input", $"Gamepad connected: instance={(uint)which}");
                }
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
            {
                var which = e.gdevice.which;
                IntPtr padPtr = IntPtr.Zero;
                lock (_gamepadGate)
                {
                    if (_gamepads.Remove((uint)which, out var ptr))
                        padPtr = ptr;
                }

                if (padPtr != IntPtr.Zero)
                {
                    SDL3.SDL_CloseGamepad((SDL_Gamepad*)padPtr);
                }

                AppLog.Info("input", $"Gamepad disconnected: instance={(uint)which}");
                lock (_stateGate)
                {
                    var (released, stick) = _state.RemovePad((uint)which);
                    PostReleases(released);
                    if (stick is { } s)
                        PostMotion(s, _state.Epoch);
                }
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
            {
                if (ResolveButton(e.gbutton.Button) is not { } button) break;
                lock (_stateGate)
                {
                    if (_state.ButtonDown((uint)e.gbutton.which, button))
                        Post(() => _rumble.FromPad(() => _pump.Press(button)));
                }
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
            {
                if (ResolveButton(e.gbutton.Button) is not { } button) break;
                lock (_stateGate)
                {
                    if (_state.ButtonUp((uint)e.gbutton.which, button) is { } released)
                        Post(() => _pump.Release(released));
                }
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
            {
                StickAxis? axis = e.gaxis.Axis switch
                {
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX => StickAxis.X,
                    SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY => StickAxis.Y,
                    _ => null,
                };
                if (axis is null) break;
                lock (_stateGate)
                {
                    if (_state.StickMoved((uint)e.gaxis.which, axis.Value, e.gaxis.value) is { } stick)
                        PostMotion(stick, _state.Epoch);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Tells the source whether the window is active. Called on the UI thread. On deactivation
    /// every held button is released, queued after any press already on its way, and then the
    /// stick is centred; a stick queued before the change is dropped when it arrives. On
    /// activation the stick is posted as it is now.
    /// </summary>
    public void SetWindowActive(bool active)
    {
        lock (_stateGate)
        {
            if (active)
            {
                PostMotion(_state.Activate(), _state.Epoch);
                return;
            }

            PostReleases(_state.Deactivate());
            Post(() => _pump.Motion(0f, 0f));
        }
    }

    private void PostReleases(IReadOnlyList<PadButton> buttons)
    {
        foreach (var button in buttons)
            Post(() => _pump.Release(button));
    }

    private void PostMotion((float X, float Y) stick, int epoch) =>
        Post(() =>
        {
            lock (_stateGate)
            {
                if (!_state.IsCurrent(epoch)) return;
            }
            _rumble.FromPad(() => _pump.Motion(stick.X, stick.Y));
        });

    /// <summary>
    /// Desktop haptics: rumbles only for presses that came from a gamepad, and only while
    /// <see cref="RumbleEnabled"/> is on. Called on the UI thread.
    /// </summary>
    public void OnHaptic(HapticKind kind) => _rumble.OnHaptic(kind);

    private static void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    private unsafe void CloseAllGamepads()
    {
        lock (_gamepadGate)
        {
            foreach (var padPtr in _gamepads.Values)
            {
                if (padPtr != IntPtr.Zero)
                {
                    SDL3.SDL_CloseGamepad((SDL_Gamepad*)padPtr);
                }
            }
            _gamepads.Clear();
        }
    }

    /// <summary>
    /// Short haptic rumble on all connected gamepads.
    /// </summary>
    public unsafe void Rumble(ushort lowFreq = 0x4000, ushort highFreq = 0x4000, uint durationMs = 80)
    {
        lock (_gamepadGate)
        {
            foreach (var padPtr in _gamepads.Values)
            {
                if (padPtr != IntPtr.Zero)
                {
                    SDL3.SDL_RumbleGamepad((SDL_Gamepad*)padPtr, lowFreq, highFreq, durationMs);
                }
            }
        }
    }

    public static PadButton? ResolveButton(SDL_GamepadButton button) => button switch
    {
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => PadButton.A,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => PadButton.B,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => PadButton.X,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => PadButton.Y,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => PadButton.Start,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => PadButton.Select,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => PadButton.L,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => PadButton.R,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => PadButton.Up,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => PadButton.Down,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => PadButton.Left,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => PadButton.Right,
        _ => null,
    };

    /// <summary>Stops input and hands the gamepad subsystem back, waiting briefly for the SDL thread.</summary>
    public void Dispose()
    {
        if (!SdlRuntime.RunAsync(StopOnSdlThread).Wait(500))
            AppLog.Warn("input", "The SDL thread did not stop the gamepad source in time");
    }
}
