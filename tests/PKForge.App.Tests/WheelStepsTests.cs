using PKForge.App.Views;
using Xunit;

namespace PKForge.App.Tests;

public sealed class WheelStepsTests
{
    [Theory]
    [InlineData(120, -1)]
    [InlineData(-120, 1)]
    [InlineData(240, -2)]
    [InlineData(-360, 3)]
    public void Add_TakesAStepPerMouseNotch(int delta, int expected)
    {
        Assert.Equal(expected, new WheelSteps().Add(delta));
    }

    [Fact]
    public void Add_IgnoresAZeroDelta()
    {
        var wheel = new WheelSteps();
        Assert.Equal(0, wheel.Add(0));
        Assert.Equal(-1, wheel.Add(120));
    }

    [Fact]
    public void Add_GathersSmallTouchpadDeltasIntoOneStep()
    {
        var wheel = new WheelSteps();
        var steps = Enumerable.Range(0, 10).Select(_ => wheel.Add(-30)).ToList();

        Assert.Equal(new[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0 }, steps);
    }

    [Fact]
    public void Add_DropsWhatWasGatheredWhenTheDirectionTurns()
    {
        var wheel = new WheelSteps();
        Assert.Equal(0, wheel.Add(100));
        Assert.Equal(0, wheel.Add(-100));
        Assert.Equal(1, wheel.Add(-20));
    }

    [Fact]
    public void Add_KeepsTheRemainderTowardTheNextStep()
    {
        var wheel = new WheelSteps();
        Assert.Equal(-1, wheel.Add(170));
        Assert.Equal(-1, wheel.Add(70));
    }
}
