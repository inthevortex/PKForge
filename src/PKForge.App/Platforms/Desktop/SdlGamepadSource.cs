namespace PKForge.App.Platforms.Desktop;

using SDL;
using PKForge.App.Services;

/// <summary>
/// Dedicated background SDL3 event loop managing gamepad connection, button input,
/// left stick deflection, and haptics rumble.
/// </summary>
public sealed class SdlGamepadSource : IDisposable
{
    private readonly PadInputPump _pump;
    private readonly Func<bool> _isWindowActive;
    private readonly Thread _thread;
    private readonly Dictionary<uint, IntPtr> _gamepads = new();
    private readonly Lock _gamepadGate = new();

    private volatile bool _running;
    private float _axisX;
    private float _axisY;

    public SdlGamepadSource(PadInputPump pump, Func<bool> isWindowActive)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _isWindowActive = isWindowActive ?? throw new ArgumentNullException(nameof(isWindowActive));

        _thread = new Thread(Run)
        {
            Name = "PKForge.SDLGamepad",
            IsBackground = true,
        };
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread.Start();
    }

    private unsafe void Run()
    {
        if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            AppLog.Warn("input", "SDL_Init(SDL_INIT_GAMEPAD) failed");
            return;
        }

        try
        {
            SDL_Event e;
            while (_running)
            {
                if (SDL3.SDL_WaitEventTimeout(&e, 50))
                {
                    HandleEvent(e);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("input", "Unhandled exception in SDL gamepad thread", ex);
        }
        finally
        {
            CloseAllGamepads();
            SDL3.SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_GAMEPAD);
        }
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _pump.Reset());
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
            {
                if (!_isWindowActive()) return;
                var button = ResolveButton(e.gbutton.Button);
                if (button is not null)
                {
                    var btn = button.Value;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _pump.Press(btn));
                }
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
            {
                if (!_isWindowActive()) return;
                var button = ResolveButton(e.gbutton.Button);
                if (button is not null)
                {
                    var btn = button.Value;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _pump.Release(btn));
                }
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
            {
                if (!_isWindowActive()) return;
                var axis = e.gaxis.Axis;
                if (axis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX)
                {
                    _axisX = Math.Clamp(e.gaxis.value / 32767f, -1f, 1f);
                    var x = _axisX;
                    var y = _axisY;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _pump.Motion(x, y));
                }
                else if (axis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY)
                {
                    _axisY = Math.Clamp(e.gaxis.value / 32767f, -1f, 1f);
                    var x = _axisX;
                    var y = _axisY;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _pump.Motion(x, y));
                }
                break;
            }
        }
    }

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

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(500);
        }
    }
}
