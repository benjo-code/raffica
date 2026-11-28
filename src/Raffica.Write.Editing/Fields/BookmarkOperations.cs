using System.Globalization;
using System.Text;
using System.Xml.Linq;
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

    /// <summary>
    /// The names of every bookmark the document holds, hidden ones included (what a new name must not repeat): those of every story (body, headers, footers, footnotes, endnotes and comments), the markers of the paragraphs and
    /// the ones Write keeps as content it does not read (between blocks, between the rows or cells of a table, in a wrapper, a drawing or a text box).
    /// </summary>
    public static IReadOnlyList<string> AllNames(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var names = new List<string>();
        Walk(
            document,
            (_, name) =>
            {
                if (name is not null)
                {
                    names.Add(name);
                }
            },
            null);
        return names;
    }

    /// <summary>
    /// What a new cross-reference bookmark (<c>_Ref</c> and digits) must not repeat: <see cref="AllNames"/> and every generated name a field or a link of the document points at, also when its bookmark is gone, so that an old
    /// reference never comes to mean a new target. A table of contents bookmark is named from <see cref="AllNames"/> alone (see <see cref="TocRefresh.TocBookmarks"/>).
    /// </summary>
    internal static List<string> TakenNames(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var names = new List<string>();
        Walk(
            document,
            (_, name) =>
            {
                if (name is not null)
                {
                    names.Add(name);
                }
            },
            text => AddGeneratedNames(text, names));
        return names;
    }

    /// <summary>One more than the highest bookmark id the document holds, in every place <see cref="AllNames"/> reads; 0 for a document with none. Null when the ids are used up.</summary>
    internal static int? NextId(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var highest = -1L;
        Walk(document, (id, _) => highest = Math.Max(highest, id ?? -1), null);
        return highest >= int.MaxValue ? null : (int)(highest + 1);
    }

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // One walk over everything the document holds, for the commands that make a bookmark (never for a key). It reports each marker (its id when it is a number, and the name of a start) and, when asked, the text that can point at a
    // bookmark: field instructions and the anchors of links. A stack of the walk's own, so no depth of tables and content controls is left out.
    private static void Walk(WordDocument document, Action<int?, string?> marker, Action<string>? pointer)
    {
        var pending = new Stack<object>();
        var notes = (document.Footnotes?.Notes ?? []).Concat(document.Endnotes?.Notes ?? []).Select(n => n.Content);
        var stories = document.HeadersFooters.Values.Select(p => p.Content).Concat(notes).Concat((document.Comments?.Comments ?? []).Select(c => c.Content)).Prepend(document.Body);
        foreach (var story in stories)
        {
            foreach (var kept in story.LeadingContent.Concat(story.TrailingContent))
            {
                Kept(kept.Element, marker, pointer);
            }

            foreach (var block in story.Blocks)
            {
                pending.Push(block);
            }
        }

        while (pending.TryPop(out var item))
        {
            switch (item)
            {
                case Paragraph paragraph:
                    Read(paragraph, marker, pointer);
                    break;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        pending.Push(row);
                    }

                    break;
                case TableRow row:
                    foreach (var child in row.Items)
                    {
                        pending.Push(child);
                    }

                    break;
                case TableCell cell:
                    foreach (var block in cell.Blocks)
                    {
                        pending.Push(block);
                    }

                    break;
                case ContentControlBlock control:
                    Frame(control.Frame, marker, pointer);
                    foreach (var block in control.Content)
                    {
                        pending.Push(block);
                    }

                    break;
                case UnknownBlock unknown:
                    Kept(unknown.Content.Element, marker, pointer);
                    break;
                case UnknownRowItem unknown:
                    Kept(unknown.Content.Element, marker, pointer);
                    break;
                default:
                    break;
            }
        }
    }

    private static void Read(Paragraph paragraph, Action<int?, string?> marker, Action<string>? pointer)
    {
        // An instruction may be split over several runs: the pieces of the paragraph are read as one text.
        StringBuilder? instruction = null;
        foreach (var inline in InlinesOf(paragraph.Inlines))
        {
            switch (inline)
            {
                case BookmarkStart start:
                    marker(start.Id, start.Name);
                    break;
                case BookmarkEnd end:
                    marker(end.Id, null);
                    break;
                case Run run:
                    foreach (var content in run.Content)
                    {
                        if (content is UnknownRunContent unknown)
                        {
                            Kept(unknown.Content.Element, marker, pointer);
                        }
                        else if (content is Picture picture)
                        {
                            Kept(picture.Raw, marker, pointer);
                        }
                        else if (pointer is not null && content is InstructionText text)
                        {
                            (instruction ??= new StringBuilder()).Append(text.Text);
                        }
                    }

                    break;
                case SimpleField field:
                    pointer?.Invoke(field.Instruction);
                    break;
                case Hyperlink { Anchor: { } anchor }:
                    pointer?.Invoke(anchor);
                    break;
                case ContentControlInline control:
                    Frame(control.Frame, marker, pointer);
                    break;
                case UnknownInline unknown:
                    Kept(unknown.Content.Element, marker, pointer);
                    break;
                default:
                    break;
            }
        }

        if (instruction is not null)
        {
            pointer!(instruction.ToString());
        }
    }

    private static void Frame(ContentControlFrame frame, Action<int?, string?> marker, Action<string>? pointer)
    {
        foreach (var other in frame.OtherChildren)
        {
            Kept(other.Element, marker, pointer);
        }

        if (frame.EndProperties is { } end)
        {
            Kept(end.Element, marker, pointer);
        }
    }

    // Content the model keeps as it was read: the markers anywhere inside it, and what points at a bookmark there.
    private static void Kept(XElement element, Action<int?, string?> marker, Action<string>? pointer)
    {
        StringBuilder? instruction = null;
        foreach (var inner in element.DescendantsAndSelf())
        {
            if (inner.Name.Namespace != W)
            {
                continue;
            }

            switch (inner.Name.LocalName)
            {
                case "bookmarkStart":
                    marker(IdOf(inner), (string?)inner.Attribute(W + "name"));
                    break;
                case "bookmarkEnd":
                    marker(IdOf(inner), null);
                    break;
                case "instrText" or "delInstrText" when pointer is not null:
                    (instruction ??= new StringBuilder()).Append(inner.Value);
                    break;
                case "fldSimple" when pointer is not null && (string?)inner.Attribute(W + "instr") is { } instr:
                    pointer(instr);
                    break;
                case "hyperlink" when pointer is not null && (string?)inner.Attribute(W + "anchor") is { } anchor:
                    pointer(anchor);
                    break;
                default:
                    break;
            }
        }

        if (instruction is not null)
        {
            pointer!(instruction.ToString());
        }
    }

    private static int? IdOf(XElement marker) =>
        int.TryParse((string?)marker.Attribute(W + "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    // Every "_Ref" or "_Toc" followed by digits in the text.
    private static void AddGeneratedNames(string text, List<string> into)
    {
        const int prefix = 4;
        for (var at = text.IndexOf('_', StringComparison.Ordinal); at >= 0; at = text.IndexOf('_', at + 1))
        {
            var rest = text.AsSpan(at);
            if (!rest.StartsWith("_Ref", StringComparison.OrdinalIgnoreCase) && !rest.StartsWith("_Toc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = at + prefix;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            if (end > at + prefix)
            {
                into.Add(text[at..end]);
            }
        }
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
