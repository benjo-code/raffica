using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The tables of contents of the lists of blocks of one document, kept between edits. Finding them walks every paragraph of a list, which an edit that touches no field markup must not pay: the answer is kept while
/// <see cref="DocumentEdits.FieldMarkEpoch"/> is the one it was made at (an edit that cannot have moved a field mark or a block index leaves it alone, anything else, an undo or redo and a derived change included, changes it, and
/// then the next question starts again) and while the list still has as many blocks. A list of blocks is told apart by reference.
/// </summary>
internal sealed class TocIndex(DocumentEdits edits)
{
    private readonly Dictionary<IList<Block>, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private int _epoch = -1;

    /// <summary>The tables of contents of the list (null for none).</summary>
    public List<TocRange>? RangesOf(IList<Block> container)
    {
        if (edits.FieldMarkEpoch != _epoch)
        {
            _entries.Clear();
            _epoch = edits.FieldMarkEpoch;
        }

        if (_entries.TryGetValue(container, out var entry) && entry.Count == container.Count)
        {
            return entry.Ranges;
        }

        var ranges = TocRanges.In(container);
        _entries[container] = new Entry(container.Count, ranges);
        return ranges;
    }

    /// <summary>The table of contents that holds the block at <paramref name="index"/>, or null.</summary>
    public TocRange? Around(IList<Block> container, int index)
    {
        if (RangesOf(container) is not { } ranges)
        {
            return null;
        }

        foreach (var range in ranges)
        {
            if (index >= range.First && index <= range.Last)
            {
                return range;
            }
        }

        return null;
    }

    private readonly record struct Entry(int Count, List<TocRange>? Ranges);
}
