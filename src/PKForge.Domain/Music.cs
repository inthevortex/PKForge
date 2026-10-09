namespace PKForge.Domain;

/// <summary>A track in the user's music library: display name + SAF document id.</summary>
public sealed record MusicTrack(string DocumentId, string Title);

/// <summary>Play order for the background music.</summary>
public enum MusicOrder { InOrder, Shuffle }

/// <summary>
/// The background music player: pick mp3s once, they persist as the default library
/// across launches (totally optional). Play/pause, skip, order. Implementations own the
/// platform media session; the domain stays engine-clean.
/// </summary>
public interface IMusicPlayer
{
    IReadOnlyList<MusicTrack> Library { get; }
    bool IsPlaying { get; }
    int? CurrentIndex { get; }
    MusicOrder Order { get; }

    /// <summary>Adds tracks from user-picked documents; returns how many were added.</summary>
    int Add(IReadOnlyList<PickedDocument> documents);
    void Remove(int index);
    void Clear();

    void Play();
    void Pause();
    void Skip();
    /// <summary>Sets play order and persists it.</summary>
    void SetOrder(MusicOrder order);
    /// <summary>Makes the current library the app's default startup music (or forgets it).</summary>
    void SetAutostart(bool on);
    bool Autostart { get; }

    /// <summary>Starts playback at app launch when the user asked for it.</summary>
    void MaybeAutostart();
    /// <summary>The app left the screen (Home, another app): music stops with it.</summary>
    void PauseForBackground();
    /// <summary>The app is back: music paused by PauseForBackground carries on.</summary>
    void ResumeFromBackground();
    /// <summary>Set when playback fails; the UI shows it so failures are never silent.</summary>
    string? LastError { get; }
}
