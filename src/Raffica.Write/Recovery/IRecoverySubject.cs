using Raffica.Write.Docx;

namespace Raffica.Write.Recovery;

/// <summary>Why a recovery copy is not written now.</summary>
internal enum SnapshotSkip
{
    /// <summary>The tab is closed.</summary>
    Disposed,

    /// <summary>Only the start of the document is open.</summary>
    Prefix,

    /// <summary>The editing session is faulted, so the model may not be what the user sees.</summary>
    Faulted,

    /// <summary>A save runs.</summary>
    Saving,

    /// <summary>A snapshot of the same file is already running.</summary>
    Busy,
}

/// <summary>
/// An optional second face of a subject: what the snapshot writer asks on its <b>pool thread</b>, inside the model lease, instead of <see cref="IRecoverySubject.CannotSnapshotNow"/>.
/// A member of this kind may read only state that is safe to read from any thread (a volatile flag, an immutable property); it must not touch a collection the UI thread changes.
/// A subject that does not implement it is asked <see cref="IRecoverySubject.CannotSnapshotNow"/> in both places.
/// </summary>
internal interface IInLeaseSnapshotCheck
{
    /// <summary>The reason no copy can be made now, from thread-safe state only; null when none.</summary>
    SnapshotSkip? CannotSnapshotInLease { get; }
}

/// <summary>
/// What the snapshot writer needs of a tab (a seam: the tests use a small stand-in). <b>Threading precondition:</b> <see cref="RecoverySnapshot.WriteAsync"/> is called on the UI
/// thread, where the start of a save (<c>WriteSaveFlow</c>) and the start of a snapshot are serialized; <see cref="CannotSnapshotNow"/> is read there.
/// </summary>
internal interface IRecoverySubject
{
    /// <summary>The id of the copy of this tab (32 lower-case hex digits), stable for the tab's life.</summary>
    string Id { get; }

    /// <summary>The tab's title (a file name or "Untitled").</summary>
    string Title { get; }

    /// <summary>The file the tab is bound to; null for an untitled document.</summary>
    string? OriginalPath { get; }

    /// <summary>The file object whose model is copied.</summary>
    DocxFile Docx { get; }

    /// <summary>The revision of the model. Read inside the lease: it is the revision of what is written.</summary>
    int Revision { get; }

    /// <summary>Whether the model has unsaved changes.</summary>
    bool IsDirty { get; }

    /// <summary>The reason no copy can be made now, or null.</summary>
    SnapshotSkip? CannotSnapshotNow { get; }

    /// <summary>Takes the model-read lease on the calling thread; disposing gives it back.</summary>
    IDisposable TakeModelLease();
}
