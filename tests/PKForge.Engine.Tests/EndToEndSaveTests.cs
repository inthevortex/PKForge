using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class EndToEndSaveTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "pkforge-emerald-e2e-" + Guid.NewGuid().ToString("N"));

    public EndToEndSaveTests()
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
    public async Task EmeraldSave_EndToEnd_Describe_Mutate_AtomicWriteViaSymlink_BackupVerified()
    {
        // 1. Obtain test template bytes (synthesized Gen 3 save with 16-byte RTC footer)
        var originalBytes = GetSourceBytes();
        Assert.Equal(131088, originalBytes.Length); // 128KB Flash + 16-byte mGBA RTC footer

        // 2. Set up working scratch copy in isolated test directory
        var scratchTarget = Path.Combine(_testDir, "Pokemon - Emerald.sav");
        await File.WriteAllBytesAsync(scratchTarget, originalBytes);

        // 3. Create symlink pointing to the scratch copy
        var symlinkPath = Path.Combine(_testDir, "Emerald_symlink.sav");
        File.CreateSymbolicLink(symlinkPath, scratchTarget);
        Assert.NotNull(File.ResolveLinkTarget(symlinkPath, returnFinalTarget: true));

        // 4. Describe via ISaveEngine
        var engine = new SaveEngine();
        var desc = engine.TryDescribe(originalBytes, "Pokemon - Emerald");
        Assert.NotNull(desc);
        Assert.Equal(3, desc.Generation);
        Assert.False(string.IsNullOrWhiteSpace(desc.TrainerName));

        // 5. Open session and verify slots
        using var session = (SaveEngineSession)engine.OpenSession(originalBytes, "Pokemon - Emerald");
        Assert.NotNull(session);
        Assert.True(session.Snapshot.Slots.Count > 0);

        // Find a slot to move/mutate so bytes genuinely change
        int fromBox = -1, fromSlot = -1, toBox = -1, toSlot = -1;
        for (var b = 0; b < 14; b++)
        {
            for (var s = 0; s < 30; s++)
            {
                var summary = session.Snapshot.Slots.FirstOrDefault(x => x.Box == b && x.Slot == s);
                if (summary is { Species: > 0 } && fromBox == -1)
                {
                    fromBox = b;
                    fromSlot = s;
                }
                else if (summary is null or { Species: null or 0 } && toBox == -1 && fromBox != -1)
                {
                    toBox = b;
                    toSlot = s;
                    break;
                }
            }
            if (fromBox != -1 && toBox != -1) break;
        }

        if (fromBox != -1 && toBox != -1)
        {
            session.MoveSlot(fromBox, fromSlot, toBox, toSlot);
        }

        var mutatedBytes = session.Serialize();
        Assert.Equal(131088, mutatedBytes.Length);

        // Verify mGBA 16-byte RTC footer is preserved intact
        Assert.Equal(originalBytes.AsSpan()[^16..].ToArray(), mutatedBytes.Span[^16..].ToArray());

        // 6. SafeSaveWriter + FileBackupService + PosixSaveFileAccess
        var backupDir = Path.Combine(_testDir, "backups");
        var backups = new FileBackupService(backupDir);
        var access = new PosixSaveFileAccess();
        var writer = new SafeSaveWriter(engine, backups, access);

        var snapshot = session.Snapshot;

        // Write through the symlink path
        var receipt = await writer.WriteScopedAsync(symlinkPath, snapshot, mutatedBytes, WriteScope.Everything, "End-to-end test edit");

        // 7. Verify receipt
        Assert.NotNull(receipt);
        Assert.True(receipt.Changed);
        Assert.NotNull(receipt.BackupId);

        // 8. Verify backup was created
        var backupList = await backups.ListAsync();
        Assert.Single(backupList);
        Assert.Equal(receipt.BackupId, backupList[0].BackupId);
        Assert.Equal("End-to-end test edit", backupList[0].ChangeDescription);

        // Verify backup bytes equal original bytes
        var backupBytes = await backups.ReadAsync(receipt.BackupId);
        Assert.Equal(originalBytes, backupBytes.ToArray());

        // 9. Verify atomic write & symlink preservation
        // Check symlink still exists and points to scratchTarget
        var linkTarget = File.ResolveLinkTarget(symlinkPath, returnFinalTarget: true);
        Assert.NotNull(linkTarget);
        Assert.Equal(Path.GetFullPath(scratchTarget), Path.GetFullPath(linkTarget.FullName));

        // Read scratchTarget directly: contains updated bytes with 131,088 byte size
        var scratchBytes = await File.ReadAllBytesAsync(scratchTarget);
        Assert.Equal(131088, scratchBytes.Length);
        Assert.Equal(mutatedBytes.ToArray(), scratchBytes);

        // Read through symlink via PosixSaveFileAccess
        var readViaLink = await access.ReadAsync(symlinkPath);
        Assert.Equal(mutatedBytes.ToArray(), readViaLink.ToArray());

        // 10. Verify no temporary files remain
        var filesInDir = Directory.GetFiles(_testDir);
        Assert.DoesNotContain(filesInDir, f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] GetSourceBytes()
    {
        // Synthesize a valid 128KB Emerald save + 16-byte mGBA RTC footer
        var raw = new byte[0x20000];
        for (var id = 0; id < 14; id++)
        {
            var sector = raw.AsSpan(id * 0x1000);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(sector[0xFF4..], (ushort)id);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFF8..], 0x08012025);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFFC..], 1);

            var sector2 = raw.AsSpan((14 + id) * 0x1000);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(sector2[0xFF4..], (ushort)id);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sector2[0xFF8..], 0x08012025);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sector2[0xFFC..], 1);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0xAC), 0x1234_5678);
        raw[0x900] = 1;
        raw[6] = 0xFF;
        raw[7] = 0xFF;
        raw[(14 * 0x1000) + 6] = 0xFF;
        raw[(14 * 0x1000) + 7] = 0xFF;

        var sav = new PKHeX.Core.SAV3E(raw);
        sav.OT = "EmeraldTester";
        sav.TID16 = 12345;
        sav.SID16 = 54321;
        var pk = new PKHeX.Core.PK3
        {
            Species = 252, // Treecko
            CurrentLevel = 5,
        };
        sav.SetBoxSlotAtIndex(pk, 0);
        sav.WriteBothSaveSlots(sav.Data);

        var data = sav.Write().ToArray();
        var result = new byte[131088];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        for (var i = 0; i < 16; i++) result[data.Length + i] = (byte)(0xAA + i);
        return result;
    }
}
