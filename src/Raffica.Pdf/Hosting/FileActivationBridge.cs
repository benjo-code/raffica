namespace Raffica.Pdf.Hosting;

/// <summary>Where the system hands files to the running app (the Finder and the Dock on macOS): local file paths only, in the order the system gave them.</summary>
internal interface IFileActivationSource
{
    /// <summary>Raised on any thread, once per batch of files.</summary>
    event Action<IReadOnlyList<string>>? FilesActivated;
}

/// <summary>
/// Holds the batches of files that arrive before the window can show them and hands them to the sink in their order once it attaches; later batches go straight to the sink.
/// A batch may arrive on any thread; the queue is guarded by a lock and the sink is never called while the lock is held.
/// </summary>
internal sealed class FileActivationBridge : IDisposable
{
    private readonly IFileActivationSource _source;
    private readonly object _gate = new();
    private readonly Queue<IReadOnlyList<string>> _pending = new();
    private Action<IReadOnlyList<string>>? _sink;
    private bool _attached;
    private bool _draining;
    private bool _disposed;

    public FileActivationBridge(IFileActivationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _source.FilesActivated += OnFiles;
    }

    /// <summary>Hands the queued batches to <paramref name="sink"/> in order, then every later batch directly. A second call throws.</summary>
    public void Attach(Action<IReadOnlyList<string>> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_gate)
        {
            if (_attached)
            {
                throw new InvalidOperationException("the file activation bridge is already attached");
            }

            _attached = true;
            _sink = sink;
        }

        Drain();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
        }

        _source.FilesActivated -= OnFiles;
    }

    private void OnFiles(IReadOnlyList<string> batch)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending.Enqueue(batch);
        }

        Drain();
    }

    // One thread at a time empties the queue, so the batches reach the sink in the order they were queued, and the sink runs outside the lock.
    // A batch raised from inside the sink is queued and picked up by the loop that is already running.
    private void Drain()
    {
        while (true)
        {
            IReadOnlyList<string> batch;
            Action<IReadOnlyList<string>> sink;
            lock (_gate)
            {
                if (!_attached || _draining || _disposed || _pending.Count == 0)
                {
                    return;
                }

                _draining = true;
                batch = _pending.Dequeue();
                sink = _sink!;
            }

            try
            {
                sink(batch);
            }
            finally
            {
                lock (_gate)
                {
                    _draining = false;
                }
            }
        }
    }
}
