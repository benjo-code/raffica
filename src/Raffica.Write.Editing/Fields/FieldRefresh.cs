using System.Text;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>What a refresh may use: the results that follow the order of the document, or also the ones that need the layout's pages.</summary>
public enum RefreshScope
{
    OrderBased,
    WithPages,

    /// <summary>As <see cref="OrderBased"/>, and the caller has shown that no page result can have moved (the page table is the one the last settled page pass saw and the touched blocks hold nothing a page result points at, see <see cref="TextEditor.PagePassCanBeSkipped"/>): the page results are left as they are and a pass that wrote nothing is settled.</summary>
    OrderBasedPagesCurrent,
}

/// <summary>
/// What a field refresh did. <paramref name="Content"/> are the block changes of the results it rewrote (one per changed top-level block, with clones as <see cref="BlockChange.OldBlocks"/>); a refresh has no caret and no
/// history step. <paramref name="Structural"/> is the change of the paragraph count of a table of contents (also among <paramref name="Content"/>, which the owner tells the layout; the owner attaches the record to the newest history step). <paramref name="NotUpdated"/> counts the fields Write left as they were (a field or switch outside
/// the support table, a missing target, a field in a tracked change, a note, a header, a footer or a comment) and <paramref name="Written"/> the results it rewrote. <paramref name="Settled"/> is false only when a page result may still change: the document has page-based fields (<paramref name="PageFields"/>: top-level <c>PAGEREF</c> fields and tables of contents) and this pass either wrote something (the layout may flow differently now) or could not use the pages (<see cref="RefreshScope.OrderBased"/> or an incomplete layout).
/// <paramref name="StructuralDeferred"/> is true when a table of contents was left as it was because its paragraph count changes and the history had a redo pending (the refresh was asked not to make a structural change): the owner looks again once the redo stack is empty.
/// <paramref name="StylesAdded"/> is true when the pass added entry styles to the style sheet (a table of contents names a level the document has no style for yet): whatever read the styles before is stale, so the owner starts its layout
/// over (<c>DocumentLayout.Restyle</c>) instead of applying <paramref name="Content"/> to it. Like the results, the styles are no history step: an undo leaves them.
/// </summary>
public sealed record FieldRefreshResult(IReadOnlyList<BlockChange> Content, DerivedRecord? Structural, int NotUpdated, int Written, bool Settled, bool StructuralDeferred = false, int PageFields = 0, bool StylesAdded = false);

/// <summary>
/// The order-based refresh of field results in the body: <c>SEQ</c>, <c>STYLEREF 1 \s</c>, <c>REF</c> and <c>NOTEREF</c>. It walks the body in document order, carries the counters through every paragraph (every field Word counts, see
/// <see cref="SeqCounter.Counts"/>, also one it does not write) and writes only the results that
/// differ, to the paragraphs of the touched blocks (every block when a heading, a bookmark, a caption or a note reference changed, because other results depend on those). A field that is nested in another field, sits in a tracked change or is
/// outside the support table keeps its stored result. Nothing here raises a revision: a refresh that wrote something raises the derived version and says so to the snapshot tracker.
/// </summary>
internal static class FieldRefresh
{
    private const int MaxReferencePasses = 4;

