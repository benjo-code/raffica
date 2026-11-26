using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>A bookmark of the body: its name, id and the range it covers (a collapsed range for a bookmark whose end marker is missing or sits at its start).</summary>
public sealed record BookmarkInfo(string Name, int Id, TextRange Range);

/// <summary>
/// The reading side of bookmarks and the helpers the editor uses to place and remove them. The body is read in document order, through tables and content controls, to the depth every other walker of the
/// editor stops at (<see cref="MaxDepth"/>). Nothing here changes the document.
/// </summary>
public static class BookmarkOperations
{
    /// <summary>The deepest nesting of tables and content controls the walkers enter.</summary>
    internal const int MaxDepth = 64;

    /// <summary>The bookmarks of the body that the user may see (names that start with an underscore are left out), in document order. The offsets are those of a plain projection (no field values, no hidden text).</summary>
    public static IReadOnlyList<BookmarkInfo> Visible(WordDocument document) => Visible(document, (_, _) => ProjectionHost.Plain);

    /// <summary>The same list with the projection host of the editor, so the offsets are those the layout and the caret use.</summary>
    internal static IReadOnlyList<BookmarkInfo> Visible(WordDocument document, Func<StoryRef, Paragraph, IProjectionHost> hosts) =>
        [.. Scan(document, hosts).Where(f => !BookmarkNames.IsHidden(f.Name)).Select(f => f.Info)];

    /// <summary>The names of every bookmark of the body, hidden ones included (what a new name must not repeat).</summary>
    public static IReadOnlyList<string> AllNames(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var names = new List<string>();
        foreach (var paragraph in ParagraphsOf(document.Body.Blocks))
        {
            foreach (var inline in InlinesOf(paragraph.Inlines))
            {
                if (inline is BookmarkStart start)
                {
                    names.Add(start.Name);
                }
            }
        }

        return names;
    }

