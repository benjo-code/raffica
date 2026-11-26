using System.Text;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>What a refresh may use: the results that follow the order of the document, or also the ones that need the layout's pages.</summary>
public enum RefreshScope
{
    OrderBased,
    WithPages,
}

/// <summary>
/// What a field refresh did. <paramref name="Content"/> are the block changes of the results it rewrote (one per changed top-level block, with clones as <see cref="BlockChange.OldBlocks"/>); a refresh has no caret and no
/// history step. <paramref name="Structural"/> is a change of the paragraph count of a list (none for the order-based fields). <paramref name="NotUpdated"/> counts the fields Write left as they were (a field or switch outside
/// the support table, a missing target, a field in a tracked change, a note, a header, a footer or a comment) and <paramref name="Written"/> the results it rewrote. <paramref name="Settled"/> is false only when a page result may still change.
/// </summary>
public sealed record FieldRefreshResult(IReadOnlyList<BlockChange> Content, DerivedRecord? Structural, int NotUpdated, int Written, bool Settled);

/// <summary>
/// The order-based refresh of field results in the body: <c>SEQ</c>, <c>STYLEREF 1 \s</c>, <c>REF</c> and <c>NOTEREF</c>. It walks the body in document order, carries the counters through every paragraph and writes only the results that
/// differ, to the paragraphs of the touched blocks (every block when a heading, a bookmark, a caption or a note reference changed, because other results depend on those). A field that is nested in another field, sits in a tracked change or is
/// outside the support table keeps its stored result. Nothing here raises a revision: a refresh that wrote something raises the derived version and says so to the snapshot tracker.
/// </summary>
internal static class FieldRefresh
{
    private const int MaxReferencePasses = 4;

    public static FieldRefreshResult Run(WordDocument document, IFieldHost host, RefreshScope scope, IReadOnlyList<BlockChange>? touched)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(host);
        _ = scope;
        var body = document.Body.Blocks;
        var targets = Targets(document, host, touched);
        if (targets is { Count: 0 })
        {
            return new FieldRefreshResult([], null, 0, 0, true);
        }

        var state = new State(host, body, targets);
        List<BlockChange> changes;
        try
        {
            state.Walk();
            changes = state.Finish();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The walk writes in place: what it wrote before it failed is told to the document and handed to the owner in the exception, so no layout, tracker or retry works from a model it thinks unchanged.
            IReadOnlyList<BlockChange> partial = [];
            try
            {
                var known = state.Finish();
                Announce(document, known);
                partial = known;
            }
            catch (Exception inner) when (inner is not OutOfMemoryException)
            {
                // The walk failed and so does the account of it: what was written is not known, and the first failure is the one to report.
            }

            throw new FieldRefreshException(partial, ex);
        }

        Announce(document, changes);

        var notUpdated = state.NotUpdated + (targets is null ? OtherStories(document) : 0);
        return new FieldRefreshResult(changes, null, notUpdated, state.Written, true);
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
    private static HashSet<int>? Targets(WordDocument document, IFieldHost host, IReadOnlyList<BlockChange>? touched)
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

        return blocks.Any(b => DependedOn(b, document, host)) ? null : indices;
    }

    // A block that holds a heading, a bookmark marker, a note reference, a SEQ field or a numbered paragraph (its label can move the labels of other paragraphs).
    private static bool DependedOn(Block block, WordDocument document, IFieldHost host)
    {
        foreach (var paragraph in BookmarkOperations.ParagraphsOf([block]))
        {
            if (HeadingLevel(paragraph) is not null || IsNumbered(paragraph, document) || host.NumberLabel(paragraph) is not null)
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
    private static bool IsNumbered(Paragraph paragraph, WordDocument document)
    {
        if (paragraph.Properties.Get(W + "numPr") is not null)
        {
            return true;
        }

        var seen = 0;
        for (var style = paragraph.Properties.StyleId is { } id ? document.Styles?.Find(id) : null; style is not null && seen < 32; seen++)
        {
            if (style.ParagraphProperties.Get(W + "numPr") is not null)
            {
                return true;
            }

            style = style.BasedOn is { } parent ? document.Styles?.Find(parent) : null;
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

    internal static int? HeadingLevel(Paragraph paragraph) =>
        paragraph.Properties.StyleId is { Length: 8 } id && id.StartsWith("Heading", StringComparison.Ordinal) && id[7] is >= '1' and <= '9' ? id[7] - '0' : null;

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

    private sealed class State(IFieldHost host, IList<Block> body, HashSet<int>? targets)
    {
        private readonly Dictionary<int, Block> _olds = [];
        private readonly HashSet<int> _changed = [];
        private readonly Dictionary<string, SeqCounter> _counters = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(int Top, Paragraph Paragraph)> _references = [];
        private Dictionary<string, (Paragraph Start, int Id)>? _bookmarks;
        private List<(int Top, Paragraph Paragraph)>? _paragraphs;

        public int NotUpdated { get; private set; }

        public int Written { get; private set; }

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
                if (HeadingLevel(paragraph) is { } level)
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
                    if (span.Parent is not null || span.Support.Kind is FieldKind.Toc or FieldKind.PageRef)
                    {
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
                            text = Counter(span.Instruction.Argument!).Next(span.Instruction);
                            break;
                        case FieldKind.StyleRef:
                            text = lastChapter is null ? null : host.NumberLabel(lastChapter) is { Length: > 0 } label ? TrimLabel(label) : null;
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
                        if (span.Parent is not null || !Usable(span) || span.Support.Kind is not (FieldKind.Ref or FieldKind.NoteRef))
                        {
                            continue;
                        }

                        var text = span.Support.Kind == FieldKind.Ref ? RefText(span.Instruction) : NoteText(span.Instruction);
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

        private string? RefText(FieldInstruction instruction)
        {
            if (Find(instruction.Argument!) is not { } mark)
            {
                return null;
            }

            if (instruction.Has("n") || instruction.Has("r"))
            {
                return host.NumberLabel(mark.Start) is { Length: > 0 } label ? TrimLabel(label) : null;
            }

            var text = new StringBuilder();
            return Scan(mark, content =>
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

        private string? NoteText(FieldInstruction instruction)
        {
            if (Find(instruction.Argument!) is not { } mark)
            {
                return null;
            }

            string? found = null;
            var ended = Scan(mark, content =>
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
    }
}
