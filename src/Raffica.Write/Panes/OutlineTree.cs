using Raffica.Write.Model;

namespace Raffica.Write.Panes;

/// <summary>Why a heading's subtree cannot be moved (<see cref="None"/>: it can).</summary>
internal enum MoveBlock
{
    None,

    /// <summary>A paragraph of the subtree ends a section: the section break would travel with it.</summary>
    SectionBreakInside,

    /// <summary>A heading of the subtree stands inside a block content control.</summary>
    InsideContentControl,

    /// <summary>The heading is not a body-level paragraph.</summary>
    NotAtTopLevel,
}

/// <summary>Where a drop lands relative to a row.</summary>
internal enum DropPlace
{
    Before,
    After,
}

/// <summary>The top-level body blocks of a heading's subtree.</summary>
/// <param name="FirstBlock">The index of the heading's block.</param>
/// <param name="Count">How many top-level blocks the subtree holds (at least one).</param>
/// <param name="LastHeadingBlock">The block index of the last listed heading in the subtree (where a promote or demote ends).</param>
/// <param name="Blocked"><see cref="MoveBlock.None"/> when the subtree can be moved, otherwise why not.</param>
internal readonly record struct Subtree(int FirstBlock, int Count, int LastHeadingBlock, MoveBlock Blocked);

/// <summary>
/// The tree the outline pane shows over <see cref="OutlineSource"/>: pure functions of the model and the headings read from it, run on the UI thread where the model is edited.
/// A subtree is the top-level body blocks from a heading up to, not including, the next listed heading of the same or a higher level (or the end of the body); tables and
/// blocks between headings belong to the heading above. The outline lists body-level paragraphs only, so a heading in a table is never a row and its table belongs to the
/// subtree above.
/// </summary>
internal static class OutlineTree
{
    /// <summary>The subtree of the heading in row <paramref name="row"/> of <paramref name="headings"/> (as <see cref="OutlineSource.ReadHeadings"/> gave them for <paramref name="document"/>).</summary>
    public static Subtree Of(WordDocument document, IReadOnlyList<OutlineHeading> headings, int row)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, headings.Count);

        var blocks = document.Body.Blocks;
        var level = headings[row].Entry.Level;
        var first = headings[row].Entry.BodyBlockIndex;
        var end = blocks.Count;
        var lastRow = row;
        for (var r = row + 1; r < headings.Count; r++)
        {
            if (headings[r].Entry.Level <= level)
            {
                end = headings[r].Entry.BodyBlockIndex;
                break;
            }

            lastRow = r;
        }

        var count = Math.Max(1, end - first);
        var blocked = BlockedReason(blocks, headings, row, lastRow, first, count);
        return new Subtree(first, count, headings[lastRow].Entry.BodyBlockIndex, blocked);
    }

    /// <summary>
    /// The index, in the body as it is before the move, at which the subtree of row <paramref name="dragged"/> lands when dropped before or after row <paramref name="row"/>
    /// (after a row is the same place as before the next row; after the last row is the end of the body). This is the <c>target</c> of <c>TextEditor.MoveBlocks</c>. -1 means the
    /// drop is not offered: the subtree cannot be moved, the drop lies inside it, the move would change nothing (the target is the subtree's own start or end), or the move
    /// would change the last block of the body to a table (Word repairs such a file; see <see cref="LastBlockAfterMove"/>).
    /// </summary>
    public static int DropTarget(IReadOnlyList<OutlineHeading> headings, WordDocument document, int dragged, int row, DropPlace place)
    {
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentNullException.ThrowIfNull(document);
        if (dragged < 0 || dragged >= headings.Count || row < 0 || row >= headings.Count)
        {
            return -1;
        }

        var blocks = document.Body.Blocks;
        var tree = Of(document, headings, dragged);
        if (tree.Blocked != MoveBlock.None)
        {
            return -1;
        }

        // Rows inside one block content control share a block index: a drop line between two of them has no place of its own.
        var between = place == DropPlace.Before
            ? row > 0 && headings[row - 1].Entry.BodyBlockIndex == headings[row].Entry.BodyBlockIndex
            : row + 1 < headings.Count && headings[row + 1].Entry.BodyBlockIndex == headings[row].Entry.BodyBlockIndex;
        if (between)
        {
            return -1;
        }

        var target = place == DropPlace.Before
            ? headings[row].Entry.BodyBlockIndex
            : row + 1 < headings.Count ? headings[row + 1].Entry.BodyBlockIndex : blocks.Count;

        // Inside the range, or the range's own place: nothing moves.
        if (target >= tree.FirstBlock && target <= tree.FirstBlock + tree.Count)
        {
            return -1;
        }

        // A body that already ends in a table is not made worse; a move that puts a table last is refused.
        var last = LastBlockAfterMove(blocks, tree.FirstBlock, tree.Count, target);
        return last is Table && !ReferenceEquals(last, blocks[^1]) ? -1 : target;
    }

    /// <summary>
    /// The block that is last in the body after moving <paramref name="count"/> blocks from <paramref name="first"/> to <paramref name="target"/> (an index in the list before
    /// the move, outside the range). A move to the end makes the subtree's last block the last one; moving away a subtree that ends the body makes the block before it the last
    /// one; any other move leaves the last block alone. A move to the end of a body that already ends in a table is refused when the subtree itself ends in another
    /// table (the body would still end in a table, and Word wants a paragraph there); it is allowed when the subtree ends in a paragraph.
    /// </summary>
    internal static Block LastBlockAfterMove(IList<Block> blocks, int first, int count, int target)
    {
        var n = blocks.Count;
        if (target >= n && first + count < n)
        {
            return blocks[first + count - 1];
        }

        if (first + count >= n && target < first && first > 0)
        {
            return blocks[first - 1];
        }

        return blocks[n - 1];
    }

    private static MoveBlock BlockedReason(IList<Block> blocks, IReadOnlyList<OutlineHeading> headings, int row, int lastRow, int first, int count)
    {
        if (first < 0 || first >= blocks.Count)
        {
            return MoveBlock.NotAtTopLevel;
        }

        if (blocks[first] is not Paragraph)
        {
            return blocks[first] is ContentControlBlock ? MoveBlock.InsideContentControl : MoveBlock.NotAtTopLevel;
        }

        for (var r = row; r <= lastRow; r++)
        {
            var index = headings[r].Entry.BodyBlockIndex;
            if (index >= 0 && index < blocks.Count && blocks[index] is ContentControlBlock)
            {
                return MoveBlock.InsideContentControl;
            }
        }

        for (var i = first; i < Math.Min(blocks.Count, first + count); i++)
        {
            if (HasSectionBreak(blocks[i], 0))
            {
                return MoveBlock.SectionBreakInside;
            }
        }

        return MoveBlock.None;
    }

    private static bool HasSectionBreak(Block block, int depth)
    {
        switch (block)
        {
            case Paragraph paragraph:
                return paragraph.SectionBreak is not null;
            case ContentControlBlock control when depth < OutlineSource.MaxControlDepth:
                return control.Content.Any(inner => HasSectionBreak(inner, depth + 1));
            default:
                return false;
        }
    }
}
