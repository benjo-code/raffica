using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The text operations on a <see cref="WordDocument"/>: insert, delete, backspace, delete forward and the paragraph break. Each operation
/// changes the model in place, raises <see cref="DocumentEdits"/> once and returns the <see cref="BlockChange"/>s that describe what it
/// did (<see cref="BlockChange.OldBlocks"/> are clones of the blocks before the edit). The editor locks nothing and keeps no history.
/// </summary>
/// <remarks>
/// Positions are the offsets of <see cref="ParagraphProjection"/>: hidden text has no length, and a field result, a picture, a note or comment
/// reference is one atomic unit. A position inside an atomic unit is not a caret position: an insert there goes to the nearer edge of the
/// unit (the end when both are equally near), and a delete of any part of a unit removes the whole unit (a complex field with its
/// instruction, a simple field inline). Content the model keeps as unknown is never copied or removed unless it lies between two
/// removed items. A selection must lie in one story and one list of blocks (the body, one table cell, a note); a selection that leaves
/// it changes nothing. A table inside the blocks between the two ends of a selection is removed with them. A paragraph break never
/// merges across a table, a cell or a story boundary: such a backspace or delete returns null.
/// </remarks>
public sealed class TextEditor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly WordDocument _document;
    private readonly Func<StoryRef, Paragraph, IProjectionHost> _hosts;

    public TextEditor(WordDocument document, Func<StoryRef, Paragraph, IProjectionHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hosts);
        _document = document;
        _hosts = hosts;
    }

    private enum PointKind
    {
        InText,
        AfterContent,
        BeforeContent,
        AfterInline,
        BeforeInline,
        Empty,
    }

    /// <summary>
    /// Replaces the selection with the text. <c>\r\n</c>, <c>\n</c> and <c>\r</c> each split the paragraph once, a tab becomes a tab content,
    /// every other control character is dropped. The new text joins the run before the caret (the run after it at a paragraph start) and
    /// keeps its properties; in a paragraph without text it takes the formatting of the paragraph mark. The caret ends after the text.
    /// </summary>
    public EditResult InsertText(TextRange selection, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = TextBoundaries.Lines(text);
        var hasText = lines.Count > 1 || lines[0].Length > 0;
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        if (!hasText)
        {
            return ApplyDelete(span) ?? NoChange(span.Story, span.First, span.FirstOffset);
        }

        var old = CloneBlocks(span);
        var paragraph = span.First;
        var offset = span.FirstOffset;
        var removed = 1;
        if (!span.IsCollapsed)
        {
            var deleted = DeleteSpan(span);
            paragraph = deleted.Survivor;
            offset = deleted.Offset;
            removed = deleted.Removed;
        }

        var map = Map(span.Story, paragraph);
        offset = Snap(map, offset);
        var point = ResolvePoint(map, offset);
        TextPosition caret;
        var added = 1;
        if (lines.Count == 1)
        {
            InsertContents(paragraph, map, point, Contents(lines[0]), null);
            caret = new TextPosition(span.Story, paragraph, offset + lines[0].Length);
        }
        else
        {
            var last = InsertLines(span, paragraph, map, point, lines);
            added = lines.Count;
            caret = new TextPosition(span.Story, last, lines[^1].Length);
        }

        _document.Edits.Raise();
        return new EditResult(caret, [new BlockChange(span.Story, span.Container, span.FirstIndex, removed, added, old)]);
    }

    /// <summary>Removes the selection. Paragraphs it spans are merged: the first paragraph's properties win unless it is left empty, then the last one's.</summary>
    public EditResult Delete(TextRange selection)
    {
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        return ApplyDelete(span) ?? NoChange(span.Story, span.First, span.FirstOffset);
    }

    /// <summary>
    /// Deletes before the caret: one grapheme cluster, or one atomic unit, or (<see cref="DeleteUnit.Word"/>) back to the start of the word:
    /// blanks right before the caret first, then one run of letters, digits, underscores and combining marks, or one run of any other kind
    /// of character. At a paragraph start it merges with the previous paragraph; at the start of a cell, a story, or after a table it
    /// returns null.
    /// </summary>
    public EditResult? Backspace(TextPosition caret, DeleteUnit unit)
    {
        var (story, container, index) = Locate(caret);
        var map = Map(story, caret.Paragraph);
        var text = map.Projection.Text;
        var offset = Snap(map, Math.Clamp(caret.Offset, 0, text.Length));
        if (offset == 0)
        {
            if (index > 0 && container[index - 1] is Paragraph previous)
            {
                return ApplyDelete(new Span(story, container, index - 1, previous, int.MaxValue, index, caret.Paragraph, 0));
            }

            return null;
        }

        var start = unit == DeleteUnit.Word
            ? TextBoundaries.PreviousWord(text, offset)
            : map.Projection.AtomicAt(offset - 1) is { } atom ? atom.Start : TextBoundaries.PreviousCluster(text, offset);
        return ApplyDelete(new Span(story, container, index, caret.Paragraph, start, index, caret.Paragraph, offset));
    }

    /// <summary>The mirror of <see cref="Backspace"/>: deletes after the caret; at a paragraph end it merges with the next paragraph.</summary>
    public EditResult? DeleteForward(TextPosition caret, DeleteUnit unit)
    {
        var (story, container, index) = Locate(caret);
        var map = Map(story, caret.Paragraph);
        var text = map.Projection.Text;
        var offset = Snap(map, Math.Clamp(caret.Offset, 0, text.Length));
        if (offset == text.Length)
        {
            if (index + 1 < container.Count && container[index + 1] is Paragraph next)
            {
                return ApplyDelete(new Span(story, container, index, caret.Paragraph, offset, index + 1, next, 0));
            }

            return null;
        }

        var end = unit == DeleteUnit.Word
            ? TextBoundaries.NextWord(text, offset)
            : map.Projection.AtomicAt(offset) is { } atom ? atom.End : TextBoundaries.NextCluster(text, offset);
        return ApplyDelete(new Span(story, container, index, caret.Paragraph, offset, index, caret.Paragraph, end));
    }

    /// <summary>Replaces the selection with a paragraph break: the second paragraph has the properties of the first and takes what follows the caret.</summary>
    public EditResult SplitParagraph(TextRange selection) => InsertText(selection, "\n");

    private static EditResult NoChange(TextPosition caret) => new(caret, []);

    private static EditResult NoChange(StoryRef story, Paragraph paragraph, int offset) => new(new TextPosition(story, paragraph, Math.Max(0, offset)), []);

    private static void RemoveRange<T>(IList<T> list, int index, int count)
    {
        if (list is List<T> concrete)
        {
            concrete.RemoveRange(index, count);
            return;
        }

        for (var i = index + count - 1; i >= index; i--)
        {
            list.RemoveAt(i);
        }
    }

    private static void InsertRange<T>(IList<T> list, int index, IEnumerable<T> items)
    {
        if (list is List<T> concrete)
        {
            concrete.InsertRange(index, items);
            return;
        }

        foreach (var item in items)
        {
            list.Insert(index++, item);
        }
    }

    private static int Snap(ParagraphMap map, int offset)
    {
        var clamped = Math.Clamp(offset, 0, map.Projection.Text.Length);
        if (map.Projection.AtomicAt(clamped) is { } atom && atom.Start < clamped)
        {
            return clamped - atom.Start < atom.End - clamped ? atom.Start : atom.End;
        }

        return clamped;
    }

    private static List<RunContent> Contents(string line)
    {
        var contents = new List<RunContent>();
        var from = 0;
        while (from <= line.Length)
        {
            var tab = line.IndexOf('\t', from);
            var end = tab < 0 ? line.Length : tab;
            if (end > from)
            {
                contents.Add(new TextContent(line[from..end]));
            }

            if (tab < 0)
            {
                break;
            }

            contents.Add(new TabContent());
            from = tab + 1;
        }

        return contents;
    }

    private static Run MakeRun(Paragraph paragraph, Run? template)
    {
        if (template is not null)
        {
            var copy = template.Clone();
            copy.Content.Clear();
            return copy;
        }

        var run = new Run();
        if (paragraph.Properties.Get(W + "rPr") is { } mark)
        {
            foreach (var child in mark.Elements())
            {
                if (child.Name.LocalName is not ("ins" or "del" or "moveFrom" or "moveTo" or "rPrChange"))
                {
                    run.Properties.AddLoaded(child);
                }
            }
        }

        return run;
    }

    private static InsertPoint ResolvePoint(ParagraphMap map, int offset)
    {
        ProjectionPiece? piece = null;
        var before = false;
        foreach (var candidate in map.Projection.Pieces)
        {
            if (candidate.Length == 0)
            {
                continue;
            }

            if (offset > 0 && candidate.Start < offset && offset <= candidate.End)
            {
                piece = candidate;
                break;
            }

            if (offset == 0 && candidate.Start == 0)
            {
                piece = candidate;
                before = true;
                break;
            }
        }

        if (piece is null)
        {
            return new InsertPoint(PointKind.Empty, -1, 0, null, null);
        }

        var flat = map.FlatOf(piece);
        if (flat < 0)
        {
            return new InsertPoint(PointKind.Empty, -1, 0, null, null);
        }

        var run = map.Items[flat].Run;
        if (piece is TextPiece { Content: TextContent })
        {
            return new InsertPoint(PointKind.InText, flat, offset - piece.Start, null, run);
        }

        if (piece is AtomicPiece { Atomic: AtomicKind.FieldResult } && map.FieldAt(flat) is { } field)
        {
            if (field.Simple is { } simple)
            {
                return new InsertPoint(before ? PointKind.BeforeInline : PointKind.AfterInline, flat, 0, simple, run);
            }

            return before
                ? new InsertPoint(PointKind.BeforeContent, field.First, 0, null, map.Items[field.First].Run)
                : new InsertPoint(PointKind.AfterContent, field.Last, 0, null, map.Items[field.Last].Run);
        }

        return new InsertPoint(before ? PointKind.BeforeContent : PointKind.AfterContent, flat, 0, null, run);
    }

    private static (int Item, int Chars) CutOf(ParagraphMap map, InsertPoint point)
    {
        switch (point.Kind)
        {
            case PointKind.InText:
                var length = ((TextContent)map.Items[point.Flat].Content).Text.Length;
                return point.Chars == 0 ? (point.Flat, 0) : point.Chars >= length ? (point.Flat + 1, 0) : (point.Flat, point.Chars);
            case PointKind.AfterContent:
                return (point.Flat + 1, 0);
            case PointKind.BeforeContent:
                return (point.Flat, 0);
            case PointKind.AfterInline:
                return (point.Simple!.End, 0);
            case PointKind.BeforeInline:
                return (point.Simple!.Start, 0);
            default:
                return (map.Items.Count, 0);
        }
    }

    private static void InsertContents(Paragraph paragraph, ParagraphMap map, InsertPoint point, List<RunContent> contents, Run? fallback)
    {
        if (contents.Count == 0)
        {
            return;
        }

        switch (point.Kind)
        {
            case PointKind.InText:
                var item = map.Items[point.Flat];
                var reuse = (TextContent)item.Content;
                var text = reuse.Text;
                var replacement = Compose(reuse, text[..point.Chars], contents, text[point.Chars..]);
                item.Run.Content.RemoveAt(item.Index);
                InsertRange(item.Run.Content, item.Index, replacement);
                break;
            case PointKind.AfterContent:
                InsertRange(map.Items[point.Flat].Run.Content, map.Items[point.Flat].Index + 1, contents);
                break;
            case PointKind.BeforeContent:
                InsertRange(map.Items[point.Flat].Run.Content, map.Items[point.Flat].Index, contents);
                break;
            case PointKind.AfterInline:
            case PointKind.BeforeInline:
                var simple = point.Simple!;
                var after = point.Kind == PointKind.AfterInline;
                var template = simple.End > simple.Start ? map.Items[after ? simple.End - 1 : simple.Start].Run : fallback;
                var run = MakeRun(paragraph, template);
                InsertRange(run.Content, 0, contents);
                var at = simple.Owner.IndexOf(simple.Field);
                simple.Owner.Insert(after ? at + 1 : at, run);
                break;
            default:
                var created = MakeRun(paragraph, fallback);
                InsertRange(created.Content, 0, contents);
                paragraph.Inlines.Add(created);
                break;
        }
    }

    // The text before the caret, the new contents and the text after the caret as one run content list: neighbouring text is one text.
    private static List<RunContent> Compose(TextContent reuse, string head, List<RunContent> contents, string tail)
    {
        var result = new List<RunContent>();
        var pending = new StringBuilder(head);
        var reused = false;

        void Flush()
        {
            if (pending.Length == 0)
            {
                return;
            }

            if (!reused)
            {
                reuse.Text = pending.ToString();
                result.Add(reuse);
                reused = true;
            }
            else
            {
                result.Add(new TextContent(pending.ToString()));
            }

            pending.Clear();
        }

        foreach (var content in contents)
        {
            if (content is TextContent text)
            {
                pending.Append(text.Text);
            }
            else
            {
                Flush();
                result.Add(content);
            }
        }

        pending.Append(tail);
        Flush();
        return result;
    }

    private static (IList<Block> Container, int Index)? FindIn(IList<Block> blocks, Paragraph paragraph)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], paragraph))
            {
                return (blocks, i);
            }
        }

        foreach (var block in blocks)
        {
            if (block is Table table)
            {
                foreach (var row in table.Rows)
                {
                    if (FindInRowItem(row, paragraph) is { } found)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    private static (IList<Block> Container, int Index)? FindInRowItem(RowItem item, Paragraph paragraph)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    if (FindInRowItem(child, paragraph) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case TableCell cell:
                return FindIn(cell.Blocks, paragraph);
            default:
                return null;
        }
    }

    private static List<Block> CloneBlocks(Span span)
    {
        var old = new List<Block>(span.LastIndex - span.FirstIndex + 1);
        for (var i = span.FirstIndex; i <= span.LastIndex; i++)
        {
            old.Add(span.Container[i].Clone());
        }

        return old;
    }

    // Removes the visible range [start, end) of one paragraph (widened to whole atomic units). Returns false when nothing was removed.
    private static bool DeleteInParagraph(ParagraphMap map, ref int start, int end)
    {
        var projection = map.Projection;
        if (projection.AtomicAt(start) is { } startAtom && startAtom.Start < start)
        {
            start = startAtom.Start;
        }

        if (end > start && projection.AtomicAt(end - 1) is { } endAtom && endAtom.End > end)
        {
            end = endAtom.End;
        }

        var kill = new HashSet<int>();
        var cuts = new Dictionary<int, (int From, int To)>();
        var simpleKill = new List<SimpleFieldSpan>();
        var lowest = int.MaxValue;
        var highest = -1;

        void Note(int flat)
        {
            lowest = Math.Min(lowest, flat);
            highest = Math.Max(highest, flat);
        }

        foreach (var piece in projection.Pieces)
        {
            if (piece.Length == 0 || piece.End <= start || piece.Start >= end)
            {
                continue;
            }

            var flat = map.FlatOf(piece);
            if (flat < 0)
            {
                continue;
            }

            if (piece is TextPiece { Content: TextContent text })
            {
                var from = Math.Max(start, piece.Start) - piece.Start;
                var to = Math.Min(end, piece.End) - piece.Start;
                if (from == 0 && to >= text.Text.Length)
                {
                    kill.Add(flat);
                }
                else
                {
                    cuts[flat] = (from, to);
                }

                Note(flat);
            }
            else if (piece is AtomicPiece { Atomic: AtomicKind.FieldResult } && map.FieldAt(flat) is { } field)
            {
                if (field.Simple is { } simple)
                {
                    if (!simpleKill.Contains(simple))
                    {
                        simpleKill.Add(simple);
                    }
                }
                else
                {
                    for (var i = field.First; i <= field.Last; i++)
                    {
                        kill.Add(i);
                    }
                }

                Note(field.First);
                Note(field.Last);
            }
            else
            {
                kill.Add(flat);
                Note(flat);
            }
        }

        if (lowest == int.MaxValue)
        {
            return false;
        }

        // Unknown content between two removed items is inside the range.
        for (var i = lowest + 1; i < highest; i++)
        {
            if (map.Items[i].Content is UnknownRunContent)
            {
                kill.Add(i);
            }
        }

        var touched = new HashSet<Run>();
        foreach (var (flat, cut) in cuts)
        {
            var content = (TextContent)map.Items[flat].Content;
            content.Text = content.Text.Remove(cut.From, cut.To - cut.From);
            touched.Add(map.Items[flat].Run);
        }

        foreach (var flat in kill.OrderByDescending(i => i))
        {
            var item = map.Items[flat];
            item.Run.Content.RemoveAt(item.Index);
            touched.Add(item.Run);
        }

        foreach (var simple in simpleKill)
        {
            simple.Owner.Remove(simple.Field);
        }

        Prune(map.Paragraph.Inlines, touched);
        return true;
    }

    // Removes the runs that lost their last content and the links, fields and insertions that were left empty by it.
    private static bool Prune(IList<Inline> list, HashSet<Run> touched)
    {
        var any = false;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            switch (list[i])
            {
                case Run run when touched.Contains(run):
                    any = true;
                    if (run.Content.Count == 0)
                    {
                        list.RemoveAt(i);
                    }

                    break;
                case var inline when ParagraphMap.ChildrenOf(inline) is { } children:
                    if (Prune(children, touched))
                    {
                        any = true;
                        if (children.Count == 0 && inline is not ContentControlInline)
                        {
                            list.RemoveAt(i);
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return any;
    }

    // A paragraph with the properties of the given one and no content.
    private static Paragraph Shell(Paragraph paragraph)
    {
        var saved = paragraph.Inlines.ToList();
        var section = paragraph.SectionBreak;
        paragraph.Inlines.Clear();
        paragraph.SectionBreak = null;
        var shell = paragraph.Clone();
        foreach (var inline in saved)
        {
            paragraph.Inlines.Add(inline);
        }

        paragraph.SectionBreak = section;
        return shell;
    }

    private ParagraphMap Map(StoryRef story, Paragraph paragraph) => ParagraphMap.Build(paragraph, _hosts(story, paragraph));

    private Story StoryOf(StoryRef story)
    {
        Story? found = story.Kind switch
        {
            StoryKind.Body => _document.Body,
            StoryKind.HeaderFooter => story.Key is not null && _document.HeadersFooters.TryGetValue(story.Key, out var part) ? part.Content : null,
            StoryKind.Note when story.Key is not null && story.Key.StartsWith("endnote:", StringComparison.Ordinal) =>
                int.TryParse(story.Key.AsSpan("endnote:".Length), out var endId) ? _document.Endnotes?.Find(endId)?.Content : null,
            StoryKind.Note => int.TryParse(story.Key, out var noteId) ? _document.Footnotes?.Find(noteId)?.Content : null,
            StoryKind.Comment => int.TryParse(story.Key, out var commentId) ? _document.Comments?.Find(commentId)?.Content : null,
            _ => null,
        };
        return found ?? throw new ArgumentException("The story is not part of the document.");
    }

    private (StoryRef Story, IList<Block> Container, int Index) Locate(TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(position.Paragraph);
        var story = StoryOf(position.Story);
        return FindIn(story.Blocks, position.Paragraph) is { } found
            ? (position.Story, found.Container, found.Index)
            : throw new ArgumentException("The paragraph is not in the story of the position.");
    }

    private Span? Resolve(TextRange selection)
    {
        if (selection.Start.Story != selection.End.Story)
        {
            return null;
        }

        var (story, container, startIndex) = Locate(selection.Start);
        var (_, endContainer, endIndex) = Locate(selection.End);
        if (!ReferenceEquals(container, endContainer))
        {
            return null;
        }

        var startOffset = Math.Max(0, selection.Start.Offset);
        var endOffset = Math.Max(0, selection.End.Offset);
        var reversed = startIndex > endIndex || (startIndex == endIndex && startOffset > endOffset);
        return reversed
            ? new Span(story, container, endIndex, selection.End.Paragraph, endOffset, startIndex, selection.Start.Paragraph, startOffset)
            : new Span(story, container, startIndex, selection.Start.Paragraph, startOffset, endIndex, selection.End.Paragraph, endOffset);
    }

    private EditResult? ApplyDelete(Span span)
    {
        if (span.IsCollapsed)
        {
            return null;
        }

        var old = CloneBlocks(span);
        var deleted = DeleteSpan(span);
        if (!deleted.Changed)
        {
            return null;
        }

        _document.Edits.Raise();
        return new EditResult(
            new TextPosition(span.Story, deleted.Survivor, deleted.Offset),
            [new BlockChange(span.Story, span.Container, span.FirstIndex, deleted.Removed, 1, old)]);
    }

    private Deleted DeleteSpan(Span span)
    {
        var first = Map(span.Story, span.First);
        var firstLength = first.Projection.Text.Length;
        var start = Math.Clamp(span.FirstOffset, 0, firstLength);
        if (span.FirstIndex == span.LastIndex)
        {
            var end = Math.Clamp(span.LastOffset, 0, firstLength);
            var changed = start < end && DeleteInParagraph(first, ref start, end);
            return new Deleted(span.First, start, 1, changed);
        }

        if (start < firstLength)
        {
            DeleteInParagraph(first, ref start, firstLength);
        }

        var last = Map(span.Story, span.Last);
        var lastEnd = Math.Clamp(span.LastOffset, 0, last.Projection.Text.Length);
        if (lastEnd > 0)
        {
            var zero = 0;
            DeleteInParagraph(last, ref zero, lastEnd);
        }

        var removed = span.LastIndex - span.FirstIndex + 1;
        var remaining = Map(span.Story, span.First).Projection.Text.Length;
        if (remaining > 0)
        {
            foreach (var inline in span.Last.Inlines)
            {
                span.First.Inlines.Add(inline);
            }

            span.First.SectionBreak = span.Last.SectionBreak ?? span.First.SectionBreak;
            RemoveRange(span.Container, span.FirstIndex + 1, span.LastIndex - span.FirstIndex);
            return new Deleted(span.First, remaining, removed, true);
        }

        var inlines = span.First.Inlines.Concat(span.Last.Inlines).ToList();
        span.Last.Inlines.Clear();
        foreach (var inline in inlines)
        {
            span.Last.Inlines.Add(inline);
        }

        span.Last.SectionBreak ??= span.First.SectionBreak;
        RemoveRange(span.Container, span.FirstIndex, span.LastIndex - span.FirstIndex);
        return new Deleted(span.Last, 0, removed, true);
    }

    // Inserts the lines of a multi-line text at the point; the paragraph keeps the first line, the lines in between become new paragraphs
    // and the last line starts the paragraph that takes what followed the caret. Returns that last paragraph.
    private Paragraph InsertLines(Span span, Paragraph paragraph, ParagraphMap map, InsertPoint point, List<string> lines)
    {
        var shell = Shell(paragraph);
        var (item, chars) = CutOf(map, point);
        var rest = InlineSplitter.Split(paragraph, item, chars);
        var tail = shell.Clone();
        foreach (var inline in rest)
        {
            tail.Inlines.Add(inline);
        }

        tail.SectionBreak = paragraph.SectionBreak;
        paragraph.SectionBreak = null;
        var template = point.Run;

        if (lines[0].Length > 0)
        {
            var head = Map(span.Story, paragraph);
            InsertContents(paragraph, head, ResolvePoint(head, head.Projection.Text.Length), Contents(lines[0]), template);
        }

        var created = new List<Block>(lines.Count - 1);
        var runShell = MakeRun(shell, template);
        for (var i = 1; i < lines.Count - 1; i++)
        {
            var middle = shell.Clone();
            if (lines[i].Length > 0)
            {
                var run = runShell.Clone();
                foreach (var content in Contents(lines[i]))
                {
                    run.Content.Add(content);
                }

                middle.Inlines.Add(run);
            }

            created.Add(middle);
        }

        if (lines[^1].Length > 0)
        {
            var tailMap = Map(span.Story, tail);
            InsertContents(tail, tailMap, ResolvePoint(tailMap, 0), Contents(lines[^1]), template);
        }

        created.Add(tail);
        var index = span.Container.IndexOf(paragraph);
        InsertRange(span.Container, index + 1, created);
        return tail;
    }

    private sealed record InsertPoint(PointKind Kind, int Flat, int Chars, SimpleFieldSpan? Simple, Run? Run);

    private sealed record Span(StoryRef Story, IList<Block> Container, int FirstIndex, Paragraph First, int FirstOffset, int LastIndex, Paragraph Last, int LastOffset)
    {
        public bool IsCollapsed => FirstIndex == LastIndex && FirstOffset == LastOffset;
    }

    private readonly record struct Deleted(Paragraph Survivor, int Offset, int Removed, bool Changed);
}
