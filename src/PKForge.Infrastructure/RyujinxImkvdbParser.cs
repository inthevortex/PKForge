using System.Buffers.Binary;

namespace PKForge.Infrastructure;

/// <summary>
/// Parser for Nintendo Switch / Ryujinx system save index database (<c>imkvdb.arc</c>).
/// Maps <c>SaveDataId</c> (directory name in <c>bis/user/save/</c>) to Switch <c>ProgramId</c> (Title ID).
/// </summary>
public static class RyujinxImkvdbParser
{
    private static ReadOnlySpan<byte> ImkvMagic => "IMKV"u8;
    private static ReadOnlySpan<byte> ImenMagic => "IMEN"u8;

    private const int HeaderSize = 12;
    private const int EntryHeaderSize = 12;
    private const int EntryFieldSize = 0x40;
    private const int MinEntrySize = EntryHeaderSize + 8 + 8;

    /// <summary>
    /// Parses an <c>imkvdb.arc</c> file and returns a mapping from SaveDataId to ProgramId (Title ID).
    /// </summary>
    /// <remarks>
    /// Layout: a 12-byte header ("IMKV", reserved u32, entry count i32), then per entry a
    /// 12-byte header ("IMEN", key size i32, value size i32), the key (a SaveDataAttribute,
    /// ProgramId in its first 8 bytes) and the value (SaveDataId in its first 8 bytes).
    /// </remarks>
    public static Dictionary<ulong, ulong> Parse(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<ulong, ulong>();
        if (data.Length < HeaderSize) return result;
        if (!data[..4].SequenceEqual(ImkvMagic)) return result;

        var declared = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
        if (declared <= 0) return result;
        var count = Math.Min(declared, (data.Length - HeaderSize) / MinEntrySize);

        var offset = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            if (offset + EntryHeaderSize > data.Length) break;
            if (!data.Slice(offset, 4).SequenceEqual(ImenMagic)) break;

            var keySize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4));
            var valueSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 8, 4));
            offset += EntryHeaderSize;

            if (keySize < 8 || valueSize < 8 || (long)offset + keySize + valueSize > data.Length)
                break;

            var programId = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            var saveDataId = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + keySize, 8));

            result[saveDataId] = programId;
            offset += keySize + valueSize;
        }

        return result;
    }

    /// <summary>
    /// Reads and parses an <c>imkvdb.arc</c> stream.
    /// </summary>
    public static Dictionary<ulong, ulong> Parse(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    /// <summary>
    /// Builds a synthetic <c>imkvdb.arc</c> payload for tests.
    /// </summary>
    public static byte[] Build(IEnumerable<KeyValuePair<ulong, ulong>> entries)
    {
        var list = entries.ToList();
        using var ms = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderSize];
        ImkvMagic.CopyTo(header[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], list.Count);
        ms.Write(header);

        Span<byte> entryHeader = stackalloc byte[EntryHeaderSize];
        ImenMagic.CopyTo(entryHeader[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(entryHeader[4..8], EntryFieldSize);
        BinaryPrimitives.WriteInt32LittleEndian(entryHeader[8..12], EntryFieldSize);

        foreach (var (saveDataId, programId) in list)
        {
            ms.Write(entryHeader);

            var key = new byte[EntryFieldSize];
            BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(0, 8), programId);
            ms.Write(key);

            var val = new byte[EntryFieldSize];
            BinaryPrimitives.WriteUInt64LittleEndian(val.AsSpan(0, 8), saveDataId);
            ms.Write(val);
        }

        return ms.ToArray();
    }
}
