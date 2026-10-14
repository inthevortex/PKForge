using PKForge.App.Platforms.Desktop;
using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class DesktopZoomTests
{
    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(2560, 1440, 2560 / 1920.0)]
    [InlineData(3840, 2160, 2.0)]
    [InlineData(1280, 1920 * 1.5, 1.5)]
    public void ALargeWindowEnlargesTheMockup(double width, double height, double expected)
    {
        Assert.Equal(expected, DesktopZoom.Factor(width, height, UiSize.Compact), 9);
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1366, 768)]
    [InlineData(800, 1280)]
    [InlineData(960, 540)]
    public void AWindowSmallerThanTheMockupIsNotShrunk(double width, double height)
    {
        Assert.Equal(1.0, DesktopZoom.Factor(width, height, UiSize.Compact));
    }

    [Theory]
    [InlineData(800, 600, 800 / 960.0)]
    [InlineData(480, 270, 0.5)]
    public void AWindowSmallerThanTheShortestSurfaceShrinksIt(double width, double height, double expected)
    {
        Assert.Equal(expected, DesktopZoom.Factor(width, height, UiSize.Compact), 9);
    }

    [Theory]
    [InlineData(1920, 1.0)]
    [InlineData(2560, 1.0)]
    [InlineData(1920, 2.0)]
    [InlineData(3840, 1.5)]
    public void AWindowAtLeastTheMockupsSizeLaysOutOneUnitPerMockupPixel(double windowLongSide, double scaling)
    {
        var display = DesktopZoom.Display(windowLongSide, scaling, UiSize.Compact);

        Assert.Equal(windowLongSide * scaling, display.LongSidePx, 9);
        Assert.Equal(scaling * DesktopZoom.Factor(windowLongSide, 0, UiSize.Compact), display.Density, 9);
        foreach (var pixels in new[] { 1.0, 14, 37, 1080 })
            Assert.Equal(pixels, DesignMetrics.Design(pixels, display), 9);
    }

    [Theory]
    [InlineData(1280, 1.0)]
    [InlineData(1366, 1.25)]
    public void ASmallerWindowShrinksMockupLengthsWithIt(double windowLongSide, double scaling)
    {
        var display = DesktopZoom.Display(windowLongSide, scaling, UiSize.Compact);

        Assert.Equal(scaling, display.Density, 9);
        Assert.Equal(32 * windowLongSide / 1920, DesignMetrics.Design(32, display), 9);
    }

    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(1280, 800, 1280, 800)]
    [InlineData(2560, 1440, 1920, 1080)]
    [InlineData(2560, 1600, 1920, 1200)]
    [InlineData(1080, 1920, 1080, 1920)]
    [InlineData(1000, 1000, 1000, 1000)]
    [InlineData(800, 600, 960, 720)]
    public void TheLogicalSizeKeepsTheWindowAspect(double width, double height, double logicalWidth, double logicalHeight)
    {
        var factor = DesktopZoom.Factor(width, height, UiSize.Compact);

        Assert.Equal(logicalWidth, width / factor, 9);
        Assert.Equal(logicalHeight, height / factor, 9);
    }

    [Theory]
    [InlineData(1920, 1080, 1.0, 1920, 1080)]
    [InlineData(2560, 1440, 1.0, 1920, 1080)]
    [InlineData(1280, 800, 1.0, 1280, 800)]
    [InlineData(1080, 1920, 1.0, 1080, 1920)]
    [InlineData(1366, 768, 1.0, 1366, 768)]
    [InlineData(1366, 768, 1.25, 1365.6, 768)]
    [InlineData(2048, 1152, 1.25, 1920, 1080)]
    [InlineData(3000, 1999, 1.0, 1920, 1279)]
    [InlineData(800, 600, 1.0, 960, 720)]
    public void TheSurfaceIsExactAndRoundedDownToWholeDevicePixels(double width, double height, double scaling, double surfaceWidth, double surfaceHeight)
    {
        var (w, h) = DesktopZoom.Surface(width, height, scaling, UiSize.Compact);

        Assert.Equal(surfaceWidth, w, 9);
        Assert.Equal(surfaceHeight, h, 9);
        Assert.Equal(Math.Floor(w * scaling + 1e-6), w * scaling, 6);
        Assert.Equal(Math.Floor(h * scaling + 1e-6), h * scaling, 6);
    }

    [Theory]
    [InlineData(2560, 1440, 1.0)]
    [InlineData(1280, 800, 1.0)]
    [InlineData(1366, 768, 1.0)]
    [InlineData(1366, 768, 1.5)]
    [InlineData(3000, 1999, 1.0)]
    public void TheZoomedSurfaceFitsTheWindow(double width, double height, double scaling)
    {
        var (w, h) = DesktopZoom.Surface(width, height, scaling, UiSize.Compact);
        var factor = DesktopZoom.Factor(width, height, UiSize.Compact);

        Assert.True(w * factor <= width + 1e-6);
        Assert.True(h * factor <= height + 1e-6);
        Assert.True(h * factor > height - factor - 1e-6);
    }

    [Theory]
    [InlineData(UiSize.Compact, 1920)]
    [InlineData(UiSize.Standard, 1280)]
    [InlineData(UiSize.Large, 960)]
    public void EachUiSizeSetsTheLongestSurface(UiSize size, double longSide)
    {
        Assert.Equal(longSide, DesktopZoom.LongSide(size));
    }

    [Theory]
    [InlineData(UiSize.Compact, 1.0)]
    [InlineData(UiSize.Standard, 1.5)]
    [InlineData(UiSize.Large, 2.0)]
    public void ALargerUiSizeDrawsAMockupSizedWindowLarger(UiSize size, double expected)
    {
        Assert.Equal(expected, DesktopZoom.Factor(1920, 1080, size), 9);
    }

    [Theory]
    [InlineData(1920, 1080, UiSize.Standard, 1280, 720)]
    [InlineData(2560, 1440, UiSize.Standard, 1280, 720)]
    [InlineData(1600, 900, UiSize.Standard, 1280, 720)]
    [InlineData(1280, 800, UiSize.Standard, 1280, 800)]
    [InlineData(1366, 768, UiSize.Standard, 1280, 719)]
    [InlineData(1920, 1080, UiSize.Large, 960, 540)]
    [InlineData(2560, 1600, UiSize.Large, 960, 600)]
    [InlineData(1280, 800, UiSize.Large, 960, 600)]
    [InlineData(800, 600, UiSize.Large, 960, 720)]
    [InlineData(1080, 1920, UiSize.Large, 540, 960)]
    public void EachUiSizeLaysOutOnItsOwnSurface(double width, double height, UiSize size, double surfaceWidth, double surfaceHeight)
    {
        var (w, h) = DesktopZoom.Surface(width, height, 1.0, size);

        Assert.Equal(surfaceWidth, w, 9);
        Assert.Equal(surfaceHeight, h, 9);
    }

    [Fact]
    public void NoUiSizeLaysOutOnASurfaceShorterThanTheMinimum()
    {
        foreach (var size in Enum.GetValues<UiSize>())
            for (var longSide = 400.0; longSide <= 3840; longSide += 40)
            {
                var surface = longSide / DesktopZoom.Factor(longSide, longSide * 9 / 16, size);
                Assert.InRange(surface, DesktopZoom.MinLongSide - 1e-9, DesktopZoom.LongSide(size) + 1e-9);
            }
    }

    [Fact]
    public void ALargerUiSizeNeverDrawsSmaller()
    {
        for (var longSide = 400.0; longSide <= 3840; longSide += 40)
        {
            var compact = DesktopZoom.Factor(longSide, longSide * 10 / 16, UiSize.Compact);
            var standard = DesktopZoom.Factor(longSide, longSide * 10 / 16, UiSize.Standard);
            var large = DesktopZoom.Factor(longSide, longSide * 10 / 16, UiSize.Large);
            Assert.True(compact <= standard && standard <= large, $"{longSide}: {compact} {standard} {large}");
        }
    }

    [Theory]
    [InlineData(1920, 1.0, UiSize.Standard)]
    [InlineData(2560, 1.0, UiSize.Large)]
    [InlineData(1280, 1.0, UiSize.Large)]
    [InlineData(1600, 1.25, UiSize.Standard)]
    [InlineData(1366, 1.0, UiSize.Compact)]
    public void AMockupLengthKeepsItsShareOfTheWindowAtEveryUiSize(double windowLongSide, double scaling, UiSize size)
    {
        var display = DesktopZoom.Display(windowLongSide, scaling, size);
        var factor = DesktopZoom.Factor(windowLongSide, windowLongSide, size);

        Assert.Equal(scaling * factor, display.Density, 9);
        Assert.Equal(windowLongSide, DesignMetrics.Design(DesignMetrics.MockupLongSide, display) * factor, 9);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 800)]
    [InlineData(double.NaN, 800)]
    [InlineData(1280, double.PositiveInfinity)]
    public void AnUnknownWindowHasNoFixedSurface(double width, double height)
    {
        var (w, h) = DesktopZoom.Surface(width, height, 1.0, UiSize.Compact);

        Assert.True(double.IsNaN(w));
        Assert.True(double.IsNaN(h));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, -5)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, 800)]
    public void AnUnknownWindowIsNotZoomed(double width, double height)
    {
        Assert.Equal(1.0, DesktopZoom.Factor(width, height, UiSize.Compact));
    }

    [Theory]
    [InlineData(1.0, 120f, 80f, 120f, 80f)]
    [InlineData(2560 / 1920.0, 300f, 150f, 400f, 200f)]
    [InlineData(1280 / 1920.0, 300f, 150f, 200f, 100f)]
    [InlineData(2.0, 0f, 0f, 0f, 0f)]
    public void ATouchMovesIntoTheZoomedSurfacePixels(double zoom, float x, float y, float surfaceX, float surfaceY)
    {
        var (sx, sy) = DesktopZoom.TouchToSurface(x, y, zoom, ignorePixelScaling: false);

        Assert.Equal(surfaceX, sx, 3);
        Assert.Equal(surfaceY, sy, 3);
    }

    [Fact]
    public void ATouchOnACanvasThatIgnoresPixelScalingStaysInDeviceIndependentUnits()
    {
        Assert.Equal((300f, 150f), DesktopZoom.TouchToSurface(300f, 150f, 1.5, ignorePixelScaling: true));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnInvalidZoomLeavesTheTouchWhereItWas(double zoom)
    {
        Assert.Equal((300f, 150f), DesktopZoom.TouchToSurface(300f, 150f, zoom, ignorePixelScaling: false));
    }

    [Fact]
    public void SetRaisesChangedOnlyForANewValidFactor()
    {
        DesktopZoom.Set(1.0);
        var raised = 0;
        void Count() => raised++;
        DesktopZoom.Changed += Count;
        try
        {
            Assert.False(DesktopZoom.Set(1.0));
            Assert.False(DesktopZoom.Set(0));
            Assert.False(DesktopZoom.Set(-1));
            Assert.False(DesktopZoom.Set(double.NaN));
            Assert.False(DesktopZoom.Set(double.PositiveInfinity));
            Assert.Equal(0, raised);
            Assert.Equal(1.0, DesktopZoom.Current);

            Assert.True(DesktopZoom.Set(1.5));
            Assert.Equal(1, raised);
            Assert.Equal(1.5, DesktopZoom.Current);

            Assert.False(DesktopZoom.Set(1.5));
            Assert.Equal(1, raised);
        }
        finally
        {
            DesktopZoom.Changed -= Count;
            DesktopZoom.Set(1.0);
        }
    }
}
