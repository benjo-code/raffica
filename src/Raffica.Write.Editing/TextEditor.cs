using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The text operations on a <see cref="WordDocument"/>: insert, delete, backspace, delete forward and the paragraph break. Each operation
/// changes the model in place, raises <see cref="DocumentEdits"/> once and returns the <see cref="BlockChange"/>s that describe what it
/// did (<see cref="BlockChange.OldBlocks"/> are clones of the blocks before the edit, except a move's, which are the live pre-move objects: never mutate them). The editor locks nothing. Every operation that
/// changes the document records one step in <see cref="History"/> (an operation that changes nothing records nothing); the document must not
/// be edited by anything but this editor, its history and the code that clears the history.
/// </summary>
/// <remarks>
/// Positions are the offsets of <see cref="ParagraphProjection"/>: hidden text has no length, and a field result, a picture, a note or comment
/// reference is one atomic unit. A position inside an atomic unit is not a caret position: an insert there goes to the nearer edge of the
/// unit (the end when both are equally near), and a delete of any part of a unit removes the whole unit (a complex field with its
/// instruction, a simple field inline). Content the model keeps as unknown is never copied or removed unless it lies between two
/// removed items. A selection must lie in one story and one list of blocks (the body, one table cell, a note); a selection that leaves
/// it changes nothing. A table inside the blocks between the two ends of a selection is removed with them. A paragraph break never
/// merges across a table, a cell or a story boundary: such a backspace or delete returns null. Nor does a delete remove the paragraph break
/// of a paragraph that carries a section break (known limit: section structure is not an editing operation of this library); such a
/// delete is refused like the boundary cases. Bookmark and comment anchors of paragraphs a delete removes move to the join point.
/// </remarks>
public sealed partial class TextEditor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly WordDocument _document;
    private readonly Func<StoryRef, Paragraph, IProjectionHost> _hosts;
    private readonly IEffectiveFormat? _format;

    /// <summary>
    /// Makes an editor. The optional parameters come in the order <paramref name="time"/>, <paramref name="limits"/>, <paramref name="format"/>; a caller
    /// that passes only a later one names it. <paramref name="format"/> answers what formatting is in force for a run: only the toggles of the
    /// character formatting need it (<see cref="ToggleCharacter"/> throws without it).
    /// </summary>
    public TextEditor(WordDocument document, Func<StoryRef, Paragraph, IProjectionHost> hosts, TimeProvider? time = null, HistoryLimits? limits = null, IEffectiveFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hosts);
        _document = document;
        _hosts = hosts;
        _format = format;
        History = new EditHistory(document, time ?? TimeProvider.System, limits ?? HistoryLimits.Default);
    }

    /// <summary>The undo and redo history of the operations of this editor.</summary>
    public EditHistory History { get; }

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
    public EditResult InsertText(TextRange selection, string text) => InsertCore(selection, text, null);

    private EditResult InsertCore(TextRange selection, string text, IReadOnlyList<PropertyChange>? typingFormat)
    {
        ArgumentNullException.ThrowIfNull(text);
        var revision = History.BeginOperation();
        var lines = TextBoundaries.Lines(text);
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        // In a text, combo box, drop-down list or date control a line break is a w:br or nothing, never a new paragraph.
        var breakRule = lines.Count > 1 ? BreakRuleOf(span.Story, span.Container) : BreakRule.Paragraph;
        if (breakRule == BreakRule.None)
        {
            lines = [string.Join(" ", lines.Where(l => l.Length > 0))];
        }

        var hasText = lines.Count > 1 || lines[0].Length > 0;
        if (!hasText)
        {
            return ApplyDelete(span, revision, new EditHistory.Point(span.FirstIndex, span.FirstOffset)) ?? NoChange(span.Story, span.First, span.FirstOffset);
        }

        if (!span.IsCollapsed && RemovesSectionBreak(span))
        {
            return NoChange(span.Story, span.First, span.FirstOffset);
        }

        var old = CloneBlocks(span);
        var references = span.IsCollapsed ? [] : ReferencesIn(span);
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
        offset = EditRules.Snap(map, offset);
        var point = ResolvePoint(map, offset);
        TextPosition caret;
        var added = 1;
        if (breakRule == BreakRule.LineBreak)
        {
            var contents = new List<RunContent>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    contents.Add(new BreakContent(BreakKind.TextWrapping));
                }

                contents.AddRange(Contents(lines[i]));
            }

            InsertContents(paragraph, map, point, contents, null);
            caret = new TextPosition(span.Story, paragraph, offset + lines.Sum(l => l.Length) + lines.Count - 1);
        }
        else if (lines.Count == 1)
        {
            InsertContents(paragraph, map, point, Contents(lines[0]), null);
            if (typingFormat is { Count: > 0 } && lines[0].Length > 0)
            {
                FormatRange(span.Story, Map(span.Story, paragraph), offset, offset + lines[0].Length, typingFormat);
            }

            caret = new TextPosition(span.Story, paragraph, offset + lines[0].Length);
        }
        else
        {
            var last = InsertLines(span, paragraph, map, point, lines);
            added = lines.Count;
            caret = new TextPosition(span.Story, last, lines[^1].Length);
        }

        var gone = RemoveNotesOf(references);
        _document.Edits.Raise();
        var inserted = new EditResult(caret, [new BlockChange(span.Story, span.Container, span.FirstIndex, removed, added, old)], Objects: gone);
        _document.Edits.Attribute(inserted.Changes);
        History.Record(inserted, new EditHistory.Point(span.FirstIndex, span.FirstOffset), revision, span.IsCollapsed && lines.Count == 1 ? lines[0] : null, breakRule != BreakRule.LineBreak && lines.Count == 1 && lines[0].Length > 0 && typingFormat is { Count: > 0 } ? typingFormat : null);
        return inserted;
    }

    /// <summary>Removes the selection. Paragraphs it spans are merged: the first paragraph's properties win unless it is left empty, then the last one's.</summary>
    public EditResult Delete(TextRange selection)
    {
        var revision = History.BeginOperation();
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        return ApplyDelete(span, revision, new EditHistory.Point(span.FirstIndex, span.FirstOffset)) ?? NoChange(span.Story, span.First, span.FirstOffset);
    }

    /// <summary>
    /// Deletes before the caret: one grapheme cluster, or one atomic unit, or (<see cref="DeleteUnit.Word"/>) back to the start of the word:
    /// blanks right before the caret first, then one run of letters, digits, underscores and combining marks, or one run of any other kind
    /// of character. At a paragraph start it merges with the previous paragraph; at the start of a cell, a story, or after a table it
    /// returns null.
    /// <para>
    /// A field result right before the caret (either unit) is selected first, not deleted: the result has <see cref="EditResult.Selected"/>
    /// set, no <see cref="EditResult.Changes"/>, no raised revision and the caret the caller passed in (which may lie inside the unit, so
    /// the UI normalizes it). The caller deletes the field with <see cref="Delete"/> of <see cref="EditResult.Selected"/>; calling this
    /// method again at the same caret selects the field again. A picture or a note reference is deleted at once as one unit. A
    /// <see cref="DeleteUnit.Word"/> delete that reaches into a field that is not adjacent to the caret removes it whole without the select
    /// step.
    /// </para>
    /// </summary>
    public EditResult? Backspace(TextPosition caret, DeleteUnit unit)
    {
        var revision = History.BeginOperation();
        var (story, container, index) = Locate(caret);
        var map = Map(story, caret.Paragraph);
        var text = map.Projection.Text;
        var offset = EditRules.Snap(map, Math.Clamp(caret.Offset, 0, text.Length));
        if (offset == 0)
        {
            if (RemoveListAtStart(story, container, index, caret.Paragraph, revision) is { } unlisted)
            {
                return unlisted;
            }

            if (index > 0 && container[index - 1] is Paragraph previous)
            {
                return ApplyDelete(new Span(story, container, index - 1, previous, int.MaxValue, index, caret.Paragraph, 0), revision, new EditHistory.Point(index, 0));
            }

            return null;
        }

        if (EditRules.FieldUnitAt(map.Projection, offset - 1) is { } fieldBefore)
        {
            return Select(caret, fieldBefore);
        }

        var start = unit == DeleteUnit.Word
            ? TextBoundaries.PreviousWord(text, offset)
            : map.Projection.AtomicAt(offset - 1) is { } atom ? atom.Start : TextBoundaries.PreviousCluster(text, offset);
        return ApplyDelete(new Span(story, container, index, caret.Paragraph, start, index, caret.Paragraph, offset), revision, new EditHistory.Point(index, offset));
    }

    /// <summary>
    /// The mirror of <see cref="Backspace"/>: deletes after the caret; at a paragraph end it merges with the next paragraph. A field result
    /// right after the caret is selected first, not deleted, with the same contract as <see cref="Backspace"/>: <see cref="EditResult.Selected"/>
    /// is set, nothing changes, the caller passes it to <see cref="Delete"/>, and a repeated call at the same caret selects it again. A
    /// picture or a note reference is deleted at once; a <see cref="DeleteUnit.Word"/> delete that reaches into a non-adjacent field removes it.
    /// </summary>
    public EditResult? DeleteForward(TextPosition caret, DeleteUnit unit)
    {
        var revision = History.BeginOperation();
        var (story, container, index) = Locate(caret);
        var map = Map(story, caret.Paragraph);
        var text = map.Projection.Text;
        var offset = EditRules.Snap(map, Math.Clamp(caret.Offset, 0, text.Length));
        if (offset == text.Length)
        {
            if (index + 1 < container.Count && container[index + 1] is Paragraph next)
            {
                return ApplyDelete(new Span(story, container, index, caret.Paragraph, offset, index + 1, next, 0), revision, new EditHistory.Point(index, offset));
            }

            return null;
        }

        if (EditRules.FieldUnitAt(map.Projection, offset) is { } fieldAfter)
        {
            return Select(caret, fieldAfter);
        }

        var end = unit == DeleteUnit.Word
            ? TextBoundaries.NextWord(text, offset)
            : map.Projection.AtomicAt(offset) is { } atom ? atom.End : TextBoundaries.NextCluster(text, offset);
        return ApplyDelete(new Span(story, container, index, caret.Paragraph, offset, index, caret.Paragraph, end), revision, new EditHistory.Point(index, offset));
    }

    /// <summary>Replaces the selection with a paragraph break (on an empty list paragraph, Enter ends the list instead: see <see cref="ToggleList"/>): the second paragraph has the properties of the first and takes what follows the caret.</summary>
    public EditResult SplitParagraph(TextRange selection) => (selection.IsEmpty ? EndEmptyListItem(selection.Start) : null) ?? InsertText(selection, "\n");

    // The first Backspace or Delete next to a field: it selects the unit and changes nothing; the caller deletes the selection next.
    private static EditResult Select(TextPosition caret, (int Start, int End) unit) =>
        new(caret, [], new TextRange(caret with { Offset = unit.Start }, caret with { Offset = unit.End }));

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

    internal static (IList<Block> Container, int Index)? FindIn(IList<Block> blocks, Paragraph paragraph)
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
            switch (block)
            {
                case ContentControlBlock control when FindIn(control.Content, paragraph) is { } inControl:
                    return inControl;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        if (FindInRowItem(row, paragraph) is { } found)
                        {
                            return found;
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return null;
    }

    /// <summary>How a list of blocks takes a line break: as a paragraph (everywhere but in a plain-text control), as a line break, or not at all.</summary>
    private enum BreakRule
    {
        Paragraph,
        LineBreak,
        None,
    }

    // A content control of the kinds text (w:text), combo box, drop-down list and date holds one paragraph, and Word refuses a file that gives one a
    // second paragraph ("unreadable content": checked with Word on poi-bug54849 for each of the four kinds): a multi-line text control (w:multiLine)
    // takes a w:br, every other one takes no break. A control without a kind (rich text) is edited like any other list of blocks.
    private BreakRule BreakRuleOf(StoryRef story, IList<Block> container) =>
        SingleParagraphControlOf(StoryOf(story).Blocks, container) is { } control
            ? control.Properties.Children.Any(c => c.Name == W + "text" && IsOn(c.Attribute(W + "multiLine")?.Value)) ? BreakRule.LineBreak : BreakRule.None
            : BreakRule.Paragraph;

    private static readonly string[] s_singleParagraphKinds = ["text", "comboBox", "dropDownList", "date"];

    private static bool IsOn(string? value) => value is "1" or "true" or "on";

    // True when the list of blocks is the content of a control of those kinds (or inside one).
    internal static bool InSingleParagraphControl(IList<Block> blocks, IList<Block> container) => SingleParagraphControlOf(blocks, container) is not null;

    private static ContentControlBlock? SingleParagraphControlOf(IList<Block> blocks, IList<Block> container)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ContentControlBlock control:
                    if (ReferenceEquals(control.Content, container))
                    {
                        return control.Properties.Children.Any(c => c.Name.Namespace == W && s_singleParagraphKinds.Contains(c.Name.LocalName)) ? control : null;
                    }

                    if (SingleParagraphControlOf(control.Content, container) is { } inner)
                    {
                        return inner;
                    }

                    break;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        if (SingleParagraphControlInRowItem(row, container) is { } found)
                        {
                            return found;
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return null;
    }

    private static ContentControlBlock? SingleParagraphControlInRowItem(RowItem item, IList<Block> container)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    if (SingleParagraphControlInRowItem(child, container) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case TableCell cell:
                return SingleParagraphControlOf(cell.Blocks, container);
            default:
                return null;
        }
    }

    internal static (IList<Block> Container, int Index)? FindInRowItem(RowItem item, Paragraph paragraph)
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
        EditRules.WidenToUnits(projection, ref start, ref end);

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
            if (flat < 0 || EditRules.SurvivesTextDelete(piece))
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

        EditRules.KillUnknownBetween(map, lowest, highest, kill);

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

    private Story StoryOf(StoryRef story) =>
        StoryResolver.TryStory(_document, story) ?? throw new StalePositionException("The story is not part of the document.");

    private (StoryRef Story, IList<Block> Container, int Index) Locate(TextPosition position)
    {
        ArgumentNullException.ThrowIfNull(position.Paragraph);
        var story = StoryOf(position.Story);
        return FindIn(story.Blocks, position.Paragraph) is { } found
            ? (position.Story, found.Container, found.Index)
            : throw new StalePositionException("The paragraph is not in the story of the position.");
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

    private EditResult? ApplyDelete(Span span, int revision, EditHistory.Point before)
    {
        if (span.IsCollapsed || RemovesSectionBreak(span))
        {
            return null;
        }

        var old = CloneBlocks(span);
        var references = ReferencesIn(span);
        var deleted = DeleteSpan(span);
        if (!deleted.Changed)
        {
            return null;
        }

        var gone = RemoveNotesOf(references);
        _document.Edits.Raise();
        var result = new EditResult(
            new TextPosition(span.Story, deleted.Survivor, deleted.Offset),
            [new BlockChange(span.Story, span.Container, span.FirstIndex, deleted.Removed, 1, old)],
            Objects: gone);
        _document.Edits.Attribute(result.Changes);
        History.Record(result, before, revision, null);
        return result;
    }

    // Changing the section structure is not an editing operation of this library: a range that would remove the paragraph break of a
    // paragraph that ends a section (the first paragraph of a merge or a paragraph in between) is refused. The last paragraph of a range
    // keeps its own break.
    private static bool RemovesSectionBreak(Span span)
    {
        if (span.FirstIndex == span.LastIndex)
        {
            return false;
        }

        for (var i = span.FirstIndex; i < span.LastIndex; i++)
        {
            if (span.Container[i] is Paragraph { SectionBreak: not null })
            {
                return true;
            }
        }

        return false;
    }

    // The text delete and the bookmark rule: a bookmark whose whole range lies in the deleted range is removed with both markers; a bookmark that is only partly deleted keeps them (moved by the anchor rule).
    private Deleted DeleteSpan(Span span)
    {
        var inside = EditRules.BookmarksInside(span.Container, span.FirstIndex, span.LastIndex);
        var deleted = DeleteSpanCore(span);
        if (inside is not null && deleted.Changed)
        {
            EditRules.RemoveEmptiedBookmarks(deleted.Survivor, inside);
        }

        return deleted;
    }

    private Deleted DeleteSpanCore(Span span)
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
        var anchors = EditRules.AnchorsOfBlocks(span.Container, span.FirstIndex + 1, span.LastIndex);
        var remaining = Map(span.Story, span.First).Projection.Text.Length;
        if (remaining > 0)
        {
            foreach (var anchor in anchors)
            {
                span.First.Inlines.Add(anchor);
            }

            foreach (var inline in span.Last.Inlines)
            {
                span.First.Inlines.Add(inline);
            }

            span.First.SectionBreak = span.Last.SectionBreak;
            RemoveRange(span.Container, span.FirstIndex + 1, span.LastIndex - span.FirstIndex);
            return new Deleted(span.First, remaining, removed, true);
        }

        var inlines = span.First.Inlines.Concat(anchors).Concat(span.Last.Inlines).ToList();
        span.Last.Inlines.Clear();
        foreach (var inline in inlines)
        {
            span.Last.Inlines.Add(inline);
        }

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
