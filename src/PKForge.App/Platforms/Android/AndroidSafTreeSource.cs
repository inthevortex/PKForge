using Android.Provider;
using PKForge.Infrastructure;
using AndroidUri = Android.Net.Uri;

namespace PKForge.App.Platforms.Android;

public sealed record ChildDocument(string DocId, string Name, bool IsDirectory, DateTimeOffset? LastModified);

/// <summary>
/// Implements <see cref="ITreeSource{TNode}"/> over Android's Storage Access Framework (SAF).
/// </summary>
public sealed class AndroidSafTreeSource : ITreeSource<ChildDocument>
{
    private AndroidUri? _treeUri;

    public AndroidSafTreeSource() { }

    public AndroidSafTreeSource(AndroidUri treeUri)
    {
        _treeUri = treeUri;
    }

    public ChildDocument Root(string treeId)
    {
        _treeUri ??= AndroidUri.Parse(treeId) ?? throw new ArgumentException("Invalid tree URI.", nameof(treeId));
        var rootDocId = DocumentsContract.GetTreeDocumentId(_treeUri)
            ?? throw new InvalidOperationException("The folder grant has no tree document id.");
        var rootName = ThreeDsSaveLayout.LastSegment(rootDocId);
        return new ChildDocument(rootDocId, rootName, true, null);
    }

    public IReadOnlyList<TreeNode<ChildDocument>> List(ChildDocument dir)
    {
        var results = new List<TreeNode<ChildDocument>>();
        if (_treeUri is null) return results;

        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(_treeUri, dir.DocId);
        if (childrenUri is null) return results;

        string[] projection =
        [
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType,
            DocumentsContract.Document.ColumnLastModified,
        ];

        using var cursor = Platform.AppContext.ContentResolver?.Query(childrenUri, projection, null, null, null);
        if (cursor is null) return results;

        // Disposing only releases the managed peer; the Java cursor stays open until Close.
        try
        {
            while (cursor.MoveToNext())
            {
                var docId = cursor.GetString(0);
                var name = cursor.GetString(1);
                if (docId is null || name is null) continue;
                var isDirectory = cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir;
                var modifiedMs = cursor.GetLong(3);
                DateTimeOffset? lastModified = modifiedMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(modifiedMs) : null;
                var child = new ChildDocument(docId, name, isDirectory, lastModified);
                results.Add(new TreeNode<ChildDocument>(name, isDirectory, lastModified, child));
            }
        }
        finally
        {
            cursor.Close();
        }

        return results;
    }

    public Stream? OpenRead(ChildDocument file)
    {
        if (_treeUri is null) return null;
        var documentUri = DocumentsContract.BuildDocumentUriUsingTree(_treeUri, file.DocId);
        if (documentUri is null) return null;
        return Platform.AppContext.ContentResolver?.OpenInputStream(documentUri);
    }

    public string DocumentId(ChildDocument file)
    {
        if (_treeUri is null) return file.DocId;
        var documentUri = DocumentsContract.BuildDocumentUriUsingTree(_treeUri, file.DocId);
        return documentUri?.ToString() ?? file.DocId;
    }

    public string PathLike(ChildDocument file) => file.DocId;

    public string RootName(ChildDocument root) => root.Name;
}
