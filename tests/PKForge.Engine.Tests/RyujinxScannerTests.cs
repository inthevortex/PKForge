using PKForge.Domain;
using PKForge.Infrastructure;
using PKHeX.Core;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class RyujinxScannerTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "pkforge-ryujinx-test-" + Guid.NewGuid().ToString("N"));
    private readonly SaveEngine _engine = new();
    private readonly LocalTreeSource _tree = new();

    public RyujinxScannerTests()
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
                // Best-effort cleanup
            }
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

    private static byte[] BdspSave(GameVersion version = GameVersion.BD) =>
        new SAV8BS { Version = version }.Write().ToArray();

    [Fact]
    public void ImkvdbParser_ParsesValidArcAndBuildsRoundtrip()
    {
        var mapping = new Dictionary<ulong, ulong>
        {
            [0x0000000000000001UL] = 0x0100000011D90000UL, // BD
            [0x0000000000000002UL] = 0x010018E011D92000UL, // SP
            [0x0000000000000003UL] = 0x0100A3D008C5C000UL, // Scarlet
        };

        var bytes = RyujinxImkvdbParser.Build(mapping);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        using var ms = new MemoryStream(bytes);
        var parsed = RyujinxImkvdbParser.Parse(ms);

        Assert.Equal(3, parsed.Count);
        Assert.Equal(0x0100000011D90000UL, parsed[1UL]);
        Assert.Equal(0x010018E011D92000UL, parsed[2UL]);
        Assert.Equal(0x0100A3D008C5C000UL, parsed[3UL]);
    }

    [Fact]
    public void ImkvdbParser_HandlesEmptyAndCorruptedStreamsGracefully()
    {
        using var emptyMs = new MemoryStream([]);
        var empty = RyujinxImkvdbParser.Parse(emptyMs);
        Assert.Empty(empty);

        using var corruptMs = new MemoryStream(new byte[128]);
        var corrupt = RyujinxImkvdbParser.Parse(corruptMs);
        Assert.Empty(corrupt);

        using var garbageMs = new MemoryStream("NOT_IMKV_HEADER_DATA"u8.ToArray());
        var garbage = RyujinxImkvdbParser.Parse(garbageMs);
        Assert.Empty(garbage);
    }

    [Fact]
    public void ImkvdbParser_ReadsRealEmptyArchive()
    {
        byte[] realEmpty = [0x49, 0x4D, 0x4B, 0x56, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.Empty(RyujinxImkvdbParser.Parse(realEmpty));
        Assert.Equal(realEmpty, RyujinxImkvdbParser.Build([]));
    }

    [Fact]
    public void ImkvdbParser_ParsesHandBuiltArchiveWithRealLayout()
    {
        var arc = ImkvdbArchive(2,
            (ProgramId: 0x0100000011D90000UL, SaveDataId: 0x0000000000000011UL),
            (ProgramId: 0x0100A3D008C5C000UL, SaveDataId: 0x0000000000000012UL));

        var parsed = RyujinxImkvdbParser.Parse(arc);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(0x0100000011D90000UL, parsed[0x11UL]);
        Assert.Equal(0x0100A3D008C5C000UL, parsed[0x12UL]);
    }

    [Fact]
    public void ImkvdbParser_ClampsDeclaredCountToTheEntriesPresent()
    {
        var arc = ImkvdbArchive(int.MaxValue, (ProgramId: 0x010018E011D92000UL, SaveDataId: 7UL));

        var parsed = RyujinxImkvdbParser.Parse(arc);

        Assert.Equal(0x010018E011D92000UL, Assert.Single(parsed).Value);
    }

    [Fact]
    public void ImkvdbParser_StopsOnEntrySizesThatOverflow()
    {
        var arc = ImkvdbArchive(1, (ProgramId: 0x010018E011D92000UL, SaveDataId: 7UL));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(arc.AsSpan(12 + 4), int.MaxValue);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(arc.AsSpan(12 + 8), int.MaxValue);

        Assert.Empty(RyujinxImkvdbParser.Parse(arc));
    }

    [Fact]
    public void ScanRyujinx_LabelsSaveFromTitleInImkvdbArchive()
    {
        var root = Path.Combine(_temp, "ryujinx_titles");
        var saveSlot = Path.Combine(root, "bis", "user", "save", "0000000000000021", "0");
        var metaDir = Path.Combine(root, "bis", "system", "save", "8000000000000000", "0");
        Directory.CreateDirectory(saveSlot);
        Directory.CreateDirectory(metaDir);
        File.WriteAllBytes(Path.Combine(saveSlot, "main"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(metaDir, "imkvdb.arc"),
            ImkvdbArchive(1, (ProgramId: 0x010018E011D92000UL, SaveDataId: 0x21UL)));

        var scanner = new EmulatorTreeScanner<string>(new UnnamedSaveEngine(), _tree);
        var result = scanner.Scan(root, EmulatorKind.Ryujinx);

        Assert.Equal("Shining Pearl", Assert.Single(result.Saves).GameLabel);
    }

    [Fact]
    public void ScanRyujinx_AcceptsOnlyTheCommittedCopyOfEachSave()
    {
        var saveRoot = Path.Combine(_temp, "bis", "user", "save");
        var committed = Path.Combine(saveRoot, "0000000000000031", "0");
        var working = Path.Combine(saveRoot, "0000000000000031", "1");
        var bdspCommitted = Path.Combine(saveRoot, "0000000000000032", "0");
        var stray = Path.Combine(saveRoot, "0000000000000033", "backup");
        var notAnId = Path.Combine(saveRoot, "settings", "0");
        foreach (var dir in new[] { committed, working, bdspCommitted, stray, notAnId })
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(committed, "main"), [1]);
        File.WriteAllBytes(Path.Combine(committed, "extra.bin"), [1]);
        File.WriteAllBytes(Path.Combine(working, "main"), [1]);
        File.WriteAllBytes(Path.Combine(bdspCommitted, "SaveData.bin"), [1]);
        File.WriteAllBytes(Path.Combine(stray, "main"), [1]);
        File.WriteAllBytes(Path.Combine(notAnId, "main"), [1]);

        var scanner = new EmulatorTreeScanner<string>(new UnnamedSaveEngine(), _tree);
        var result = scanner.Scan(saveRoot, EmulatorKind.Ryujinx);

        Assert.Equal(
            [Path.Combine(committed, "main"), Path.Combine(bdspCommitted, "SaveData.bin")],
            result.Saves.Select(s => s.DocumentId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ScanRyujinx_IgnoresAnOversizedImkvdbArchive()
    {
        var root = CreateRyujinxRootWithOneSave("ryujinx_big_index", out var metaDir);
        var arc = ImkvdbArchive(1, (ProgramId: 0x010018E011D92000UL, SaveDataId: 0x41UL));
        Array.Resize(ref arc, 1024 * 1024 + 1);
        File.WriteAllBytes(Path.Combine(metaDir, "imkvdb.arc"), arc);

        var result = new EmulatorTreeScanner<string>(new UnnamedSaveEngine(), _tree).Scan(root, EmulatorKind.Ryujinx);

        Assert.NotEqual("Shining Pearl", Assert.Single(result.Saves).GameLabel);
        Assert.Contains(result.Diagnostics!, d => d.Contains("imkvdb.arc ignored"));
    }

    [Fact]
    public void ScanRyujinx_KeepsScanningWhenTheImkvdbArchiveCannotBeRead()
    {
        var root = CreateRyujinxRootWithOneSave("ryujinx_bad_index", out var metaDir);
        File.WriteAllBytes(Path.Combine(metaDir, "imkvdb.arc"),
            ImkvdbArchive(1, (ProgramId: 0x010018E011D92000UL, SaveDataId: 0x41UL)));

        var tree = new ThrowingOnImkvdbTreeSource(_tree);
        var result = new EmulatorTreeScanner<string>(new UnnamedSaveEngine(), tree).Scan(root, EmulatorKind.Ryujinx);

        Assert.Single(result.Saves);
        Assert.Contains(result.Diagnostics!, d => d.Contains("imkvdb.arc unreadable"));
    }

    [Fact]
    public async Task ScanRyujinx_DoesNotBlockOnAnImkvdbFifo()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = CreateRyujinxRootWithOneSave("ryujinx_fifo_index", out var metaDir);
        Assert.Equal(0, MakeFifo(Path.Combine(metaDir, "imkvdb.arc"), 0x180));

        var scan = Task.Run(() => new EmulatorTreeScanner<string>(new UnnamedSaveEngine(), _tree).Scan(root, EmulatorKind.Ryujinx));
        Assert.Same(scan, await Task.WhenAny(scan, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.Single((await scan).Saves);
    }

    private string CreateRyujinxRootWithOneSave(string name, out string metaDir)
    {
        var root = Path.Combine(_temp, name);
        var saveSlot = Path.Combine(root, "bis", "user", "save", "0000000000000041", "0");
        metaDir = Path.Combine(root, "bis", "system", "save", "8000000000000000", "0");
        Directory.CreateDirectory(saveSlot);
        Directory.CreateDirectory(metaDir);
        File.WriteAllBytes(Path.Combine(saveSlot, "main"), [1, 2, 3, 4]);
        return root;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path, uint mode);

    private sealed class ThrowingOnImkvdbTreeSource(LocalTreeSource inner) : ITreeSource<string>
    {
        public string Root(string treeId) => inner.Root(treeId);
        public IReadOnlyList<TreeNode<string>> List(string dir) => inner.List(dir);
        public string DocumentId(string file) => inner.DocumentId(file);
        public string PathLike(string file) => inner.PathLike(file);
        public string RootName(string root) => inner.RootName(root);

        public Stream? OpenRead(string file) =>
            Path.GetFileName(file) == "imkvdb.arc" ? throw new IOException("Input/output error") : inner.OpenRead(file);
    }

    /// <summary>
    /// An archive in the layout Ryujinx writes, built byte by byte so it does not depend on
    /// <see cref="RyujinxImkvdbParser.Build"/>. Keys and values carry non-zero filler past
    /// their ids, as real SaveDataAttribute and SaveDataIndexerValue records do.
    /// </summary>
    private static byte[] ImkvdbArchive(int declaredCount, params (ulong ProgramId, ulong SaveDataId)[] entries)
    {
        var bytes = new byte[12 + entries.Length * (12 + 0x40 + 0x40)];
        var span = bytes.AsSpan();
        "IMKV"u8.CopyTo(span);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[8..], declaredCount);

        var offset = 12;
        foreach (var (programId, saveDataId) in entries)
        {
            "IMEN"u8.CopyTo(span[offset..]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 4)..], 0x40);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], 0x40);
            offset += 12;

            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span[offset..], programId);
            span.Slice(offset + 8, 0x38).Fill(0xAB);
            offset += 0x40;

            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span[offset..], saveDataId);
            span.Slice(offset + 8, 0x38).Fill(0xCD);
            offset += 0x40;
        }

        return bytes;
    }

    /// <summary>Accepts any bytes as a save the engine cannot name, so the scanner's own label shows through.</summary>
    private sealed class UnnamedSaveEngine : ISaveEngine
    {
        public SaveDescription? TryDescribe(ReadOnlyMemory<byte> bytes, string? displayName = null) =>
            new(string.Empty, 8, "Trainer", "0:00");

        public SaveSnapshot Open(ReadOnlyMemory<byte> bytes, string? displayName = null) => throw new NotSupportedException();
        public ISaveEngineSession OpenSession(ReadOnlyMemory<byte> bytes, string? displayName = null) => throw new NotSupportedException();
        public ReadOnlyMemory<byte> Serialize(SaveSnapshot snapshot) => throw new NotSupportedException();
        public bool Validate(ReadOnlyMemory<byte> bytes) => throw new NotSupportedException();
        public BankEntryInfo? TryDescribeEntity(byte[] bytes, string sourceName, string? format = null) => throw new NotSupportedException();
        public ISaveEngineSession? OpenEntitySession(byte[] entityBytes, string? displayName = null, string? format = null) => throw new NotSupportedException();
        public ISaveEngineSession OpenBlankSession(int generation, string? displayName = null) => throw new NotSupportedException();
    }

    [Fact]
    public void ScanRyujinx_DiscoversSavesInUserSaveDirectoryWithImkvdbLabels()
    {
        var root = Path.Combine(_temp, "ryujinx_app");
        var saveSlot = Path.Combine(root, "bis", "user", "save", "0000000000000001", "0");
        var metaDir = Path.Combine(root, "bis", "system", "save", "8000000000000000", "0");

        Directory.CreateDirectory(saveSlot);
        Directory.CreateDirectory(metaDir);

        File.WriteAllBytes(Path.Combine(saveSlot, "main"), BdspSave(GameVersion.BD));

        var imkvdbBytes = RyujinxImkvdbParser.Build(new Dictionary<ulong, ulong>
        {
            [1UL] = 0x0100000011D90000UL // BD Title ID
        });
        File.WriteAllBytes(Path.Combine(metaDir, "imkvdb.arc"), imkvdbBytes);

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(root, EmulatorKind.Ryujinx);

        var save = Assert.Single(result.Saves);
        Assert.Equal("main", save.FileName);
        Assert.Contains("Brilliant Diamond", save.GameLabel);
        Assert.Equal(8, save.Generation);
        Assert.True(save.RequiresExtraCare);
    }

    [Fact]
    public void ScanRyujinx_DiscoversSavesWhenPointingDirectlyToSaveDirectory()
    {
        var saveRoot = Path.Combine(_temp, "save");
        var saveSlot = Path.Combine(saveRoot, "0000000000000002", "0");

        Directory.CreateDirectory(saveSlot);
        File.WriteAllBytes(Path.Combine(saveSlot, "main"), BdspSave(GameVersion.SP));

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(saveRoot, EmulatorKind.Ryujinx);

        var save = Assert.Single(result.Saves);
        Assert.Equal("main", save.FileName);
        Assert.Contains("Shining Pearl", save.GameLabel);
    }

    [Fact]
    public void ScanRomFolder_FindsSavesInArbitraryDirectories()
    {
        var romDir = Path.Combine(_temp, "my_roms", "sub_folder");
        Directory.CreateDirectory(romDir);
        File.WriteAllBytes(Path.Combine(romDir, "Pokemon Emerald.sav"), EmeraldSave());

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(Path.Combine(_temp, "my_roms"), EmulatorKind.RomFolder);

        var save = Assert.Single(result.Saves);
        Assert.Equal("Pokemon Emerald.sav", save.FileName);
        Assert.Contains("Emerald", save.GameLabel);
    }

    [Fact]
    public void ScanRomFolder_FindsSaveReachedThroughFileSymlink()
    {
        if (!OperatingSystem.IsLinux()) return;

        var storage = Path.Combine(_temp, "storage");
        Directory.CreateDirectory(storage);
        var realSave = Path.Combine(storage, "emerald-real.sav");
        File.WriteAllBytes(realSave, EmeraldSave());

        var romDir = Path.Combine(_temp, "linked_roms");
        Directory.CreateDirectory(romDir);
        File.CreateSymbolicLink(Path.Combine(romDir, "Pokemon Emerald.sav"), realSave);

        var scanner = new EmulatorTreeScanner<string>(_engine, _tree);
        var result = scanner.Scan(romDir, EmulatorKind.RomFolder);

        var save = Assert.Single(result.Saves);
        Assert.Equal("Pokemon Emerald.sav", save.FileName);
        Assert.Contains("Emerald", save.GameLabel);
    }
}
