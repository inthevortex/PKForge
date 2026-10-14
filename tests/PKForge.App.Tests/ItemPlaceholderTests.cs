using System.Buffers.Binary;
using System.IO.Compression;
using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class ItemPlaceholderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkforge-placeholder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ThePlaceholderDecodesAsA24By24RgbaPng()
    {
        var pixels = Decode(ItemPlaceholder.Png, out var width, out var height);

        Assert.Equal(24, width);
        Assert.Equal(24, height);
        Assert.Equal(new byte[] { 0x2E, 0x8A, 0xC8, 0xFF }, Pixel(pixels, width, 0, 0));
        Assert.Equal(new byte[] { 0x20, 0x3C, 0x88, 0xFF }, Pixel(pixels, width, 1, 1));
        Assert.Equal(new byte[] { 0x14, 0x2E, 0x6E, 0xFF }, Pixel(pixels, width, 3, 3));
        Assert.Equal(new byte[] { 0xFC, 0xFD, 0xFE, 0xFF }, Pixel(pixels, width, 12, 9));
    }

    [Fact]
    public void ThePlaceholderIsWrittenWhenMissing()
    {
        var path = ItemPlaceholder.Ensure(_root);

        Assert.Equal(Path.Combine(_root, ItemPlaceholder.FileName), path);
        Assert.Equal(ItemPlaceholder.Png, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void APlaceholderThatDiffersOnDiskIsReplaced()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, ItemPlaceholder.FileName);
        var stale = ItemPlaceholder.Png.ToArray();
        stale[45] ^= 0xFF; // inside the IDAT data: same length, same signature and IEND
        File.WriteAllBytes(path, stale);
        Assert.True(ItemIconFile.IsWhole(path));

        ItemPlaceholder.Ensure(_root);

        Assert.Equal(ItemPlaceholder.Png, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    private static byte[] Pixel(byte[] pixels, int width, int x, int y) =>
        pixels.AsSpan((y * width + x) * 4, 4).ToArray();

    /// <summary>
    /// Decodes an 8-bit RGBA PNG with unfiltered rows, checking what a strict decoder checks:
    /// chunk CRCs, and an IDAT holding a zlib stream with a valid header and Adler-32.
    /// </summary>
    private static byte[] Decode(byte[] png, out int width, out int height)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        width = height = 0;
        using var idat = new MemoryStream();
        var offset = 8;
        var ended = false;
        while (!ended)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var typeAndData = png.AsSpan(offset + 4, 4 + length);
            var type = System.Text.Encoding.ASCII.GetString(typeAndData[..4]);
            var data = typeAndData[4..];
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length)), Crc32(typeAndData));
            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, data[8..].ToArray());
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    ended = true;
                    break;
            }
            offset += 12 + length;
        }
        Assert.Equal(png.Length, offset);

        var zlib = idat.ToArray();
        Assert.Equal(8, zlib[0] & 0x0F); // deflate
        Assert.Equal(0, ((zlib[0] << 8) | zlib[1]) % 31);
        using var inflated = new MemoryStream();
        using (var stream = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress))
            stream.CopyTo(inflated);
        var raw = inflated.ToArray();
        Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(zlib.AsSpan(zlib.Length - 4)), Adler32(raw));

        var stride = 1 + width * 4;
        Assert.Equal(height * stride, raw.Length);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, raw[y * stride]);
            Array.Copy(raw, y * stride + 1, pixels, y * width * 4, width * 4);
        }
        return pixels;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        uint a = 1, b = 0;
        foreach (var value in bytes)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}
