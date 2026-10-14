using PKForge.App.Views;
using Xunit;

namespace PKForge.App.Tests;

public sealed class ProgressCoalescerTests
{
    private long _now = 10_000;

    private ProgressCoalescer Coalescer() => new(100, () => _now);

    [Fact]
    public void TheFirstReportIsShownAtOnce()
    {
        var wait = Coalescer().Offer("1 / 10", 0.1);

        Assert.NotNull(wait);
        Assert.True(wait <= 0);
    }

    [Fact]
    public void ReportsWhileAnUpdateIsPendingJoinIt()
    {
        var progress = Coalescer();
        progress.Offer("1 / 10", 0.1);

        Assert.Null(progress.Offer("2 / 10", 0.2));
        Assert.Null(progress.Offer("3 / 10", 0.3));
        Assert.Equal(("3 / 10", 0.3), progress.Take());
    }

    [Fact]
    public void TheNextUpdateWaitsOutTheInterval()
    {
        var progress = Coalescer();
        progress.Offer("1 / 10", 0.1);
        progress.Take();

        _now += 30;
        Assert.Equal(70, progress.Offer("2 / 10", 0.2));
        Assert.Null(progress.Offer("3 / 10", 0.3));

        _now += 70;
        Assert.Equal(("3 / 10", 0.3), progress.Take());
    }

    [Fact]
    public void AReportAfterTheIntervalIsShownAtOnce()
    {
        var progress = Coalescer();
        progress.Offer("1 / 10", 0.1);
        progress.Take();

        _now += 250;

        Assert.True(progress.Offer("2 / 10", 0.2) <= 0);
    }
}
