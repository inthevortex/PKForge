using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class FlowCounterTests
{
    [Fact]
    public void NothingRunsBeforeAFlowBegins() => Assert.False(new FlowCounter().IsRunning);

    [Fact]
    public void AFlowRunsUntilItsScopeIsDisposed()
    {
        var flows = new FlowCounter();
        var flow = flows.Begin();
        Assert.True(flows.IsRunning);

        flow.Dispose();
        Assert.False(flows.IsRunning);
    }

    [Fact]
    public void NestedFlowsRunUntilTheOuterOneEnds()
    {
        var flows = new FlowCounter();
        using (flows.Begin())
        {
            using (flows.Begin())
                Assert.True(flows.IsRunning);
            Assert.True(flows.IsRunning);
        }
        Assert.False(flows.IsRunning);
    }

    [Fact]
    public void DisposingAScopeTwiceCountsOnce()
    {
        var flows = new FlowCounter();
        var outer = flows.Begin();
        var inner = flows.Begin();

        inner.Dispose();
        inner.Dispose();
        Assert.True(flows.IsRunning);

        outer.Dispose();
        Assert.False(flows.IsRunning);
    }

    [Fact]
    public void AFlowThatThrowsStillEnds()
    {
        var flows = new FlowCounter();

        void FailingFlow()
        {
            using var flow = flows.Begin();
            throw new InvalidOperationException("the flow failed");
        }

        Assert.Throws<InvalidOperationException>(FailingFlow);
        Assert.False(flows.IsRunning);
    }

    [Fact]
    public async Task FlowsBegunAndEndedAcrossThreadsBalance()
    {
        var flows = new FlowCounter();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                var flow = flows.Begin();
                flow.Dispose();
                flow.Dispose();
            }
        })));

        Assert.False(flows.IsRunning);
    }
}
