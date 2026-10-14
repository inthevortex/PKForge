using PKForge.App.Platforms.Desktop;
using Xunit;

namespace PKForge.App.Tests;

public sealed class PaintFenceTests
{
    private readonly List<IDisposable> _disposed = [];

    private sealed class Image : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Fact]
    public void WithNoPaintRunningAnImageIsDisposedAtOnce()
    {
        var fence = new PaintFence(_disposed.Add);
        var image = new Image();

        fence.Retire(image);

        Assert.Same(image, Assert.Single(_disposed));
    }

    [Fact]
    public void AnImageRetiredDuringAPaintOutlivesIt()
    {
        var fence = new PaintFence(_disposed.Add);
        var image = new Image();

        fence.Enter();
        fence.Retire(image);
        Assert.Empty(_disposed);
        fence.Exit();

        Assert.Same(image, Assert.Single(_disposed));
    }

    [Fact]
    public void OverlappingPaintsReleaseOnlyWhenTheLastEnds()
    {
        var fence = new PaintFence(_disposed.Add);
        var image = new Image();

        fence.Enter();
        fence.Retire(image);
        fence.Enter();
        fence.Exit();
        Assert.Empty(_disposed);
        fence.Exit();

        Assert.Same(image, Assert.Single(_disposed));
    }

    [Fact]
    public void AnImageIsReleasedOnce()
    {
        var fence = new PaintFence(_disposed.Add);
        var image = new Image();

        fence.Enter();
        fence.Retire(image);
        fence.Exit();
        fence.Enter();
        fence.Exit();

        Assert.Same(image, Assert.Single(_disposed));
    }

    [Fact]
    public async Task PaintsOnAnotherThreadHoldBackDisposal()
    {
        var disposed = 0;
        var fence = new PaintFence(_ => Interlocked.Increment(ref disposed));
        using var painting = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();

        var paint = Task.Run(() =>
        {
            fence.Enter();
            painting.Set();
            finish.Wait();
            fence.Exit();
        });
        painting.Wait();
        fence.Retire(new Image());
        Assert.Equal(0, Volatile.Read(ref disposed));
        finish.Set();
        await paint;

        Assert.Equal(1, Volatile.Read(ref disposed));
    }
}
