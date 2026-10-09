using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// Platform-independent engine for scanning an emulator directory tree for Pokémon saves.
/// File-name filters are only a pre-filter; candidate bytes must pass engine validation.
/// </summary>
public sealed class EmulatorTreeScanner<TNode>(
    ISaveEngine engine,
    ITreeSource<TNode> tree,
    IScanCacheStore? cacheStore = null,
    long buildEpoch = 0) where TNode : class
{
    private const int MaxDepth = 8;
    private const int EdenMaxDepth = 8;
    private const long MaxSaveBytes = 32 * 1024 * 1024;
    private const int MaxCacheEntries = 512;

    private readonly IScanCacheStore? _cacheStore = cacheStore;
    private readonly long _buildEpoch = buildEpoch;
    private Dictionary<string, ScanCacheEntry>? _cacheEntries;
    private readonly Lock _cacheGate = new();
    private bool _cacheDirty;

    private sealed class ScanState
    {
        public int FilesSeen;
        /// <summary>The emulator this root was linked for; decides which folders are pruned.</summary>
        public EmulatorKind Kind;
        /// <summary>Names in the folder currently being walked, for ROM-name hints.</summary>
        public IReadOnlyList<string>? Siblings;
        public readonly List<string> Rejected = [];
        public readonly List<string> Diagnostics = [];

        public void Trace(string message)
        {
            const int maxLines = 2000;
            if (Diagnostics.Count < maxLines)
                Diagnostics.Add(message);
            else if (Diagnostics.Count == maxLines)
                Diagnostics.Add("DIAGNOSTICS TRUNCATED after 2000 lines");
        }
    }

    public EmulatorScanResult Scan(string treeId, EmulatorKind kind,
        CancellationToken cancellationToken = default, Action<DetectedSave>? onSave = null)
    {
        var root = tree.Root(treeId);
        var state = new ScanState { Kind = kind };
        state.Trace($"Scanner={nameof(EmulatorTreeScanner<TNode>)} kind={kind}");
        state.Trace($"TreeId={treeId}");
        state.Trace($"RootName={tree.RootName(root)}");
        List<DetectedSave> found;
        try
        {
            found = kind switch
            {
                EmulatorKind.RetroArch or EmulatorKind.MelonDS or EmulatorKind.Linkboy or
                EmulatorKind.DraStic or EmulatorKind.PizzaBoyGba or EmulatorKind.PizzaBoyGbc or EmulatorKind.Dolphin =>
                    ScanFlatFolder(root, kind, cancellationToken, state, onSave),
                EmulatorKind.Eden => ScanEden(root, cancellationToken, state, onSave),
                EmulatorKind.Azahar or EmulatorKind.CitraMmj => ScanThreeDs(root, kind, cancellationToken, state, onSave),
                _ => [],
            };
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException)
                throw;
            state.Trace($"SCAN EXCEPTION {error}");
            found = [];
        }
        state.Trace($"Scanner complete files={state.FilesSeen} saves={found.Count} rejected={state.Rejected.Count}");
        Flush();
        return new EmulatorScanResult(EmulatorSaveHeuristics.Normalize(found), state.FilesSeen,
            [.. state.Rejected], [.. state.Diagnostics]);
    }

    public async IAsyncEnumerable<EmulatorScanUpdate> ScanIncrementalAsync(
        string treeId, EmulatorKind kind,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<EmulatorScanUpdate>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
        var producer = Task.Run(() =>
        {
            try
            {
                var result = Scan(treeId, kind, cancellationToken,
                    save => channel.Writer.TryWrite(new EmulatorScanUpdate(save, false)));
                channel.Writer.TryWrite(new EmulatorScanUpdate(
                    null, true, result.FilesSeen, result.Saves.Count,
                    result.RejectedCandidates, result.Diagnostics));
                Flush();
                channel.Writer.TryComplete();
            }
            catch (Exception error)
            {
                channel.Writer.TryComplete(error);
            }
        });

        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            yield return update;
        await producer;
    }

    /// <summary>Folders whose contents can never be saves - pruned so a whole-RetroArch grant stays fast.</summary>
    private static readonly HashSet<string> PrunedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets", "autoconfig", "cheats", "config", "cores", "database", "downloads", "filters",
        "info", "logs", "overlays", "playlists", "remaps", "screenshots", "shaders", "states",
        "system", "thumbnails", "cache", "temp", "roms", "shader_cache",
    };

    private static bool IsPruned(string folder, EmulatorKind kind) =>
        PrunedDirectories.Contains(folder)
        && (kind == EmulatorKind.RetroArch || !string.Equals(folder, "roms", StringComparison.OrdinalIgnoreCase));

    private List<DetectedSave> ScanFlatFolder(TNode root, EmulatorKind kind,
        CancellationToken cancellationToken, ScanState state, Action<DetectedSave>? onSave)
    {
        var results = new List<DetectedSave>();
        void OnFile(TreeNode<TNode> child)
        {
            state.FilesSeen++;
            if (kind == EmulatorKind.Dolphin && EmulatorSaveHeuristics.IsDolphinMemoryCard(child.Name))
            {
                state.Rejected.Add($"{child.Name}: export Colosseum/XD as GCI with Dolphin's Memory Card Manager, or use GCI Folder for the card slot.");
                state.Trace($"RAW MEMORY CARD {child.Name}: individual GCI export required");
                return;
            }
            if (!EmulatorSaveHeuristics.IsCandidateFileName(child.Name, kind)) return;
            if (TryDetect(child, kind, gameLabel: child.Name, state: state) is { } save)
            {
                results.Add(save);
                onSave?.Invoke(save);
            }
            else
                state.Rejected.Add(child.Name);
        }

        var rootChildren = tree.List(root);
        string[] preferredFolders = kind switch
        {
            EmulatorKind.Dolphin => ["GC"],
            EmulatorKind.DraStic => ["backup"],
            EmulatorKind.PizzaBoyGba or EmulatorKind.PizzaBoyGbc => ["save", "saves"],
            _ => ["saves"],
        };
        var savesDirs = rootChildren.Where(x => x.IsDirectory && preferredFolders.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (savesDirs.Length > 0)
        {
            state.Siblings = rootChildren.Where(x => !x.IsDirectory).Select(x => x.Name).ToArray();
            foreach (var file in rootChildren.Where(x => !x.IsDirectory))
                OnFile(file);
            foreach (var savesDir in savesDirs)
                FindFilesRecursive(savesDir.Node, MaxDepth, cancellationToken, OnFile, state);
        }
        else
        {
            FindFilesRecursive(root, MaxDepth, cancellationToken, OnFile, state);
        }
        return results;
    }

    private List<DetectedSave> ScanEden(TNode root, CancellationToken cancellationToken,
        ScanState state, Action<DetectedSave>? onSave)
    {
        var rootName = tree.RootName(root);
        TNode? saveDir = null;

        if (IsEdenSaveDirName(rootName))
        {
            state.Trace($"Eden root grant recognized by root name '{rootName}'");
            saveDir = root;
        }
        else
        {
            var rootChildren = tree.List(root);
            if (rootChildren.Any(x => x.IsDirectory && IsEdenDirectChild(x.Name)))
            {
                state.Trace("Eden root recognized: root contains account or 0000000000000000 directory");
                saveDir = root;
            }
            else
            {
                string[][] prefixes = [["nand", "user", "save"], ["user", "save"], ["save"]];
                foreach (var prefix in prefixes)
                {
                    state.Trace($"Trying Eden prefix {string.Join('/', prefix)}");
                    saveDir = NavigatePath(root, prefix, state.Trace);
                    state.Trace(saveDir is null ? "Prefix not found" : $"Save root found via prefix {string.Join('/', prefix)}");
                    if (saveDir is not null)
                        break;
                }
            }
        }

        if (saveDir is null)
        {
            state.Trace("EDEN FAILURE: none of nand/user/save, user/save, or save exists under the granted root");
            return [];
        }

        var results = new List<DetectedSave>();
        FindFilesRecursive(saveDir, EdenMaxDepth, cancellationToken, child =>
        {
            state.FilesSeen++;
            if (!EmulatorSaveHeuristics.IsEdenSaveFileName(child.Name))
            {
                state.Trace($"SKIP file name={child.Name} docId={tree.DocumentId(child.Node)}");
                return;
            }
            state.Trace($"CANDIDATE name={child.Name} modified={child.LastModified?.ToString("O") ?? "unknown"} docId={tree.DocumentId(child.Node)}");
            var label = EmulatorSaveHeuristics.GuessSwitchGameLabel(tree.PathLike(child.Node));
            if (TryDetect(child, EmulatorKind.Eden, label, state) is { } save)
            {
                results.Add(save);
                onSave?.Invoke(save);
            }
            else
                state.Rejected.Add(child.Name);
        }, state, diagnostic: true);
        return results;
    }

    private static bool IsEdenSaveDirName(string name) =>
        string.Equals(name, "save", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "account", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "0000000000000000", StringComparison.OrdinalIgnoreCase);

    private static bool IsEdenDirectChild(string name) =>
        string.Equals(name, "account", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "0000000000000000", StringComparison.OrdinalIgnoreCase);

    private List<DetectedSave> ScanThreeDs(TNode root, EmulatorKind kind,
        CancellationToken cancellationToken, ScanState state, Action<DetectedSave>? onSave)
    {
        var results = new List<DetectedSave>();
        var nodes = new Dictionary<TNode, TreeNode<TNode>>();

        IEnumerable<TreeEntry<TNode>> List(TNode doc)
        {
            var children = tree.List(doc);
            foreach (var child in children)
                nodes[child.Node] = child;
            return children.Select(x => new TreeEntry<TNode>(x.Name, x.IsDirectory, x.Node));
        }

        var rootName = tree.RootName(root);
        foreach (var hit in ThreeDsSaveLayout.FindMainSaves(root, rootName, List, state.Trace, cancellationToken))
        {
            state.FilesSeen++;
            var mainNode = hit.File;
            if (!nodes.TryGetValue(mainNode, out var treeNode))
            {
                var fileName = ThreeDsSaveLayout.LastSegment(tree.DocumentId(mainNode));
                if (string.IsNullOrEmpty(fileName)) fileName = "main";
                treeNode = new TreeNode<TNode>(fileName, false, null, mainNode);
            }

            if (TryDetect(treeNode, kind, gameLabel: $"3DS save ({hit.TitleId})", state: state) is { } save)
            {
                results.Add(save);
                onSave?.Invoke(save);
            }
            else
                state.Rejected.Add($"3DS save {hit.TitleId}");
        }
        return results;
    }

    private DetectedSave? TryDetect(TreeNode<TNode> child, EmulatorKind kind,
        string gameLabel, ScanState state)
    {
        try
        {
            var docId = tree.DocumentId(child.Node);
            state.Trace($"DocumentId={docId}");

            var cacheKey = docId + "#" + kind + "#" + _buildEpoch;
            var modifiedTicks = child.LastModified?.UtcTicks ?? 0;
            if (TryGetCached(cacheKey, modifiedTicks, out var cached))
            {
                state.Trace(cached is null ? "CACHED rejection (unchanged since it last failed to parse)" : $"CACHED {cached.GameLabel}");
                return cached;
            }

            using var stream = tree.OpenRead(child.Node);
            if (stream is null)
            {
                state.Trace("REJECT OpenRead returned null");
                return null;
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxSaveBytes)
                {
                    state.Trace($"REJECT larger than {MaxSaveBytes} bytes");
                    return null;
                }
            }
            if (buffer.Length == 0)
            {
                state.Trace("REJECT zero-byte file");
                return null;
            }

            var bytes = buffer.ToArray();
            var headerLength = Math.Min(16, bytes.Length);
            var bdspSize = bytes.Length is 956456 or 973856 or 978316 or 979108;
            state.Trace($"BYTES length={bytes.Length} header16={Convert.ToHexString(bytes.AsSpan(0, headerLength))} knownBdspSize={bdspSize}");

            var description = engine.TryDescribe(bytes, child.Name);
            if (description is null)
            {
                state.Trace("PARSER REJECTED: ISaveEngine.TryDescribe returned null");
                if (!EmulatorSaveHeuristics.RequiresExtraCare(kind))
                    StoreCache(cacheKey, modifiedTicks, null);
                return null;
            }
            state.Trace($"PARSER ACCEPTED game={description.GameName} generation={description.Generation} trainer={description.TrainerName} playTime={description.PlayTime}");

            var romName = EmulatorSaveHeuristics.FindSiblingRom(child.Name, state.Siblings);
            var guess = SaveIdentityRules.Guess(description.GameName, description.Generation, child.Name, romName, gameLabel);
            state.Trace($"IDENTITY label={guess.Label} family={guess.Family} format={guess.Format} language={description.Language ?? "-"} rom={romName ?? "-"}");
            var detected = new DetectedSave(
                docId,
                child.Name,
                guess.Label,
                kind,
                EmulatorSaveHeuristics.RequiresExtraCare(kind),
                child.LastModified,
                description.Generation,
                description.TrainerName,
                description.PlayTime,
                guess,
                romName,
                EmulatorSaveHeuristics.FolderHint(tree.PathLike(child.Node)),
                Language: description.Language);
            StoreCache(cacheKey, modifiedTicks, detected);
            return detected;
        }
        catch (Exception error)
        {
            state.Trace($"CANDIDATE EXCEPTION {error}");
            return null;
        }
    }

    private void FindFilesRecursive(TNode parentNode, int maxDepth,
        CancellationToken cancellationToken, Action<TreeNode<TNode>> onFile, ScanState state,
        bool diagnostic = false)
    {
        if (maxDepth <= 0)
        {
            if (diagnostic) state.Trace($"DEPTH LIMIT reached at {tree.RootName(parentNode)}");
            return;
        }
        var children = tree.List(parentNode);
        var siblings = children.Where(x => !x.IsDirectory).Select(x => x.Name).ToArray();
        if (diagnostic) state.Trace($"WALK depthRemaining={maxDepth} parent={tree.RootName(parentNode)} children={children.Count}");
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.IsDirectory)
            {
                if (diagnostic) state.Trace($"DIR name={child.Name}");
                if (IsPruned(child.Name, state.Kind))
                {
                    if (diagnostic) state.Trace($"PRUNED directory {child.Name}");
                    continue;
                }
                FindFilesRecursive(child.Node, maxDepth - 1, cancellationToken, onFile, state, diagnostic);
            }
            else
            {
                state.Siblings = siblings;
                onFile(child);
            }
        }
    }

    private TNode? NavigatePath(TNode fromNode, string[] segments, Action<string>? trace = null)
    {
        var current = fromNode;
        foreach (var segment in segments)
        {
            var children = tree.List(current);
            trace?.Invoke($"At {tree.RootName(current)}: {children.Count} children [{string.Join(", ", children.Select(x => x.IsDirectory ? x.Name + "/" : x.Name))}]");
            var next = children.FirstOrDefault(x => x.IsDirectory && string.Equals(x.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (next.Node is null)
            {
                trace?.Invoke($"Missing directory segment '{segment}'");
                return null;
            }
            trace?.Invoke($"Matched '{segment}' -> {next.Name}");
            current = next.Node;
        }
        return current;
    }

    private bool TryGetCached(string key, long modifiedTicks, out DetectedSave? save)
    {
        if (_cacheStore is null || modifiedTicks == 0)
        {
            save = null;
            return false;
        }

        lock (_cacheGate)
        {
            EnsureCacheLoaded();
            if (_cacheEntries!.TryGetValue(key, out var entry) && entry.ModifiedTicks == modifiedTicks)
            {
                save = entry.Save;
                return true;
            }
        }
        save = null;
        return false;
    }

    private void StoreCache(string key, long modifiedTicks, DetectedSave? save)
    {
        if (_cacheStore is null || modifiedTicks == 0) return;

        lock (_cacheGate)
        {
            EnsureCacheLoaded();
            _cacheEntries![key] = new ScanCacheEntry(modifiedTicks, save);
            while (_cacheEntries.Count > MaxCacheEntries)
                _cacheEntries.Remove(_cacheEntries.Keys.First());
            _cacheDirty = true;
        }
    }

    public void Flush()
    {
        if (_cacheStore is null) return;
        lock (_cacheGate)
        {
            if (!_cacheDirty || _cacheEntries is null) return;
            _cacheStore.Save(_cacheEntries);
            _cacheDirty = false;
        }
    }

    private void EnsureCacheLoaded()
    {
        if (_cacheEntries is not null) return;
        try
        {
            _cacheEntries = _cacheStore?.Load() ?? [];
        }
        catch
        {
            _cacheEntries = [];
        }
    }
}
