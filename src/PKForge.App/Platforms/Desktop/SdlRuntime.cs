namespace PKForge.App.Platforms.Desktop;

using SDL;
using PKForge.App.Services;

/// <summary>
/// The one owner of SDL on desktop: its subsystem lifetimes and its event loop.
/// </summary>
/// <remarks>
/// SDL_InitSubSystem, SDL_QuitSubSystem and event pumping must not run concurrently, so all
/// three happen on one thread: callers on other threads hand their init and quit to it and
/// wait. SDL's main-thread rules cover its video subsystem, which is never initialised
/// because Avalonia owns the window. Subsystems are reference-counted, so one user releasing
/// SDL never shuts down a subsystem another user still holds. SDL's error text is per
/// thread, so failures are logged here rather than by the caller.
/// </remarks>
internal static class SdlRuntime
{
    // Short enough that queued init and quit wait little while events are being pumped.
    private const int PumpTimeoutMs = 10;

    private static readonly SdlSubsystems Subsystems = new(new NativeSdl(), message => AppLog.Warn("sdl", message));
    private static readonly SdlOwnerThread Owner = new(
        "PKForge.SDL",
        () => _eventHandler is not null,
        PumpEvents,
        error => AppLog.Error("sdl", "SDL event pump failed", error));

    // Read and written on the owner thread only.
    private static Action<SDL_Event>? _eventHandler;

    /// <summary>
    /// Initialises each subsystem in <paramref name="flags"/> that is not already held. Returns
    /// false, holding nothing from this call, when SDL fails to initialise one of them.
    /// </summary>
    public static bool Acquire(SDL_InitFlags flags) => Owner.Invoke(() => Subsystems.Acquire((uint)flags));

    /// <summary>Releases one hold on each subsystem in <paramref name="flags"/>, shutting down any that nobody holds.</summary>
    public static void Release(SDL_InitFlags flags) => Owner.Invoke(() =>
    {
        Subsystems.Release((uint)flags);
        return true;
    });

    /// <summary>Holds SDL audio and SDL_mixer; false, holding neither, when either fails to start.</summary>
    public static bool AcquireMixer() => Owner.Invoke(() => Subsystems.AcquireMixer((uint)SDL_InitFlags.SDL_INIT_AUDIO));

    /// <summary>Releases one <see cref="AcquireMixer"/> hold, after the holder has destroyed its mixer objects.</summary>
    public static void ReleaseMixer() => Owner.Invoke(() =>
    {
        Subsystems.ReleaseMixer((uint)SDL_InitFlags.SDL_INIT_AUDIO);
        return true;
    });

    /// <summary>Runs <paramref name="work"/> on the SDL thread without waiting for it.</summary>
    public static Task RunAsync(Action work) => Owner.InvokeAsync(() =>
    {
        work();
        return true;
    });

    /// <summary>
    /// Sets the handler every pumped event goes to; events are pumped only while one is set.
    /// Call on the SDL thread, with the subsystems the events come from held.
    /// </summary>
    public static void SetEventHandler(Action<SDL_Event>? handler)
    {
        if (!Owner.IsCurrentThread)
            throw new InvalidOperationException("The SDL event handler is set on the SDL thread.");
        _eventHandler = handler;
    }

    private static unsafe void PumpEvents()
    {
        SDL_Event e;
        if (!SDL3.SDL_WaitEventTimeout(&e, PumpTimeoutMs))
            return;
        do
        {
            _eventHandler?.Invoke(e);
        }
        while (_eventHandler is not null && SDL3.SDL_PollEvent(&e));
    }

    private sealed class NativeSdl : ISdlNative
    {
        public bool InitSubSystem(uint flag) => SDL3.SDL_InitSubSystem((SDL_InitFlags)flag);

        public void QuitSubSystem(uint flag) => SDL3.SDL_QuitSubSystem((SDL_InitFlags)flag);

        public bool InitMixer() => SDL3_mixer.MIX_Init();

        public void QuitMixer() => SDL3_mixer.MIX_Quit();

        public string LastError => SDL3.SDL_GetError() ?? "";
    }
}
