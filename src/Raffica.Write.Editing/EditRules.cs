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

    /// <summary>A bookmark with content in its range whose both markers lie in the blocks a delete reaches; <see cref="Content"/> is what lies between the markers.</summary>
    internal sealed record BookmarkInside(BookmarkStart Start, BookmarkEnd End, IReadOnlyList<RunContent> Content);

    /// <summary>
    /// The bookmark rule, first half: the bookmarks whose start and end markers are both in the blocks [<paramref name="from"/>, <paramref name="to"/>] and that hold some content between them, taken before a delete. Null when
    /// no bookmark marker is there (the usual case costs one scan). A bookmark with no content between its markers is a point and never removed by a delete.
    /// </summary>
    internal static List<BookmarkInside>? BookmarksInside(IList<Block> blocks, int from, int to)
    {
        var events = new List<object>();
        var any = false;
        foreach (var paragraph in BookmarkOperations.ParagraphsOf(blocks.Skip(from).Take(to - from + 1)))
        {
            foreach (var inline in BookmarkOperations.InlinesOf(paragraph.Inlines))
            {
                switch (inline)
                {
                    case BookmarkStart or BookmarkEnd:
                        any = true;
                        events.Add(inline);
                        break;
                    case Run run:
                        events.AddRange(run.Content);
                        break;
                    default:
                        break;
                }
            }
        }

        if (!any)
        {
            return null;
        }

        var found = new List<BookmarkInside>();
        var taken = new HashSet<BookmarkEnd>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not BookmarkStart start)
            {
                continue;
            }

            var content = new List<RunContent>();
            for (var j = i + 1; j < events.Count; j++)
            {
                if (events[j] is BookmarkEnd end && end.Id == start.Id && !taken.Contains(end))
                {
                    taken.Add(end);
                    if (content.Count > 0)
                    {
                        found.Add(new BookmarkInside(start, end, content));
                    }

                    break;
                }

                if (events[j] is RunContent item)
                {
                    content.Add(item);
                }
            }
        }

        return found.Count == 0 ? null : found;
    }

    /// <summary>
    /// The bookmark rule, second half: after the delete, a bookmark of <paramref name="candidates"/> whose content is all gone from the surviving paragraph is removed with both markers (a bookmark whose range was only
    /// partly deleted keeps its markers, which sit where the anchor rule left them). Returns how many were removed.
    /// </summary>
    internal static int RemoveEmptiedBookmarks(Paragraph survivor, List<BookmarkInside> candidates)
    {
        var alive = new HashSet<RunContent>(ReferenceEqualityComparer.Instance);
        foreach (var inline in BookmarkOperations.InlinesOf(survivor.Inlines))
        {
            if (inline is Run run)
            {
                alive.UnionWith(run.Content);
            }
        }

        var removed = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Content.Any(alive.Contains))
            {
                continue;
            }

            BookmarkOperations.Remove(survivor.Inlines, candidate.Start);
            BookmarkOperations.Remove(survivor.Inlines, candidate.End);
            removed++;
        }

        return removed;
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
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    CollectAnchors(inner, anchors);
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
