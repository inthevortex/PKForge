namespace PKForge.App.Platforms.Desktop;

/// <summary>How pixel art is sampled onto the screen along one axis.</summary>
public enum PixelArtFilter
{
    /// <summary>A whole number of device pixels per art pixel: nearest-neighbour, every art pixel the same size.</summary>
    Exact,

    /// <summary>
    /// Between whole multiples: the art is enlarged by the next whole factor with nearest-neighbour,
    /// then resampled linearly onto the screen, so art pixels stay even and only their edges blend.
    /// </summary>
    Smooth,

    /// <summary>Shrunk to under half its size: linear sampling with mipmaps.</summary>
    Mipmapped,
}

/// <summary>
/// Recognises pixel art among decoded bitmaps and picks how to scale it to device pixels.
/// Pixels are 32-bit with alpha in the top byte, which holds for both BGRA and RGBA 8888.
/// </summary>
public static class PixelArtScaling
{
    /// <summary>Bitmaps with a side longer than this are treated as artwork, never as pixel art.</summary>
    public const int MaxSide = 512;

    /// <summary>Pixel art uses at most this many colours; photographs and painted artwork use more.</summary>
    public const int MaxColors = 256;

    /// <summary>
    /// True for a small bitmap with a small palette: sprites, PKSM chrome, pixel fonts and keys.
    /// Fully transparent pixels count as one colour whatever their colour channels hold.
    /// </summary>
    public static bool IsPixelArt(ReadOnlySpan<uint> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide || pixels.Length < width * height) return false;
        var colors = new HashSet<uint>();
        foreach (var pixel in pixels[..(width * height)])
        {
            colors.Add(pixel >> 24 == 0 ? 0u : pixel);
            if (colors.Count > MaxColors) return false;
        }
        return true;
    }

    /// <summary>
    /// The side of the square blocks the art was drawn in: art enlarged 4x before it was saved
    /// has every run of equal pixels, in rows and in columns, a multiple of 4. One for art
    /// stored at its own resolution.
    /// </summary>
    public static int BlockSize(ReadOnlySpan<uint> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height) return 1;
        var block = Gcd(width, height);
        for (var y = 0; y < height && block > 1; y++)
        {
            var row = pixels.Slice(y * width, width);
            var run = 1;
            for (var x = 1; x < width; x++)
            {
                if (row[x] == row[x - 1]) run++;
                else
                {
                    block = Gcd(block, run);
                    run = 1;
                }
            }
        }
        for (var x = 0; x < width && block > 1; x++)
        {
            var run = 1;
            for (var y = 1; y < height; y++)
            {
                if (pixels[y * width + x] == pixels[(y - 1) * width + x]) run++;
                else
                {
                    block = Gcd(block, run);
                    run = 1;
                }
            }
        }
        return Math.Max(1, block);
    }

    /// <summary>
    /// How to draw <paramref name="artPixels"/> art pixels across <paramref name="devicePixels"/>
    /// device pixels: the whole factor to enlarge the art by and the sampling to finish with.
    /// Exact when the result lands within half a device pixel of a whole multiple.
    /// </summary>
    public static (int Multiple, PixelArtFilter Filter) Plan(double artPixels, double devicePixels)
    {
        if (!(artPixels > 0) || !(devicePixels > 0) || !double.IsFinite(artPixels) || !double.IsFinite(devicePixels))
            return (1, PixelArtFilter.Mipmapped);
        var ratio = devicePixels / artPixels;
        var whole = Math.Round(ratio);
        if (whole >= 1 && Math.Abs(whole * artPixels - devicePixels) < 0.5) return ((int)whole, PixelArtFilter.Exact);
        if (ratio < 0.5) return (1, PixelArtFilter.Mipmapped);
        return ((int)Math.Ceiling(ratio), PixelArtFilter.Smooth);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
