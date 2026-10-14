using PKForge.App.Services;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Views;

/// <summary>
/// Draws Pokémon HOME renders where <see cref="SpriteStyleSetting"/> picks them over the pixel
/// sprites, from the sizes <see cref="ISpriteService.TryGetHomeImage"/> prepares.
/// </summary>
internal static class HomeRender
{
    /// <summary>For a prepared render drawn at about its own size.</summary>
    private static readonly SKSamplingOptions Near = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>For a render enlarged, or a stand-in of another size.</summary>
    public static readonly SKSamplingOptions Smooth = new(SKCubicResampler.Mitchell);

    /// <summary>Whether the boxes, the party and the summary draw HOME renders right now.</summary>
    public static bool Active => SpriteStyleSetting.Current == SpriteStyle.Home;

    /// <summary>
    /// The render to fit a square of <paramref name="box"/> canvas units, prepared for the device
    /// pixels that square covers on <paramref name="canvas"/>. Three states as
    /// <see cref="ISpriteService.TryGetHomeImage"/>.
    /// </summary>
    public static bool TryGet(SKCanvas canvas, ISpriteService sprites, SpriteLook look, float box, Action onLoaded, out SKImage? image) =>
        sprites.TryGetHomeImage(look, DevicePixels(canvas, box), onLoaded, out image);

    /// <summary>Draws <paramref name="image"/> fitted to a square of <paramref name="box"/> units, centred on <paramref name="centerX"/>, standing on <paramref name="bottom"/>.</summary>
    public static void DrawStanding(SKCanvas canvas, SKImage image, float box, float centerX, float bottom, SKPaint? paint = null)
    {
        var scale = box / Math.Max(image.Width, image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        Draw(canvas, image, new SKRect(centerX - w / 2, bottom - h, centerX + w / 2, bottom), paint);
    }

    /// <summary>Draws <paramref name="image"/> fitted to a square of <paramref name="box"/> units centred on a point.</summary>
    public static void DrawCentered(SKCanvas canvas, SKImage image, float box, float centerX, float centerY)
    {
        var h = image.Height * box / Math.Max(image.Width, image.Height);
        DrawStanding(canvas, image, box, centerX, centerY + h / 2);
    }

    private static void Draw(SKCanvas canvas, SKImage image, SKRect dest, SKPaint? paint)
    {
        var pixels = DevicePixels(canvas, Math.Max(dest.Width, dest.Height));
        var own = Math.Max(image.Width, image.Height);
        // Within a prepared step of its own size the plain filter is sharpest; further off, cubic.
        var sampling = pixels <= own && own - pixels <= SpriteStyles.PreparedStep ? Near : Smooth;
        canvas.DrawImage(image, dest, sampling, paint);
    }

    /// <summary>Device pixels a length in canvas units covers, through the canvas's current transform.</summary>
    private static int DevicePixels(SKCanvas canvas, float length)
    {
        var m = canvas.TotalMatrix;
        var scale = MathF.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY);
        return (int)MathF.Ceiling(length * (scale > 0 ? scale : 1f));
    }
}
