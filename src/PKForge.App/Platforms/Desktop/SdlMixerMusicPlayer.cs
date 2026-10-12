using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PKForge.App.Services;
using PKForge.Domain;
using SDL;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// SDL3_mixer background music player: gapless playback through SDL3 audio device,
/// track advancement on completion, persisted library + order + autostart in Preferences.
/// Safeguards against infinite loops on missing tracks and resumes on pause rather than restart.
/// </summary>
public sealed class SdlMixerMusicPlayer : IMusicPlayer, IDisposable
{
    private static SdlMixerMusicPlayer? _activeInstance;

    private readonly List<MusicTrack> _library = [];
    private readonly Random _rng = new();
    private int? _index;
    private int _consecutiveFailures;
    private bool _pausedInBackground;
    private bool _explicitStop;
    private bool _disposed;

    private unsafe MIX_Mixer* _mixer;
    private unsafe MIX_Track* _track;
    private unsafe MIX_Audio* _currentAudio;

    public IReadOnlyList<MusicTrack> Library => _library;

    public bool IsPlaying
    {
        get
        {
            unsafe
            {
                if (_track == null) return false;
                return SDL3_mixer.MIX_TrackPlaying(_track) && !SDL3_mixer.MIX_TrackPaused(_track);
            }
        }
    }

    public int? CurrentIndex => _index;

    public string? LastError { get; private set; }

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

    public SdlMixerMusicPlayer()
    {
        _activeInstance = this;

        var saved = Preferences.Default.Get("music_library", "");
        if (saved.Length > 0)
        {
            foreach (var entry in saved.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split('\u0001');
                if (parts.Length == 2) _library.Add(new MusicTrack(parts[0], parts[1]));
            }
        }

        EnsureMixerInitialized();
    }

    private void EnsureMixerInitialized()
    {
        unsafe
        {
            if (_mixer != null) return;
            try
            {
                if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO))
                {
                    AppLog.Warn("music", $"SDL_Init audio failed: {SDL3.SDL_GetError()}");
                    return;
                }

                if (!SDL3_mixer.MIX_Init())
                {
                    AppLog.Warn("music", $"MIX_Init failed: {SDL3.SDL_GetError()}");
                    return;
                }

                _mixer = SDL3_mixer.MIX_CreateMixerDevice(SDL3.SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, null);
                if (_mixer == null)
                {
                    AppLog.Warn("music", $"MIX_CreateMixerDevice failed: {SDL3.SDL_GetError()}");
                    return;
                }

                _track = SDL3_mixer.MIX_CreateTrack(_mixer);
                if (_track == null)
                {
                    AppLog.Warn("music", $"MIX_CreateTrack failed: {SDL3.SDL_GetError()}");
                    return;
                }

                delegate* unmanaged[Cdecl]<IntPtr, MIX_Track*, void> cb = &OnTrackStopped;
                SDL3_mixer.MIX_SetTrackStoppedCallback(_track, cb, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                AppLog.Warn("music", $"Failed to initialize SDL3 mixer: {ex.Message}");
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
        if (_index == index) { StopInternal(); _index = null; }
        else if (_index > index) _index--;
        _library.RemoveAt(index);
        Persist();
    }

    public void Clear()
    {
        StopInternal();
        _library.Clear();
        _index = null;
        Persist();
    }

    public void Play()
    {
        if (_library.Count == 0) return;

        unsafe
        {
            if (_track != null && SDL3_mixer.MIX_TrackPaused(_track))
            {
                SDL3_mixer.MIX_ResumeTrack(_track);
                return;
            }
        }

        _index ??= 0;
        _consecutiveFailures = 0;
        PlayIndex(_index.Value);
    }

    public void Pause()
    {
        unsafe
        {
            if (_track != null && SDL3_mixer.MIX_TrackPlaying(_track))
            {
                SDL3_mixer.MIX_PauseTrack(_track);
            }
        }
    }

    public void Skip()
    {
        if (_library.Count == 0) return;
        var next = Order == MusicOrder.Shuffle
            ? _rng.Next(_library.Count)
            : ((_index ?? 0) + 1) % _library.Count;
        _index = next;
        PlayIndex(next);
    }

    public void SetOrder(MusicOrder order) => Order = order;

    public void SetAutostart(bool on) => Autostart = on;

    public void MaybeAutostart()
    {
        if (Autostart && _library.Count > 0) Play();
    }

    public void PauseForBackground()
    {
        if (!IsPlaying) return;
        Pause();
        _pausedInBackground = true;
    }

    public void ResumeFromBackground()
    {
        if (!_pausedInBackground) return;
        _pausedInBackground = false;
        Play();
    }

    private void PlayIndex(int index)
    {
        StopInternal();
        if ((uint)index >= _library.Count) return;

        var track = _library[index];
        LastError = null;
        EnsureMixerInitialized();

        try
        {
            unsafe
            {
                if (_mixer == null || _track == null)
                    throw new InvalidOperationException("SDL audio mixer not initialized.");

                if (!File.Exists(track.DocumentId))
                    throw new FileNotFoundException("Audio track not found.", track.DocumentId);

                _currentAudio = SDL3_mixer.MIX_LoadAudio(_mixer, track.DocumentId, true);
                if (_currentAudio == null)
                    throw new InvalidOperationException($"MIX_LoadAudio failed: {SDL3.SDL_GetError()}");

                SDL3_mixer.MIX_SetTrackAudio(_track, _currentAudio);
                if (!SDL3_mixer.MIX_PlayTrack(_track, 0))
                    throw new InvalidOperationException($"MIX_PlayTrack failed: {SDL3.SDL_GetError()}");
            }
            _consecutiveFailures = 0;
            _index = index;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            AppLog.Warn("music", $"Playback failed ({track.Title}): {error.Message}");
            _consecutiveFailures++;
            if (_consecutiveFailures < _library.Count)
            {
                Skip();
            }
            else
            {
                AppLog.Warn("music", "All library tracks failed to play; stopping.");
                _consecutiveFailures = 0;
            }
        }
    }

    private void StopInternal()
    {
        _explicitStop = true;
        try
        {
            unsafe
            {
                if (_track != null)
                {
                    SDL3_mixer.MIX_StopTrack(_track, 0);
                    SDL3_mixer.MIX_SetTrackAudio(_track, null);
                }
                if (_currentAudio != null)
                {
                    SDL3_mixer.MIX_DestroyAudio(_currentAudio);
                    _currentAudio = null;
                }
            }
        }
        finally
        {
            _explicitStop = false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnTrackStopped(IntPtr userdata, MIX_Track* track)
    {
        var player = _activeInstance;
        if (player == null || player._explicitStop || player._disposed) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!player._explicitStop && !player._disposed)
            {
                player.Skip();
            }
        });
    }

    private void Persist() =>
        Preferences.Default.Set("music_library", string.Join("\n", _library.Select(t => $"{t.DocumentId}\u0001{t.Title}")));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();

        unsafe
        {
            if (_track != null)
            {
                SDL3_mixer.MIX_DestroyTrack(_track);
                _track = null;
            }
            if (_mixer != null)
            {
                SDL3_mixer.MIX_DestroyMixer(_mixer);
                _mixer = null;
            }
        }
    }
}
