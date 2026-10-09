using AndroidUri = Android.Net.Uri;
using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.App;

/// <summary>
/// Walks a granted SAF tree looking for parseable Pokémon saves. Delegates scan engine logic
/// to <see cref="EmulatorTreeScanner{TNode}"/> over <see cref="Platforms.Android.AndroidSafTreeSource"/>.
/// </summary>
public sealed class AndroidEmulatorScanner(ISaveEngine engine) : IIncrementalEmulatorDetectionService
{
    private static readonly Platforms.Android.PreferencesScanCacheStore CacheStore = new();

    private static long _installEpoch = -1;
    private static long InstallEpoch
    {
        get
        {
            if (_installEpoch >= 0) return _installEpoch;
            try
            {
                var context = Platform.AppContext;
                _installEpoch = context.PackageManager!.GetPackageInfo(context.PackageName!, 0)?.LastUpdateTime ?? 0;
            }
            catch
            {
                _installEpoch = 0;
            }
            return _installEpoch;
        }
    }

    public ValueTask<EmulatorScanResult> ScanAsync(string treeId, EmulatorKind kind, CancellationToken cancellationToken = default)
    {
        return new ValueTask<EmulatorScanResult>(Task.Run(() =>
        {
            var treeUri = AndroidUri.Parse(treeId) ?? throw new ArgumentException("Invalid tree URI.", nameof(treeId));
            var source = new Platforms.Android.AndroidSafTreeSource(treeUri);
            var scanner = new EmulatorTreeScanner<Platforms.Android.ChildDocument>(engine, source, CacheStore, InstallEpoch);
            return scanner.Scan(treeId, kind, cancellationToken);
        }, cancellationToken));
    }

    public IAsyncEnumerable<EmulatorScanUpdate> ScanIncrementalAsync(
        string treeId, EmulatorKind kind,
        CancellationToken cancellationToken = default)
    {
        var treeUri = AndroidUri.Parse(treeId) ?? throw new ArgumentException("Invalid tree URI.", nameof(treeId));
        var source = new Platforms.Android.AndroidSafTreeSource(treeUri);
        var scanner = new EmulatorTreeScanner<Platforms.Android.ChildDocument>(engine, source, CacheStore, InstallEpoch);
        return scanner.ScanIncrementalAsync(treeId, kind, cancellationToken);
    }
}
