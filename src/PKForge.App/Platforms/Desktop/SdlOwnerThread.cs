using System.Collections.Concurrent;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// One dedicated thread that runs queued work in order and, while <c>shouldPump</c> holds,
/// pumps events between work items, so the two never run at the same time. Started by the
/// first queued work item; a background thread that lives as long as the process.
/// </summary>
internal sealed class SdlOwnerThread(string name, Func<bool> shouldPump, Action pump, Action<Exception> onPumpError)
{
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Lock _startGate = new();
    private volatile Thread? _thread;

    /// <summary>Whether the caller is running on the owner thread.</summary>
    public bool IsCurrentThread => ReferenceEquals(Thread.CurrentThread, _thread);

    /// <summary>Queues <paramref name="work"/> for the owner thread; the task completes with its result or exception.</summary>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception error)
            {
                done.SetException(error);
            }
        });
        EnsureStarted();
        _wake.Set();
        return done.Task;
    }

    /// <summary>Runs <paramref name="work"/> on the owner thread and waits for it; runs inline when already there.</summary>
    public T Invoke<T>(Func<T> work) =>
        IsCurrentThread ? work() : InvokeAsync(work).GetAwaiter().GetResult();

    private void EnsureStarted()
    {
        if (_thread is not null) return;
        lock (_startGate)
        {
            if (_thread is not null) return;
            var thread = new Thread(Run) { Name = name, IsBackground = true };
            _thread = thread;
            thread.Start();
        }
    }

    private void Run()
    {
        while (true)
        {
            while (_work.TryDequeue(out var item))
                item();

            if (!shouldPump())
            {
                _wake.WaitOne();
                continue;
            }

            try
            {
                pump();
            }
            catch (Exception error)
            {
                onPumpError(error);
            }
        }
    }
}
