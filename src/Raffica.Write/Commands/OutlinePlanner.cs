using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;

namespace Raffica.Write.Commands;

/// <summary>What an outline command would do: the range of body blocks and, for a move, where it lands (<see cref="Reason"/> is <see cref="OutlineReason.None"/> when it can run).</summary>
/// <param name="Reason">Why the command cannot run, or None.</param>
/// <param name="First">The first body block of the chapter.</param>
/// <param name="Count">How many top-level blocks the chapter holds.</param>
/// <param name="Target">The index, before the move, at which a move lands (<c>TextEditor.MoveBlocks</c>); -1 for promote and demote.</param>
/// <param name="Last">The body block of the last heading of the chapter (where a promote or demote ends).</param>
internal readonly record struct OutlinePlan(OutlineReason Reason, int First, int Count, int Target, int Last);

/// <summary>
/// The pure planning of the outline commands over <see cref="OutlineSource.ReadHeadings"/> and <see cref="OutlineTree"/>: which heading a command acts on, and where a one-row move lands. Moving
/// up puts the chapter before the heading above it, moving down puts it after the next heading that is not in its own subtree (a move by one row, as Word's Alt+Shift+Down does). Nothing here changes
/// the document.
/// </summary>
internal static class OutlinePlanner
{
    /// <summary>
    /// The row of the heading at <paramref name="caret"/>: the heading paragraph itself, else the last heading at or before the body block of the caret. -1 when the caret is not in the body or lies
    /// before the first heading.
    /// </summary>
    public static int RowAtCaret(IReadOnlyList<OutlineHeading> headings, WordDocument document, TextPosition? caret)
    {
        if (caret is not { } position || !position.Story.Equals(StoryRef.Body) || headings.Count == 0)
        {
            return -1;
        }

        for (var i = 0; i < headings.Count; i++)
        {
            if (ReferenceEquals(headings[i].Paragraph, position.Paragraph))
            {
                return i;
            }
        }

        var block = TextUnits.IndexUnder(document.Body.Blocks, position.Paragraph);
        if (block < 0)
        {
            return -1;
        }

        var row = -1;
        for (var i = 0; i < headings.Count && headings[i].Entry.BodyBlockIndex <= block; i++)
        {
            row = i;
        }

        return row;
    }

    /// <summary>The plan of moving the chapter of <paramref name="row"/> up or down by one row.</summary>
    public static OutlinePlan Move(IReadOnlyList<OutlineHeading> headings, WordDocument document, int row, bool up)
    {
        if (row < 0 || row >= headings.Count)
        {
            return new OutlinePlan(OutlineReason.NoHeading, -1, 0, -1, -1);
        }

        var tree = OutlineTree.Of(document, headings, row);
        if (tree.Blocked != MoveBlock.None)
        {
            return Refused(Blocked(tree.Blocked), tree);
        }

        int neighbour;
        DropPlace place;
        if (up)
        {
            if (row == 0)
            {
                return Refused(OutlineReason.AtTop, tree);
            }

            (neighbour, place) = (row - 1, DropPlace.Before);
        }
        else
        {
            // The first row after the chapter's own subtree (its rows have a deeper level).
            var level = headings[row].Entry.Level;
            neighbour = row + 1;
            while (neighbour < headings.Count && headings[neighbour].Entry.Level > level)
            {
                neighbour++;
            }

            if (neighbour >= headings.Count)
            {
                return Refused(OutlineReason.AtEnd, tree);
            }

            place = DropPlace.After;
        }

        return Drop(headings, document, row, neighbour, place);
    }

    /// <summary>The row of the heading whose paragraph is <paramref name="paragraph"/>, or -1 (the paragraph is no heading any more).</summary>
    public static int RowOf(IReadOnlyList<OutlineHeading> headings, Paragraph paragraph)
    {
        for (var i = 0; i < headings.Count; i++)
        {
            if (ReferenceEquals(headings[i].Paragraph, paragraph))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The plan of moving the chapter of row <paramref name="dragged"/> before or after row <paramref name="row"/> (a drag of the outline pane); the one-row moves end here as well.</summary>
    public static OutlinePlan Drop(IReadOnlyList<OutlineHeading> headings, WordDocument document, int dragged, int row, DropPlace place)
    {
        if (dragged < 0 || dragged >= headings.Count || row < 0 || row >= headings.Count)
        {
            return new OutlinePlan(OutlineReason.NoHeading, -1, 0, -1, -1);
        }

        var tree = OutlineTree.Of(document, headings, dragged);
        if (tree.Blocked != MoveBlock.None)
        {
            return Refused(Blocked(tree.Blocked), tree);
        }

        var check = OutlineTree.CheckDrop(headings, document, dragged, row, place);
        if (check.Block != MoveBlock.None)
        {
            return Refused(Blocked(check.Block), tree);
        }

        return check.Target < 0 ? Refused(OutlineReason.CannotMoveThere, tree) : new OutlinePlan(OutlineReason.None, tree.FirstBlock, tree.Count, check.Target, tree.LastHeadingBlock);
    }

    /// <summary>The plan of promoting (<paramref name="delta"/> -1) or demoting (+1) the heading of <paramref name="row"/> and the headings of its subtree.</summary>
    public static OutlinePlan Shift(IReadOnlyList<OutlineHeading> headings, WordDocument document, int row, int delta, TextEditor editor)
    {
        if (row < 0 || row >= headings.Count)
        {
            return new OutlinePlan(OutlineReason.NoHeading, -1, 0, -1, -1);
        }

        var tree = OutlineTree.Of(document, headings, row);
        var facts = editor.ReadOutlineShift(tree.FirstBlock, tree.LastHeadingBlock, delta);
        if (facts.Allowed)
        {
            return new OutlinePlan(OutlineReason.None, tree.FirstBlock, tree.Count, -1, tree.LastHeadingBlock);
        }

        var reason = facts.Reason switch
        {
            OutlineShiftReason.NotAHeadingStyle => OutlineReason.NotAHeadingStyle,
            OutlineShiftReason.OutOfRange => OutlineReason.OutOfRange,
            _ => OutlineReason.NothingToShift,
        };
        return Refused(reason, tree);
    }

    private static OutlinePlan Refused(OutlineReason reason, Subtree tree) => new(reason, tree.FirstBlock, tree.Count, -1, tree.LastHeadingBlock);

    private static OutlineReason Blocked(MoveBlock block) => block switch
    {
        MoveBlock.LeavesAnEmptyFinalSection => OutlineReason.LeavesAnEmptyFinalSection,
        MoveBlock.InsideContentControl => OutlineReason.InsideContentControl,
        _ => OutlineReason.NotAtTopLevel,
    };
}
