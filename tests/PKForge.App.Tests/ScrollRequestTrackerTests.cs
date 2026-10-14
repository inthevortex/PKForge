using PKForge.App.Platforms.Android;
using Xunit;

namespace PKForge.App.Tests;

public sealed class ScrollRequestTrackerTests
{
    private sealed class Animation;

    [Fact]
    public void EachRequestGetsANewCurrentGeneration()
    {
        var tracker = new ScrollRequestTracker<Animation>();

        var first = tracker.Supersede(out _);
        var second = tracker.Supersede(out _);

        Assert.NotEqual(first, second);
        Assert.False(tracker.IsCurrent(first));
        Assert.True(tracker.IsCurrent(second));
    }

    [Fact]
    public void SupersedeHandsOverTheRunningAnimationOnce()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var animation = new Animation();
        tracker.Supersede(out _);
        tracker.Started(animation);

        tracker.Supersede(out var running);
        tracker.Supersede(out var again);

        Assert.Same(animation, running);
        Assert.Null(again);
    }

    [Fact]
    public void ACurrentAnimationCompletesItsRequestExactlyOnce()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var animation = new Animation();
        tracker.Supersede(out _);
        tracker.Started(animation);

        Assert.True(tracker.Ended(animation));
        Assert.False(tracker.Ended(animation));
    }

    [Fact]
    public void ACancelledAnimationDoesNotCompleteAnything()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var cancelled = new Animation();
        tracker.Supersede(out _);
        tracker.Started(cancelled);

        tracker.Supersede(out _);

        Assert.False(tracker.Ended(cancelled));
    }

    [Fact]
    public void OnlyTheNewestAnimationCompletes()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var older = new Animation();
        var newer = new Animation();
        tracker.Supersede(out _);
        tracker.Started(older);
        tracker.Supersede(out _);
        tracker.Started(newer);

        Assert.False(tracker.Ended(older));
        Assert.True(tracker.Ended(newer));
    }

    [Fact]
    public void ALayoutWaitFiresOnlyOnce()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var wait = new ScrollLayoutWait<Animation>(tracker, tracker.Supersede(out _));

        Assert.True(wait.TryFire());
        Assert.False(wait.TryFire());
    }

    [Fact]
    public void ALayoutWaitServesItsRequestWhileCurrent()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var wait = new ScrollLayoutWait<Animation>(tracker, tracker.Supersede(out _));

        Assert.Equal(ScrollWaitOutcome.Serve, wait.Resolve(handlerOwnsView: true));
    }

    [Fact]
    public void ASupersededLayoutWaitLeavesCompletionToTheNewerRequest()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var wait = new ScrollLayoutWait<Animation>(tracker, tracker.Supersede(out _));
        tracker.Supersede(out _);

        Assert.Equal(ScrollWaitOutcome.Drop, wait.Resolve(handlerOwnsView: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALayoutWaitOnADetachedViewFinishesWithoutScrolling(bool superseded)
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var wait = new ScrollLayoutWait<Animation>(tracker, tracker.Supersede(out _));
        if (superseded) tracker.Supersede(out _);

        Assert.Equal(ScrollWaitOutcome.Finish, wait.Resolve(handlerOwnsView: false));
    }

    [Fact]
    public async Task ConcurrentFiresActOnce()
    {
        var tracker = new ScrollRequestTracker<Animation>();
        var wait = new ScrollLayoutWait<Animation>(tracker, tracker.Supersede(out _));
        using var start = new ManualResetEventSlim();

        var fires = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return wait.TryFire();
        })).ToArray();
        start.Set();

        Assert.Single(await Task.WhenAll(fires), fired => fired);
    }
}
