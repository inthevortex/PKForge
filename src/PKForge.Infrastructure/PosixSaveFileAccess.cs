using System.Runtime.InteropServices;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// POSIX filesystem save access.
/// Document IDs are canonical absolute paths. Writes are atomic via temporary file + fsync + rename.
/// Symlinks are resolved to their target before writing so the link itself is preserved.
/// </summary>
public class PosixSaveFileAccess : ISaveFileAccess
{
    public virtual async ValueTask<ReadOnlyMemory<byte>> ReadAsync(string documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(documentId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Save file not found at '{path}'.", path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    public virtual async ValueTask WriteAtomicallyAsync(string documentId, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Path.GetFullPath(documentId);

        // Resolve symlink target (if any) so we write to the target file and leave the symlink pointing to it.
        var writeTarget = ResolveFinalTarget(target);
        var directory = Path.GetDirectoryName(writeTarget)
            ?? throw new IOException($"Cannot determine parent directory for '{writeTarget}'.");

        if (!Directory.Exists(directory))
            throw new IOException($"Target directory '{directory}' does not exist.");

        var fileName = Path.GetFileName(writeTarget);
        var tmp = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.pkforge-tmp");

        UnixFileMode? originalMode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(writeTarget))
        {
            try { originalMode = File.GetUnixFileMode(writeTarget); }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        try
        {
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
                catch (UnauthorizedAccessException) { }
            }

            File.Move(tmp, writeTarget, overwrite: true);
            TryFsyncDirectory(directory);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch { }

            if (ex is UnauthorizedAccessException)
                throw new IOException($"Access denied writing to '{writeTarget}'.", ex);

            throw;
        }
    }

    public static string ResolveFinalTarget(string path)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            var targetInfo = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
            if (targetInfo != null)
                return targetInfo.FullName;
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return fullPath;
    }

    private static void TryFsyncDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        try
        {
            int fd = Libc.open(directory, Libc.O_RDONLY | Libc.O_DIRECTORY);
            if (fd >= 0)
            {
                try { Libc.fsync(fd); }
                finally { Libc.close(fd); }
            }
        }
        catch
        {
        }
    }

    private static class Libc
    {
        public const int O_RDONLY = 0;
        public const int O_DIRECTORY = 0x10000;

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        public static extern int open(string pathname, int flags);

        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        public static extern int fsync(int fd);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int close(int fd);
    }
}
