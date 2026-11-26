using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// The reason a footnote or endnote cannot be inserted at <paramref name="caret"/> (see <see cref="NoteRules.Refusal"/>), or null: also a caret in an inline content control of the kinds text, combo box,
    /// drop-down list or date, which only the offset can tell. A stale position throws <see cref="StalePositionException"/>.
    /// </summary>
    public NoteRefusal? RefusalForNote(TextPosition caret, bool prefixDocument = false)
    {
        if (NoteRules.Refusal(_document, caret, prefixDocument) is { } refused)
        {
            return refused;
        }

        var (story, _, _) = Locate(caret);
        var map = Map(story, caret.Paragraph);
        var point = ResolvePoint(map, EditRules.Snap(map, Math.Max(0, caret.Offset)));
        return point.Run is { } run && InRestrictedInlineControl(caret.Paragraph.Inlines, run, 0) ? NoteRefusal.RestrictedControl : null;
    }

    /// <summary>Whether the notes part of the kind holds a note with the highest possible id (<see cref="int.MaxValue"/>, a hostile file), so that no new note can get an id: <see cref="InsertNote"/> then returns null.</summary>
    public bool NoteIdsUsedUp(NoteType kind) => NoteOperations.NextId(kind == NoteType.Endnote ? _document.Endnotes : _document.Footnotes) is null;

    /// <summary>
    /// Inserts a footnote or an endnote reference at the caret and the note it points to, as one history step (undo removes both). The reference is a run of its own in the notes' reference style, the note a new
    /// entry of the footnotes or endnotes part (the part is made when the document has none, see <see cref="NotesParts.Ensure"/>) with its own mark and a space; its id is one more than the highest of the part. The
    /// styles <c>FootnoteReference</c> and <c>FootnoteText</c> (or the endnote pair) are made when the document has none and are companions of the step (<see cref="EditResult.Style"/>): a layout starts over for the
    /// first note, and later notes are applied as an ordinary block change. The result's caret is in the note's first paragraph, at the end of the text the editor can see (the note mark has no text until the
    /// layout has numbered it, so the caller re-reads the caret from the layout). Returns null for a place that takes no note (<see cref="RefusalForNote"/> says why). A stale position throws
    /// <see cref="StalePositionException"/> before anything changes.
    /// </summary>
    public EditResult? InsertNote(TextPosition caret, NoteType kind, bool prefixDocument = false)
    {
        if (RefusalForNote(caret, prefixDocument) is not null)
        {
            return null;
        }

        var (story, container, index) = Locate(caret);
        if (NoteIdsUsedUp(kind))
        {
            return null;
        }

        var revision = History.BeginOperation();
        var paragraph = caret.Paragraph;
        var map = Map(story, paragraph);
        var offset = EditRules.Snap(map, Math.Max(0, caret.Offset));
        var old = paragraph.Clone();

        var styles = NoteOperations.EnsureStyles(_document, kind);
        var collection = NotesParts.Ensure(_document, kind);
        var id = NoteOperations.NextId(collection) ?? throw new InvalidOperationException("The notes part has no free id.");
        PlaceRun(paragraph, map, offset, NoteOperations.ReferenceRun(_document, kind, id));
        var note = NoteOperations.NewNote(_document, kind, id);
        collection.Notes.Add(note);

        _document.Edits.Raise();
        var noteStory = NoteOperations.StoryOf(kind, id);
        var noteParagraph = (Paragraph)note.Content.Blocks[0];
        var noteCaret = new TextPosition(noteStory, noteParagraph, Map(noteStory, noteParagraph).Projection.Text.Length);
        var result = new EditResult(noteCaret, [new BlockChange(story, container, index, 1, 1, [old])], Style: styles, Objects: new ObjectChange([note], [], [], []));
        result.Attribute(_document);
        var place = new EditHistory.Point(index, offset);
        History.Record(result, place, revision, null, after: place);
        return result;
    }

    // The note references inside the blocks a span covers, taken before the span is deleted.
    private static List<RunContent> ReferencesIn(Span span) =>
        NoteOperations.References(span.Container.Skip(span.FirstIndex).Take(span.LastIndex - span.FirstIndex + 1));

    // After an edit replaced the blocks of a span: the notes whose reference was in the old blocks and that no reference of the whole document names any more are removed from their part, and returned.
    // A note whose reference only moved (a merged paragraph) stays, and so does a note that a second reference elsewhere in the document still names. Null when no note went.
    private ObjectChange? RemoveNotesOf(List<RunContent> before)
    {
        if (before.Count == 0)
        {
            return null;
        }

        var remaining = NoteOperations.DocumentReferenceKeys(_document);
        var removed = new List<Note>();
        foreach (var reference in before)
        {
            var (type, id) = NoteOperations.Describe(reference);
            if (remaining.Contains((type, id)))
            {
                continue;
            }

            var collection = type == NoteType.Endnote ? _document.Endnotes : _document.Footnotes;
            if (collection?.Find(id) is { Kind: NoteKind.Normal } note && !removed.Contains(note))
            {
                collection.Notes.Remove(note);
                removed.Add(note);
            }
        }

        return removed.Count == 0 ? null : new ObjectChange([], removed, [], []);
    }

    // True when the run is inside an inline content control of the kinds that hold text only (looking through hyperlinks, fields and tracked changes).
    private static bool InRestrictedInlineControl(IEnumerable<Inline> inlines, Run run, int depth)
    {
        var restricted = false;
        return FindRun(inlines, run, ref restricted, depth) && restricted;
    }

    private static bool FindRun(IEnumerable<Inline> inlines, Run run, ref bool restricted, int depth)
    {
        if (depth > 64)
        {
            return false;
        }

        foreach (var inline in inlines)
        {
            if (ReferenceEquals(inline, run))
            {
                return true;
            }

            if (ParagraphMap.ChildrenOf(inline) is { } children && FindRun(children, run, ref restricted, depth + 1))
            {
                if (inline is ContentControlInline control && control.Properties.Children.Any(c => c.Name.Namespace == W && s_singleParagraphKinds.Contains(c.Name.LocalName)))
                {
                    restricted = true;
                }

                return true;
            }
        }

        return false;
    }
    // Puts a run of its own into the paragraph at a caret offset (already snapped to a unit edge). The run around the place is cut in two when the place is inside it (neither InlineSplitter nor a hyperlink
    // split is used, so a reference inside a link stays inside it); a place at the edge of a field is outside the field. The run goes next to the visible content that the offset names, never next to hidden
    // content (deleted text, moved-from text): a paragraph with no visible content gets it at its end, in the paragraph itself, where a tracked change cannot hide it.
    private static void PlaceRun(Paragraph paragraph, ParagraphMap map, int offset, Inline placed)
    {
        var point = ResolvePoint(map, offset);
        switch (point.Kind)
        {
            case PointKind.Empty:
                paragraph.Inlines.Add(placed);
                return;
            case PointKind.AfterInline or PointKind.BeforeInline:
                var simple = point.Simple!;
                var at = simple.Owner.IndexOf(simple.Field);
                simple.Owner.Insert(point.Kind == PointKind.AfterInline ? at + 1 : at, placed);
                return;
            default:
                break;
        }

        var target = map.Items[point.Flat];
        var contentIndex = target.Index;
        if (point.Kind == PointKind.AfterContent)
        {
            contentIndex++;
        }
        else if (point.Kind == PointKind.InText && target.Content is TextContent text && point.Chars > 0)
        {
            if (point.Chars >= text.Text.Length)
            {
                contentIndex++;
            }
            else
            {
                var tail = new TextContent(text.Text[point.Chars..]);
                text.Text = text.Text[..point.Chars];
                target.Run.Content.Insert(target.Index + 1, tail);
                contentIndex = target.Index + 1;
            }
        }

        var run = target.Run;
        var owner = target.Owner;
        var runIndex = owner.IndexOf(run);
        if (contentIndex <= 0)
        {
            owner.Insert(runIndex, placed);
        }
        else if (contentIndex >= run.Content.Count)
        {
            owner.Insert(runIndex + 1, placed);
        }
        else
        {
            var rest = run.Clone();
            rest.Content.Clear();
            while (run.Content.Count > contentIndex)
            {
                rest.Content.Add(run.Content[contentIndex]);
                run.Content.RemoveAt(contentIndex);
            }

            owner.Insert(runIndex + 1, placed);
            owner.Insert(runIndex + 2, rest);
        }
    }
}
