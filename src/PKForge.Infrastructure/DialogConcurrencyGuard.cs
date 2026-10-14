namespace PKForge.Infrastructure;

/// <summary>
/// Concurrency guard ensuring only a single file/folder dialog is active at any time.
/// Throws <see cref="InvalidOperationException"/> if another dialog is already active.
/// </summary>
public static class DialogConcurrencyGuard
{
    private static int _active;

    /// <summary>
    /// Attempts to enter the dialog guard. Throws <see cref="InvalidOperationException"/> if another dialog is already open.
    /// </summary>
    public static IDisposable Enter()
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("Another dialog is already open.");

        return new Releaser();
    }

    /// <summary>
    /// Whether any dialog is currently active.
    /// </summary>
    public static bool IsActive => Volatile.Read(ref _active) != 0;

    /// <summary>Releases the guard on its first dispose only, so a stale releaser cannot free a later dialog's guard.</summary>
    private sealed class Releaser : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Exchange(ref _active, 0);
        }
    }
}