    /// <summary>One more than the highest bookmark id in any story of the document (body, headers, footers, notes and comments); 0 for a document with none. Null when the ids are used up.</summary>
    internal static int? NextId(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var highest = -1L;
        void Look(IList<Block> blocks)
        {
            foreach (var paragraph in ParagraphsOf(blocks))
            {
                foreach (var inline in InlinesOf(paragraph.Inlines))
                {
                    switch (inline)
                    {
                        case BookmarkStart start:
                            highest = Math.Max(highest, start.Id);
                            break;
                        case BookmarkEnd end:
                            highest = Math.Max(highest, end.Id);
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        Look(document.Body.Blocks);
        foreach (var part in document.HeadersFooters.Values)
        {
            Look(part.Content.Blocks);
        }

        foreach (var note in (document.Footnotes?.Notes ?? []).Concat(document.Endnotes?.Notes ?? []))
        {
            Look(note.Content.Blocks);
        }

        foreach (var comment in document.Comments?.Comments ?? [])
        {
            Look(comment.Content.Blocks);
        }

        return highest >= int.MaxValue ? null : (int)(highest + 1);
    }

    /// <summary>Every paragraph of the blocks in document order, through tables and block content controls.</summary>
    internal static IEnumerable<Paragraph> ParagraphsOf(IEnumerable<Block> blocks, int depth = 0)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Table table when depth < MaxDepth:
                    foreach (var row in table.Rows)
                    {
                        foreach (var inner in ParagraphsOfRow(row, depth + 1))
                        {
                            yield return inner;
                        }
                    }

                    break;
                case ContentControlBlock control when depth < MaxDepth:
                    foreach (var inner in ParagraphsOf(control.Content, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static IEnumerable<Paragraph> ParagraphsOfRow(RowItem item, int depth)
    {
        switch (item)
        {
            case TableRow row when depth < MaxDepth:
                foreach (var child in row.Items)
                {
                    foreach (var inner in ParagraphsOfRow(child, depth + 1))
                    {
                        yield return inner;
                    }
                }

                break;
            case TableCell cell when depth < MaxDepth:
                foreach (var inner in ParagraphsOf(cell.Blocks, depth + 1))
                {
                    yield return inner;
                }

                break;
            default:
                break;
        }
    }

    /// <summary>Every inline of the list in document order, the containers (links, fields, controls, tracked changes) before their children.</summary>
    internal static IEnumerable<Inline> InlinesOf(IEnumerable<Inline> inlines, int depth = 0)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (depth < MaxDepth && ParagraphMap.ChildrenOf(inline) is { } children)
            {
                foreach (var inner in InlinesOf(children, depth + 1))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>A bookmark found in the body: its markers and where each sits.</summary>
    internal sealed record Found(string Name, int Id, BookmarkStart Start, Paragraph StartParagraph, int StartOffset, BookmarkEnd? End, Paragraph? EndParagraph, int EndOffset)
    {
        public BookmarkInfo Info
        {
            get
            {
                var start = new TextPosition(StoryRef.Body, StartParagraph, StartOffset);
                var end = EndParagraph is null ? start : new TextPosition(StoryRef.Body, EndParagraph, EndOffset);
                return new BookmarkInfo(Name, Id, new TextRange(start, end));
            }
        }
    }

    /// <summary>Every bookmark of the body in document order of its start marker, with the projection offsets of both markers. The end of a bookmark is the first end marker of its id that follows its start.</summary>
    internal static List<Found> Scan(WordDocument document, Func<StoryRef, Paragraph, IProjectionHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hosts);
        var markers = new List<(Inline Marker, Paragraph Paragraph, int Offset)>();
        foreach (var paragraph in ParagraphsOf(document.Body.Blocks))
        {
            if (!InlinesOf(paragraph.Inlines).Any(i => i is BookmarkStart or BookmarkEnd))
            {
                continue;
            }

            foreach (var (marker, offset) in OffsetsOfMarkers(paragraph, hosts(StoryRef.Body, paragraph)))
            {
                markers.Add((marker, paragraph, offset));
            }
        }

        var found = new List<Found>();
        var taken = new HashSet<int>();
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i].Marker is not BookmarkStart start)
            {
                continue;
            }

            var endAt = -1;
            for (var j = i + 1; j < markers.Count && endAt < 0; j++)
            {
                if (markers[j].Marker is BookmarkEnd end && end.Id == start.Id && !taken.Contains(j))
                {
                    endAt = j;
                }
            }

            if (endAt >= 0)
            {
                taken.Add(endAt);
                found.Add(new Found(start.Name, start.Id, start, markers[i].Paragraph, markers[i].Offset, (BookmarkEnd)markers[endAt].Marker, markers[endAt].Paragraph, markers[endAt].Offset));
            }
            else
            {
                found.Add(new Found(start.Name, start.Id, start, markers[i].Paragraph, markers[i].Offset, null, null, 0));
            }
        }

        return found;
    }

    /// <summary>
    /// The projection offset of every bookmark marker of the paragraph: the start of the next content that follows the marker in the paragraph, or the length of the text when none follows (a marker
    /// is zero-width, so it never changes an offset).
    /// </summary>
    internal static List<(Inline Marker, int Offset)> OffsetsOfMarkers(Paragraph paragraph, IProjectionHost host)
    {
        var map = ParagraphMap.Build(paragraph, host);
        var text = map.Projection.Text.Length;
        var startOfFlat = new int[map.Items.Count];
        Array.Fill(startOfFlat, -1);
        foreach (var piece in map.Projection.Pieces)
        {
            var flat = map.FlatOf(piece);
            if (flat >= 0 && startOfFlat[flat] < 0)
            {
                startOfFlat[flat] = piece.Start;
            }
        }

        var next = text;
        for (var i = startOfFlat.Length - 1; i >= 0; i--)
        {
            if (startOfFlat[i] < 0)
            {
                startOfFlat[i] = next;
            }
            else
            {
                next = startOfFlat[i];
            }
        }

        var result = new List<(Inline, int)>();
        var waiting = new List<Inline>();
        foreach (var inline in InlinesOf(paragraph.Inlines))
        {
            switch (inline)
            {
                case BookmarkStart or BookmarkEnd:
                    waiting.Add(inline);
                    break;
                case Run run:
                    foreach (var content in run.Content)
                    {
                        if (waiting.Count > 0 && map.IndexOf.TryGetValue(content, out var flat))
                        {
                            var offset = startOfFlat[flat];
                            result.AddRange(waiting.Select(w => (w, offset)));
                            waiting.Clear();
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        result.AddRange(waiting.Select(w => (w, text)));
        return result;
    }

    /// <summary>Removes the marker from the inline tree; false when it is not in it.</summary>
    internal static bool Remove(IList<Inline> inlines, Inline marker) =>
        Find(inlines, marker) is { } found && RemoveAt(found);

    private static bool RemoveAt((IList<Inline> Owner, int Index) found)
    {
        found.Owner.RemoveAt(found.Index);
        return true;
    }

    /// <summary>The list that holds the marker and its index in it, or null.</summary>
    internal static (IList<Inline> Owner, int Index)? Find(IList<Inline> inlines, Inline marker, int depth = 0)
    {
        for (var i = 0; i < inlines.Count; i++)
        {
            if (ReferenceEquals(inlines[i], marker))
            {
                return (inlines, i);
            }
        }

        if (depth >= MaxDepth)
        {
            return null;
        }

        foreach (var inline in inlines)
        {
            if (ParagraphMap.ChildrenOf(inline) is { } children && Find(children, marker, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
