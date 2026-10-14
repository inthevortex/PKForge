using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Maui.SkiaSharp.Views.Controls;
using Avalonia.Controls.Maui.SkiaSharp.Views.Handlers;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Gives each <c>SKCanvasView</c> a surface at the window's real pixel size under
/// <see cref="DesktopZoom"/>; the stock control sizes it for the unzoomed window, so the
/// zoom would stretch a smaller bitmap and blur it.
/// </summary>
public sealed class ZoomedSKCanvasViewHandler : SKCanvasViewHandler
{
    protected override SKCanvasViewControl CreatePlatformView() => new ZoomedSKCanvasViewControl();

    protected override void ConnectHandler(SKCanvasViewControl platformView)
    {
        base.ConnectHandler(platformView);
        if (platformView.TouchAction is { } touch)
            platformView.TouchAction = args => touch(ZoomedSKCanvasViewControl.Zoom(args, platformView.IgnorePixelScaling));
    }
}

/// <summary>An <see cref="SKCanvasViewControl"/> that paints at window scaling times <see cref="DesktopZoom.Current"/>.</summary>
public sealed class ZoomedSKCanvasViewControl : SKCanvasViewControl
{
    /// <summary>Every zoomed canvas paint, for disposing cached images only between paints.</summary>
    internal static PaintFence Paints { get; } = new(item => MainThread.BeginInvokeOnMainThread(item.Dispose));

    /// <summary>Moves a touch from the unzoomed pixels the base control reports into this surface's pixels.</summary>
    internal static SKTouchEventArgs Zoom(SKTouchEventArgs args, bool ignorePixelScaling)
    {
        var (x, y) = DesktopZoom.TouchToSurface(args.Location.X, args.Location.Y, DesktopZoom.Current, ignorePixelScaling);
        if (x == args.Location.X && y == args.Location.Y) return args;
        return new SKTouchEventArgs(args.Id, args.ActionType, args.MouseButton, args.DeviceType,
            new SKPoint(x, y), args.InContact, args.WheelDelta, args.Pressure);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DesktopZoom.Changed += InvalidateCanvas;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DesktopZoom.Changed -= InvalidateCanvas;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        if (PaintSurfaceAction is not { } paint || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scaling = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0) * DesktopZoom.Current;
        context.Custom(new DrawOperation(new Rect(Bounds.Size), paint, IgnorePixelScaling, scaling));
    }

    /// <summary>Paints into a raster surface of the bounds times <c>scaling</c>, then draws it back at one surface pixel per device pixel.</summary>
    private sealed class DrawOperation(Rect bounds, Action<SKPaintSurfaceEventArgs> paint, bool ignorePixelScaling, double scaling)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            using var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            if (lease?.SkCanvas is not { } target) return;

            var info = new SKImageInfo((int)(bounds.Width * scaling), (int)(bounds.Height * scaling), SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            if (info.Width <= 0 || info.Height <= 0) return;
            using var surface = SKSurface.Create(info);
            if (surface is null) return;

            var reported = info;
            if (ignorePixelScaling)
            {
                reported = new SKImageInfo((int)bounds.Width, (int)bounds.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                surface.Canvas.Scale((float)scaling);
            }
            Paints.Enter();
            try
            {
                paint(new SKPaintSurfaceEventArgs(surface, reported, info));
                surface.Flush();
            }
            finally { Paints.Exit(); }

            using var image = surface.Snapshot();
            using var fade = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(lease.CurrentOpacity, 0, 1))) };
            target.Save();
            target.Scale(1f / (float)scaling);
            target.DrawImage(image, 0, 0, SKSamplingOptions.Default, fade);
            target.Restore();
        }
    }
}
