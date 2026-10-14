namespace PKForge.App.Services;

/// <summary>
/// Fetches remote files into an on-disk cache. Every caller that asks for a file while it
/// downloads shares that one download; each download writes its own temporary file and renames
/// it into place, so a cached file is always whole. Files over <see cref="MaxFileBytes"/> are
/// refused, before download and before decode.
/// </summary>
/// <param name="http">Sends the downloads.</param>
/// <param name="timeout">How long one download may take in all, headers and body.</param>
internal sealed class CachedFileFetcher(HttpClient http, TimeSpan timeout)
{
    /// <summary>The largest file fetched or decoded from the cache; a sprite is a small fraction of it.</summary>
    public const long MaxFileBytes = 8L << 20;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task> _downloads = new(StringComparer.Ordinal);

    /// <summary>
    /// Completes once <paramref name="diskPath"/> holds the file at <paramref name="url"/>,
    /// downloading it unless it is cached already or on its way. Cancelling ends only this
    /// caller's wait: a download others may share runs on, bounded by its own timeout.
    /// </summary>
    public Task EnsureAsync(string url, string diskPath, CancellationToken cancellationToken)
    {
        if (File.Exists(diskPath)) return Task.CompletedTask;
        var key = Path.GetFullPath(diskPath);
        TaskCompletionSource download;
        lock (_gate)
        {
            if (_downloads.TryGetValue(key, out var running)) return running.WaitAsync(cancellationToken);
            download = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _downloads[key] = download.Task;
        }
        _ = RunAsync(url, diskPath, key, download);
        return download.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures the file, then decodes it with <paramref name="decode"/>. A cached file that is too
    /// large or will not decode is deleted and fetched once more; a fresh download that will not
    /// decode is deleted too and gives null (no art), so a later launch fetches it again. Throws
    /// when the download fails. <paramref name="downloadGate"/>, when given, bounds the downloads
    /// this call starts.
    /// </summary>
    public async Task<T?> LoadAsync<T>(string url, string diskPath, Func<string, Task<T?>> decode, SemaphoreSlim? downloadGate)
        where T : class
    {
        for (var attempt = 0; ; attempt++)
        {
            var cached = File.Exists(diskPath);
            if (!cached)
            {
                if (downloadGate is not null) await downloadGate.WaitAsync().ConfigureAwait(false);
                try { await EnsureAsync(url, diskPath, CancellationToken.None).ConfigureAwait(false); }
                finally { downloadGate?.Release(); }
            }

            var stamp = Stamp(diskPath);
            if (stamp is { Length: <= MaxFileBytes } && await decode(diskPath).ConfigureAwait(false) is { } decoded)
                return decoded;
            // Another caller may have replaced a bad file meanwhile; only the file that failed goes.
            if (stamp is not null && Stamp(diskPath) == stamp) TryDelete(diskPath);
            if (!cached || attempt > 0) return null;
        }
    }

    /// <summary>
    /// Deletes the temporary files of downloads cut short by a closed or killed app from
    /// <paramref name="folders"/>: every "*.part" file not written to for longer than the download
    /// timeout, so a download still running keeps its file. Returns how many were deleted.
    /// </summary>
    public int SweepStaleParts(IEnumerable<string> folders)
    {
        var deleted = 0;
        var cutoff = DateTime.UtcNow - timeout;
        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var part in Directory.EnumerateFiles(folder, "*.part"))
                {
                    if (TryDeleteStale(new FileInfo(part), cutoff)) deleted++;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        return deleted;
    }

    /// <summary>
    /// Deletes <paramref name="part"/> when it was last written before <paramref name="cutoff"/>.
    /// False when it is gone by the time it is looked at: a download that finished, or another
    /// sweep, took it, and this sweep deleted nothing.
    /// </summary>
    internal static bool TryDeleteStale(FileInfo part, DateTime cutoff) =>
        part.Exists && part.LastWriteTimeUtc < cutoff && TryDelete(part.FullName);

    private async Task RunAsync(string url, string diskPath, string key, TaskCompletionSource download)
    {
        Exception? failure = null;
        try { await DownloadAsync(url, diskPath).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        lock (_gate) _downloads.Remove(key);
        if (failure is null)
        {
            download.SetResult();
            return;
        }
        download.SetException(failure);
        // Observed here, so a failure every waiter stopped waiting for is not reported as unobserved.
        _ = download.Task.Exception;
    }

    private async Task DownloadAsync(string url, string diskPath)
    {
        if (File.Exists(diskPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        var temporary = $"{diskPath}.{Guid.NewGuid():N}.part";
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxFileBytes) throw TooLarge(url);
                await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                await using var target = File.Create(temporary);
                var buffer = new byte[1 << 16];
                var written = 0L;
                int read;
                while ((read = await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaxFileBytes) throw TooLarge(url);
                    await target.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
                }
            }
            File.Move(temporary, diskPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static InvalidDataException TooLarge(string url) => new($"{url} is larger than {MaxFileBytes >> 20} MB.");

    private readonly record struct FileStamp(long Length, DateTime Written);

    private static FileStamp? Stamp(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new FileStamp(file.Length, file.LastWriteTimeUtc) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
