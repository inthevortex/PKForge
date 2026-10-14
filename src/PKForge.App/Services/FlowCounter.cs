namespace PKForge.App.Services;

/// <summary>
/// Counts the flows running at once (a menu or prompt, or the work between two of them);
/// a flow counts from <see cref="Begin"/> until its scope is disposed. Thread-safe.
/// </summary>
public sealed class FlowCounter
{
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) > 0;

    /// <summary>Counts a flow as running until the returned scope is disposed; disposing it again does nothing.</summary>
    public IDisposable Begin()
    {
        Interlocked.Increment(ref _running);
        return new Scope(this);
    }

    private sealed class Scope(FlowCounter counter) : IDisposable
    {
        private FlowCounter? _counter = counter;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _counter, null) is { } owner) Interlocked.Decrement(ref owner._running);
        }
    }
}
