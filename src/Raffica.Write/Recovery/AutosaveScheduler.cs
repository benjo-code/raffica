namespace Raffica.Write.Recovery;

/// <summary>
/// The autosave of one window: every <see cref="Interval"/> after the first dirty tab, and on <see cref="TickNow"/> (the window was deactivated), it writes a recovery copy
/// of each tracked tab whose revision is not the one last written, one snapshot at a time. A tab that cannot be copied now (closed, only a prefix, faulted, saving) is skipped
/// without a trace; a failed copy is counted in <see cref="FailureCount"/> and tried again at the next tick, never shown. A tab that is clean again (saved, or undone to the saved
/// revision) has its copy deleted, but never while a snapshot of the same file is running. Nothing exists before <see cref="Track"/>: no clock, no folder.
/// </summary>
/// <remarks>
/// <b>Threading.</b> All state belongs to the UI thread (the clock ticks there, the window calls it there); the only pool work is the snapshot's, awaited by this class, and the
/// await returns to the thread that started it (the UI thread's synchronization context). <see cref="RecoverySnapshot.WriteAsync"/> is called on the UI thread as it requires.
/// A tick that arrives while a pass runs does not stack a second one: it asks for one more pass when this one ends.
/// </remarks>
/// <param name="store">The folder of the copies.</param>
/// <param name="clock">The beat.</param>
/// <param name="enabled">Whether the setting is on, asked at every tick (the live setting).</param>
internal sealed class AutosaveScheduler(RecoveryStore store, IRecoveryClock clock, Func<bool> enabled) : IDisposable
{
    /// <summary>The time between two copies.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<IRecoverySubject, Entry> _entries = [];
    private readonly CancellationTokenSource _cancellation = new();
    private IDisposable? _clockHandle;
    private Task _pass = Task.CompletedTask;
    private bool _running;
    private bool _again;
    private bool _disposed;

    /// <summary>How many copies failed since the scheduler was made (never shown to the user).</summary>
    public int FailureCount { get; private set; }

    /// <summary>Whether the clock runs.</summary>
    internal bool IsClockRunning => _clockHandle is not null;

    /// <summary>The number of tabs tracked.</summary>
    internal int TrackedCount => _entries.Count;

    /// <summary>Completes when no pass runs (tests await it; a pass never throws).</summary>
    internal Task WhenIdleAsync() => _pass;

    /// <summary>Starts to watch a tab; the clock starts with the first one. Tracking a tab twice is one tracking.</summary>
    public void Track(IRecoverySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (_disposed)
        {
            return;
        }

        _entries.TryAdd(subject, new Entry(subject.Id));
        _clockHandle ??= clock.Every(Interval, OnTick);
    }

    /// <summary>
    /// Watches a tab made from a restored copy: the copy <paramref name="existingId"/> is the tab's own (the subject's id must be that id, so the next copy replaces it) and is
    /// deleted when the tab is released; no revision is known as written, so the next pass writes a copy of the tab.
    /// </summary>
    public void Adopt(IRecoverySubject subject, string existingId)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(existingId);
        if (!string.Equals(subject.Id, existingId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The subject must carry the id of the copy it adopts.", nameof(existingId));
        }

        if (_disposed)
        {
            return;
        }

        _entries[subject] = new Entry(existingId) { CopyExists = true };
        _clockHandle ??= clock.Every(Interval, OnTick);
    }

    /// <summary>A copy now (the window was deactivated): the same pass as a tick. Does nothing while the setting is off.</summary>
    public void TickNow() => OnTick();

    /// <summary>
    /// The tab is saved or closed with Save or Don't save: its copy is deleted and the tab is no longer watched. A snapshot of the tab that is running deletes what it wrote
    /// when it ends. Never call it for a bare dispose or a Cancel.
    /// </summary>
    public void Release(IRecoverySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!_entries.Remove(subject, out var entry))
        {
            // Never tracked: there is no copy of ours to take away.
            return;
        }

        entry.Released = true;
        if (!RecoverySnapshot.IsRunning(subject.Docx))
        {
            store.Delete(entry.Id);
            entry.CopyExists = false;
        }
    }

    /// <summary>The tab may have become clean (saved, or undone to the saved revision): deletes its copy now, or after the running snapshot of it.</summary>
    public void Cleaned(IRecoverySubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (_entries.TryGetValue(subject, out var entry))
        {
            DeleteIfClean(subject, entry);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clockHandle?.Dispose();
        _clockHandle = null;

        // A snapshot in flight ends silently; nothing is deleted (a bare dispose keeps the copies for the next start).
        try
        {
            _cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A callback of the token threw; the scheduler is ending anyway.
        }
    }

    private void OnTick()
    {
        if (_disposed || !enabled())
        {
            return;
        }

        if (_running)
        {
            _again = true;
            return;
        }

        _running = true;
        _pass = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            do
            {
                _again = false;
                foreach (var subject in _entries.Keys.ToList())
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_entries.TryGetValue(subject, out var entry))
                    {
                        await StepAsync(subject, entry);
                    }
                }
            }
            while (_again && !_disposed);
        }
        finally
        {
            _running = false;
            _again = false;
        }
    }

    private async Task StepAsync(IRecoverySubject subject, Entry entry)
    {
        try
        {
            if (subject.CannotSnapshotNow == SnapshotSkip.Disposed)
            {
                // The tab is gone without a release: not watched any more, its copy stays for the next start.
                _entries.Remove(subject);
                return;
            }

            if (!subject.IsDirty)
            {
                DeleteIfClean(subject, entry);
                return;
            }

            if (subject.CannotSnapshotNow is not null || entry.LastRevision == subject.Revision)
            {
                return;
            }

            var result = await RecoverySnapshot.WriteAsync(subject, store, _cancellation.Token);
            switch (result)
            {
                case SnapshotResult.Written written:
                    entry.LastRevision = written.Revision;
                    entry.CopyExists = true;
                    break;
                case SnapshotResult.Failed failed when failed.Error is not OperationCanceledException:
                    FailureCount++;
                    break;
            }

            // What happened to the tab while the snapshot ran: released (the copy it wrote goes), or clean again (the same).
            if (entry.Released)
            {
                store.Delete(entry.Id);
                entry.CopyExists = false;
            }
            else if (!subject.IsDirty)
            {
                DeleteIfClean(subject, entry);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The autosave never breaks the window; the tab is tried again at the next tick.
            FailureCount++;
        }
    }

    private void DeleteIfClean(IRecoverySubject subject, Entry entry)
    {
        if (subject.IsDirty || !entry.CopyExists || RecoverySnapshot.IsRunning(subject.Docx))
        {
            return;
        }

        store.Delete(entry.Id);
        entry.CopyExists = false;
        entry.LastRevision = null;
    }

    private sealed class Entry(string id)
    {
        public string Id { get; } = id;

        public int? LastRevision { get; set; }

        public bool CopyExists { get; set; }

        public bool Released { get; set; }
    }
}
