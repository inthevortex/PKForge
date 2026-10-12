using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Dedicated desktop audio player for the Bad Apple!! easter egg.
/// Decodes and plays badapple/audio.ogg via SDL3_mixer with frame-accurate millisecond tracking.
/// </summary>
public sealed class DesktopBadAppleAudio : IDisposable
{
    private static DesktopBadAppleAudio? _activeInstance;

    private unsafe MIX_Mixer* _mixer;
    private unsafe MIX_Track* _track;
    private unsafe MIX_Audio* _audio;
    private bool _closed;

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

    public DesktopBadAppleAudio(string path)
    {
        _activeInstance = this;
        unsafe
        {
            SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_AUDIO);
            SDL3_mixer.MIX_Init();

            _mixer = SDL3_mixer.MIX_CreateMixerDevice(SDL3.SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, null);
            if (_mixer == null)
                throw new InvalidOperationException($"MIX_CreateMixerDevice failed: {SDL3.SDL_GetError()}");

            _track = SDL3_mixer.MIX_CreateTrack(_mixer);
            if (_track == null)
                throw new InvalidOperationException($"MIX_CreateTrack failed: {SDL3.SDL_GetError()}");

            _audio = SDL3_mixer.MIX_LoadAudio(_mixer, path, true);
            if (_audio == null)
                throw new InvalidOperationException($"MIX_LoadAudio failed: {SDL3.SDL_GetError()}");

            delegate* unmanaged[Cdecl]<IntPtr, MIX_Track*, void> cb = &OnStopped;
            SDL3_mixer.MIX_SetTrackStoppedCallback(_track, cb, IntPtr.Zero);
        }
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
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnStopped(IntPtr userdata, MIX_Track* track)
    {
        var active = _activeInstance;
        if (active == null || active._closed) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!active._closed)
            {
                active.Completion?.Invoke();
            }
        });
    }
}
