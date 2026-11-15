namespace Raffica.Write.Model;

/// <summary>Counts the edits made to a document so that "unsaved changes" follows the history: undoing back to the saved revision is clean again.</summary>
public sealed class DocumentEdits
{
    /// <summary>The revision of the model now; it rises with every edit and is set back by an undo.</summary>
    public int Revision { get; private set; }

    /// <summary>The revision the file on disk has.</summary>
    public int SavedRevision { get; private set; }

    public bool IsDirty => Revision != SavedRevision;

    /// <summary>Notes one more edit.</summary>
    public void Raise() => Revision++;

    /// <summary>Notes that the current revision is the saved one.</summary>
    public void MarkSaved() => SavedRevision = Revision;

    /// <summary>Sets the revision back to one that was current before (undo and redo restore the revision a step started from or ended at).</summary>
    public void Restore(int revision) => Revision = revision;
}
