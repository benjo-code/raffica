using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;

namespace Raffica.Write.Recovery;

/// <summary>
/// A <see cref="WriteTab"/> as the snapshot writer sees it. The model lease is the runner's edit lease taken on the calling (pool) thread, the same one a save takes; the
/// state that forbids a copy (closed, only a prefix open, faulted, a save running) is read on the UI thread before the snapshot and again on the pool thread inside the lease.
/// </summary>
/// <param name="tab">The tab.</param>
/// <param name="id">The id of the copy: null makes a new one; a restored copy passes its own id (see <see cref="AutosaveScheduler.Adopt"/>).</param>
/// <param name="saveRunning">Asks the window's save flow whether a save of the tab runs (null: only the editing session's saving flag).</param>
internal sealed class WriteTabRecoverySubject(WriteTab tab, string? id = null, Func<bool>? saveRunning = null) : IRecoverySubject, IInLeaseSnapshotCheck
{
    public WriteTab Tab { get; } = tab ?? throw new ArgumentNullException(nameof(tab));

    public string Id { get; } = id ?? RecoveryStore.NewId();

    public string Title => Tab.Title;

    // A tab made from a restored copy has no path but remembers the old name: the next copy keeps it.
    public string? OriginalPath => Tab.Path ?? Tab.SuggestedSavePath;

    public DocxFile Docx => Tab.Docx;

    public int Revision => Tab.Docx.Model.Edits.Revision;

    public bool IsDirty => Tab.IsDirty;

    /// <summary>The early check, on the UI thread: everything, the save flow's own record of a running save included.</summary>
    public SnapshotSkip? CannotSnapshotNow
    {
        get
        {
            if (CannotSnapshotInLease is { } reason)
            {
                return reason;
            }

            if (Tab.Editor?.BlockedReason == EditBlockedReason.Faulted)
            {
                return SnapshotSkip.Faulted;
            }

            return saveRunning?.Invoke() == true ? SnapshotSkip.Saving : null;
        }
    }

    /// <summary>
    /// The check the snapshot repeats on its pool thread inside the lease. Every input is safe to read from any thread: <see cref="WriteTab.IsDisposed"/> is a volatile read,
    /// <see cref="DocxFile.IsPrefix"/> is a get-only property of a file object that is never changed, <c>Runner.IsFaulted</c> is a volatile read of the fault (the runner exists
    /// already: the lease was taken through it, so no pool thread creates it), <c>Editor</c> is a reference written once and <c>Editor.IsSaving</c> a volatile read of a counter.
    /// The save flow's collection of running saves is deliberately not read here (the UI thread changes it); a save that has not yet reached <c>BeginSave</c> waits for the
    /// snapshot in <c>WriteSaveFlow</c> (<see cref="RecoverySnapshot.WhenIdleAsync"/>), and one that has is seen as <c>IsSaving</c>.
    /// </summary>
    public SnapshotSkip? CannotSnapshotInLease
    {
        get
        {
            if (Tab.IsDisposed)
            {
                return SnapshotSkip.Disposed;
            }

            if (Tab.Docx.IsPrefix)
            {
                return SnapshotSkip.Prefix;
            }

            if (Tab.Runner.IsFaulted)
            {
                return SnapshotSkip.Faulted;
            }

            return Tab.Editor is { IsSaving: true } ? SnapshotSkip.Saving : null;
        }
    }

    public IDisposable TakeModelLease() => Tab.Runner.PauseForEdit();
}