    public static FieldRefreshResult Run(WordDocument document, IFieldHost host, RefreshScope scope, IReadOnlyList<BlockChange>? touched, bool allowStructural = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(host);
        var body = document.Body.Blocks;
        // Page results are given only for the scope that asks for them and only from a complete layout; they depend on every block, so such a pass walks the whole body.
        var pagesOn = scope == RefreshScope.WithPages && host.IsComplete;
        var pagesCurrent = scope == RefreshScope.OrderBasedPagesCurrent;
        var styles = new FieldStyles(document);
        var targets = pagesOn ? null : Targets(document, styles, host, touched);
        if (targets is { Count: 0 })
        {
            return new FieldRefreshResult([], null, 0, 0, true);
        }

        var tocs = new TocRefresh(document, styles, host, allowStructural, pagesOn);
        var state = new State(styles, host, body, targets, pagesOn, tocs.ParagraphsOfLists);
        List<BlockChange> changes;
        TocPass? pass = null;
        try
        {
            state.Walk();
            changes = state.Finish();

            // The tables of contents come after the results they list (a caption's number is part of its entry); a walk of only some blocks never touches one (those blocks hold nothing a table of contents lists).
            if (targets is null)
            {
                pass = tocs.Run();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The walk writes in place: what it wrote before it failed is told to the document and handed to the owner in the exception, so no layout, tracker or retry works from a model it thinks unchanged.
            IReadOnlyList<BlockChange> partial = [];
            DerivedRecord? partialRecord = null;
            try
            {
                var known = state.Finish();
                known.AddRange(tocs.Changes);
                Announce(document, known);
                partial = known;
                partialRecord = tocs.Structural.Count > 0 ? new DerivedRecord([.. tocs.Structural]) : null;
            }
            catch (Exception inner) when (inner is not OutOfMemoryException)
            {
                // The walk failed and so does the account of it: what was written is not known, and the first failure is the one to report.
            }

            throw new FieldRefreshException(partial, ex) { Structural = partialRecord, StylesAdded = tocs.StylesAdded };
        }

        if (pass is not null)
        {
            changes.AddRange(pass.Changes);
        }

        Announce(document, changes);

        var notUpdated = state.NotUpdated + (targets is null ? OtherStories(document) + (pass?.NotUpdated ?? 0) : 0);
        var record = pass is { Structural.Count: > 0 } ? new DerivedRecord([.. pass.Structural]) : null;
        var written = state.Written + (pass?.Written ?? 0);
        var pageFields = state.PageFields;
        var settled = pageFields == 0 || ((pagesOn || pagesCurrent) && written == 0 && !(pass?.Deferred ?? false));
        return new FieldRefreshResult(changes, record, notUpdated, written, settled, pass?.Deferred ?? false, pageFields, tocs.StylesAdded);
    }

    /// <summary>
    /// True when the touched blocks can change what a page result says or points at, whatever the page table does: a change that adds or removes blocks (the top-level indices of later blocks move), or a block (before or after) that holds a heading,
    /// an outline level, a bookmark, a note reference, a field (a caption, a <c>PAGEREF</c>, a table of contents) or a section break. Only paragraphs of the touched blocks are looked at.
    /// </summary>
    internal static bool CarriesPageAnchor(IReadOnlyList<BlockChange> touched, WordDocument document)
    {
        var styles = new FieldStyles(document);
        foreach (var change in touched)
        {
            if (change.Added != change.OldBlocks.Count)
            {
                return true;
            }

            var blocks = new List<Block>(change.OldBlocks);
            for (var i = change.First; i < change.First + change.Added && i < change.Container.Count; i++)
            {
                if (i >= 0)
                {
                    blocks.Add(change.Container[i]);
                }
            }

            foreach (var paragraph in BookmarkOperations.ParagraphsOf(blocks))
            {
                if (paragraph.SectionBreak is not null || styles.CanBeListed(paragraph) || HasFieldMarkup(paragraph))
                {
                    return true;
                }

                foreach (var leaf in Leaves(paragraph.Inlines, false))
                {
                    if (leaf.Inline is BookmarkStart or BookmarkEnd || (leaf.Inline is Run run && run.Content.Any(c => c is FootnoteReference or EndnoteReference)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void Announce(WordDocument document, List<BlockChange> changes)
    {
        if (changes.Count > 0)
        {
            document.Edits.RaiseDerived();
            document.Edits.AttributeDerived(changes);
        }
    }

    // Null means every block. Otherwise the top-level indices of the touched blocks of the body, or every block when one of them (before or after the change) holds something other results depend on,
    // or when a change names a nested list of the body (a table cell, a content control): the enclosing block is not known there, so the whole body is walked.
    private static HashSet<int>? Targets(WordDocument document, FieldStyles styles, IFieldHost host, IReadOnlyList<BlockChange>? touched)
    {
        if (touched is null)
        {
            return null;
        }

        var body = document.Body.Blocks;
        var indices = new HashSet<int>();
        var blocks = new List<Block>();
        foreach (var change in touched)
        {
            if (change.Story.Kind != StoryKind.Body)
            {
                continue;
            }

            if (!ReferenceEquals(change.Container, body))
            {
                return null;
            }

            blocks.AddRange(change.OldBlocks);
            for (var i = change.First; i < change.First + change.Added && i < body.Count; i++)
            {
                if (i >= 0)
                {
                    indices.Add(i);
                    blocks.Add(body[i]);
                }
            }
        }

        return blocks.Any(b => DependedOn(b, styles, host)) ? null : indices;
    }

    // A block that holds a heading, a bookmark marker, a note reference, a SEQ field or a numbered paragraph (its label can move the labels of other paragraphs).
    private static bool DependedOn(Block block, FieldStyles styles, IFieldHost host)
    {
        foreach (var paragraph in BookmarkOperations.ParagraphsOf([block]))
        {
            if (styles.CanBeListed(paragraph) || IsNumbered(paragraph, styles) || host.NumberLabel(paragraph) is not null)
            {
                return true;
            }

            foreach (var leaf in Leaves(paragraph.Inlines, false))
            {
                switch (leaf.Inline)
                {
                    case BookmarkStart or BookmarkEnd:
                        return true;
                    case Run run when run.Content.Any(c => c is FootnoteReference or EndnoteReference):
                        return true;
                    default:
                        break;
                }
            }

            if (HasFieldMarkup(paragraph) && FieldView.InParagraph(paragraph).Any(f => f.Support.Kind == FieldKind.Seq))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly System.Xml.Linq.XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // A direct numPr, or one in the paragraph's style or the styles it is based on (a numPr with numId 0 is no list and still counts: the change may have switched it).
    private static bool IsNumbered(Paragraph paragraph, FieldStyles styles)
    {
        if (paragraph.Properties.Get(W + "numPr") is not null)
        {
            return true;
        }

        var seen = 0;
        for (var style = paragraph.Properties.StyleId is { } id ? styles.Find(id) : null; style is not null && seen < 32; seen++)
        {
            if (style.ParagraphProperties.Get(W + "numPr") is not null)
            {
                return true;
            }

            style = style.BasedOn is { } parent ? styles.Find(parent) : null;
        }

        return false;
    }

    private static int OtherStories(WordDocument document)
    {
        var count = 0;
        void In(IList<Block> blocks)
        {
            foreach (var paragraph in BookmarkOperations.ParagraphsOf(blocks))
            {
                if (!HasFieldMarkup(paragraph))
                {
                    continue;
                }

                count += FieldView.InParagraph(paragraph).Count(f => f.Parent is null && f.Support.Kind is FieldKind.Seq or FieldKind.StyleRef or FieldKind.Ref or FieldKind.NoteRef);
            }
        }

        foreach (var note in (document.Footnotes?.Notes ?? []).Concat(document.Endnotes?.Notes ?? []))
        {
            In(note.Content.Blocks);
        }

        foreach (var part in document.HeadersFooters.Values)
        {
            In(part.Content.Blocks);
        }

        foreach (var comment in document.Comments?.Comments ?? [])
        {
            In(comment.Content.Blocks);
        }

        return count;
    }

    /// <summary>True when a refresh may write the field: a field of a supported name and switches that is closed and not in a tracked change.</summary>
    internal static bool IsUsable(FieldSpan span) =>
        span.Support.Kind != FieldKind.None && span.Support.IsSupported && span.IsComplete && !span.InTrackedChange;

    // True when the paragraph has an instruction or a simple field anywhere: the cheap test before the fields are parsed.
    internal static bool HasFieldMarkup(Paragraph paragraph) =>
        Leaves(paragraph.Inlines, false).Any(l => l.Inline is SimpleField || (l.Inline is Run run && run.Content.Any(c => c is InstructionText)));

    internal static string TrimLabel(string label) => label.TrimEnd('.');

    /// <summary>The inlines in reading order, containers entered; a simple field is yielded itself before its content. <c>Deleted</c> is true inside a tracked deletion or a moved-from range.</summary>
    private static IEnumerable<(Inline Inline, bool Deleted)> Leaves(IEnumerable<Inline> inlines, bool deleted)
    {
        foreach (var inline in inlines)
        {
            yield return (inline, deleted);
            switch (inline)
            {
                case Hyperlink link:
                    foreach (var inner in Leaves(link.Inlines, deleted))
                    {
                        yield return inner;
                    }

                    break;
                case SimpleField simple:
                    foreach (var inner in Leaves(simple.Inlines, deleted))
                    {
                        yield return inner;
                    }

                    break;
                case ContentControlInline control:
                    foreach (var inner in Leaves(control.Inlines, deleted))
                    {
                        yield return inner;
                    }

                    break;
                case TrackedChange change:
                    foreach (var inner in Leaves(change.Inlines, deleted || change.Kind is TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// What a <c>REF</c> field shows for the bookmark that starts in <paramref name="start"/> with id <paramref name="id"/>: the number label of the paragraph without its trailing periods when <paramref name="number"/> is set (the <c>\n</c> and
    /// <c>\r</c> switches), else the text between the markers (both must stand in that paragraph; a deleted range is left out, a tab and a line break read as the refresh reads them). Null when the label or the range is not there.
    /// </summary>
    internal static string? ReferenceText(Paragraph start, int id, bool number, IFieldHost host)
    {
        if (number)
        {
            return host.NumberLabel(start) is { Length: > 0 } label ? TrimLabel(label) : null;
        }

        var text = new StringBuilder();
        return Scan(
            (start, id),
            content =>
            {
                switch (content)
                {
                    case TextContent t:
                        text.Append(t.Text);
                        break;
                    case TabContent:
                        text.Append('\t');
                        break;
                    case BreakContent:
                        text.Append(' ');
                        break;
                    default:
                        break;
                }

                return false;
            })
            ? text.ToString()
            : null;
    }

    /// <summary>What a <c>NOTEREF</c> field shows for that bookmark: the mark of the first note reference in its range, or null when there is none (or the host knows no mark).</summary>
    internal static string? NoteMark(Paragraph start, int id, IFieldHost host)
    {
        string? found = null;
        var ended = Scan(
            (start, id),
            content =>
            {
                switch (content)
                {
                    case FootnoteReference f:
                        found = host.NoteMarkText(f.Id, NoteType.Footnote);
                        return true;
                    case EndnoteReference e:
                        found = host.NoteMarkText(e.Id, NoteType.Endnote);
                        return true;
                    default:
                        return false;
                }
            });
        return ended ? found : null;
    }

    // Reads the contents of the live runs between the bookmark's markers (a deleted range is left out), which must both stand in the one paragraph, start first. True when the range was complete or the visitor stopped it.
    private static bool Scan((Paragraph Start, int Id) mark, Func<RunContent, bool> visit)
    {
        var inside = false;
        foreach (var (inline, deleted) in Leaves(mark.Start.Inlines, false))
        {
            switch (inline)
            {
                case BookmarkStart start when start.Id == mark.Id:
                    inside = true;
                    break;
                case BookmarkEnd end when end.Id == mark.Id:
                    return inside;
                case Run run when inside && !deleted:
                    foreach (var content in run.Content)
                    {
                        if (visit(content))
                        {
                            return true;
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return false;
    }

    private sealed class State(FieldStyles styles, IFieldHost host, IList<Block> body, HashSet<int>? targets, bool pagesOn, Func<HashSet<Paragraph>> tocParagraphs)
    {
        private readonly Dictionary<int, Block> _olds = [];
        private readonly HashSet<int> _changed = [];
        private readonly Dictionary<string, SeqCounter> _counters = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int Top, Paragraph Paragraph)> _references = [];
        private Dictionary<string, (Paragraph Start, int Id)>? _bookmarks;
        private HashSet<Paragraph>? _tocParagraphs;
        private List<(int Top, Paragraph Paragraph)>? _paragraphs;

        public int NotUpdated { get; private set; }

        public int Written { get; private set; }

        // The top-level PAGEREF fields and TOC fields seen in the whole body (a table of contents wraps its own PAGEREF fields, which the scanner shows as top-level in their paragraphs: they count as page fields too).
        public int PageFields { get; private set; }

        // Keep in step with CaptionBuilder.Results: it counts a new caption's SEQ number and chapter label with the same walk (headings are noted for the restarts, the SEQ fields SeqCounter.Counts accepts are counted); a change here needs the same change there and a case in CaptionEditingTests.
        public void Walk()
        {
            _paragraphs = [];
            for (var top = 0; top < body.Count; top++)
            {
                foreach (var paragraph in BookmarkOperations.ParagraphsOf([body[top]]))
                {
                    _paragraphs.Add((top, paragraph));
                }
            }

            Paragraph? lastChapter = null;
            foreach (var (top, paragraph) in _paragraphs)
            {
                if (styles.HeadingLevel(paragraph) is { } level)
                {
                    foreach (var counter in _counters.Values)
                    {
                        counter.NoteHeading(level);
                    }

                    if (level == 1)
                    {
                        lastChapter = paragraph;
                    }
                }

                if (!HasFieldMarkup(paragraph))
                {
                    continue;
                }

                var inTarget = targets is null || targets.Contains(top);
                var wantsReferences = false;
                foreach (var span in FieldView.InParagraph(paragraph))
                {
                    if (span.Parent is null && span.Support.Kind is FieldKind.Toc or FieldKind.PageRef)
                    {
                        PageFields++;
                    }

                    // Every sequence field Word counts moves its counter, also one Write does not write (nested, hidden, in an insertion).
                    var counted = SeqCounter.Counts(span) ? Counter(span.Instruction.Argument!).Next(span.Instruction) : null;

                    if (span.Parent is not null || span.Support.Kind is FieldKind.Toc)
                    {
                        continue;
                    }

                    if (span.Support.Kind == FieldKind.PageRef)
                    {
                        // A PAGEREF outside every table of contents is a result of its own, given only when the pages are (the entries of a list are the table of contents' own).
                        if (pagesOn && inTarget && Usable(span) && !(_tocParagraphs ??= tocParagraphs()).Contains(paragraph))
                        {
                            wantsReferences = true;
                        }

                        continue;
                    }

                    if (!Usable(span))
                    {
                        NotUpdated += inTarget ? 1 : 0;
                        continue;
                    }

                    string? text;
                    switch (span.Support.Kind)
                    {
                        case FieldKind.Seq:
                            text = counted;
                            break;
                        case FieldKind.StyleRef:
                            text = lastChapter is null ? null : host.ChapterNumber(lastChapter);
                            break;
                        default:
                            wantsReferences = true;
                            continue;
                    }

                    if (text is null)
                    {
                        NotUpdated += inTarget ? 1 : 0;
                    }
                    else if (inTarget)
                    {
                        Write(top, paragraph, span, text, countRefusal: true);
                    }
                }

                if (wantsReferences && inTarget)
                {
                    _references.Add((top, paragraph));
                }
            }

            ResolveReferences();
        }

        public List<BlockChange> Finish()
        {
            var changes = new List<BlockChange>();
            foreach (var top in _changed.Order())
            {
                changes.Add(new BlockChange(StoryRef.Body, body, top, 1, 1, [_olds[top]]));
            }

            return changes;
        }

        private static bool Usable(FieldSpan span) => IsUsable(span);

        private SeqCounter Counter(string label)
        {
            if (!_counters.TryGetValue(label, out var counter))
            {
                counter = new SeqCounter();
                _counters[label] = counter;
            }

            return counter;
        }

        private bool Write(int top, Paragraph paragraph, FieldSpan span, string text, bool countRefusal)
        {
            if (!_olds.ContainsKey(top))
            {
                _olds[top] = body[top].Clone();
            }

            bool written;
            try
            {
                written = FieldWriter.SetResult(paragraph, span, text);
            }
            catch
            {
                // A write that failed half-way may have changed the paragraph.
                _changed.Add(top);
                throw;
            }

            if (!written)
            {
                // False also means "already shows this text"; only a field with no place for a result is one Write could not update.
                NotUpdated += countRefusal && !FieldWriter.CanWrite(span) ? 1 : 0;
                return false;
            }

            _changed.Add(top);
            Written++;
            return true;
        }

        // REF and NOTEREF read the text and the labels the first phase wrote; a reference to a bookmark that holds another reference is run again until the results are stable.
        private void ResolveReferences()
        {
            for (var pass = 0; pass < MaxReferencePasses; pass++)
            {
                var wrote = false;
                foreach (var (top, paragraph) in _references)
                {
                    foreach (var span in FieldView.InParagraph(paragraph))
                    {
                        if (span.Parent is not null || !Usable(span) || span.Support.Kind is not (FieldKind.Ref or FieldKind.NoteRef or FieldKind.PageRef) || (span.Support.Kind == FieldKind.PageRef && (!pagesOn || (_tocParagraphs ??= tocParagraphs()).Contains(paragraph))))
                        {
                            continue;
                        }

                        var text = span.Support.Kind switch
                        {
                            FieldKind.Ref => RefText(span),
                            FieldKind.PageRef => PageText(span.Instruction),
                            _ => NoteText(span.Instruction),
                        };
                        if (text is null)
                        {
                            NotUpdated += pass == 0 ? 1 : 0;
                        }
                        else
                        {
                            wrote |= Write(top, paragraph, span, text, countRefusal: pass == 0);
                        }
                    }
                }

                if (!wrote)
                {
                    return;
                }
            }
        }

        private (Paragraph Start, int Id)? Find(string name)
        {
            if (_bookmarks is null)
            {
                _bookmarks = new Dictionary<string, (Paragraph, int)>(StringComparer.OrdinalIgnoreCase);
                foreach (var (_, paragraph) in _paragraphs!)
                {
                    foreach (var leaf in Leaves(paragraph.Inlines, false))
                    {
                        if (leaf.Inline is BookmarkStart start)
                        {
                            _bookmarks.TryAdd(start.Name, (paragraph, start.Id));
                        }
                    }
                }
            }

            return _bookmarks.TryGetValue(name, out var found) ? found : null;
        }

        // A text reference to a bookmark that holds the field itself has no text of its own: Word shows "Error! Not a valid bookmark self-reference." and a refresh would grow it every time, so Write leaves the stored result (counted as not
        // updated). The number of the paragraph (the n and r switches) is not such a text.
        private string? RefText(FieldSpan span)
        {
            var number = span.Instruction.Has("n") || span.Instruction.Has("r");
            return Find(span.Instruction.Argument!) is { } mark && (number || !HoldsItself(mark, span)) ? ReferenceText(mark.Start, mark.Id, number, host) : null;
        }

        private static bool HoldsItself((Paragraph Start, int Id) mark, FieldSpan span)
        {
            var inside = false;
            foreach (var (inline, _) in Leaves(mark.Start.Inlines, false))
            {
                switch (inline)
                {
                    case BookmarkStart start when start.Id == mark.Id:
                        inside = true;
                        break;
                    case BookmarkEnd end when end.Id == mark.Id:
                        return false;
                    default:
                        if (inside && (ReferenceEquals(inline, span.Info!.BeginRun) || ReferenceEquals(inline, span.Info.Simple)))
                        {
                            return true;
                        }

                        break;
                }
            }

            return false;
        }

        // The printed number of the page the bookmark's paragraph starts on; null (counted as not updated) when the bookmark is gone or the layout has no answer.
        private string? PageText(FieldInstruction instruction) =>
            Find(instruction.Argument!) is { } mark ? host.PageOf(new TextPosition(StoryRef.Body, mark.Start, 0))?.PageText : null;

        private string? NoteText(FieldInstruction instruction) =>
            Find(instruction.Argument!) is { } mark ? NoteMark(mark.Start, mark.Id, host) : null;
    }
}
