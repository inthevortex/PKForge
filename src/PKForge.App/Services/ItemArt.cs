using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Item sprites from the PokeAPI sprite database, keyed by the item's English name
/// ("Master Ball" → items/master-ball.png). Fetched once, cached forever.
/// Misses expire after a day: a network blip must not blind an item forever.
/// </summary>
public static class ItemArt
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim Gate = new(6);

    /// <summary>
    /// PokeAPI slugs are plain ascii with dashes. Names arrive with diacritics
    /// ("Poké Doll") that must fold to their base letters (poke-doll) - the old
    /// filter turned é into a dash and every accented item 404'd forever.
    /// </summary>
    public static string Slug(string itemName) => SpritePack.ItemSlug(itemName);

    /// <summary>
    /// True when the icon is on disk, or a recent attempt found none upstream. A recent download
    /// failure does not count, so the sprite pack fetches that icon again.
    /// </summary>
    public static bool IsCachedOrKnownAbsent(string itemName) =>
        ItemIconMisses.IsSettled(CachePath(itemName), rememberFailures: false, DateTimeOffset.UtcNow);

    /// <summary>
    /// The item's icon when a whole one is cached, else null; never fetches. A damaged icon is
    /// deleted and counts as missing, so callers show their placeholder until a screen that
    /// fetches icons, or the sprite pack, puts it back.
    /// </summary>
    public static string? CachedPathOrNull(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        var cache = CachePath(itemName);
        return ItemIconFile.IsUsable(cache) ? cache : null;
    }

    private static string ItemsDirectory => Path.Combine(FileSystem.AppDataDirectory, "items");

    private static string CachePath(string itemName) => Path.Combine(ItemsDirectory, Slug(itemName) + ".png");

    /// <summary>
    /// Checks every cached icon once, so the first picker of a run does not read them on the UI
    /// thread. Call off the UI thread.
    /// </summary>
    public static void WarmIconCache()
    {
        var deleted = ItemIconFile.Warm(ItemsDirectory, ItemPlaceholder.FileName);
        if (deleted > 0) AppLog.Info("items", $"Removed {deleted} damaged item icons");
    }

    /// <summary>
    /// Records <paramref name="path"/>, a file the sprite pack archive put in place, as whole when
    /// it is an item icon: the archive's checksum covers its bytes.
    /// </summary>
    public static void NoteUnpacked(string path)
    {
        if (string.Equals(Path.GetDirectoryName(path), ItemsDirectory, StringComparison.Ordinal)
            && string.Equals(Path.GetExtension(path), ".png", StringComparison.Ordinal))
            ItemIconFile.MarkWhole(path);
    }

    /// <summary>Local path of the item's sprite, or null (unknown item / offline first time).</summary>
    public static async Task<string?> GetAsync(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        return (await FetchAsync(itemName, rememberFailures: true).ConfigureAwait(false)).Path;
    }

    /// <summary>
    /// Fetches the icon for the sprite pack: false when the download failed for a reason other
    /// than the icon being absent upstream. Only absence is remembered as a miss, and only an
    /// absence is honored, so the pack tries again an icon a screen recorded as failed.
    /// </summary>
    public static async Task<bool> FetchForPackAsync(string itemName) =>
        !(await FetchAsync(itemName, rememberFailures: false).ConfigureAwait(false)).Failed;

    private static async Task<(string? Path, bool Failed)> FetchAsync(string itemName, bool rememberFailures)
    {
        var cache = CachePath(itemName);
        var miss = ItemIconMisses.MissPath(cache);
        if (ItemIconFile.IsUsable(cache)) return (cache, false);
        if (ItemIconMisses.IsSettled(cache, rememberFailures, DateTimeOffset.UtcNow)) return (null, false);

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ItemIconFile.IsUsable(cache)) return (cache, false);
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            var bytes = await Http.GetByteArrayAsync(
                $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/items/{Slug(itemName)}.png").ConfigureAwait(false);
            await ItemIconFile.WriteAsync(cache, bytes).ConfigureAwait(false);
            return (cache, false);
        }
        catch (Exception error)
        {
            var kind = ItemIconMisses.KindOf(error);
            if (ItemIconMisses.Holds(kind, rememberFailures))
                await ItemIconMisses.WriteAsync(miss, kind, DateTimeOffset.UtcNow).ConfigureAwait(false);
            return (null, kind == ItemMissKind.Failed);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// A tiny pixel capsule tile drawn once and reused whenever a sprite is missing,
    /// so bag rows never render blank. Pure-local: no network, no dependency on names.
    /// </summary>
    public static string PlaceholderPath() =>
        ItemPlaceholder.Ensure(ItemsDirectory);

    /// <summary>
    /// Upgrade sweep: misses recorded by the old broken slugger (é mangled to a dash)
    /// poisoned items forever. One launch, wipe them all so every item retries.
    /// </summary>
    public static void PurgeLegacyMisses()
    {
        try
        {
            var directory = Path.Combine(FileSystem.AppDataDirectory, "items");
            if (!Directory.Exists(directory)) return;
            foreach (var miss in Directory.EnumerateFiles(directory, "*.miss"))
                File.Delete(miss);
        }
        catch
        {
            // Never block startup on a cache sweep.
        }
    }
}
