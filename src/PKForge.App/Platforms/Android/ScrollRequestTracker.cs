namespace PKForge.App.Platforms.Android;

/// <summary>
/// The per-view bookkeeping of programmatic scrolls: which request is current, and which
/// smooth scroll may complete it. Each new request supersedes the previous one.
/// </summary>
internal sealed class ScrollRequestTracker<TAnimation> where TAnimation : class
{
    private TAnimation? _animation;

    public int Generation { get; private set; }

    /// <summary>
    /// Starts a new request and returns its generation. <paramref name="running"/> is the smooth
    /// scroll in flight, for the caller to stop; it no longer completes anything.
    /// </summary>
    public int Supersede(out TAnimation? running)
    {
        running = _animation;
        _animation = null;
        return ++Generation;
    }

    public bool IsCurrent(int generation) => Generation == generation;

    public void Started(TAnimation animation) => _animation = animation;

    /// <summary>
    /// True when <paramref name="animation"/> is the current smooth scroll, whose end completes
    /// its request. A superseded or already ended one returns false.
    /// </summary>
    public bool Ended(TAnimation animation)
    {
        if (!ReferenceEquals(_animation, animation)) return false;
        _animation = null;
        return true;
    }
}

internal enum ScrollWaitOutcome
{
    /// <summary>A newer request on the view replaced this one and completes it.</summary>
    Drop,
    /// <summary>The view is still the handler's and the request is current: scroll now.</summary>
    Serve,
    /// <summary>The handler moved to another view: complete the request without scrolling.</summary>
    Finish,
}

/// <summary>
/// A request waiting for its view's first layout. Both the layout and the timeout end the wait,
/// and only the first of them acts.
/// </summary>
internal sealed class ScrollLayoutWait<TAnimation>(ScrollRequestTracker<TAnimation> tracker, int generation)
    where TAnimation : class
{
    private int _fired;

    /// <summary>True only for the first call.</summary>
    public bool TryFire() => Interlocked.Exchange(ref _fired, 1) == 0;

    public ScrollWaitOutcome Resolve(bool handlerOwnsView) =>
        !handlerOwnsView ? ScrollWaitOutcome.Finish
        : tracker.IsCurrent(generation) ? ScrollWaitOutcome.Serve
        : ScrollWaitOutcome.Drop;
}
