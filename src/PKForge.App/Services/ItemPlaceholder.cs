using System.Buffers.Binary;
using System.IO.Compression;

namespace PKForge.App.Services;

/// <summary>
/// The tile shown for an item without an icon: a 24x24 navy plate with a cyan rim and a white
/// question mark. Drawn and encoded here, so it needs no network and no bundled asset.
/// </summary>
internal static class ItemPlaceholder
{
    public const string FileName = "_placeholder.png";

    private const int Side = 24;

    private static readonly Lazy<byte[]> Encoded = new(() => EncodePng(Draw(), Side, Side));

    /// <summary>The path last found to hold exactly <see cref="Png"/> this run.</summary>
    private static string? _verified;

    /// <summary>The placeholder's PNG bytes.</summary>
    public static byte[] Png => Encoded.Value;

    /// <summary>
    /// The placeholder's path in <paramref name="directory"/>. The first call in a run compares
    /// the file there with <see cref="Png"/> and replaces it, through a temporary file renamed
    /// into place, when it is missing or differs, so a tile written by an older build is redrawn.
    /// Best effort: the path is returned even when it cannot be written.
    /// </summary>
    public static string Ensure(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (string.Equals(Volatile.Read(ref _verified), path, StringComparison.Ordinal)) return path;
        try
        {
            if (!Holds(path, Png))
            {
                Directory.CreateDirectory(directory);
                Write(path, Png);
            }
            Volatile.Write(ref _verified, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A blank row beats a crash.
        }
        return path;
    }

    private static bool Holds(string path, byte[] expected)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length == expected.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(expected);
    }

    private static void Write(string path, byte[] bytes)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            ItemIconFile.TryDelete(temporary);
            throw;
        }
    }

    private static byte[] Draw()
    {
        var px = new byte[Side * Side * 4];
        for (var y = 0; y < Side; y++)
            for (var x = 0; x < Side; x++)
            {
                var edge = x is 0 or 23 || y is 0 or 23;
                var inner = x is >= 2 and <= 21 && y is >= 2 and <= 21;
                var r = edge ? (byte)0x2E : inner ? (byte)0x14 : (byte)0x20;
                var g = edge ? (byte)0x8A : inner ? (byte)0x2E : (byte)0x3C;
                var b = edge ? (byte)0xC8 : inner ? (byte)0x6E : (byte)0x88;
                // question-mark dot cluster
                if (inner && y is >= 8 and <= 15 && ((y < 11 && x is >= 10 and <= 14) || (y is 11 or 14 && x is >= 12 and <= 14) || (y is >= 12 and <= 13 && x == 13) || (y is >= 14 and <= 16 && x is >= 13 and <= 14)))
                { r = 0xFC; g = 0xFD; b = 0xFE; }
                var o = (y * Side + x) * 4;
                px[o] = r; px[o + 1] = g; px[o + 2] = b; px[o + 3] = 0xFF;
            }
        return px;
    }

    private static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        var stride = 1 + width * 4;
        var raw = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            raw[y * stride] = 0; // filter: none
            Array.Copy(rgba, y * width * 4, raw, y * stride + 1, width * 4);
        }
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        WriteChunk(output, "IHDR"u8, ihdr.ToArray());
        WriteChunk(output, "IDAT"u8, Compress(raw));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream to, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        to.Write(header);
        to.Write(type);
        to.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var b in type) crc = CrcStep(crc, b);
        foreach (var b in data) crc = CrcStep(crc, b);
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, ~crc);
        to.Write(tail);
    }

    private static uint CrcStep(uint crc, byte b)
    {
        crc ^= b;
        for (var k = 0; k < 8; k++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }

    /// <summary>IDAT holds a zlib stream (header and Adler-32 around the deflate data), not raw deflate.</summary>
    private static byte[] Compress(byte[] raw)
    {
        using var zipped = new MemoryStream();
        using (var zlib = new ZLibStream(zipped, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);
        return zipped.ToArray();
    }
}
