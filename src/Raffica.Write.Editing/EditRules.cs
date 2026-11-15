using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The editing rules of the spec that do not depend on an operation: what is one unit, what a text delete never removes, where anchors go.
/// <see cref="TextEditor"/> asks these helpers; they read a projection or a map and change nothing except where a method says so.
/// </summary>
internal static class EditRules
{
    /// <summary>
    /// The field-unit rule: the range of the field result that contains the offset, or null when the offset lies in anything else. A first
    /// Backspace or Delete next to such a unit selects it instead of deleting it.
    /// </summary>
    public static (int Start, int End)? FieldUnitAt(ParagraphProjection projection, int offset)
    {
        if (offset < 0)
        {
            return null;
        }

        foreach (var piece in projection.Pieces)
        {
            if (piece is AtomicPiece { Atomic: AtomicKind.FieldResult } atom && atom.Length > 0 && offset >= atom.Start && offset < atom.End)
            {
                return projection.AtomicAt(offset);
            }
        }

        return null;
    }

    /// <summary>
    /// The atomic-unit rule for a caret: a position inside an atomic unit (a field result, a picture, a reference) is not a caret
    /// position; it moves to the nearer edge of the unit (the end when both are equally near).
    /// </summary>
    public static int Snap(ParagraphMap map, int offset)
    {
        var clamped = Math.Clamp(offset, 0, map.Projection.Text.Length);
        if (map.Projection.AtomicAt(clamped) is { } atom && atom.Start < clamped)
        {
            return clamped - atom.Start < atom.End - clamped ? atom.Start : atom.End;
        }

        return clamped;
    }

    /// <summary>The atomic-unit rule for a range: a range that touches part of a unit takes the whole unit.</summary>
    public static void WidenToUnits(ParagraphProjection projection, ref int start, ref int end)
    {
        if (projection.AtomicAt(start) is { } startAtom && startAtom.Start < start)
        {
            start = startAtom.Start;
        }

        if (end > start && projection.AtomicAt(end - 1) is { } endAtom && endAtom.End > end)
        {
            end = endAtom.End;
        }
    }

    /// <summary>
    /// A comment reference is never removed by a text delete, even in a host that gives it a length: a comment goes only through its own
    /// command. Its range markers stay too (the delete does not touch zero-width anchors), so a deleted range collapses them.
    /// </summary>
    public static bool SurvivesTextDelete(ProjectionPiece piece) => piece is AtomicPiece { Atomic: AtomicKind.CommentReference };

    /// <summary>The kept-content rule: unknown content between two removed items lies inside the range and goes; at the edges it stays.</summary>
    public static void KillUnknownBetween(ParagraphMap map, int lowest, int highest, HashSet<int> kill)
    {
        for (var i = lowest + 1; i < highest; i++)
        {
            if (map.Items[i].Content is UnknownRunContent)
            {
                kill.Add(i);
            }
        }
    }

    /// <summary>
    /// The anchor rule: the zero-width anchors (bookmark and comment range marks, comment references) of the blocks [from, to) in document
    /// order. When the blocks are removed they move to the join point, so no anchor is left without its partner and a comment keeps its
    /// reference.
    /// </summary>
    public static List<Inline> AnchorsOfBlocks(IList<Block> blocks, int from, int to)
    {
        var anchors = new List<Inline>();
        for (var i = from; i < to; i++)
        {
            CollectAnchors(blocks[i], anchors);
        }

        return anchors;
    }

    private static void CollectAnchors(Block block, List<Inline> anchors)
    {
        switch (block)
        {
            case Paragraph paragraph:
                CollectAnchors(paragraph.Inlines, anchors);
                break;
            case Table table:
                foreach (var row in table.Rows)
                {
                    CollectAnchors(row, anchors);
                }

                break;
            default:
                break;
        }
    }

    private static void CollectAnchors(RowItem item, List<Inline> anchors)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    CollectAnchors(child, anchors);
                }

                break;
            case TableCell cell:
                foreach (var block in cell.Blocks)
                {
                    CollectAnchors(block, anchors);
                }

                break;
            default:
                break;
        }
    }

    private static void CollectAnchors(IList<Inline> inlines, List<Inline> anchors)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case BookmarkStart or BookmarkEnd or CommentRangeStart or CommentRangeEnd:
                    anchors.Add(inline);
                    break;
                case Run run when run.Content.Any(c => c is CommentReference):
                    var reference = run.Clone();
                    reference.Content.Clear();
                    foreach (var content in run.Content.Where(c => c is CommentReference))
                    {
                        reference.Content.Add(content);
                    }

                    anchors.Add(reference);
                    break;
                default:
                    if (ParagraphMap.ChildrenOf(inline) is { } children)
                    {
                        CollectAnchors(children, anchors);
                    }

                    break;
            }
        }
    }
}
