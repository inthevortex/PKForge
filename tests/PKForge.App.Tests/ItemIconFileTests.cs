using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class ItemIconFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkforge-item-file-" + Guid.NewGuid().ToString("N"));
    private readonly string _icon;

    public ItemIconFileTests()
    {
        Directory.CreateDirectory(_root);
        _icon = Path.Combine(_root, "master-ball.png");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A minimal PNG: signature, a stand-in IHDR, and the IEND chunk.</summary>
    internal static byte[] Png()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] header = [0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 24, 0, 0, 0, 24, 8, 6, 0, 0, 0, 0x1C, 0x5B, 0x2A, 0x45];
        byte[] end = [0, 0, 0, 0, .. "IEND"u8, 0xAE, 0x42, 0x60, 0x82];
        return [.. signature, .. header, .. end];
    }

    [Fact]
    public async Task AWholeIconIsWrittenAndReadsAsWhole()
    {
        await ItemIconFile.WriteAsync(_icon, Png());

        Assert.Equal(Png(), await File.ReadAllBytesAsync(_icon));
        Assert.True(ItemIconFile.IsWhole(_icon));
        Assert.Equal([_icon], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task BytesThatAreNotAWholePngAreNeverWritten()
    {
        var truncated = Png()[..^4];

        await Assert.ThrowsAsync<InvalidDataException>(() => ItemIconFile.WriteAsync(_icon, truncated));
        await Assert.ThrowsAsync<InvalidDataException>(() => ItemIconFile.WriteAsync(_icon, "<html>rate limited</html>"u8.ToArray()));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task AWriteThatFailsLeavesNoTemporaryFileBehind()
    {
        Directory.CreateDirectory(_icon); // the rename into place cannot replace a folder

        await Assert.ThrowsAnyAsync<IOException>(() => ItemIconFile.WriteAsync(_icon, Png()));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task AnIconCutShortOnDiskIsNotWhole()
    {
        var png = Png();
        await File.WriteAllBytesAsync(_icon, png[..^1]);
        Assert.False(ItemIconFile.IsWhole(_icon));

        await File.WriteAllBytesAsync(_icon, png[..8]);
        Assert.False(ItemIconFile.IsWhole(_icon));

        await File.WriteAllBytesAsync(_icon, []);
        Assert.False(ItemIconFile.IsWhole(_icon));

        Assert.False(ItemIconFile.IsWhole(Path.Combine(_root, "absent.png")));
    }

    [Fact]
    public async Task ALittleTrailingDataAfterTheEndStillCountsAsWhole()
    {
        await File.WriteAllBytesAsync(_icon, [.. Png(), 0, 0, 0, 0]);

        Assert.True(ItemIconFile.IsWhole(_icon));
        Assert.True(ItemIconFile.IsWhole([.. Png(), 0, 0]));
    }

    [Fact]
    public async Task AWholeIconOnDiskIsUsable()
    {
        await File.WriteAllBytesAsync(_icon, Png());

        Assert.True(ItemIconFile.IsUsable(_icon));
        Assert.True(File.Exists(_icon));
    }

    [Fact]
    public async Task ADamagedIconIsNotUsableAndIsDeletedSoItIsFetchedAgain()
    {
        await File.WriteAllBytesAsync(_icon, Png()[..^4]);

        Assert.False(ItemIconFile.IsUsable(_icon));
        Assert.False(File.Exists(_icon));
    }

    [Fact]
    public void AMissingIconIsNotUsable() =>
        Assert.False(ItemIconFile.IsUsable(_icon));

    [Fact]
    public async Task AnIconWrittenThroughTheCacheIsUsable()
    {
        await ItemIconFile.WriteAsync(_icon, Png());

        Assert.True(ItemIconFile.IsUsable(_icon));
    }

    [Fact]
    public async Task WarmingChecksEachIconOnceSoItIsNotReadAgainWhenShown()
    {
        await File.WriteAllBytesAsync(_icon, Png());

        Assert.Equal(0, ItemIconFile.Warm(_root, skip: "_placeholder.png"));
        await File.WriteAllBytesAsync(_icon, Png()[..^4]);

        Assert.True(ItemIconFile.IsUsable(_icon));
    }

    [Fact]
    public async Task WarmingDeletesDamagedIconsAndLeavesOtherFilesAlone()
    {
        var damaged = Path.Combine(_root, "poke-ball.png");
        var skipped = Path.Combine(_root, "_placeholder.png");
        var miss = Path.Combine(_root, "great-ball.png.miss");
        var partial = Path.Combine(_root, "ultra-ball.png.1234.part");
        await File.WriteAllBytesAsync(_icon, Png());
        await File.WriteAllBytesAsync(damaged, Png()[..^4]);
        await File.WriteAllBytesAsync(skipped, [1, 2, 3]);
        await File.WriteAllBytesAsync(miss, [1]);
        await File.WriteAllBytesAsync(partial, [1]);

        Assert.Equal(1, ItemIconFile.Warm(_root, skip: "_placeholder.png"));

        Assert.Equal([skipped, miss, _icon, partial], Directory.GetFiles(_root).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WarmingAMissingFolderDoesNothing() =>
        Assert.Equal(0, ItemIconFile.Warm(Path.Combine(_root, "absent"), skip: "_placeholder.png"));

    [Fact]
    public async Task AnIconMarkedWholeIsUsableWithoutBeingRead()
    {
        await File.WriteAllBytesAsync(_icon, [1, 2, 3]);

        ItemIconFile.MarkWhole(_icon);

        Assert.True(ItemIconFile.IsUsable(_icon));
        Assert.True(File.Exists(_icon));
    }
}
