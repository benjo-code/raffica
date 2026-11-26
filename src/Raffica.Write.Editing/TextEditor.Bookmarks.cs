using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>The bookmarks of the body that the user may see (names that start with an underscore are left out), in document order, with the offsets the editor's projection gives.</summary>
    public IReadOnlyList<BookmarkInfo> VisibleBookmarks() => BookmarkOperations.Visible(_document, _hosts);

    /// <summary>The range of the bookmark of that name (names compare ignoring case; hidden names are found too), or null when the body has none.</summary>
    public TextRange? FindBookmark(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return BookmarkOperations.Scan(_document, _hosts).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Info.Range;
    }

    /// <summary>
    /// Adds a bookmark over <paramref name="range"/> (a collapsed range makes a point) as one history step. The name follows <see cref="BookmarkNames.Problem"/> and must be new; its id is one more than the highest id
    /// of the document. The markers are zero-width, so no offset of the document changes, and the selection stays. Returns null, and changes nothing, for a bad or a taken name, a range that is not in the body or
    /// spans containers (a table cell and the text around it), a prefix document (<paramref name="prefixDocument"/>: the editor cannot see that the body was cut) and when the ids are used up. A stale position throws
    /// <see cref="StalePositionException"/> before anything changes.
    /// </summary>
    public EditResult? AddBookmark(TextRange range, string name, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (prefixDocument || BookmarkNames.Problem(name, BookmarkOperations.AllNames(_document)) is not null)
        {
            return null;
        }

        if (Resolve(range) is not { } span || span.Story.Kind != StoryKind.Body || BookmarkOperations.NextId(_document) is not { } id)
        {
            return null;
        }

        if (TocRefusesAny(range))
        {
            return null;
        }

        var revision = History.BeginOperation();
        var old = CloneBlocks(span);

        // The start first, then the end from a fresh map: at one place the end goes right behind the start, never before it.
        var startMap = Map(span.Story, span.First);
        var startOffset = EditRules.Snap(startMap, Math.Max(0, span.FirstOffset));
        var start = new BookmarkStart(id, name);
        PlaceRun(span.First, startMap, startOffset, start);

        var end = new BookmarkEnd(id);
        var endMap = Map(span.Story, span.Last);
        var endOffset = EditRules.Snap(endMap, Math.Max(0, span.LastOffset));
        if (ReferenceEquals(span.First, span.Last) && endOffset <= startOffset)
        {
            var at = BookmarkOperations.Find(span.First.Inlines, start) ?? throw new InvalidOperationException("The start marker was not placed.");
            at.Owner.Insert(at.Index + 1, end);
        }
        else
        {
            PlaceRun(span.Last, endMap, endOffset, end);
        }

        _document.Edits.Raise();
        var count = span.LastIndex - span.FirstIndex + 1;
        var result = new EditResult(
            new TextPosition(span.Story, span.Last, span.LastOffset),
            [new BlockChange(span.Story, span.Container, span.FirstIndex, count, count, old)],
            KeepSelection: true);
        result.Attribute(_document);
        History.Record(result, new EditHistory.Point(span.FirstIndex, span.FirstOffset), revision, null);
        return result;
    }

    /// <summary>
    /// Removes the bookmark of that name (ignoring case) with both markers, as one history step; the text stays. Returns null, and changes nothing, when the body has no such bookmark or the document is a prefix
    /// (<paramref name="prefixDocument"/>).
    /// </summary>
    public EditResult? DeleteBookmark(string name, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (prefixDocument)
        {
            return null;
        }

        var found = BookmarkOperations.Scan(_document, _hosts).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return null;
        }

        var blocks = _document.Body.Blocks;
        var first = TopLevelIndexOf(blocks, found.StartParagraph);
        var last = found.EndParagraph is null ? first : TopLevelIndexOf(blocks, found.EndParagraph);
        if (first < 0 || last < 0)
        {
            return null;
        }

        (first, last) = (Math.Min(first, last), Math.Max(first, last));
        var revision = History.BeginOperation();
        var old = new List<Block>(last - first + 1);
        for (var i = first; i <= last; i++)
        {
            old.Add(blocks[i].Clone());
        }

        BookmarkOperations.Remove(found.StartParagraph.Inlines, found.Start);
        if (found.End is not null && found.EndParagraph is not null)
        {
            BookmarkOperations.Remove(found.EndParagraph.Inlines, found.End);
        }

        _document.Edits.Raise();
        var count = last - first + 1;
        var result = new EditResult(
            new TextPosition(StoryRef.Body, found.StartParagraph, found.StartOffset),
            [new BlockChange(StoryRef.Body, blocks, first, count, count, old)],
            KeepSelection: true);
        result.Attribute(_document);
        History.Record(result, new EditHistory.Point(first, 0), revision, null);
        return result;
    }

    // The index of the top-level block that holds the paragraph (the paragraph itself, or its table or block content control), or -1.
    private static int TopLevelIndexOf(IList<Block> blocks, Paragraph paragraph)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], paragraph) || (blocks[i] is not Paragraph && BookmarkOperations.ParagraphsOf([blocks[i]]).Any(p => ReferenceEquals(p, paragraph))))
            {
                return i;
            }
        }

        return -1;
    }
}
