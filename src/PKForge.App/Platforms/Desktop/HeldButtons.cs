namespace PKForge.App.Platforms.Desktop;

using PKForge.App.Services;

/// <summary>
/// Remembers which pad button each held input (a key, a pad's button) pressed, so its release
/// goes to that same button whatever has changed since, and a button is released only once no
/// held input still presses it.
/// </summary>
internal sealed class HeldButtons<TSource> where TSource : notnull
{
    private readonly Dictionary<TSource, PadButton> _held = new();

    /// <summary>Records <paramref name="source"/> as pressing <paramref name="button"/>; false when it is already held.</summary>
    public bool Press(TSource source, PadButton button) => _held.TryAdd(source, button);

    public bool IsHeld(TSource source) => _held.ContainsKey(source);

    /// <summary>
    /// Forgets <paramref name="source"/> and returns the button to release, or null when it held
    /// nothing or another held input still presses the same button.
    /// </summary>
    public PadButton? Release(TSource source)
    {
        if (!_held.Remove(source, out var button))
            return null;
        return _held.ContainsValue(button) ? null : button;
    }

    /// <summary>Forgets every held input matching <paramref name="match"/> and returns the buttons to release.</summary>
    public IReadOnlyList<PadButton> ReleaseWhere(Func<TSource, bool> match)
    {
        var released = new List<PadButton>();
        foreach (var source in _held.Keys.Where(match).ToList())
        {
            if (Release(source) is { } button)
                released.Add(button);
        }
        return released;
    }

    /// <summary>Forgets every held input and returns each button it pressed, once.</summary>
    public IReadOnlyList<PadButton> ReleaseAll()
    {
        var released = _held.Values.Distinct().ToList();
        _held.Clear();
        return released;
    }
}
