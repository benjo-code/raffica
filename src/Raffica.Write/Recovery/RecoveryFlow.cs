namespace Raffica.Write.Recovery;

/// <summary>What the user answered to the recovery card.</summary>
internal enum RecoveryChoice
{
    /// <summary>Open every listed copy as a tab.</summary>
    Restore,

    /// <summary>Delete every listed copy.</summary>
    Discard,

    /// <summary>Keep every copy and ask again at the next start (also Esc).</summary>
    Later,
}

/// <summary>What the offer needs from the window: the card, one tab opened from a copy, and the note for a copy that cannot be opened.</summary>
internal interface IRecoveryUi
{
    /// <summary>Shows the one card for all offered copies and returns the answer.</summary>
    Task<RecoveryChoice> AskAsync(IReadOnlyList<RecoveryEntry> entries);

    /// <summary>Opens a copy as a dirty tab bound to no path; null when it cannot be opened (the copy stays).</summary>
    Task<Documents.WriteTab?> OpenRecoveredAsync(RecoveryEntry entry);

    /// <summary>Tells the user that a copy could not be opened (the normal open-error wording); the copy is kept.</summary>
    void ShowOpenError(RecoveryEntry entry);
}

/// <summary>
/// The offer after a crash: lists the copies of the recovery folder, leaves out those of another running Write process (they are neither offered nor touched), asks once and
/// then restores, discards or keeps. Discard deletes exactly the copies that were offered; a copy that cannot be opened is reported and kept, and does not stop the others.
/// </summary>
internal sealed class RecoveryFlow(RecoveryStore store, IProcessProbe probe, IRecoveryUi ui, int selfProcessId)
{
    /// <summary>Runs the offer. Reads the folder on a pool thread (the first moment anything of it is read).</summary>
    public async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var offered = await Task.Run(
            () => store.List().Entries.Where(entry => !RecoveryLiveness.IsLive(entry, selfProcessId, probe)).ToList(),
            token);
        if (offered.Count == 0)
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        switch (await ui.AskAsync(offered))
        {
            case RecoveryChoice.Discard:
                // Exactly the offered list, off the UI thread (fifty deletes under a virus scanner would stall it).
                await Task.Run(
                    () =>
                    {
                        foreach (var entry in offered)
                        {
                            store.Delete(entry.Id);
                        }
                    },
                    CancellationToken.None);
                break;
            case RecoveryChoice.Restore:
                foreach (var entry in offered)
                {
                    token.ThrowIfCancellationRequested();
                    await RestoreOneAsync(entry);
                }

                break;
            default:
                break;
        }
    }

    private async Task RestoreOneAsync(RecoveryEntry entry)
    {
        Documents.WriteTab? tab;
        try
        {
            tab = await ui.OpenRecoveredAsync(entry);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            tab = null;
        }

        if (tab is null)
        {
            ui.ShowOpenError(entry);
        }
    }
}
