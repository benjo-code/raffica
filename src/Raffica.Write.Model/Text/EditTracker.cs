namespace Raffica.Write.Model;

/// <summary>
/// What changed in the body since the last <see cref="Take"/>, for a reader that keeps something derived from the top-level blocks (the recovery snapshot keeps their serialized
/// form). The editor reports every model change twice: <see cref="DocumentEdits.Raise"/> or <see cref="DocumentEdits.Restore"/> (the revision moved) and
/// <see cref="DocumentEdits.Attribute"/> (the block ranges that moved it). A change that was raised and not attributed makes the answer incomplete, so a reader must then start from nothing.
/// </summary>
/// <remarks>
/// <b>Threads.</b> The editor writes on the editing (UI) thread; the reader takes under the edit lease, so no edit runs while it does and the lease hand-over orders the memory.
/// </remarks>
public sealed class EditTracker
{
    private readonly IList<Block> _body;
    private readonly HashSet<Block> _dirty = new(ReferenceEqualityComparer.Instance);
    private int _raised;
    private int _attributed;
    private bool _nested;

    internal EditTracker(IList<Block> body) => _body = body;

    /// <summary>What <see cref="Take"/> found.</summary>
    /// <param name="Complete">Every raise since the last take was attributed. When false nothing else is to be trusted.</param>
    /// <param name="Dirty">The top-level blocks that stand in a changed range of the body now (a removed block is not in it).</param>
    /// <param name="NestedChanged">A block list inside a top-level block (a table cell, a content control) changed: any top-level block that is not a paragraph may have changed.</param>
    public sealed record Changes(bool Complete, IReadOnlySet<Block> Dirty, bool NestedChanged);

    /// <summary>Returns what changed since the last take (or since the tracker was made) and starts over.</summary>
    public Changes Take()
    {
        var result = new Changes(_raised == _attributed, new HashSet<Block>(_dirty, ReferenceEqualityComparer.Instance), _nested);
        _dirty.Clear();
        _raised = 0;
        _attributed = 0;
        _nested = false;
        return result;
    }

    internal void Raised() => _raised++;

    internal void Attributed(IReadOnlyList<BlockChange> changes)
    {
        _attributed++;
        MarkDirty(changes);
    }

    internal void AttributedDerived(IReadOnlyList<BlockChange> changes) => MarkDirty(changes);

    private void MarkDirty(IReadOnlyList<BlockChange> changes)
    {
        foreach (var change in changes)
        {
            if (ReferenceEquals(change.Container, _body))
            {
                var end = Math.Min(change.First + change.Added, _body.Count);
                for (var i = Math.Max(0, change.First); i < end; i++)
                {
                    _dirty.Add(_body[i]);
                }
            }
            else if (change.Story.Kind == StoryKind.Body)
            {
                _nested = true;
            }
        }
    }
}
