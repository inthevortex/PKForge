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

    /// <summary>
    /// Parses an <c>imkvdb.arc</c> file and returns a mapping from SaveDataId to ProgramId (Title ID).
    /// </summary>
    public static Dictionary<ulong, ulong> Parse(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<ulong, ulong>();
        if (data.Length < 8) return result;
        if (!data[..4].SequenceEqual(ImkvMagic)) return result;

        var count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        if (count <= 0) return result;

        var offset = 8;
        for (var i = 0; i < count; i++)
        {
            if (offset + 12 > data.Length) break;
            if (!data.Slice(offset, 4).SequenceEqual(ImenMagic)) break;

            var keySize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4));
            var valueSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 8, 4));
            offset += 12;

            if (keySize < 8 || valueSize < 8 || offset + keySize + valueSize > data.Length)
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
        ms.Write(ImkvMagic);
        Span<byte> countBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(countBytes, list.Count);
        ms.Write(countBytes);

        Span<byte> header = stackalloc byte[12];
        ImenMagic.CopyTo(header[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], 0x40);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], 0x40);

        foreach (var (saveDataId, programId) in list)
        {
            ms.Write(header);

            var key = new byte[0x40];
            BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(0, 8), programId);
            ms.Write(key);

            var val = new byte[0x40];
            BinaryPrimitives.WriteUInt64LittleEndian(val.AsSpan(0, 8), saveDataId);
            ms.Write(val);
        }

        return ms.ToArray();
    }
}
