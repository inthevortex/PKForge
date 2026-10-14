namespace PKForge.App.Services;

/// <summary>
/// Which prepared HOME renders are ready, loading or failing, per render and prepared size: the
/// state behind <c>ISpriteService.TryGetHomeImage</c>. Thread-safe. Failures are kept per
/// render, whatever size asked for it, and are retried once <paramref name="retryDelay"/> has
/// passed; images pushed out past <paramref name="capacity"/> go to <paramref name="retire"/>.
/// </summary>
internal sealed class HomeImageCache<T>(int capacity, TimeSpan retryDelay, Func<DateTime> now, Action<T> retire)
    where T : class
{
    /// <summary>
    /// What a paint should do: <paramref name="Known"/> and <paramref name="Image"/> as
    /// <c>ISpriteService.TryGetHomeImage</c> returns them, and whether the caller now
    /// owns preparing this size (<paramref name="Load"/>), reporting back with
    /// <see cref="Complete"/> or <see cref="Fail"/>.
    /// </summary>
    public readonly record struct Lookup(bool Known, T? Image, bool Load);

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Render, int Side), T?> _images = [];
    private readonly Queue<(string Render, int Side)> _eviction = new();
    private readonly HashSet<(string Render, int Side)> _loading = [];
    // The last size prepared for each render, the stand-in while another size is prepared.
    private readonly Dictionary<string, int> _latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _retryAfter = new(StringComparer.Ordinal);

    public Lookup Get(string render, int side)
    {
        var key = (render, side);
        lock (_gate)
        {
            if (_images.TryGetValue(key, out var image)) return new Lookup(true, image, false);
            var failedBefore = _retryAfter.TryGetValue(render, out var after);
            // A recent failure (offline) stands in with the pixel sprite until it is worth retrying.
            if (failedBefore && now() < after) return new Lookup(true, null, false);
            var standIn = _latest.TryGetValue(render, out var latest) && _images.TryGetValue((render, latest), out var made) ? made : null;
            // A retry after a failure keeps the pixel sprite up instead of blanking while it runs.
            var known = failedBefore || standIn is not null;
            if (!_loading.Add(key)) return new Lookup(known, standIn, false);
            // One retry at a time per render, however many sizes ask for it.
            if (failedBefore) _retryAfter[render] = now() + retryDelay;
            return new Lookup(known, standIn, true);
        }
    }

    /// <summary>Stores a prepared size; null when the render has no usable art, so the pixel sprite stands in from then on.</summary>
    public void Complete(string render, int side, T? image)
    {
        var key = (render, side);
        List<T>? evicted = null;
        lock (_gate)
        {
            _loading.Remove(key);
            _retryAfter.Remove(render);
            if (_images.TryGetValue(key, out var replaced))
            {
                if (replaced is not null && !ReferenceEquals(replaced, image)) (evicted ??= []).Add(replaced);
            }
            else
            {
                _eviction.Enqueue(key);
            }
            _images[key] = image;
            _latest[render] = side;
            while (_images.Count > capacity && _eviction.TryDequeue(out var oldest))
            {
                if (_images.Remove(oldest, out var old) && old is not null) (evicted ??= []).Add(old);
            }
        }
        evicted?.ForEach(retire);
    }

    /// <summary>A size could not be prepared: the render waits <c>retryDelay</c> before the next try.</summary>
    public void Fail(string render, int side)
    {
        lock (_gate)
        {
            _loading.Remove((render, side));
            _retryAfter[render] = now() + retryDelay;
        }
    }

    /// <summary>Records a failure to load <paramref name="render"/> by another path, as <see cref="Fail"/> does.</summary>
    public void RecordFailure(string render)
    {
        lock (_gate) _retryAfter[render] = now() + retryDelay;
    }

    /// <summary>True while a recent failure keeps <paramref name="render"/> from being retried.</summary>
    public bool IsFailing(string render)
    {
        lock (_gate) return _retryAfter.TryGetValue(render, out var after) && now() < after;
    }
}
