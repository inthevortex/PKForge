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
}
