namespace PKForge.App.Platforms.Desktop;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PKForge.App.Services;

/// <summary>
/// Routes Avalonia keyboard events into <see cref="PadInputPump"/> with tunneling priority
/// and text-entry conflict rules.
/// </summary>
public sealed class KeyboardSource
{
    private readonly Window _window;
    private readonly PadInputPump _pump;
    private readonly HashSet<Key> _heldKeys = new();

    public KeyboardSource(Window window, PadInputPump pump)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));

        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        _window.Deactivated += (_, _) =>
        {
            _heldKeys.Clear();
            _pump.Reset();
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = _window.FocusManager?.GetFocusedElement();
        bool isTextBox = focused is TextBox;

        // §5.6.2: Text-entry conflict rule:
        // When a TextBox has focus, only Escape (-> B) and Enter (-> A) go to the router.
        // Every other key goes to the text box.
        if (isTextBox)
        {
            if (e.Key is Key.Escape)
            {
                if (_heldKeys.Add(e.Key))
                    _pump.Press(PadButton.B);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Enter or Key.Return)
            {
                if (_heldKeys.Add(e.Key))
                    _pump.Press(PadButton.A);
                e.Handled = true;
                return;
            }

            return;
        }

        var padButton = ResolveKey(e.Key);
        if (padButton is null)
            return;

        // Auto-repeat: ignore OS key repeats; the pump owns repetition.
        if (!_heldKeys.Add(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (_pump.Press(padButton.Value))
        {
            e.Handled = true;
        }
        else if (PadInputPump.IsDirectional(padButton.Value))
        {
            // Directional keys must never fall through to default Avalonia focus navigation
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);

        var focused = _window.FocusManager?.GetFocusedElement();
        bool isTextBox = focused is TextBox;

        if (isTextBox)
        {
            if (e.Key is Key.Escape)
            {
                _pump.Release(PadButton.B);
                e.Handled = true;
            }
            else if (e.Key is Key.Enter or Key.Return)
            {
                _pump.Release(PadButton.A);
                e.Handled = true;
            }
            return;
        }

        var padButton = ResolveKey(e.Key);
        if (padButton is not null)
        {
            _pump.Release(padButton.Value);
            e.Handled = true;
        }
    }

    public static PadButton? ResolveKey(Key key) => key switch
    {
        Key.Up => PadButton.Up,
        Key.Down => PadButton.Down,
        Key.Left => PadButton.Left,
        Key.Right => PadButton.Right,

        Key.Enter or Key.Return or Key.Space => PadButton.A,
        Key.Escape or Key.Back => PadButton.B,
        Key.X => PadButton.X,
        Key.Y or Key.Tab => PadButton.Y,

        Key.Q or Key.PageUp => PadButton.L,
        Key.E or Key.PageDown => PadButton.R,

        Key.F1 or Key.Apps => PadButton.Start,
        Key.F2 => PadButton.Select,

        _ => null,
    };
}
