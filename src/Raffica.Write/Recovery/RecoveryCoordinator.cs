namespace Raffica.Write.Recovery;

/// <summary>One per process: the first window that asks runs the offer, every later ask (a second window, a second call) does nothing.</summary>
internal sealed class RecoveryCoordinator
{
    private int _offered;

    /// <summary>Whether the offer was claimed already.</summary>
    public bool WasOffered => Volatile.Read(ref _offered) != 0;

    /// <summary>
    /// Runs <paramref name="run"/> if no window has claimed the offer yet and returns its task; otherwise returns a completed task and does not call it.
    /// </summary>
    public Task OfferOnce(Func<Task> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Interlocked.Exchange(ref _offered, 1) != 0 ? Task.CompletedTask : run();
    }
}
