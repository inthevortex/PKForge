using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.App.Services;

/// <summary>How a sprite pack download ended.</summary>
public enum SpritePackStatus
{
    /// <summary>Every pack file is on the device.</summary>
    Complete,

    /// <summary>No network: nothing was attempted.</summary>
    Offline,

    /// <summary>Not enough free space for the archive; nothing was downloaded.</summary>
    NoSpace,

    /// <summary>The download stopped part-way; the next run resumes it.</summary>
    Interrupted,

    /// <summary>The run finished, but some files could not be fetched.</summary>
    Incomplete,
}

/// <summary>A download's outcome, with the files still missing or the space it lacked.</summary>
public sealed record SpritePackResult(SpritePackStatus Status, int Missing = 0, long SpaceNeeded = 0, long SpaceFree = 0);

/// <summary>
/// Downloads the complete offline sprite pack: animated Showdown sprites, HOME renders and
/// item icons. The fast path is one archive (built by tools/SpritePack, published as a
/// release asset) put in place by <see cref="SpritePackArchiveInstaller"/>: a single resumable
/// download, checked against its SHA-256, then unpacked file by file into the same caches the
/// app already reads. Whatever is still missing after that, or everything when the archive is
/// unavailable, is fetched file by file. Purely additive and resumable: existing files are
/// never touched.
/// </summary>
public sealed class SpritePackDownloader(ISpriteService sprites, IGameDataService data)
{
    /// <summary>Rough size of the full pack; shown before starting.</summary>
    public const string SizeHint = "~600 MB";

    // The main pack: Showdown animations, HOME renders, item icons. Falls back to per-file downloads.
    private static readonly SpritePackArchive Main = new("Sprite pack",
        "https://github.com/sofianeelhor/PKForge/releases/download/sprites-1/pkforge-sprites.zip",
        625_017_170, "611f384b2475f24946ecd51ba8a726aafffc04736f0cedf47236a1ac00ff6cd7");

    // Add-ons: archive only, no per-file source. A new add-on is appended here; players who
    // have the others fetch just that one. Missing add-ons change nothing: their art is optional.
    private static readonly SpritePackArchive[] AddOns =
    [
        // BDSP-style box icons, normal and shiny (Team Luminescent's bdsp-shiny-icons, MIT),
        // drawn in Brilliant Diamond / Shining Pearl and Luminescent Platinum boxes.
        new("BDSP icons", "https://github.com/sofianeelhor/PKForge/releases/download/bdsp-icons-1/pkforge-bdsp-icons.zip",
            44_217_786, "cdbc291dae933b57f7c28018410b5162aed67342a9847843a8586fa741823baf"),
    ];

    /// <summary>Below this many missing files, fetching them one by one beats a full archive.</summary>
    private const int ArchiveThreshold = 400;

    private const int Parallelism = 16;

    private static readonly SpritePackArchiveInstaller Installer = new(
        new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, SpritePackArchiveInstaller.FreeSpace, TimeSpan.FromSeconds(30),
        ItemArt.NoteUnpacked);

    private static bool? _mainPackInstalled;

    /// <summary>True once the main pack is on this device; checked once, then kept until a download changes it.</summary>
    public static bool MainPackInstalled => _mainPackInstalled ??= File.Exists(Main.DoneMarker(FileSystem.AppDataDirectory));

    /// <summary>How many pack files this device still lacks; 0 once the pack is complete.</summary>
    public int CountMissing()
    {
        var root = FileSystem.AppDataDirectory;
        return SpritePack.Entries([]).Count(e => !File.Exists(Path.Combine(root, e.CachePath))) + WantedItems(root).Count;
    }

    // Many item names have no icon upstream. Once the pack is complete (the archive is in, or
    // a per-file run fetched every icon except those absent upstream), the ones still lacking
    // are known absent: counting them again would refetch the whole archive or send ~1800
    // requests that can only fail. The UI still asks for them on demand, as before.
    private List<string> WantedItems(string root) => File.Exists(Main.DoneMarker(root))
        ? []
        : [.. data.ItemNames.Where(n => SpritePack.ItemSlug(n).Length > 0).Distinct().Where(n => !ItemArt.IsCachedOrKnownAbsent(n))];

    /// <summary>
    /// Downloads whatever the device lacks, off the UI thread. Cancelling throws
    /// <see cref="OperationCanceledException"/>; everything finished so far is kept.
    /// </summary>
    public Task<SpritePackResult> RunAsync(Action<string, double> onProgress, CancellationToken cancellationToken) =>
        Task.Run(() => RunCoreAsync(onProgress, cancellationToken), cancellationToken);

