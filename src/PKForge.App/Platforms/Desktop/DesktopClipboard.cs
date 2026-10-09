using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using MauiClipboard = Microsoft.Maui.ApplicationModel.DataTransfer.IClipboard;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopClipboard : MauiClipboard
{
    private static Avalonia.Input.Platform.IClipboard? Clipboard =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;

    public bool HasText
    {
        get
        {
            var cb = Clipboard;
            if (cb is null) return false;
            var text = cb.TryGetTextAsync().GetAwaiter().GetResult();
            return !string.IsNullOrEmpty(text);
        }
    }

    public async Task SetTextAsync(string? text)
    {
        var cb = Clipboard;
        if (cb != null)
        {
            await cb.SetTextAsync(text ?? string.Empty);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        var cb = Clipboard;
        return cb != null ? await cb.TryGetTextAsync() : null;
    }

    public event EventHandler<EventArgs>? ClipboardContentChanged
    {
        add { }
        remove { }
    }
}
