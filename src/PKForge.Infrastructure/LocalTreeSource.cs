using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PKForge.Infrastructure;

/// <summary>
/// Filesystem-backed tree source for Linux desktop.
/// Walks real filesystem directories with symlink loop protection via POSIX device/inode tracking.
/// </summary>
public sealed class LocalTreeSource : ITreeSource<string>
{
    public void ResetVisited()
    {
    }

    public string Root(string treeId)
    {
        var path = treeId;
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path.Length > 2 ? path[2..] : "");
        }
        return Path.GetFullPath(path);
    }

    public string RootName(string root)
    {
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(full);
        return string.IsNullOrEmpty(name) ? root : name;
    }

    public string DocumentId(string file) => Path.GetFullPath(file);

    public string PathLike(string file) => Path.GetFullPath(file);

    public Stream? OpenRead(string file)
    {
        try
        {
            var fullPath = Path.GetFullPath(file);
            if (!File.Exists(fullPath)) return null;
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<TreeNode<string>> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var result = new List<TreeNode<string>>();

        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                RecurseSubdirectories = false,
            };

            foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", options))
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name) || name.StartsWith('.'))
                    continue;

                var isDir = false;
                DateTimeOffset? lastModified = null;
                try
                {
                    var fileInfo = new FileInfo(entry);
                    var isSymlink = fileInfo.LinkTarget is not null || fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
                    if (fileInfo.Attributes.HasFlag(FileAttributes.Directory))
                    {
                        isDir = true;
                    }
                    else if (isSymlink)
                    {
                        var target = Directory.ResolveLinkTarget(entry, returnFinalTarget: true);
                        if (target is DirectoryInfo)
                            isDir = true;
                    }

                    if (isDir)
                    {
                        if (isSymlink && IsAncestorCycle(dir, entry))
                            continue;

                        var dirInfo = new DirectoryInfo(entry);
                        lastModified = dirInfo.LastWriteTimeUtc;
                    }
                    else
                    {
                        lastModified = fileInfo.LastWriteTimeUtc;
                    }

                    result.Add(new TreeNode<string>(name, isDir, lastModified, entry));
                }
                catch
                {
                    // Inaccessible entry
                }
            }
        }
        catch
        {
            // Directory unreadable
        }
        return result;
    }

    private static bool IsAncestorCycle(string currentDir, string symlinkEntry)
    {
        var targetReal = GetRealPath(symlinkEntry);
        if (TryGetInode(targetReal, out var targetDev, out var targetIno))
        {
            var dir = new DirectoryInfo(currentDir);
            while (dir is not null)
            {
                if (TryGetInode(dir.FullName, out var dDev, out var dIno))
                {
                    if (dDev == targetDev && dIno == targetIno)
                        return true;
                }
                dir = dir.Parent;
            }
        }
        else
        {
            var targetNormalized = targetReal.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var currentNormalized = GetRealPath(currentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            while (!string.IsNullOrEmpty(currentNormalized))
            {
                if (string.Equals(currentNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase))
                    return true;

                var parent = Path.GetDirectoryName(currentNormalized);
                if (parent == currentNormalized) break;
                currentNormalized = parent;
            }
        }
        return false;
    }

    private static string GetRealPath(string path)
    {
        try
        {
            var target = Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                         ?? File.ResolveLinkTarget(path, returnFinalTarget: true);
            return Path.GetFullPath(target?.FullName ?? path);
        }
        catch
        {
            return Path.GetFullPath(path);
        }
    }

    internal static bool TryGetInode(string path, out ulong dev, out ulong ino)
    {
        dev = 0;
        ino = 0;
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var buf = new byte[256];
            if (PosixStat(path, buf) == 0)
            {
                dev = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(0, 8));
                ino = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(8, 8));
                return true;
            }
        }
        catch
        {
            // Fall back
        }
        return false;
    }

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int PosixStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buf);
}
