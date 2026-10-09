using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Desktop implementation of IMusicPlayer.
/// Manages music library persistence and preferences.
/// (Full audio playback through SDL3_mixer is wired in Step 7).
/// </summary>
public sealed class DesktopMusicPlayer : IMusicPlayer
{
    private readonly List<MusicTrack> _library = [];

    public IReadOnlyList<MusicTrack> Library => _library;
    public bool IsPlaying => false;
    public int? CurrentIndex => null;
    public string? LastError => null;

    public MusicOrder Order
    {
        get => Enum.Parse<MusicOrder>(Preferences.Default.Get("music_order", nameof(MusicOrder.InOrder)));
        set => Preferences.Default.Set("music_order", value.ToString());
    }

    public bool Autostart
    {
        get => Preferences.Default.Get("music_autostart", false);
        set => Preferences.Default.Set("music_autostart", value);
    }

    public DesktopMusicPlayer()
    {
        var saved = Preferences.Default.Get("music_library", "");
        if (saved.Length > 0)
        {
            foreach (var entry in saved.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split('\u0001');
                if (parts.Length == 2) _library.Add(new MusicTrack(parts[0], parts[1]));
            }
        }
    }

    public int Add(IReadOnlyList<PickedDocument> documents)
    {
        var added = 0;
        foreach (var document in documents)
        {
            if (_library.Any(t => t.DocumentId == document.DocumentId)) continue;
            _library.Add(new MusicTrack(document.DocumentId, document.DisplayName));
            added++;
        }
        Persist();
        return added;
    }

    public void Remove(int index)
    {
        if ((uint)index >= _library.Count) return;
        _library.RemoveAt(index);
        Persist();
    }

    public void Clear()
    {
        _library.Clear();
        Persist();
    }

    public void Play() { }
    public void Pause() { }
    public void Skip() { }
    public void SetOrder(MusicOrder order) => Order = order;
    public void SetAutostart(bool on) => Autostart = on;
    public void MaybeAutostart() { }
    public void PauseForBackground() { }
    public void ResumeFromBackground() { }

    private void Persist() =>
        Preferences.Default.Set("music_library", string.Join("\n", _library.Select(t => $"{t.DocumentId}\u0001{t.Title}")));
}
