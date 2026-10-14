using PKForge.App.Services;
using SkiaSharp;

namespace PKForge.App.Views;

/// <summary>
/// The bundled NDS12 pixel face and M PLUS Rounded fallback for Skia painters. MAUI font
/// aliases never resolve through SKTypeface.FromFamilyName, so each face is loaded once from
/// its font file at the app package root (both heads package the fonts there) and shared.
/// </summary>
public static class PixelFont
{
    private const string PixelFile = "NDS12.ttf";
    private const string FallbackFile = "MPLUSRounded1c-Regular.ttf";

    private static readonly object Gate = new();
    private static readonly Lazy<SKTypeface?> Pixel = new(() => LoadBundled(PixelFile));
    private static readonly Lazy<SKTypeface?> Fallback = new(() => LoadBundled(FallbackFile));
    private static Task? _loadTask;
    private static Task? _fallbackTask;

    /// <summary>The pixel face, or <see cref="SKTypeface.Default"/> until it has loaded.</summary>
    public static SKTypeface Face
    {
        get
        {
            if (Pixel.IsValueCreated) return Pixel.Value ?? SKTypeface.Default;
            _ = WarmAsync();
            // Font loading must never block a Skia paint callback. The next timer
            // repaint automatically picks up the bundled face after it is ready.
            return SKTypeface.Default;
        }
    }

    /// <summary>The pixel face, loaded on the calling thread if it is not ready yet; null when the bundled file cannot be read.</summary>
    internal static SKTypeface? LoadedFace => Pixel.Value;

    public static Task WarmAsync()
    {
        lock (Gate)
            return _loadTask ??= Task.Run(() => _ = Pixel.Value);
    }

    /// <summary>The bundled M PLUS Rounded face: full Latin + Japanese coverage, the
    /// UI's own rounded style. The fallback for text the pixel face cannot draw.</summary>
    public static SKTypeface FallbackFace
    {
        get
        {
            if (Fallback.IsValueCreated) return Fallback.Value ?? SKTypeface.Default;
            _ = WarmFallbackAsync();
            return SKTypeface.Default;
        }
    }

    public static Task WarmFallbackAsync()
    {
        lock (Gate)
            return _fallbackTask ??= Task.Run(() => _ = Fallback.Value);
    }

    /// <summary>Copies a bundled font to the cache and opens it by path; null (logged once) when it cannot be read.</summary>
    private static SKTypeface? LoadBundled(string fontName)
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(fontName).GetAwaiter().GetResult();
            var cache = System.IO.Path.Combine(FileSystem.CacheDirectory, fontName);
            using (var output = File.Create(cache))
                stream.CopyTo(output);
            var face = SKTypeface.FromFile(cache);
            if (face is null) AppLog.Warn("fonts", $"Skia could not open {fontName}; using the default face");
            return face;
        }
        catch (Exception ex)
        {
            AppLog.Warn("fonts", $"Failed to load {fontName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The pixel face at a size, with antialiasing off-ish (it is a pixel font).</summary>
    public static SKFont At(float size) => new(Face, size) { Edging = SKFontEdging.Antialias, Embolden = true };

    /// <summary>The pixel face when it can draw every glyph of the text, else the bundled fallback face (CJK nicknames).</summary>
    public static SKFont For(string text, float size)
    {
        var face = Face;
        using var probe = new SKFont(face);
        var covered = text.All(c => probe.GetGlyph(c) != 0);
        return new SKFont(covered ? face : FallbackFace, size) { Edging = SKFontEdging.Antialias, Embolden = true };
    }
}
