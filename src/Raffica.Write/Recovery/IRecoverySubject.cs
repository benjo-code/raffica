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
}

/// <summary>What the snapshot writer needs of a tab (a seam: the tests use a small stand-in).</summary>
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
