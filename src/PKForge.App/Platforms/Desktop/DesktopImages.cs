using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.Maui.Handlers;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using AImage = Avalonia.Controls.Image;
using APanel = Avalonia.Controls.Panel;
using IAImage = Avalonia.Media.IImage;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;
using Size = Avalonia.Size;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Draws bitmaps at the window's real pixel size under <see cref="DesktopZoom"/>. The stock image
/// handler decodes a bitmap at its own size and lets Avalonia sample it through the zoom bilinearly
/// without mipmaps, which blurs enlarged pixel art and makes shrunk images shimmer. Pixel art is
/// scaled here from its own art grid to the device pixels it covers; other bitmaps get Avalonia's
/// high-quality sampling.
/// </summary>
public static class DesktopImages
{
    private static readonly ConditionalWeakTable<AImage, object> Watched = new();
    private static readonly ConditionalWeakTable<Bitmap, PixelArt?> Art = new();
    private static readonly ConditionalWeakTable<SKImage, PixelArt?> CanvasArt = new();
    private static readonly object Marker = new();
    private static bool _registered;

    /// <summary>Hooks every MAUI <c>Image</c> once its platform view exists.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        ImageHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IImageSourcePart.Source), (handler, _) =>
        {
            if (handler.PlatformView is APanel panel && panel.Children.OfType<AImage>().FirstOrDefault() is { } image)
                Watch(image);
        });
    }

    /// <summary>Keeps <paramref name="image"/> drawing every bitmap it is given at device resolution.</summary>
    public static void Watch(AImage image)
    {
        if (!Watched.TryAdd(image, Marker)) return;
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        image.PropertyChanged += (_, e) =>
        {
            if (e.Property == AImage.SourceProperty && e.NewValue is Bitmap bitmap) Adopt(image, bitmap);
        };
        if (image.Source is Bitmap current) Adopt(image, current);
    }

    /// <summary>
    /// Draws <paramref name="image"/> into <paramref name="dest"/> on a Skia canvas the way the
    /// image controls draw it: pixel art from its art grid to the device pixels it covers,
    /// anything else with mipmapped linear sampling.
    /// </summary>
    public static void DrawPixelArt(SKCanvas canvas, SKImage image, SKRect dest, SKPaint? paint = null)
    {
        if (CanvasArt.GetValue(image, PixelArt.TryCreate) is { } art)
            art.Draw(canvas, new SKRect(0, 0, art.Width, art.Height), dest, paint);
        else
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
    }

    private static void Adopt(AImage image, Bitmap bitmap)
    {
        var art = Art.GetValue(bitmap, PixelArt.TryCreate);
        if (art is not null) image.Source = new PixelArtImage(art, bitmap.Size);
    }

    /// <summary>A bitmap's art at one pixel per art pixel, with its enlargements cached for drawing.</summary>
    private sealed class PixelArt
    {
        private const int CachedEnlargements = 4;
        private readonly SKImage _image;
        private readonly List<(int X, int Y, SKImage Image)> _enlarged = [];
        private readonly Lock _gate = new();

        private PixelArt(SKImage image) => _image = image;

        public int Width => _image.Width;

        public int Height => _image.Height;

        public static unsafe PixelArt? TryCreate(Bitmap bitmap)
        {
            var size = bitmap.PixelSize;
            if (size.Width <= 0 || size.Height <= 0 || size.Width > PixelArtScaling.MaxSide || size.Height > PixelArtScaling.MaxSide) return null;
            var colorType = bitmap.Format == PixelFormats.Bgra8888 ? SKColorType.Bgra8888
                : bitmap.Format == PixelFormats.Rgba8888 ? SKColorType.Rgba8888
                : SKColorType.Unknown;
            var alphaType = bitmap.AlphaFormat switch
            {
                AlphaFormat.Premul => SKAlphaType.Premul,
                AlphaFormat.Unpremul => SKAlphaType.Unpremul,
                AlphaFormat.Opaque => SKAlphaType.Opaque,
                _ => SKAlphaType.Unknown,
            };
            if (colorType == SKColorType.Unknown || alphaType == SKAlphaType.Unknown) return null;

            var pixels = new uint[size.Width * size.Height];
            try
            {
                fixed (uint* buffer = pixels)
                    bitmap.CopyPixels(new PixelRect(size), (nint)buffer, pixels.Length * sizeof(uint), size.Width * sizeof(uint));
            }
            catch (NotSupportedException)
            {
                return null;
            }
            return FromPixels(pixels, size.Width, size.Height, colorType, alphaType);
        }

        public static unsafe PixelArt? TryCreate(SKImage image)
        {
            if (image.Width <= 0 || image.Height <= 0 || image.Width > PixelArtScaling.MaxSide || image.Height > PixelArtScaling.MaxSide) return null;
            var pixels = new uint[image.Width * image.Height];
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            fixed (uint* buffer = pixels)
                if (!image.ReadPixels(info, (nint)buffer, image.Width * sizeof(uint), 0, 0)) return null;
            return FromPixels(pixels, image.Width, image.Height, info.ColorType, info.AlphaType);
        }

        private static PixelArt? FromPixels(uint[] pixels, int width, int height, SKColorType colorType, SKAlphaType alphaType)
        {
            if (!PixelArtScaling.IsPixelArt(pixels, width, height)) return null;
            var block = PixelArtScaling.BlockSize(pixels, width, height);
            var artWidth = width / block;
            var artHeight = height / block;
            var art = pixels;
            if (block > 1)
            {
                art = new uint[artWidth * artHeight];
                for (var y = 0; y < artHeight; y++)
                    for (var x = 0; x < artWidth; x++)
                        art[y * artWidth + x] = pixels[y * block * width + x * block];
            }
            var info = new SKImageInfo(artWidth, artHeight, colorType, alphaType);
            var image = SKImage.FromPixelCopy(info, MemoryMarshal.AsBytes(art.AsSpan()), artWidth * sizeof(uint));
            return image is null ? null : new PixelArt(image);
        }

        /// <summary>Draws <paramref name="source"/>, in art pixels, into <paramref name="dest"/> on a canvas that maps it to device pixels.</summary>
        public void Draw(SKCanvas canvas, SKRect source, SKRect dest, SKPaint? paint)
        {
            var matrix = canvas.TotalMatrix;
            var deviceWidth = dest.Width * Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
            var deviceHeight = dest.Height * Math.Sqrt(matrix.SkewX * matrix.SkewX + matrix.ScaleY * matrix.ScaleY);
            var (multipleX, filterX) = PixelArtScaling.Plan(source.Width, deviceWidth);
            var (multipleY, filterY) = PixelArtScaling.Plan(source.Height, deviceHeight);
            var filter = (PixelArtFilter)Math.Max((int)filterX, (int)filterY);

            lock (_gate)
            {
                switch (filter)
                {
                    case PixelArtFilter.Exact:
                        canvas.DrawImage(_image, source, dest, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
                        break;
                    case PixelArtFilter.Smooth:
                        var enlarged = Enlarged(multipleX, multipleY);
                        var scaled = new SKRect(source.Left * multipleX, source.Top * multipleY, source.Right * multipleX, source.Bottom * multipleY);
                        canvas.DrawImage(enlarged, scaled, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
                        break;
                    default:
                        canvas.DrawImage(_image, source, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
                        break;
                }
            }
        }

        private SKImage Enlarged(int x, int y)
        {
            if (x == 1 && y == 1) return _image;
            foreach (var entry in _enlarged)
                if (entry.X == x && entry.Y == y) return entry.Image;

            var info = new SKImageInfo(_image.Width * x, _image.Height * y, _image.ColorType, _image.AlphaType);
            using var surface = SKSurface.Create(info);
            if (surface is null) return _image;
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(_image, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            var image = surface.Snapshot();
            if (_enlarged.Count == CachedEnlargements)
            {
                _enlarged[0].Image.Dispose();
                _enlarged.RemoveAt(0);
            }
            _enlarged.Add((x, y, image));
            return image;
        }
    }

    /// <summary>Pixel art standing in for its bitmap, at the bitmap's size, drawn to the device pixels it covers.</summary>
    private sealed class PixelArtImage(PixelArt art, Size size) : IAImage
    {
        public Size Size => size;

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            if (size.Width <= 0 || size.Height <= 0) return;
            var toArtX = art.Width / size.Width;
            var toArtY = art.Height / size.Height;
            var source = new SKRect((float)(sourceRect.Left * toArtX), (float)(sourceRect.Top * toArtY),
                (float)(sourceRect.Right * toArtX), (float)(sourceRect.Bottom * toArtY));
            context.Custom(new DrawOperation(art, source, destRect));
        }
    }

    private sealed class DrawOperation(PixelArt art, SKRect source, Rect dest) : ICustomDrawOperation
    {
        public Rect Bounds => dest;

        public bool HitTest(Point p) => dest.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            using var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            if (lease?.SkCanvas is not { } canvas) return;
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(lease.CurrentOpacity, 0, 1))) };
            art.Draw(canvas, source, dest.ToSKRect(), paint);
        }
    }
}
