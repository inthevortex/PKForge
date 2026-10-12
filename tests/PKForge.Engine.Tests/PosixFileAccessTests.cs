using PKForge.Domain;
using PKForge.Infrastructure;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class PosixFileAccessTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "pkforge-posix-tests-" + Guid.NewGuid().ToString("N"));

    public PosixFileAccessTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task ReadAsync_Throws_WhenFileNotFound()
    {
        var access = new PosixSaveFileAccess();
        var missingPath = Path.Combine(_testDir, "non_existent.sav");

        await Assert.ThrowsAsync<FileNotFoundException>(() => access.ReadAsync(missingPath).AsTask());
    }

    [Fact]
    public async Task WriteAtomicallyAsync_And_ReadAsync_RoundTrips()
    {
        var access = new PosixSaveFileAccess();
        var filePath = Path.Combine(_testDir, "save.sav");
        var data = new byte[] { 10, 20, 30, 40, 50 };

        await access.WriteAtomicallyAsync(filePath, data);

        var read = await access.ReadAsync(filePath);
        Assert.Equal(data, read.ToArray());

        var tmpFiles = Directory.EnumerateFiles(_testDir, ".*pkforge-tmp").ToList();
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public async Task WriteAtomicallyAsync_FollowsSymlink_TargetUpdated_SymlinkPreserved()
    {
        var access = new PosixSaveFileAccess();
        var targetDir = Path.Combine(_testDir, "real_dir");
        Directory.CreateDirectory(targetDir);

        var realPath = Path.Combine(targetDir, "original.sav");
        await File.WriteAllBytesAsync(realPath, [1, 2, 3]);

        var linkDir = Path.Combine(_testDir, "link_dir");
        Directory.CreateDirectory(linkDir);
        var linkPath = Path.Combine(linkDir, "shortcut.sav");

        File.CreateSymbolicLink(linkPath, realPath);

        Assert.NotNull(File.ResolveLinkTarget(linkPath, returnFinalTarget: true));

        var updatedData = new byte[] { 99, 88, 77, 66 };
        await access.WriteAtomicallyAsync(linkPath, updatedData);

        var realBytes = await File.ReadAllBytesAsync(realPath);
        Assert.Equal(updatedData, realBytes);

        var resolved = File.ResolveLinkTarget(linkPath, returnFinalTarget: true);
        Assert.NotNull(resolved);
        Assert.Equal(Path.GetFullPath(realPath), Path.GetFullPath(resolved.FullName));

        var linkBytes = await access.ReadAsync(linkPath);
        Assert.Equal(updatedData, linkBytes.ToArray());

        Assert.Empty(Directory.EnumerateFiles(targetDir, ".*pkforge-tmp"));
        Assert.Empty(Directory.EnumerateFiles(linkDir, ".*pkforge-tmp"));
    }

    [Fact]
    public async Task WriteAtomicallyAsync_MultiHopSymlink_TargetUpdated_AllSymlinksPreserved()
    {
        var access = new PosixSaveFileAccess();
        var realPath = Path.Combine(_testDir, "final.sav");
        await File.WriteAllBytesAsync(realPath, [1, 1, 1]);

        var hop1 = Path.Combine(_testDir, "hop1.sav");
        File.CreateSymbolicLink(hop1, realPath);

        var hop2 = Path.Combine(_testDir, "hop2.sav");
        File.CreateSymbolicLink(hop2, hop1);

        var updated = new byte[] { 2, 2, 2 };
        await access.WriteAtomicallyAsync(hop2, updated);

        Assert.Equal(updated, await File.ReadAllBytesAsync(realPath));

        Assert.NotNull(File.ResolveLinkTarget(hop1, returnFinalTarget: true));
        Assert.NotNull(File.ResolveLinkTarget(hop2, returnFinalTarget: true));
    }

    [Fact]
    public async Task WriteAtomicallyAsync_Throws_WhenDirectoryNotFound()
    {
        var access = new PosixSaveFileAccess();
        var invalidPath = Path.Combine(_testDir, "non_existent_folder", "save.sav");

        await Assert.ThrowsAsync<IOException>(() => access.WriteAtomicallyAsync(invalidPath, new byte[] { 1, 2, 3 }).AsTask());
    }

    [Fact]
    public async Task PosixFolderFileAccess_ListFilesAsync_ReturnsOnlyTopDirectoryFiles()
    {
        var saveAccess = new PosixSaveFileAccess();
        var folderAccess = new PosixFolderFileAccess(saveAccess);

        var file1 = Path.Combine(_testDir, "game1.sav");
        var file2 = Path.Combine(_testDir, "game2.sav");
        await File.WriteAllBytesAsync(file1, [1]);
        await File.WriteAllBytesAsync(file2, [2]);

        var subDir = Path.Combine(_testDir, "nested");
        Directory.CreateDirectory(subDir);
        await File.WriteAllBytesAsync(Path.Combine(subDir, "game3.sav"), [3]);

        var list = await folderAccess.ListFilesAsync(_testDir);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, d => d.DisplayName == "game1.sav" && d.DocumentId == Path.GetFullPath(file1));
        Assert.Contains(list, d => d.DisplayName == "game2.sav" && d.DocumentId == Path.GetFullPath(file2));
        Assert.DoesNotContain(list, d => d.DisplayName == "game3.sav");
    }

    [Fact]
    public async Task PosixFolderFileAccess_ListFilesAsync_ReturnsEmpty_WhenDirectoryMissing()
    {
        var saveAccess = new PosixSaveFileAccess();
        var folderAccess = new PosixFolderFileAccess(saveAccess);

        var list = await folderAccess.ListFilesAsync(Path.Combine(_testDir, "missing"));
        Assert.Empty(list);
    }

    [Fact]
    public async Task PosixFolderFileAccess_WriteFileAsync_And_ReadFileAsync()
    {
        var saveAccess = new PosixSaveFileAccess();
        var folderAccess = new PosixFolderFileAccess(saveAccess);

        var data = new byte[] { 42, 43, 44 };
        await folderAccess.WriteFileAsync(_testDir, "test.sav", data);

        var docPath = Path.Combine(_testDir, "test.sav");
        var read = await folderAccess.ReadFileAsync(docPath);
        Assert.Equal(data, read.ToArray());
    }

    [Fact]
    public void DialogConcurrencyGuard_EnforcesSingleActiveDialog()
    {
        using (var guard1 = DialogConcurrencyGuard.Enter())
        {
            Assert.True(DialogConcurrencyGuard.IsActive);
            var ex = Assert.Throws<InvalidOperationException>(() => DialogConcurrencyGuard.Enter());
            Assert.Equal("Another dialog is already open.", ex.Message);
        }

        Assert.False(DialogConcurrencyGuard.IsActive);

        using (var guard2 = DialogConcurrencyGuard.Enter())
        {
            Assert.True(DialogConcurrencyGuard.IsActive);
        }

        Assert.False(DialogConcurrencyGuard.IsActive);
    }
}
