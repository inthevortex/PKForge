using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// Flat folder file access for desktop POSIX filesystems.
/// Tree IDs and document IDs are canonical absolute paths.
/// </summary>
public class PosixFolderFileAccess(ISaveFileAccess saveFileAccess) : IFolderFileAccess
{
    public virtual ValueTask<IReadOnlyList<PickedDocument>> ListFilesAsync(string treeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullTreePath = ExpandPath(treeId);
        if (!Directory.Exists(fullTreePath))
            return ValueTask.FromResult<IReadOnlyList<PickedDocument>>([]);

        var results = new List<PickedDocument>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(fullTreePath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(new PickedDocument(Path.GetFullPath(file), Path.GetFileName(file)));
            }
        }
        catch (DirectoryNotFoundException)
        {
            return ValueTask.FromResult<IReadOnlyList<PickedDocument>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<PickedDocument>>(results);
    }

    public virtual ValueTask<ReadOnlyMemory<byte>> ReadFileAsync(string documentId, CancellationToken cancellationToken = default) =>
        saveFileAccess.ReadAsync(documentId, cancellationToken);

    public virtual ValueTask WriteFileAsync(string treeId, string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(ExpandPath(treeId), fileName);
        return saveFileAccess.WriteAtomicallyAsync(target, bytes, cancellationToken);
    }

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var fullPath = path;
        if (fullPath.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            fullPath = Path.Combine(home, fullPath.Length > 1 && (fullPath[1] == '/' || fullPath[1] == '\\')
                ? fullPath[2..]
                : fullPath[1..]);
        }
        return Path.GetFullPath(fullPath);
    }
}
