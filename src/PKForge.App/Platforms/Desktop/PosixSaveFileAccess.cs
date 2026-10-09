using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// POSIX filesystem save access.
/// Document IDs are canonical absolute paths. Writes are atomic via temporary file + fsync + rename.
/// </summary>
public sealed class PosixSaveFileAccess : ISaveFileAccess
{
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(documentId);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    public async ValueTask WriteAtomicallyAsync(string documentId, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var target = Path.GetFullPath(documentId);
        var directory = Path.GetDirectoryName(target)
            ?? throw new IOException($"Cannot determine parent directory for '{target}'.");

        var fileName = Path.GetFileName(target);
        var tmp = Path.Combine(directory, $".{fileName}.pkforge-tmp");

        UnixFileMode? originalMode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(target))
        {
            try { originalMode = File.GetUnixFileMode(target); }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }
        }

        await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(true); // fsync
        }

        if (!OperatingSystem.IsWindows() && originalMode.HasValue)
        {
            try { File.SetUnixFileMode(tmp, originalMode.Value); }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }
        }

        File.Move(tmp, target, overwrite: true);
    }
}
