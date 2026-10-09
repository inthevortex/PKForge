namespace PKForge.Domain;

/// <summary>
/// An emulator or ROM folder automatically discovered on the host system.
/// </summary>
/// <param name="Kind">The emulator engine/family (e.g. RetroArch, melonDS, Dolphin, Ryujinx, RomFolder).</param>
/// <param name="TreeId">The root path that can be watched and scanned.</param>
/// <param name="DisplayName">User-facing label (e.g. "RetroArch Saves", "melonDS Saves", "Ryujinx User Saves").</param>
/// <param name="SourceName">Where this root was discovered (e.g. "retroarch.cfg", "EmuDeck", "Default path").</param>
public sealed record DiscoveredRoot(
    EmulatorKind Kind,
    string TreeId,
    string DisplayName,
    string SourceName);

/// <summary>
/// Discovers configured emulators and ROM/save roots on the host platform without requiring manual folder picking.
/// </summary>
public interface IEmulatorRootDiscovery
{
    ValueTask<IReadOnlyList<DiscoveredRoot>> DiscoverRootsAsync(CancellationToken cancellationToken = default);
}

public sealed class NullEmulatorRootDiscovery : IEmulatorRootDiscovery
{
    public static readonly NullEmulatorRootDiscovery Instance = new();
    public ValueTask<IReadOnlyList<DiscoveredRoot>> DiscoverRootsAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<DiscoveredRoot>>([]);
}
