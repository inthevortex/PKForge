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
    // Saves are small; a matching extension on a ROM/archive must not stall the scan.
    private const long MaxSaveBytes = 32 * 1024 * 1024;
    private const int MaxCacheEntries = 512;
    private const int MaxImkvdbBytes = 1024 * 1024;

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
                EmulatorKind.DraStic or EmulatorKind.PizzaBoyGba or EmulatorKind.PizzaBoyGbc or
                EmulatorKind.Dolphin or EmulatorKind.RomFolder =>
                    ScanFlatFolder(root, kind, cancellationToken, state, onSave),
                EmulatorKind.Eden => ScanEden(root, cancellationToken, state, onSave),
                EmulatorKind.Ryujinx => ScanRyujinx(root, cancellationToken, state, onSave),
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

    // RetroArch keeps ROMs apart from its saves folder, so skipping "roms" keeps a whole-folder
    // grant fast. Standalone emulators (melonDS, DraStic, Pizza Boy...) save beside the ROM.
    private static bool IsPruned(string folder, EmulatorKind kind) =>
        PrunedDirectories.Contains(folder)
        && (kind == EmulatorKind.RetroArch || !string.Equals(folder, "roms", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Battery saves and GCI files can be nested: RetroArch uses per-core folders,
    /// DraStic uses backup/, and Dolphin uses GC/&lt;region&gt;/Card A or Card B.
    /// Prefer the emulator's save subtrees when its whole data folder was granted.
    /// Direct save-folder grants and custom locations are also supported.
    /// </summary>
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
            EmulatorKind.RomFolder => [],
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

    /// <summary>
    /// Eden (Switch): saves are files named "main" (or "*.bin" for BDSP) under nand/user/save/…
    /// The user may have granted the files root, nand/, or user/ - try each prefix.
    /// A grant of save/, account/ or 0000000000000000/ itself, or of a folder holding
    /// account/ or 0000000000000000/, is walked as the save root directly.
    /// </summary>
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

    private static bool IsEdenSaveDirName(string name) => name is "save" or "account" or "0000000000000000";

    private static bool IsEdenDirectChild(string name) => name is "account" or "0000000000000000";

    private List<DetectedSave> ScanRyujinx(TNode root, CancellationToken cancellationToken,
        ScanState state, Action<DetectedSave>? onSave)
    {
        var rootName = tree.RootName(root);
        TNode? userSaveDir = null;
        Dictionary<ulong, ulong> imkvdbMap = [];

        string[][] imkvdbPaths =
        [
            ["bis", "system", "save", "8000000000000000", "0"],
            ["system", "save", "8000000000000000", "0"],
            ["8000000000000000", "0"]
        ];
        foreach (var path in imkvdbPaths)
        {
            var dir = NavigatePath(root, path, state.Trace);
            if (dir is not null)
            {
                var arcFile = tree.List(dir).FirstOrDefault(x => !x.IsDirectory && string.Equals(x.Name, "imkvdb.arc", StringComparison.OrdinalIgnoreCase));
                if (arcFile.Node is not null)
                {
                    state.Trace($"Found imkvdb.arc at {tree.DocumentId(arcFile.Node)}");
                    imkvdbMap = ReadImkvdb(arcFile.Node, state);
                    break;
                }
            }
        }

        if (string.Equals(rootName, "save", StringComparison.OrdinalIgnoreCase))
        {
            state.Trace($"Ryujinx root grant recognized by root name '{rootName}'");
            userSaveDir = root;
        }
        else
        {
            string[][] userSavePaths =
            [
                ["bis", "user", "save"],
                ["user", "save"],
                ["save"]
            ];
            foreach (var path in userSavePaths)
            {
                state.Trace($"Trying Ryujinx save prefix {string.Join('/', path)}");
                userSaveDir = NavigatePath(root, path, state.Trace);
                if (userSaveDir is not null)
                {
                    state.Trace($"User save root found via prefix {string.Join('/', path)}");
                    break;
                }
            }
        }

        userSaveDir ??= root;

        var results = new List<DetectedSave>();
        FindFilesRecursive(userSaveDir, EdenMaxDepth, cancellationToken, child =>
        {
            state.FilesSeen++;
            var pathLike = tree.PathLike(child.Node);
            if (!IsRyujinxCommittedSave(pathLike))
            {
                state.Trace($"SKIP file name={child.Name} docId={tree.DocumentId(child.Node)}");
                return;
            }

            state.Trace($"CANDIDATE name={child.Name} modified={child.LastModified?.ToString("O") ?? "unknown"} docId={tree.DocumentId(child.Node)}");
            var label = ResolveRyujinxGameLabel(pathLike, imkvdbMap);
            if (TryDetect(child, EmulatorKind.Ryujinx, label, state) is { } save)
            {
                results.Add(save);
                onSave?.Invoke(save);
            }
            else
                state.Rejected.Add(child.Name);
        }, state, diagnostic: true);

        return results;
    }

    /// <summary>
    /// Title ids only improve labels: an oversized, unreadable or corrupt index leaves the saves
    /// unlabelled rather than unscanned.
    /// </summary>
    private Dictionary<ulong, ulong> ReadImkvdb(TNode arcFile, ScanState state)
    {
        try
        {
            using var stream = tree.OpenRead(arcFile);
            if (stream is null)
            {
                state.Trace("imkvdb.arc could not be opened");
                return [];
            }
            var buffer = new byte[MaxImkvdbBytes + 1];
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (length > MaxImkvdbBytes)
            {
                state.Trace($"imkvdb.arc ignored: larger than {MaxImkvdbBytes} bytes");
                return [];
            }
            var map = RyujinxImkvdbParser.Parse(buffer.AsSpan(0, length));
            state.Trace($"Parsed {map.Count} entries from imkvdb.arc");
            return map;
        }
        catch (Exception error)
        {
            state.Trace($"imkvdb.arc unreadable: {error.Message}");
            return [];
        }
    }

    /// <summary>
    /// Only &lt;SaveDataId&gt;/0/{main, SaveData.bin, savedata.bin}: /0 is the committed copy,
    /// while /1 is a working copy Ryujinx rebuilds from /0, so edits made there are lost.
    /// </summary>
    private static bool IsRyujinxCommittedSave(string pathLike)
    {
        var parts = pathLike.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3
            && (parts[^1] is "main" or "SaveData.bin" or "savedata.bin")
            && parts[^2] == "0"
            && IsSaveDataId(parts[^3]);
    }

    private static bool IsSaveDataId(string name) =>
        name.Length == 16 && ulong.TryParse(name, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _);

    private static string ResolveRyujinxGameLabel(string pathLike, Dictionary<ulong, ulong> imkvdbMap)
    {
        var parts = pathLike.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Typical structure: .../<SaveDataId:x16>/0/main
        for (var i = parts.Length - 2; i >= 0; i--)
        {
            if (parts[i].Length == 16 && ulong.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var saveId))
            {
                if (imkvdbMap.TryGetValue(saveId, out var programId))
                {
                    var guessed = EmulatorSaveHeuristics.GuessSwitchGameLabel("/" + programId.ToString("X16") + "/main");
                    if (guessed != "Switch save") return guessed;
                }
            }
        }

        return EmulatorSaveHeuristics.GuessSwitchGameLabel(pathLike);
    }

    /// <summary>
    /// Citra-family 3DS (Azahar, Lime3DS, Citra MMJ): sdmc/Nintendo 3DS/&lt;ID0&gt;/&lt;ID1&gt;/title/00040000/&lt;game&gt;/data/00000001/(main | sav.dat),
    /// with the root resolved from whichever level the user granted (see <see cref="ThreeDsSaveLayout"/>).
    /// </summary>
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

    /// <summary>Reads a candidate's bytes and reports it only if the engine can parse them.</summary>
    private DetectedSave? TryDetect(TreeNode<TNode> child, EmulatorKind kind,
        string gameLabel, ScanState state)
    {
        try
        {
            var docId = tree.DocumentId(child.Node);
            state.Trace($"DocumentId={docId}");

            // A file already parsed at this modification time never gets re-read: rescans
            // are instant. The install epoch in the key makes each fresh APK (or desktop build)
            // re-read every file exactly once, so detection improvements (new romhack labels,
            // say) reach saves whose timestamps have not changed since the previous install.
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

            // Describe consumes a copy: the engine decrypts Switch saves in place during parsing.
            var description = engine.TryDescribe(bytes, child.Name);
            if (description is null)
            {
                state.Trace("PARSER REJECTED: ISaveEngine.TryDescribe returned null");
                // Remembering a rejection keeps big RetroArch folders fast. A 3DS or Switch save
                // is one known file per game: always re-read it, so a read that caught the
                // emulator mid-write does not hide the game until the file changes again.
                if (!EmulatorSaveHeuristics.RequiresExtraCare(kind))
                    StoreCache(cacheKey, modifiedTicks, null);
                return null;
            }
            state.Trace($"PARSER ACCEPTED game={description.GameName} generation={description.Generation} trainer={description.TrainerName} playTime={description.PlayTime}");

            // The label comes from the bytes alone; the save/ROM name is only a Rename prefill.
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
            return null; // unreadable candidates are simply not saves
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
            var next = children.FirstOrDefault(x => x.IsDirectory && x.Name == segment);
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