    private async Task<SpritePackResult> RunCoreAsync(Action<string, double> onProgress, CancellationToken cancellationToken)
    {
        if (Connectivity.Current.NetworkAccess is NetworkAccess.None or NetworkAccess.Local)
            return new SpritePackResult(SpritePackStatus.Offline);

        var root = FileSystem.AppDataDirectory;
        try
        {
            var spriteEntries = SpritePack.Entries([]);
            var missing = spriteEntries.Count(e => !File.Exists(Path.Combine(root, e.CachePath))) + WantedItems(root).Count;
            if (missing >= ArchiveThreshold)
            {
                var archive = await Installer.InstallAsync(root, Main, onProgress, cancellationToken).ConfigureAwait(false);
                switch (archive.Outcome)
                {
                    case ArchiveOutcome.NoSpace:
                        return new SpritePackResult(SpritePackStatus.NoSpace, SpaceNeeded: archive.SpaceNeeded, SpaceFree: archive.SpaceFree);
                    case ArchiveOutcome.Interrupted:
                        return new SpritePackResult(SpritePackStatus.Interrupted);
                }
            }

            var itemFailures = await DownloadMissingAsync(root, spriteEntries, WantedItems(root), onProgress, cancellationToken).ConfigureAwait(false);
            await FetchAddOnsAsync(root, onProgress, cancellationToken).ConfigureAwait(false);

            // Every sprite in place, file by file or from the archive, counts as the pack once no
            // item icon failed for a reason other than being absent upstream; until then the
            // failed icons stay counted as missing and the next run fetches them again.
            if (ItemIconMisses.PackCanBeMarkedComplete(itemFailures, spriteEntries.All(e => File.Exists(Path.Combine(root, e.CachePath))))
                && !File.Exists(Main.DoneMarker(root)))
                await File.WriteAllTextAsync(Main.DoneMarker(root), Main.Url, cancellationToken).ConfigureAwait(false);

            var left = CountMissing();
            return left == 0 ? new SpritePackResult(SpritePackStatus.Complete) : new SpritePackResult(SpritePackStatus.Incomplete, left);
        }
        finally
        {
            _mainPackInstalled = null;
        }
    }

    // One add-on download at a time: the quiet one and a full download share archive files.
    private static readonly SemaphoreSlim AddOnGate = new(1, 1);
    private DateTime _quietRetryAfter = DateTime.MinValue;

    /// <summary>
    /// Fetches the add-ons in the background for players who already have the main pack, so
    /// new art arrives with an update and nobody has to download anything again. Silent: no
    /// overlay, no status, only on Wi-Fi or Ethernet (never mobile data), and any failure is
    /// logged and retried later. Whoever has no main pack gets add-ons with it instead.
    /// </summary>
    public async Task FetchAddOnsQuietlyAsync()
    {
        var root = FileSystem.AppDataDirectory;
        if (DateTime.UtcNow < _quietRetryAfter || !File.Exists(Main.DoneMarker(root))
            || AddOns.All(a => File.Exists(a.DoneMarker(root))))
            return;
        try
        {
            var network = Connectivity.Current;
            if (network.NetworkAccess != NetworkAccess.Internet
                || !network.ConnectionProfiles.Any(p => p is ConnectionProfile.WiFi or ConnectionProfile.Ethernet))
                return;
            if (!await Task.Run(() => FetchAddOnsAsync(root, static (_, _) => { }, CancellationToken.None)).ConfigureAwait(false))
            {
                _quietRetryAfter = DateTime.UtcNow.AddMinutes(15);
                AppLog.Warn("sprites", "Add-on download did not finish; will retry later");
            }
        }
        catch (Exception error)
        {
            _quietRetryAfter = DateTime.UtcNow.AddMinutes(15);
            AppLog.Error("sprites", "Add-on download failed", error);
        }
    }

    /// <summary>Downloads every add-on not yet unpacked; true when all of them are in.</summary>
    private async Task<bool> FetchAddOnsAsync(string root, Action<string, double> onProgress, CancellationToken cancellationToken)
    {
        await AddOnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var complete = true;
            foreach (var addOn in AddOns.Where(a => !File.Exists(a.DoneMarker(root))))
            {
                var result = await Installer.InstallAsync(root, addOn, onProgress, cancellationToken).ConfigureAwait(false);
                if (result.Outcome != ArchiveOutcome.Installed)
                {
                    complete = false;
                    AppLog.Warn("sprites", $"{addOn.Name}: {result.Outcome}");
                }
            }
            sprites.ForgetAddOnLookups(); // icons that were absent a minute ago may be here now
            return complete;
        }
        finally { AddOnGate.Release(); }
    }

    /// <summary>
    /// The per-file pass: only what is still missing, a bounded number at a time. Returns how
    /// many item icons failed for a reason other than being absent upstream.
    /// </summary>
    private async Task<int> DownloadMissingAsync(string root, IReadOnlyList<SpritePack.Entry> spriteEntries, IReadOnlyList<string> itemNames,
        Action<string, double> onProgress, CancellationToken cancellationToken)
    {
        var units = new List<Func<Task>>();
        foreach (var entry in spriteEntries)
        {
            if (!File.Exists(Path.Combine(root, entry.CachePath)))
                units.Add(() => sprites.DownloadPackFileAsync(entry, cancellationToken));
        }
        var itemFailures = 0;
        foreach (var name in itemNames)
        {
            if (!ItemArt.IsCachedOrKnownAbsent(name))
                units.Add(async () =>
                {
                    if (!await ItemArt.FetchForPackAsync(name).ConfigureAwait(false)) Interlocked.Increment(ref itemFailures);
                });
        }

        var total = units.Count;
        if (total == 0) return 0;
        var done = 0;
        var gate = new SemaphoreSlim(Parallelism);
        var tasks = units.Select(async unit =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await unit().ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
                var current = Interlocked.Increment(ref done);
                if (current % 20 == 0 || current == total)
                    onProgress($"{current} / {total}", (double)current / total);
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return itemFailures;
    }
}
