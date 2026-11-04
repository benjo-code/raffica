namespace Raffica.Pdf.Settings;

/// <summary>
/// Writes a file in the background so the UI thread never waits for the disk: a post hands over the complete write (a snapshot taken on
/// the UI thread), the newest one wins, and there is one writer at a time, so two writes of the same file never run side by side. A write
/// that fails is dropped silently (settings are never a reason to fail). <see cref="FlushAsync"/> and <see cref="Flush"/> wait until what
/// was posted has been written; the window flushes once when it closes.
/// </summary>
internal sealed class CoalescingWriter
{
    private readonly object _gate = new();
    private Action? _latest;
    private Task _running = Task.CompletedTask;
    private bool _active;

    /// <summary>Queues a write; one still waiting is replaced by this one. Safe to call from any thread, never blocks on the disk.</summary>
    /// <param name="write">The complete write. It runs on a pool thread and must only use what it captured.</param>
    public void Post(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_gate)
        {
            _latest = write;
            if (_active)
            {
                return;
            }

            _active = true;
            _running = Task.Run(Drain);
        }
    }

    /// <summary>Completes when everything posted so far has been written (or has failed).</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _active ? _running : Task.CompletedTask;
        }
    }

    /// <summary>Waits until everything posted so far has been written, for at most <paramref name="timeout"/>; returns whether it was written in time.</summary>
    public bool Flush(TimeSpan timeout)
    {
        try
        {
            return FlushAsync().Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    private void Drain()
    {
        while (true)
        {
            Action? write;
            lock (_gate)
            {
                write = _latest;
                _latest = null;
                if (write is null)
                {
                    _active = false;
                    return;
                }
            }

            try
            {
                write();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A write that cannot be made leaves the old file as it was; the list stays in memory for this run.
            }
        }
    }
}
