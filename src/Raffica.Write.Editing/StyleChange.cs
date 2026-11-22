using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// A change of one style of the style sheet, as the history keeps it: <paramref name="Old"/> and <paramref name="New"/> are read-only snapshots (clones) of
/// the style before and after, never the live object. Callers must not change them. <see cref="Companions"/> are further styles that changed in the same step
/// (the nine heading styles of the numbering switch): undo and redo swap all of <see cref="All"/> or none, and the history counts every one of them.
/// </summary>
public sealed record StyleChange(Style Old, Style New)
{
    /// <summary>The other style changes of the same step; empty for a step of one style.</summary>
    public IReadOnlyList<StyleChange> Companions { get; init; } = [];

    /// <summary>This change, then its companions.</summary>
    public IEnumerable<StyleChange> All
    {
        get
        {
            yield return this;
            foreach (var companion in Companions)
            {
                yield return companion;
            }
        }
    }
}
