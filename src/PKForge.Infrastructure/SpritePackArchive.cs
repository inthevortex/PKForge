using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// One release archive of sprite pack files: its URL, exact size and SHA-256 (printed by
/// tools/SpritePack). A new archive gets a new release tag, so an app build always verifies
/// exactly the bytes it expects, and an archive never changes once published.
/// </summary>
public sealed record SpritePackArchive(string Name, string Url, long Bytes, string Sha256)
{
    /// <summary>Written once this archive is unpacked under <paramref name="root"/>.</summary>
    public string DoneMarker(string root) => Path.Combine(root, "spritepack", Sha256[..16] + ".done");

    /// <summary>
    /// Free bytes an install still needs with <paramref name="downloaded"/> bytes of the archive
    /// already on disk: the archive and its unpacked files coexist until unpacking ends.
    /// </summary>
    public long SpaceNeeded(long downloaded) => Bytes * 2 + (64L << 20) - Math.Clamp(downloaded, 0, Bytes);
}

/// <summary>How an archive install ended.</summary>
public enum ArchiveOutcome
{
    /// <summary>Downloaded, verified and unpacked.</summary>
    Installed,

    /// <summary>The server would not serve the archive (not published, gone, refused).</summary>
    Unavailable,

    /// <summary>
    /// The download or the unpacking stopped part-way (connection lost or stalled, a file that
    /// could not be written); what is done is kept and the next attempt continues from there.
    /// </summary>
    Interrupted,

    /// <summary>The bytes were not the expected archive (size, checksum, unreadable zip) and were discarded.</summary>
    Damaged,

    /// <summary>Not enough free space; nothing was downloaded.</summary>
    NoSpace,
}

/// <summary>An install's outcome; for <see cref="ArchiveOutcome.NoSpace"/>, the bytes it needed and the bytes free.</summary>
public readonly record struct ArchiveResult(ArchiveOutcome Outcome, long SpaceNeeded = 0, long SpaceFree = 0);

/// <summary>
/// Installs a <see cref="SpritePackArchive"/>: one resumable download into a ".part" file,
/// checked against its size and SHA-256, then unpacked file by file into the caches under the
/// app data directory. Additive: files already there are never touched, and only plain cache
/// files (<see cref="SpritePack.IsSafeEntryName"/>) are written.
/// </summary>
/// <param name="http">Sends the download; its own timeout should be infinite, as stalls are caught per read.</param>
/// <param name="freeSpace">Free bytes on the volume holding a path.</param>
/// <param name="stallTimeout">How long the download may wait for a byte before it counts as interrupted.</param>
public sealed class SpritePackArchiveInstaller(HttpClient http, Func<string, long> freeSpace, TimeSpan stallTimeout)
{
    /// <summary>Free bytes on the volume holding <paramref name="path"/>.</summary>
    public static long FreeSpace(string path) => new DriveInfo(path).AvailableFreeSpace;

    public async Task<ArchiveResult> InstallAsync(string root, SpritePackArchive source, Action<string, double> onProgress,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(root, "spritepack");
        var archive = Path.Combine(folder, Path.GetFileName(new Uri(source.Url).LocalPath));
        var partial = archive + ".part";
        try
        {
            Directory.CreateDirectory(folder);
            var downloaded = File.Exists(archive) ? source.Bytes : File.Exists(partial) ? new FileInfo(partial).Length : 0;
            var needed = source.SpaceNeeded(downloaded);
            var free = freeSpace(root);
            if (free < needed) return new ArchiveResult(ArchiveOutcome.NoSpace, needed, free);

            if (!File.Exists(archive))
            {
                await DownloadAsync(partial, source, onProgress, cancellationToken).ConfigureAwait(false);
                onProgress($"{source.Name} · checking the download…", 1);
                if (!await MatchesAsync(partial, source, cancellationToken).ConfigureAwait(false))
                {
                    TryDelete(partial);
                    return new ArchiveResult(ArchiveOutcome.Damaged);
                }
                File.Move(partial, archive, overwrite: true);
            }

            await UnpackAsync(archive, root, source.Name, onProgress, cancellationToken).ConfigureAwait(false);
            File.Delete(archive);
            await File.WriteAllTextAsync(source.DoneMarker(root), source.Url, cancellationToken).ConfigureAwait(false);
            return new ArchiveResult(ArchiveOutcome.Installed);
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The server does not recognise what is on disk as a prefix of the archive.
            TryDelete(partial);
            return new ArchiveResult(ArchiveOutcome.Damaged);
        }
        catch (HttpRequestException error) when (error.StatusCode is not null)
        {
            return new ArchiveResult(ArchiveOutcome.Unavailable);
        }
        catch (InvalidDataException)
        {
            TryDelete(partial);
            TryDelete(archive);
            return new ArchiveResult(ArchiveOutcome.Damaged);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException
                                      or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ArchiveResult(ArchiveOutcome.Interrupted);
        }
    }

    /// <summary>Downloads into <paramref name="partial"/>, resuming from what an earlier attempt left.</summary>
    private async Task DownloadAsync(string partial, SpritePackArchive pack, Action<string, double> onProgress, CancellationToken cancellationToken)
    {
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset > pack.Bytes) { File.Delete(partial); offset = 0; }
        if (offset == pack.Bytes) return;

        // Cancelled when no byte arrives for a while; re-armed after every read.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(stallTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, pack.Url);
        if (offset > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent) offset = 0; // no range support: start over
        if (offset > 0 && response.Content.Headers.ContentRange?.From != offset)
            throw new InvalidDataException($"{pack.Name} resumed at the wrong place.");

        await using var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        await using var target = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        var written = offset;
        var lastReport = 0L;
        int read;
        while ((read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
        {
            stall.CancelAfter(stallTimeout);
            if (written + read > pack.Bytes) throw new InvalidDataException($"{pack.Name} is larger than expected.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            if (written - lastReport >= 1 << 20 || written == pack.Bytes)
            {
                lastReport = written;
                onProgress($"{pack.Name} · downloading {written >> 20} / {pack.Bytes >> 20} MB", (double)written / pack.Bytes);
            }
        }
    }

    private static async Task<bool> MatchesAsync(string path, SpritePackArchive source, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != source.Bytes) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexStringLower(hash), source.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes every entry the device lacks, each through a temporary file and a rename.</summary>
    private static async Task UnpackAsync(string archive, string root, string name, Action<string, double> onProgress, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archive);
        var total = zip.Entries.Count;
        var done = 0;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;
            if (SpritePack.IsSafeEntryName(entry.FullName))
            {
                var target = Path.Combine(root, entry.FullName);
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var temporary = target + ".part";
                    await using (var source = entry.Open())
                    await using (var output = File.Create(temporary))
                        await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, target, overwrite: true);
                }
            }
            if (done % 100 == 0 || done == total)
                onProgress($"{name} · unpacking {done} / {total}", (double)done / total);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
