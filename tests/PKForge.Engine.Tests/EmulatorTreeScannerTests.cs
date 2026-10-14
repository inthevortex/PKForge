using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using PKHeX.Core;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class EmulatorTreeScannerTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "pkforge-scanner-test-" + Guid.NewGuid().ToString("N"));
    private readonly SaveEngine _engine = new();
    private readonly LocalDirectoryTreeSource _tree = new();

    public EmulatorTreeScannerTests()
    {
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            try
            {
                Directory.Delete(_temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class LocalDirectoryTreeSource : ITreeSource<string>
    {
        public string Root(string treeId) => treeId;
        public string RootName(string root) => Path.GetFileName(root) is { Length: > 0 } name ? name : root;
        public string DocumentId(string file) => file;
        public string PathLike(string file) => file;

        public IReadOnlyList<TreeNode<string>> List(string dir)
        {
            if (!Directory.Exists(dir)) return [];
            var list = new List<TreeNode<string>>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var isDir = Directory.Exists(entry);
                var name = Path.GetFileName(entry);
                DateTimeOffset? mtime = isDir
                    ? Directory.GetLastWriteTimeUtc(entry)
                    : File.GetLastWriteTimeUtc(entry);
                list.Add(new TreeNode<string>(name, isDir, mtime, entry));
            }
            return list;
        }

        public Stream? OpenRead(string file)
        {
            if (!File.Exists(file)) return null;
            return File.OpenRead(file);
        }
    }

    private static byte[] EmeraldSave()
    {
        var raw = new byte[0x20000];
        for (var id = 0; id < 14; id++)
        {
            var sector = raw.AsSpan(id * 0x1000);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(sector[0xFF4..], (ushort)id);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFF8..], 0x08012025);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFFC..], 1);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0xAC), 0x1234_5678);
        raw[0x900] = 1;
        var save = new SAV3E(raw);
        return save.Write().ToArray();
    }

    private static byte[] BlackSave() =>
        BlankSaveFile.Get(GameVersion.B, "PKForge", LanguageID.English).Write().ToArray();

    private static byte[] BdspSave(GameVersion version = GameVersion.BD) =>
        new SAV8BS { Version = version }.Write().ToArray();

    private static byte[] ColosseumGci()
    {
        var raw = new byte[ColoCrypto.SAVE_SIZE];
        for (var index = 0; index < 3; index++)
        {
            var slot = ColoCrypto.GetSlot(raw, index).Span;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(slot, 0x101);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(slot[4..], index + 1);
            ColoCrypto.SetChecksums(slot);
            ColoCrypto.Encrypt(slot);
        }
        var result = new byte[raw.Length + 0x40];
        result.AsSpan(0, 0x40).Fill(0x5A);
        System.Text.Encoding.ASCII.GetBytes("GC6E").CopyTo(result, 0);
        "01"u8.CopyTo(result.AsSpan(4));
        raw.CopyTo(result, 0x40);
        return result;
    }

    [Fact]
    public void RetroArchPrunesRomsDirectoryAndNonSaveFolders()
    {
        var root = Path.Combine(_temp, "retroarch");
        var savesDir = Path.Combine(root, "saves");
        var romsDir = Path.Combine(root, "roms");
        var statesDir = Path.Combine(root, "states");

        Directory.CreateDirectory(savesDir);
        Directory.CreateDirectory(romsDir);
        Directory.CreateDirectory(statesDir);

        File.WriteAllBytes(Path.Combine(savesDir, "Pokemon Emerald.srm"), EmeraldSave());
        File.WriteAllBytes(Path.Combine(romsDir, "Pokemon FireRed.srm"), EmeraldSave());
        File.WriteAllBytes(Path.Combine(statesDir, "Pokemon Emerald.state"), EmeraldSave());

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(root, EmulatorKind.RetroArch);

        var detected = Assert.Single(result.Saves);
        Assert.Equal("Pokemon Emerald.srm", detected.FileName);
        Assert.Contains("retroarch", detected.DocumentId);
        Assert.DoesNotContain(result.Saves, s => s.DocumentId.Contains("/roms/"));
        Assert.DoesNotContain(result.Saves, s => s.DocumentId.Contains("/states/"));
    }

    [Fact]
    public void MelonDsDoesNotPruneRomsDirectory()
    {
        var root = Path.Combine(_temp, "melonds");
        var romsDir = Path.Combine(root, "roms");
        Directory.CreateDirectory(romsDir);

        File.WriteAllBytes(Path.Combine(romsDir, "Pokemon Black.sav"), BlackSave());

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(root, EmulatorKind.MelonDS);

        var detected = Assert.Single(result.Saves);
        Assert.Equal("Pokemon Black.sav", detected.FileName);
        Assert.Contains("/roms/", detected.DocumentId.Replace('\\', '/'));
    }

    [Fact]
    public void SiblingRomProvidesRomNameHint()
    {
        var root = Path.Combine(_temp, "gba");
        Directory.CreateDirectory(root);

        File.WriteAllBytes(Path.Combine(root, "Pokemon Emerald (USA).sav"), EmeraldSave());
        File.WriteAllBytes(Path.Combine(root, "Pokemon Emerald (USA).gba"), [0, 1, 2]);

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(root, EmulatorKind.PizzaBoyGba);

        var detected = Assert.Single(result.Saves);
        Assert.Equal("Pokemon Emerald (USA).gba", detected.RomFileName);
    }

    [Fact]
    public void DolphinPrefersGcFolderAndGuidesRawMemoryCards()
    {
        var root = Path.Combine(_temp, "dolphin");
        var gcDir = Path.Combine(root, "GC");
        Directory.CreateDirectory(gcDir);

        File.WriteAllBytes(Path.Combine(gcDir, "01-GC6E-PokemonColosseum.gci"), ColosseumGci());
        File.WriteAllBytes(Path.Combine(gcDir, "MemoryCardA.USA.raw"), new byte[0x200000]);

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(root, EmulatorKind.Dolphin);

        var detected = Assert.Single(result.Saves);
        Assert.Equal("01-GC6E-PokemonColosseum.gci", detected.FileName);
        Assert.Contains("Colosseum", detected.GameLabel);

        Assert.Contains(result.RejectedCandidates, r =>
            r.Contains("MemoryCardA.USA.raw") && r.Contains("export Colosseum/XD as GCI"));
    }

    [Fact]
    public void EdenPrefixSegmentsMatchCaseSensitively()
    {
        var root = Path.Combine(_temp, "eden_case");
        var slot = Path.Combine(root, "NAND", "User", "Save", "0000000000000000", "0000000000000001", "0100000011D90000");
        Directory.CreateDirectory(slot);
        File.WriteAllBytes(Path.Combine(slot, "main"), BdspSave(GameVersion.BD));

        var result = new EmulatorTreeScanner<string>(_engine, _tree).Scan(root, EmulatorKind.Eden);

        Assert.Empty(result.Saves);
        Assert.Contains(result.Diagnostics!, d => d.Contains("Missing directory segment 'nand'"));
    }

    [Fact]
    public void CacheHitsOnMatchingMtimeAndMissesOnModificationOrEpochChange()
    {
        var root = Path.Combine(_temp, "cache_test");
        Directory.CreateDirectory(root);
        var savePath = Path.Combine(root, "Pokemon Emerald.sav");
        File.WriteAllBytes(savePath, EmeraldSave());

        var cacheStore = new MemoryScanCacheStore();
        var epoch = 100L;

        var scanner1 = new EmulatorTreeScanner<string>(_engine, _tree, cacheStore, buildEpoch: epoch);
        var result1 = scanner1.Scan(root, EmulatorKind.PizzaBoyGba);
        Assert.Single(result1.Saves);

        var scanner2 = new EmulatorTreeScanner<string>(_engine, _tree, cacheStore, buildEpoch: epoch);
        var result2 = scanner2.Scan(root, EmulatorKind.PizzaBoyGba);
        Assert.Single(result2.Saves);
        Assert.NotNull(result2.Diagnostics);
        Assert.Contains(result2.Diagnostics!, d => d.Contains("CACHED"));

        File.SetLastWriteTimeUtc(savePath, DateTime.UtcNow.AddMinutes(5));
        var scanner3 = new EmulatorTreeScanner<string>(_engine, _tree, cacheStore, buildEpoch: epoch);
        var result3 = scanner3.Scan(root, EmulatorKind.PizzaBoyGba);
        Assert.Single(result3.Saves);
        Assert.NotNull(result3.Diagnostics);
        Assert.DoesNotContain(result3.Diagnostics!, d => d.Contains("CACHED"));

        var scanner4 = new EmulatorTreeScanner<string>(_engine, _tree, cacheStore, buildEpoch: epoch + 1);
        var result4 = scanner4.Scan(root, EmulatorKind.PizzaBoyGba);
        Assert.Single(result4.Saves);
        Assert.NotNull(result4.Diagnostics);
        Assert.DoesNotContain(result4.Diagnostics!, d => d.Contains("CACHED"));
    }

    [Fact]
    public void NegativeCacheSkipsUnchangedNonSaves()
    {
        var root = Path.Combine(_temp, "neg_cache_test");
        Directory.CreateDirectory(root);
        var bogusPath = Path.Combine(root, "corrupted.sav");
        File.WriteAllBytes(bogusPath, [1, 2, 3, 4, 5]);

        var cacheStore = new MemoryScanCacheStore();
        var scanner1 = new EmulatorTreeScanner<string>(_engine, _tree, cacheStore);
        var res1 = scanner1.Scan(root, EmulatorKind.MelonDS);
        Assert.Empty(res1.Saves);
        Assert.Contains("corrupted.sav", res1.RejectedCandidates);

        var scanner2 = new EmulatorTreeScanner<string>(_engine, _tree, cacheStore);
        var res2 = scanner2.Scan(root, EmulatorKind.MelonDS);
        Assert.Empty(res2.Saves);
        Assert.NotNull(res2.Diagnostics);
        Assert.Contains(res2.Diagnostics!, d => d.Contains("CACHED rejection"));
    }

    [Fact]
    public async Task ScanIncrementalStreamsResultsAndEndsWithCompletionUpdate()
    {
        var root = Path.Combine(_temp, "incremental_test");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "emerald.sav"), EmeraldSave());
        File.WriteAllBytes(Path.Combine(root, "black.dsv"), BlackSave());

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var updates = new List<EmulatorScanUpdate>();

        await foreach (var update in scanner.ScanIncrementalAsync(root, EmulatorKind.DraStic))
        {
            updates.Add(update);
        }

        Assert.True(updates.Count >= 2);
        var saveUpdates = updates.Where(u => !u.IsComplete).ToList();
        var finalUpdate = updates.Last();

        Assert.True(finalUpdate.IsComplete);
        Assert.Null(finalUpdate.Save);
        Assert.Equal(2, finalUpdate.SavesFound);
        Assert.Equal(2, saveUpdates.Count);
    }
}
