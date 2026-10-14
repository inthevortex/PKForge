using System.Globalization;
using System.Net;

namespace PKForge.App.Services;

/// <summary>Why an item icon has no file on the device.</summary>
internal enum ItemMissKind
{
    /// <summary>The icon does not exist upstream: the server answered 404.</summary>
    Absent,

    /// <summary>The download failed for another reason: a timeout, a server error, no connection.</summary>
    Failed,
}

/// <summary>
/// The ".miss" file left beside an item icon that could not be fetched, recording when and
/// whether the icon is absent upstream or its download failed. Screens honor any fresh miss,
/// so they do not ask again on every paint; the sprite pack honors only an absence, so an icon
/// that merely failed is fetched again before the pack counts as complete.
/// </summary>
internal static class ItemIconMisses
{
    /// <summary>How long a miss holds before the icon is asked for again.</summary>
    public static TimeSpan Lifetime { get; } = TimeSpan.FromHours(20);

    /// <summary>The miss file kept beside <paramref name="iconPath"/>.</summary>
    public static string MissPath(string iconPath) => iconPath + ".miss";

    /// <summary>The kind of miss a download that threw <paramref name="error"/> leaves.</summary>
    public static ItemMissKind KindOf(Exception error) =>
        error is HttpRequestException { StatusCode: HttpStatusCode.NotFound } ? ItemMissKind.Absent : ItemMissKind.Failed;

    /// <summary>
    /// Whether a miss of <paramref name="kind"/> is recorded and honored for a caller: an absence
    /// always; a failure only for a caller that remembers failures, never for the sprite pack.
    /// </summary>
    public static bool Holds(ItemMissKind kind, bool rememberFailures) => kind == ItemMissKind.Absent || rememberFailures;

    /// <summary>
    /// True when the icon at <paramref name="iconPath"/> needs no fetch: a whole one is on disk, or
    /// a fresh miss that <see cref="Holds"/> for the caller says there is none to get. A damaged
    /// icon is deleted and counts as missing.
    /// </summary>
    public static bool IsSettled(string iconPath, bool rememberFailures, DateTimeOffset now) =>
        ItemIconFile.IsUsable(iconPath) || ReadFresh(MissPath(iconPath), now) is { } kind && Holds(kind, rememberFailures);

    /// <summary>
    /// True once the sprite pack may be marked complete: every sprite is in place and no item
    /// icon failed for a reason other than being absent upstream.
    /// </summary>
    public static bool PackCanBeMarkedComplete(int itemFailures, bool spritesInPlace) => itemFailures == 0 && spritesInPlace;

    /// <summary>Records a miss of <paramref name="kind"/> at <paramref name="missPath"/>; best effort.</summary>
    public static async Task WriteAsync(string missPath, ItemMissKind kind, DateTimeOffset now)
    {
        try
        {
            await File.WriteAllTextAsync(missPath, $"{kind}\n{now.ToString("O", CultureInfo.InvariantCulture)}").ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The kind of the miss at <paramref name="missPath"/> while it is fresh; null when there is
    /// none, it is unreadable, or it expired (an expired miss is deleted, so the next look
    /// retries). A miss that records only its time reads as <see cref="ItemMissKind.Failed"/>.
    /// </summary>
    public static ItemMissKind? ReadFresh(string missPath, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(missPath) || !TryParse(File.ReadAllText(missPath), out var kind, out var written)) return null;
            if (now - written < Lifetime) return kind;
            File.Delete(missPath);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryParse(string text, out ItemMissKind kind, out DateTimeOffset written)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        kind = ItemMissKind.Failed;
        written = default;
        return lines.Length switch
        {
            1 => TryParseTime(lines[0], out written),
            2 => Enum.TryParse(lines[0], out kind) && Enum.IsDefined(kind) && TryParseTime(lines[1], out written),
            _ => false,
        };
    }

    private static bool TryParseTime(string text, out DateTimeOffset written) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out written);
}
