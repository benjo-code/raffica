namespace Raffica.Write.Model;

/// <summary>Counts the edits made to a document so that "unsaved changes" follows the history: undoing back to the saved revision is clean again.</summary>
public sealed class DocumentEdits
{
    /// <summary>The revision of the model now; it rises with every edit and is set back by an undo.</summary>
    public int Revision { get; private set; }

    /// <summary>The revision the file on disk has.</summary>
    public int SavedRevision { get; private set; }

    public bool IsDirty => Revision != SavedRevision;

    private int _highest;

    /// <summary>
    /// Counts the derived changes (a field refresh): text the editor did not type. It never falls, not even in <see cref="Restore"/>, so a stamp names one state of the content.
    /// A derived change is not an edit: it moves neither <see cref="Revision"/> nor <see cref="IsDirty"/>.
    /// </summary>
    public int DerivedVersion { get; private set; }

    /// <summary>The revision and the derived version together: what a reader of text, runs or paragraph identity keys on (the history, the dirty mark and the save stay on <see cref="Revision"/>).</summary>
    public EditStamp Stamp => new(Revision, DerivedVersion);

    /// <summary>Notes a derived change: the content changed, the history did not.</summary>
    public void RaiseDerived() => DerivedVersion++;

    /// <summary>
    /// Notes one more edit. The new revision is one that was never current before, also after an undo: a revision number names one
    /// state of the content, so a new edit after an undo cannot look like the saved state it replaced.
    /// </summary>
    public void Raise()
    {
        Revision = ++_highest;
        NoteRaised();
    }

    /// <summary>Notes that the current revision is the saved one.</summary>
    public void MarkSaved() => SavedRevision = Revision;

    /// <summary>Sets the revision back to one that was current before (undo and redo restore the revision a step started from or ended at).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The revision was never current.</exception>
    public void Restore(int revision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(revision, _highest);
        Revision = revision;
        NoteRaised();
    }

    private readonly List<WeakReference<EditTracker>> _trackers = [];

    /// <summary>
    /// Makes a tracker of the changes to <paramref name="body"/> (the body's block list) from now on. The tracker is held weakly: it goes away with its owner.
    /// Call it where no edit can run (under the edit lease).
    /// </summary>
    public EditTracker Track(IList<Block> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var tracker = new EditTracker(body);
        _trackers.RemoveAll(w => !w.TryGetTarget(out _));
        _trackers.Add(new WeakReference<EditTracker>(tracker));
        return tracker;
    }

    /// <summary>
    /// Says which block ranges the edit that was just raised (or restored) changed. The editor calls it once per <see cref="Raise"/> and <see cref="Restore"/>, with the changes it returns;
    /// a tracker that sees more raises than attributions reports itself incomplete.
    /// </summary>
    public void Attribute(IReadOnlyList<BlockChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var weak in _trackers)
        {
            if (weak.TryGetTarget(out var tracker))
            {
                tracker.Attributed(changes);
            }
        }
    }

    /// <summary>
    /// Says which block ranges a derived change touched. The trackers mark those blocks dirty, but the call counts as neither a raise nor an attribution, so it never makes a tracker complete or incomplete.
    /// </summary>
    public void AttributeDerived(IReadOnlyList<BlockChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var weak in _trackers)
        {
            if (weak.TryGetTarget(out var tracker))
            {
                tracker.AttributedDerived(changes);
            }
        }
    }

    private void NoteRaised()
    {
        foreach (var weak in _trackers)
        {
            if (weak.TryGetTarget(out var tracker))
            {
                tracker.Raised();
            }
        }
    }
}
