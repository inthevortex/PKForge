using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Flat folder file access for desktop POSIX filesystems.
/// Tree IDs and document IDs are canonical absolute paths.
/// </summary>
public sealed class PosixFolderFileAccess(ISaveFileAccess saveFileAccess) : IFolderFileAccess
{
    public ValueTask<IReadOnlyList<PickedDocument>> ListFilesAsync(string treeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullTreePath = Path.GetFullPath(treeId);
        if (!Directory.Exists(fullTreePath))
            return ValueTask.FromResult<IReadOnlyList<PickedDocument>>([]);

        var results = new List<PickedDocument>();
        foreach (var file in Directory.EnumerateFiles(fullTreePath, "*", SearchOption.TopDirectoryOnly))
        {
            results.Add(new PickedDocument(Path.GetFullPath(file), Path.GetFileName(file)));
        }
        return ValueTask.FromResult<IReadOnlyList<PickedDocument>>(results);
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadFileAsync(string documentId, CancellationToken cancellationToken = default) =>
        saveFileAccess.ReadAsync(documentId, cancellationToken);

    public ValueTask WriteFileAsync(string treeId, string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(Path.GetFullPath(treeId), fileName);
        return saveFileAccess.WriteAtomicallyAsync(target, bytes, cancellationToken);
    }
}
