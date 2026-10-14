namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Holds back disposing images dropped from the sprite caches while a canvas paint runs.
/// Canvases paint on Avalonia's render thread while the caches evict from background threads:
/// an image a paint already fetched must outlive that paint. Paints fetch images afresh from
/// the caches and never keep them, so once no paint is running, nothing evicted can be drawn.
/// </summary>
/// <param name="dispose">Disposes a released image.</param>
internal sealed class PaintFence(Action<IDisposable> dispose)
{
    private readonly Lock _gate = new();
    private int _painting;
    private List<IDisposable> _held = [];

    /// <summary>A paint starts; pair with <see cref="Exit"/>.</summary>
    public void Enter()
    {
        lock (_gate) _painting++;
    }

    /// <summary>A paint ended; the last running paint to end releases what was retired meanwhile.</summary>
    public void Exit()
    {
        List<IDisposable> released;
        lock (_gate)
        {
            if (--_painting > 0 || _held.Count == 0) return;
            released = _held;
            _held = [];
        }
        foreach (var item in released) dispose(item);
    }

    /// <summary>Disposes <paramref name="item"/> now when no paint is running, otherwise once the running paints end.</summary>
    public void Retire(IDisposable item)
    {
        lock (_gate)
        {
            if (_painting > 0)
            {
                _held.Add(item);
                return;
            }
        }
        dispose(item);
    }
}
