namespace PKForge.App.Platforms.Desktop;

/// <summary>The SDL calls <see cref="SdlSubsystems"/> makes, so its bookkeeping can run without SDL.</summary>
internal interface ISdlNative
{
    bool InitSubSystem(uint flag);
    void QuitSubSystem(uint flag);
    bool InitMixer();
    void QuitMixer();
    string LastError { get; }
}

/// <summary>
/// Reference counts SDL subsystems per flag and SDL_mixer as a whole, so a subsystem is
/// initialised by its first holder and shut down only when its last holder lets go.
/// </summary>
/// <remarks>Not thread-safe: every call must come from the thread that owns SDL.</remarks>
internal sealed class SdlSubsystems(ISdlNative native, Action<string> warn)
{
    private readonly Dictionary<uint, int> _refCounts = new();
    private int _mixerHolds;

    /// <summary>Whether any subsystem is held.</summary>
    public bool HoldsAny => _refCounts.Count > 0;

    public int RefCount(uint flag) => _refCounts.GetValueOrDefault(flag);

    public int MixerHolds => _mixerHolds;

    /// <summary>
    /// Initialises each subsystem in <paramref name="flags"/> that is not already held. Returns
    /// false, holding nothing from this call, when one of them fails to initialise.
    /// </summary>
    public bool Acquire(uint flags)
    {
        var acquired = new List<uint>();
        foreach (var flag in Split(flags))
        {
            var count = _refCounts.GetValueOrDefault(flag);
            if (count == 0 && !native.InitSubSystem(flag))
            {
                warn($"SDL_InitSubSystem(0x{flag:X}) failed: {native.LastError}");
                foreach (var held in acquired)
                    ReleaseOne(held);
                return false;
            }

            _refCounts[flag] = count + 1;
            acquired.Add(flag);
        }

        return true;
    }

    /// <summary>Releases one hold on each subsystem in <paramref name="flags"/>, shutting down any that nobody holds.</summary>
    public void Release(uint flags)
    {
        foreach (var flag in Split(flags))
            ReleaseOne(flag);
    }

    /// <summary>
    /// Holds the audio subsystem <paramref name="audioFlag"/> and SDL_mixer together. Returns
    /// false, holding neither, when either fails to initialise.
    /// </summary>
    public bool AcquireMixer(uint audioFlag)
    {
        if (!Acquire(audioFlag))
            return false;

        if (_mixerHolds == 0 && !native.InitMixer())
        {
            warn($"MIX_Init failed: {native.LastError}");
            Release(audioFlag);
            return false;
        }

        _mixerHolds++;
        return true;
    }

    /// <summary>Releases one <see cref="AcquireMixer"/> hold, quitting SDL_mixer before the audio subsystem when it was the last.</summary>
    public void ReleaseMixer(uint audioFlag)
    {
        if (_mixerHolds == 0)
        {
            warn("SDL_mixer released more often than acquired");
            return;
        }

        if (--_mixerHolds == 0)
            native.QuitMixer();
        Release(audioFlag);
    }

    private void ReleaseOne(uint flag)
    {
        var count = _refCounts.GetValueOrDefault(flag);
        if (count == 0)
        {
            warn($"SDL subsystem 0x{flag:X} released more often than acquired");
            return;
        }

        if (count > 1)
        {
            _refCounts[flag] = count - 1;
            return;
        }

        _refCounts.Remove(flag);
        native.QuitSubSystem(flag);
    }

    private static IEnumerable<uint> Split(uint bits)
    {
        while (bits != 0)
        {
            var lowest = bits & (~bits + 1);
            yield return lowest;
            bits &= ~lowest;
        }
    }
}
