namespace PKForge.Infrastructure;

public readonly record struct TreeNode<TNode>(string Name, bool IsDirectory, DateTimeOffset? LastModified, TNode Node);

public interface ITreeSource<TNode>
{
    TNode Root(string treeId);
    IReadOnlyList<TreeNode<TNode>> List(TNode dir);
    Stream? OpenRead(TNode file);
    string DocumentId(TNode file);
    string PathLike(TNode file);
    string RootName(TNode root);
}
