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
internal sealed class WriteTabRecoverySubject(WriteTab tab, string? id = null, Func<bool>? saveRunning = null) : IRecoverySubject
{
    public WriteTab Tab { get; } = tab ?? throw new ArgumentNullException(nameof(tab));

    public string Id { get; } = id ?? RecoveryStore.NewId();

    public string Title => Tab.Title;

    // A tab made from a restored copy has no path but remembers the old name: the next copy keeps it.
    public string? OriginalPath => Tab.Path ?? Tab.SuggestedSavePath;

    public DocxFile Docx => Tab.Docx;

    public int Revision => Tab.Docx.Model.Edits.Revision;

    public bool IsDirty => Tab.IsDirty;

    public SnapshotSkip? CannotSnapshotNow
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

            if (Tab.Runner.IsFaulted || Tab.Editor?.BlockedReason == EditBlockedReason.Faulted)
            {
                return SnapshotSkip.Faulted;
            }

            return Tab.Editor is { IsSaving: true } || saveRunning?.Invoke() == true ? SnapshotSkip.Saving : null;
        }
    }

    public IDisposable TakeModelLease() => Tab.Runner.PauseForEdit();
}
