using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class PixelArtScalingTests
{
    private const uint Clear = 0x00000000;
    private const uint Ink = 0xFF2040C0;
    private const uint Paper = 0xFFFFFFFF;

    /// <summary>A checkerboard of <paramref name="cells"/> x <paramref name="cells"/> art pixels, each drawn as a <paramref name="block"/>-pixel square.</summary>
    private static uint[] Checker(int cells, int block)
    {
        var side = cells * block;
        var pixels = new uint[side * side];
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
                pixels[y * side + x] = ((x / block) + (y / block)) % 2 == 0 ? Ink : Paper;
        return pixels;
    }

    [Fact]
    public void ASmallPaletteIsPixelArt()
    {
        Assert.True(PixelArtScaling.IsPixelArt(Checker(15, 4), 60, 60));
    }

    [Fact]
    public void ManyColoursAreArtwork()
    {
        var pixels = new uint[64 * 64];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = 0xFF000000 | (uint)(i * 7);

        Assert.False(PixelArtScaling.IsPixelArt(pixels, 64, 64));
    }

    [Fact]
    public void ALargeBitmapIsArtworkWhateverItsPalette()
    {
        var side = PixelArtScaling.MaxSide + 1;

        Assert.False(PixelArtScaling.IsPixelArt(new uint[side * side], side, side));
    }

    [Fact]
    public void TransparentPixelsCountAsOneColour()
    {
        var pixels = new uint[20 * 20];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = i % 2 == 0 ? Ink : (uint)i;

        Assert.True(PixelArtScaling.IsPixelArt(pixels, 20, 20));
    }

    [Theory]
    [InlineData(15, 4, 4)]
    [InlineData(16, 2, 2)]
    [InlineData(16, 6, 6)]
    [InlineData(28, 1, 1)]
    public void TheBlockSizeIsTheEnlargementTheArtWasSavedAt(int cells, int block, int expected)
    {
        Assert.Equal(expected, PixelArtScaling.BlockSize(Checker(cells, block), cells * block, cells * block));
    }

    [Fact]
    public void ASinglePixelDetailKeepsTheArtAtItsOwnResolution()
    {
        var pixels = Checker(8, 2);
        pixels[3] = Clear;

        Assert.Equal(1, PixelArtScaling.BlockSize(pixels, 16, 16));
    }

    [Fact]
    public void TheBlockSizeDividesBothSides()
    {
        var pixels = new uint[6 * 4];
        Array.Fill(pixels, Ink);

        Assert.Equal(2, PixelArtScaling.BlockSize(pixels, 6, 4));
    }

    [Theory]
    [InlineData(16, 48, 3)]
    [InlineData(15, 30, 2)]
    [InlineData(16, 16, 1)]
    [InlineData(16, 48.4, 3)]
    public void WholeMultiplesAreExact(double art, double device, int multiple)
    {
        Assert.Equal((multiple, PixelArtFilter.Exact), PixelArtScaling.Plan(art, device));
    }

    [Theory]
    [InlineData(15, 29.33, 2)]
    [InlineData(15, 22, 2)]
    [InlineData(16, 50.4, 4)]
    [InlineData(16, 12, 1)]
    public void BetweenMultiplesTheArtIsEnlargedPastTheScreenThenSmoothed(double art, double device, int multiple)
    {
        Assert.Equal((multiple, PixelArtFilter.Smooth), PixelArtScaling.Plan(art, device));
    }

    [Theory]
    [InlineData(96, 40)]
    [InlineData(68, 20)]
    public void ShrinkingPastHalfUsesMipmaps(double art, double device)
    {
        Assert.Equal((1, PixelArtFilter.Mipmapped), PixelArtScaling.Plan(art, device));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public void UnknownSizesFallBackToMipmaps(double art, double device)
    {
        Assert.Equal((1, PixelArtFilter.Mipmapped), PixelArtScaling.Plan(art, device));
    }
}
