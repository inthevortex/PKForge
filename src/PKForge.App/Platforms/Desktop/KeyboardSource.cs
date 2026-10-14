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
    private readonly HeldButtons<Key> _heldKeys = new();

    public KeyboardSource(Window window, PadInputPump pump)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));

        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        _window.Deactivated += (_, _) =>
        {
            foreach (var button in _heldKeys.ReleaseAll())
                _pump.Release(button);
            _pump.Reset();
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = _window.FocusManager?.GetFocusedElement();
        bool isTextBox = focused is TextBox;

        // When a TextBox has focus, only Escape and Enter route to the pad; all other keys pass to the text box.
        if (isTextBox)
        {
            PadButton? textBoxButton = e.Key switch
            {
                Key.Escape => PadButton.B,
                Key.Enter => PadButton.A,
                _ => null,
            };
            if (textBoxButton is not { } button)
                return;

            if (_heldKeys.Press(e.Key, button))
                _pump.Press(button);
            e.Handled = true;
            return;
        }

        var padButton = ResolveKey(e.Key);
        if (padButton is null)
            return;

        // Auto-repeat: ignore OS key repeats; the pump owns repetition.
        if (!_heldKeys.Press(e.Key, padButton.Value))
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

    /// <remarks>
    /// A key releases the button its press went to, whatever has focus now: a key held while
    /// focus moves into a text box still lets go of its button.
    /// </remarks>
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        var wasHeld = _heldKeys.IsHeld(e.Key);
        if (_heldKeys.Release(e.Key) is { } button)
            _pump.Release(button);

        if (wasHeld || (_window.FocusManager?.GetFocusedElement() is not TextBox && ResolveKey(e.Key) is not null))
            e.Handled = true;
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
