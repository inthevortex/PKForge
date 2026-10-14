namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// The player's UI size: how large the desktop window draws text and controls (see
/// <see cref="DesktopZoom"/>). A player setting in Settings, applied as soon as it changes.
/// </summary>
public static class UiSizeSetting
{
    private const string Key = "ui_size";

    private static UiSize? _current;

    /// <summary>The stored size; <see cref="UiSize.Standard"/> until the player picks one.</summary>
    public static UiSize Current => _current ??= Read();

    /// <summary>Raised on the UI thread after <see cref="Current"/> changes.</summary>
    public static event Action? Changed;

    public static void Set(UiSize size)
    {
        var changed = Current != size;
        Preferences.Default.Set(Key, size.ToString());
        _current = size;
        if (changed) Changed?.Invoke();
    }

    private static UiSize Read() =>
        Enum.TryParse<UiSize>(Preferences.Default.Get<string?>(Key, null), out var size) && Enum.IsDefined(size)
            ? size
            : UiSize.Standard;
}
