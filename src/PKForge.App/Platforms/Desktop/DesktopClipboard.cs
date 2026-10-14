using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using MauiClipboard = Microsoft.Maui.ApplicationModel.DataTransfer.IClipboard;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopClipboard : MauiClipboard
{
    private static Avalonia.Input.Platform.IClipboard? Clipboard =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;

    private bool _hasText;

    /// <summary>
    /// Whether the text this app last set or read was non-empty. The system clipboard is only
    /// read asynchronously, so this never blocks on it; <see cref="GetTextAsync"/> refreshes it.
    /// </summary>
    public bool HasText => _hasText;

    public async Task SetTextAsync(string? text)
    {
        var cb = Clipboard;
        if (cb != null)
        {
            await cb.SetTextAsync(text ?? string.Empty);
            _hasText = !string.IsNullOrEmpty(text);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        var cb = Clipboard;
        var text = cb != null ? await cb.TryGetTextAsync() : null;
        _hasText = !string.IsNullOrEmpty(text);
        return text;
    }

    public event EventHandler<EventArgs>? ClipboardContentChanged
    {
        add { }
        remove { }
    }
}
