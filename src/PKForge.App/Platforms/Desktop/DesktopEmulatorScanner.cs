using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Scans granted emulator trees and ROM folders on desktop.
/// Wraps <see cref="EmulatorTreeScanner{TNode}"/> over <see cref="LocalTreeSource"/>
/// with persistent <see cref="JsonFileScanCacheStore"/>.
/// </summary>
public sealed class DesktopEmulatorScanner(ISaveEngine engine) : IIncrementalEmulatorDetectionService
{
    private static readonly JsonFileScanCacheStore CacheStore = new();

    private static long _buildEpoch = -1;
    private static long BuildEpoch
    {
        get
        {
            if (_buildEpoch >= 0) return _buildEpoch;
            try
            {
                var binary = Environment.ProcessPath ?? AppContext.BaseDirectory;
                _buildEpoch = File.Exists(binary) ? File.GetLastWriteTimeUtc(binary).Ticks : 0;
            }
            catch
            {
                _buildEpoch = 0;
            }
            return _buildEpoch;
        }
    }

    public ValueTask<EmulatorScanResult> ScanAsync(string treeId, EmulatorKind kind, CancellationToken cancellationToken = default)
    {
        return new ValueTask<EmulatorScanResult>(Task.Run(() =>
        {
            var source = new LocalTreeSource();
            var scanner = new EmulatorTreeScanner<string>(engine, source, CacheStore, BuildEpoch);
            return scanner.Scan(treeId, kind, cancellationToken);
        }, cancellationToken));
    }

    public IAsyncEnumerable<EmulatorScanUpdate> ScanIncrementalAsync(
        string treeId, EmulatorKind kind,
        CancellationToken cancellationToken = default)
    {
        var source = new LocalTreeSource();
        var scanner = new EmulatorTreeScanner<string>(engine, source, CacheStore, BuildEpoch);
        return scanner.ScanIncrementalAsync(treeId, kind, cancellationToken);
    }
}
