using PKForge.App.Services;
using Xunit;

namespace PKForge.App.Tests;

public sealed class HomeImageCacheTests
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private sealed class Image;

    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly List<Image> _retired = [];

    private HomeImageCache<Image> Cache(int capacity = 8) => new(capacity, RetryDelay, () => _now, _retired.Add);

    [Fact]
    public void AFirstLookStartsOneLoadAndDrawsNothingMeanwhile()
    {
        var cache = Cache();

        var first = cache.Get("6.png", 128);
        var again = cache.Get("6.png", 128);

        Assert.Equal(new HomeImageCache<Image>.Lookup(false, null, true), first);
        Assert.Equal(new HomeImageCache<Image>.Lookup(false, null, false), again);
    }

    [Fact]
    public void APreparedSizeIsReturnedAsIs()
    {
        var cache = Cache();
        var image = new Image();
        cache.Get("6.png", 128);
        cache.Complete("6.png", 128, image);

        Assert.Equal(new HomeImageCache<Image>.Lookup(true, image, false), cache.Get("6.png", 128));
    }

    [Fact]
    public void ARenderWithNoUsableArtIsKnownAndEmpty()
    {
        var cache = Cache();
        cache.Get("6.png", 128);
        cache.Complete("6.png", 128, null);

        Assert.Equal(new HomeImageCache<Image>.Lookup(true, null, false), cache.Get("6.png", 128));
    }

    [Fact]
    public void ASizeMadeEarlierStandsInWhileANewSizeIsPrepared()
    {
        var cache = Cache();
        var small = new Image();
        cache.Get("6.png", 128);
        cache.Complete("6.png", 128, small);

        var bigger = cache.Get("6.png", 256);
        var whileLoading = cache.Get("6.png", 256);

        Assert.Equal(new HomeImageCache<Image>.Lookup(true, small, true), bigger);
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, small, false), whileLoading);
    }

    [Fact]
    public void AFailureShowsThePixelSpriteForEverySizeUntilTheRetryIsDue()
    {
        var cache = Cache();
        cache.Get("6.png", 128);
        cache.Fail("6.png", 128);

        Assert.Equal(new HomeImageCache<Image>.Lookup(true, null, false), cache.Get("6.png", 128));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, null, false), cache.Get("6.png", 256));
        Assert.True(cache.IsFailing("6.png"));

        _now += RetryDelay;
        Assert.False(cache.IsFailing("6.png"));
        // The retry keeps the pixel sprite up, and only one size retries at a time.
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, null, true), cache.Get("6.png", 128));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, null, false), cache.Get("6.png", 256));
    }

    [Fact]
    public void ASuccessfulRetryClearsTheFailure()
    {
        var cache = Cache();
        cache.Get("6.png", 128);
        cache.Fail("6.png", 128);
        _now += RetryDelay;
        cache.Get("6.png", 128);
        var image = new Image();
        cache.Complete("6.png", 128, image);

        Assert.False(cache.IsFailing("6.png"));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, image, false), cache.Get("6.png", 128));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, image, true), cache.Get("6.png", 256));
    }

    [Fact]
    public void AFailureRecordedByAnotherPathHoldsBackPreparedSizesToo()
    {
        var cache = Cache();
        cache.RecordFailure("6.png");

        Assert.True(cache.IsFailing("6.png"));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, null, false), cache.Get("6.png", 128));
    }

    [Fact]
    public void TheOldestImagesAreRetiredPastTheCapacity()
    {
        var cache = Cache(capacity: 2);
        var images = new[] { new Image(), new Image(), new Image() };
        for (var i = 0; i < images.Length; i++)
        {
            cache.Get($"{i}.png", 128);
            cache.Complete($"{i}.png", 128, images[i]);
        }

        Assert.Same(images[0], Assert.Single(_retired));
        // The evicted size is prepared again, with nothing left to stand in.
        Assert.Equal(new HomeImageCache<Image>.Lookup(false, null, true), cache.Get("0.png", 128));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, images[2], false), cache.Get("2.png", 128));
    }

    [Fact]
    public void AnEvictedSizeIsPreparedAgainWithTheLatestStandingIn()
    {
        var cache = Cache(capacity: 1);
        var first = new Image();
        cache.Get("6.png", 128);
        cache.Complete("6.png", 128, first);
        cache.Get("6.png", 256);
        var second = new Image();
        cache.Complete("6.png", 256, second);

        Assert.Same(first, Assert.Single(_retired));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, second, true), cache.Get("6.png", 128));
    }

    [Fact]
    public void ReplacingASizeRetiresTheImageItReplaces()
    {
        var cache = Cache();
        var first = new Image();
        var second = new Image();
        cache.Complete("6.png", 128, first);
        cache.Complete("6.png", 128, second);

        Assert.Same(first, Assert.Single(_retired));
        Assert.Equal(new HomeImageCache<Image>.Lookup(true, second, false), cache.Get("6.png", 128));
    }
}
