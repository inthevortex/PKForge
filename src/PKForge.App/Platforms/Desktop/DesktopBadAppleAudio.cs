using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PKForge.App.Services;
using SDL;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Dedicated desktop audio player for the Bad Apple!! easter egg.
/// Streams badapple/audio.ogg through SDL3_mixer, decoding while it plays, and reports the
/// playback position in milliseconds for the frame clock.
/// </summary>
public sealed class DesktopBadAppleAudio : IDisposable
{
    private static DesktopBadAppleAudio? _activeInstance;

    private unsafe MIX_Mixer* _mixer;
    private unsafe MIX_Track* _track;
    private unsafe MIX_Audio* _audio;
    private bool _closed;
    private bool _mixerHeld;

    public event Action? Completion;

    public int CurrentPosition
    {
        get
        {
            unsafe
            {
                if (_track == null) return 0;
                var frames = SDL3_mixer.MIX_GetTrackPlaybackPosition(_track);
                return (int)SDL3_mixer.MIX_TrackFramesToMS(_track, frames);
            }
        }
    }

    /// <summary>Opens the audio device and the song; on failure nothing stays open.</summary>
    /// <exception cref="InvalidOperationException">SDL could not open the device or the song.</exception>
    public DesktopBadAppleAudio(string path)
    {
        if (!SdlRuntime.AcquireMixer())
            throw new InvalidOperationException("SDL audio could not start.");
        _mixerHeld = true;

        try
        {
            unsafe
            {
                _mixer = SDL3_mixer.MIX_CreateMixerDevice(SDL3.SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, null);
                if (_mixer == null)
                    throw new InvalidOperationException($"MIX_CreateMixerDevice failed: {SDL3.SDL_GetError()}");

                _track = SDL3_mixer.MIX_CreateTrack(_mixer);
                if (_track == null)
                    throw new InvalidOperationException($"MIX_CreateTrack failed: {SDL3.SDL_GetError()}");

                _audio = SDL3_mixer.MIX_LoadAudio(_mixer, path, false);
                if (_audio == null)
                    throw new InvalidOperationException($"MIX_LoadAudio failed: {SDL3.SDL_GetError()}");

                delegate* unmanaged[Cdecl]<IntPtr, MIX_Track*, void> cb = &OnStopped;
                SDL3_mixer.MIX_SetTrackStoppedCallback(_track, cb, IntPtr.Zero);
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        _activeInstance = this;
    }

    public void Start()
    {
        unsafe
        {
            if (_track != null && _audio != null)
            {
                SDL3_mixer.MIX_SetTrackAudio(_track, _audio);
                SDL3_mixer.MIX_PlayTrack(_track, 0);
            }
        }
    }

    public void Stop()
    {
        unsafe
        {
            if (_track != null)
            {
                SDL3_mixer.MIX_StopTrack(_track, 0);
            }
        }
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        Stop();

        unsafe
        {
            if (_track != null)
            {
                SDL3_mixer.MIX_DestroyTrack(_track);
                _track = null;
            }
            if (_audio != null)
            {
                SDL3_mixer.MIX_DestroyAudio(_audio);
                _audio = null;
            }
            if (_mixer != null)
            {
                SDL3_mixer.MIX_DestroyMixer(_mixer);
                _mixer = null;
            }
        }

        if (_mixerHeld)
        {
            _mixerHeld = false;
            SdlRuntime.ReleaseMixer();
        }
        if (ReferenceEquals(_activeInstance, this)) _activeInstance = null;
    }

    // Runs on SDL's audio thread. An exception leaving an UnmanagedCallersOnly method ends
    // the process without a crash report, so none may escape.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnStopped(IntPtr userdata, MIX_Track* track)
    {
        try
        {
            var active = _activeInstance;
            if (active == null || active._closed) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (!active._closed)
                    {
                        active.Completion?.Invoke();
                    }
                }
                catch (Exception error)
                {
                    AppLog.Error("about", "Ending the easter egg after its song failed", error);
                }
            });
        }
        catch (Exception error)
        {
            AppLog.Error("about", "Song-stopped callback failed", error);
        }
    }
}
