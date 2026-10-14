using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class DesignMetricsTests
{
    [Theory]
    [InlineData(1920, 2.0, 64, 32)]
    [InlineData(1920, 1.0, 32, 32)]
    [InlineData(1280, 1.0, 32, 32 * 1280 / 1920.0)]
    [InlineData(2560, 1.0, 32, 32 * 2560 / 1920.0)]
    [InlineData(3840, 2.0, 32, 32)]
    public void DesignScalesTheMockupByTheLongSideInDips(double longSidePx, double density, double pixels, double expected)
    {
        var design = DesignMetrics.Design(pixels, new DisplaySnapshot(longSidePx, density));

        Assert.Equal(expected, design, 9);
    }

    [Fact]
    public void DesignMatchesTheAndroidFormulaBitForBit()
    {
        const double longSide = 2400, density = 2.625, pixels = 37;

        Assert.Equal(pixels * (longSide / 1920) / density, DesignMetrics.Design(pixels, new DisplaySnapshot(longSide, density)));
    }

    [Theory]
    [InlineData(0, 2.0)]
    [InlineData(1920, 0)]
    [InlineData(0, 0)]
    public void DesignHalvesTheMockupWhileTheDisplayIsUnknown(double longSidePx, double density)
    {
        Assert.Equal(16, DesignMetrics.Design(32, new DisplaySnapshot(longSidePx, density)));
    }

    [Fact]
    public void CacheShowsTheLastReadingWithoutMeasuring()
    {
        var calls = 0;
        var cache = new DesignMetricsCache(() => { calls++; return new DisplaySnapshot(1920, 1.0); });

        Assert.Null(cache.Last);
        Assert.Equal(0, calls);

        _ = cache.Current;
        Assert.Equal(1920, cache.Last?.LongSidePx);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void CacheMeasuresOnceUntilRefreshed()
    {
        var calls = 0;
        var longSide = 1920.0;
        var cache = new DesignMetricsCache(() => { calls++; return new DisplaySnapshot(longSide, 1.0); });

        Assert.Equal(1920, cache.Current.LongSidePx);
        longSide = 2560;
        Assert.Equal(1920, cache.Current.LongSidePx);
        Assert.Equal(1, calls);

        Assert.Equal(2560, cache.Refresh().LongSidePx);
        Assert.Equal(2560, cache.Current.LongSidePx);
    }

    [Fact]
    public void CacheKeepsMeasuringWhileTheDisplayIsUnknown()
    {
        var calls = 0;
        var reading = new DisplaySnapshot(0, 0);
        var cache = new DesignMetricsCache(() => { calls++; return reading; });

        Assert.False(cache.Current.IsValid);
        Assert.False(cache.Current.IsValid);
        Assert.Equal(2, calls);

        reading = new DisplaySnapshot(1920, 2.0);
        Assert.Equal(1920, cache.Current.LongSidePx);
        Assert.Equal(1920, cache.Current.LongSidePx);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void RefreshKeepsTheLastValidReading()
    {
        var reading = new DisplaySnapshot(1920, 1.0);
        var cache = new DesignMetricsCache(() => reading);
        _ = cache.Current;

        reading = new DisplaySnapshot(0, 0);

        Assert.Equal(1920, cache.Refresh().LongSidePx);
    }

    [Fact]
    public void InvalidateMakesTheNextReadMeasureOnceAgain()
    {
        var calls = 0;
        var longSide = 1920.0;
        var cache = new DesignMetricsCache(() => { calls++; return new DisplaySnapshot(longSide, 2.0); });
        _ = cache.Current;

        longSide = 2560;
        cache.Invalidate();

        Assert.Equal(1, calls);
        Assert.Equal(2560, cache.Current.LongSidePx);
        Assert.Equal(2560, cache.Current.LongSidePx);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void InvalidateKeepsTheLastValidReadingUntilTheDisplayReportsAgain()
    {
        var calls = 0;
        var reading = new DisplaySnapshot(1920, 2.0);
        var cache = new DesignMetricsCache(() => { calls++; return reading; });
        _ = cache.Current;

        reading = new DisplaySnapshot(0, 0);
        cache.Invalidate();

        Assert.Equal(1920, cache.Current.LongSidePx);
        Assert.Equal(1920, cache.Current.LongSidePx);
        Assert.Equal(3, calls);

        reading = new DisplaySnapshot(2560, 2.0);
        Assert.Equal(2560, cache.Current.LongSidePx);
        Assert.Equal(2560, cache.Current.LongSidePx);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void InvalidateBeforeAnyReadingStillMeasuresOnce()
    {
        var calls = 0;
        var cache = new DesignMetricsCache(() => { calls++; return new DisplaySnapshot(1920, 1.0); });

        cache.Invalidate();

        Assert.Equal(1920, cache.Current.LongSidePx);
        Assert.Equal(1920, cache.Current.LongSidePx);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RefreshClearsAPendingInvalidation()
    {
        var calls = 0;
        var cache = new DesignMetricsCache(() => { calls++; return new DisplaySnapshot(1920, 1.0); });
        _ = cache.Current;
        cache.Invalidate();

        cache.Refresh();
        _ = cache.Current;

        Assert.Equal(2, calls);
    }
}
