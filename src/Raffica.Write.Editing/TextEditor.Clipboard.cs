using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Copy, cut and paste of <see cref="TextEditor"/>.</summary>
public sealed partial class TextEditor
{
    private string? _sourceId;

    /// <summary>
    /// The codec of the private fragment format. Null (the default) means plain text only: <see cref="Copy"/> makes no fragment and
    /// <see cref="Paste"/> ignores one. Nothing of the clipboard exists before the first <see cref="Copy"/> or <see cref="Paste"/>.
    /// </summary>
    public IFragmentCodec? FragmentCodec { get; set; }

    /// <summary>
    /// Copies the range. The plain text has a line feed between paragraphs and no picture or reference characters; the fragment (when
    /// <see cref="FragmentCodec"/> is set) holds the paragraphs with their direct formatting, cut at the ends of the range (a field or
    /// another atomic unit that the range touches is taken whole). A range that is empty or leaves its story or block list gives empty
    /// plain text and no fragment. The document is not touched and nothing is raised or recorded.
    /// </summary>
    /// <remarks>
    /// A fragment never carries bookmarks, comment range markers or comment references (a comment goes only through its own command),
    /// the section break of a paragraph, paragraph ids, deleted text or tracked-change wrappers (the visible, accepted text is copied as
    /// plain runs). A table inside the range is copied as text: one paragraph per row, the text of a cell's paragraphs joined by a blank
    /// and the cells joined by a tab. Blocks other than paragraphs and tables (content controls, unknown blocks) are not copied.
    /// </remarks>
    public ClipboardContent Copy(TextRange range)
    {
        var id = SourceId();
        if (Collect(range) is not { } copied)
        {
            return new ClipboardContent(string.Empty, null, id);
        }

        byte[]? fragment = null;
        if (FragmentCodec is { } codec && copied.Paragraphs.Count > 0)
        {
            fragment = codec.Write(copied.Paragraphs);
        }

        return new ClipboardContent(copied.Text, fragment, id);
    }

    /// <summary>
    /// <see cref="Copy"/> and <see cref="Delete"/> of the range as one history step and one raise. When the delete changes nothing (an
    /// empty range, a range across a section break) the result has no changes, nothing is recorded, and the caller should not replace
    /// the clipboard with the content.
    /// </summary>
    public CutResult Cut(TextRange range) => new(Copy(range), Delete(range));

    /// <summary>
    /// Replaces the selection with the clipboard content as one history step and one raise. With a readable fragment the paragraphs
    /// are pasted with their direct formatting: the first one joins the paragraph at the caret (which keeps its paragraph properties
    /// unless nothing stands before the caret, then it takes the first pasted paragraph's), middle ones keep their own, the last one
    /// takes what followed the caret (and takes the target's paragraph properties unless nothing followed, then its own). A field is
    /// pasted as its current result text, a picture, drawing or note reference as nothing. In the same document (the content was copied
    /// by this editor) styles and numbering stay; from anywhere else a paragraph or run style is kept only where
    /// <paramref name="styleExists"/> says so, the numbering reference is dropped and a link with a relationship id becomes its text.
    /// Without a fragment, or when the fragment cannot be read (a refused fragment is treated as absent, never partly pasted), the plain
    /// text goes through <see cref="InsertText"/>. A selection that leaves its story or block list or runs over a section break pastes
    /// nothing and records nothing.
    /// </summary>
    public EditResult Paste(TextRange selection, ClipboardContent content, Func<string, bool> styleExists)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(styleExists);
        var paragraphs = ReadFragment(content);
        if (paragraphs is null)
        {
            return InsertText(selection, content.PlainText ?? string.Empty);
        }

        var same = content.SourceId is not null && content.SourceId == _sourceId;
        foreach (var paragraph in paragraphs)
        {
            FragmentCleaner.ForPaste(paragraph, same, styleExists);
        }

        if (paragraphs.Count == 1 && paragraphs[0].Inlines.Count == 0)
        {
            return InsertText(selection, string.Empty);
        }

        return PasteParagraphs(selection, paragraphs);
    }

    private string SourceId() => _sourceId ??= Guid.NewGuid().ToString("N");

    private List<Paragraph>? ReadFragment(ClipboardContent content)
    {
        if (content.Fragment is not { } bytes || FragmentCodec is not { } codec)
        {
            return null;
        }

        try
        {
            var read = codec.Read(bytes);
            return read.Count == 0 ? null : [.. read];
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private EditResult PasteParagraphs(TextRange selection, List<Paragraph> paragraphs)
    {
        var revision = History.BeginOperation();
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        if (!span.IsCollapsed && RemovesSectionBreak(span))
        {
            return NoChange(span.Story, span.First, span.FirstOffset);
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
        offset = EditRules.Snap(map, offset);
        var restLength = map.Projection.Text.Length - offset;
        var (item, chars) = CutOf(map, ResolvePoint(map, offset));
        var shell = Shell(paragraph);
        var rest = InlineSplitter.Split(paragraph, item, chars);
        TextPosition caret;
        if (paragraphs.Count == 1)
        {
            AppendAll(paragraph.Inlines, paragraphs[0].Inlines);
            AppendAll(paragraph.Inlines, rest);
            caret = new TextPosition(span.Story, paragraph, Map(span.Story, paragraph).Projection.Text.Length - restLength);
        }
        else
        {
            var section = paragraph.SectionBreak;
            paragraph.SectionBreak = null;
            if (offset == 0)
            {
                AdoptProperties(paragraph, paragraphs[0]);
            }

            AppendAll(paragraph.Inlines, paragraphs[0].Inlines);
            var created = new List<Block>(paragraphs.Count - 1);
            for (var i = 1; i < paragraphs.Count - 1; i++)
            {
                created.Add(paragraphs[i]);
            }

            var last = paragraphs[^1];
            Paragraph tail;
            if (restLength == 0)
            {
                tail = last;
            }
            else
            {
                tail = shell;
                AppendAll(tail.Inlines, last.Inlines);
            }

            AppendAll(tail.Inlines, rest);
            tail.SectionBreak = section;
            created.Add(tail);
            InsertRange(span.Container, span.Container.IndexOf(paragraph) + 1, created);
            caret = new TextPosition(span.Story, tail, Map(span.Story, tail).Projection.Text.Length - restLength);
        }

        _document.Edits.Raise();
        var result = new EditResult(caret, [new BlockChange(span.Story, span.Container, span.FirstIndex, removed, paragraphs.Count, old)]);
        History.Record(result, new EditHistory.Point(span.FirstIndex, span.FirstOffset), revision, null);
        return result;
    }

    private static void AppendAll(IList<Inline> target, IEnumerable<Inline> items)
    {
        foreach (var inline in items.ToList())
        {
            target.Add(inline);
        }
    }

    // The paragraph takes the paragraph properties of the source (children only; the paragraph keeps its own unknown attributes).
    private static void AdoptProperties(Paragraph paragraph, Paragraph source)
    {
        var bag = paragraph.Properties;
        while (bag.Children.Count > 0)
        {
            bag.Remove(bag.Children[0].Name);
        }

        foreach (var child in source.Properties.Children)
        {
            bag.Set(child.Name, new XElement(child));
        }

        bag.WasPresent = bag.WasPresent || source.Properties.WasPresent;
    }

    private (List<Paragraph> Paragraphs, string Text)? Collect(TextRange range)
    {
        if (Resolve(range) is not { } span || span.IsCollapsed)
        {
            return null;
        }

        var paragraphs = new List<Paragraph>();
        var lines = new List<string>();
        for (var i = span.FirstIndex; i <= span.LastIndex; i++)
        {
            switch (span.Container[i])
            {
                case Paragraph paragraph:
                    var start = i == span.FirstIndex ? span.FirstOffset : 0;
                    var end = i == span.LastIndex ? span.LastOffset : int.MaxValue;
                    var (slice, text) = Slice(span.Story, paragraph, start, end);
                    paragraphs.Add(slice);
                    lines.Add(text);
                    break;
                case Table table:
                    foreach (var rowText in TableRowTexts(span.Story, table))
                    {
                        paragraphs.Add(TextParagraph(rowText));
                        lines.Add(rowText);
                    }

                    break;
                default:
                    break;
            }
        }

        return (paragraphs, string.Join('\n', lines));
    }

    // A copy of the paragraph restricted to the visible range [start, end), cleaned for the clipboard, with its plain text.
    private (Paragraph Paragraph, string Text) Slice(StoryRef story, Paragraph source, int start, int end)
    {
        var copy = source.Clone();
        copy.SectionBreak = null;
        var map = Map(story, copy);
        var length = map.Projection.Text.Length;
        var from = Math.Clamp(start, 0, length);
        var to = Math.Clamp(end, 0, length);
        EditRules.WidenToUnits(map.Projection, ref from, ref to);
        if (to < length)
        {
            var (item, chars) = CutOf(map, ResolvePoint(map, to));
            InlineSplitter.Split(copy, item, chars);
        }

        if (from > 0)
        {
            map = Map(story, copy);
            var (item, chars) = CutOf(map, ResolvePoint(map, from));
            var tail = InlineSplitter.Split(copy, item, chars);
            copy.Inlines.Clear();
            AppendAll(copy.Inlines, tail);
        }

        FragmentCleaner.ForCopy(copy);
        var text = Map(story, copy).Projection.Text.Replace(ParagraphProjection.ObjectChar.ToString(), string.Empty, StringComparison.Ordinal);
        return (copy, text);
    }

    private IEnumerable<string> TableRowTexts(StoryRef story, Table table)
    {
        foreach (var rowItem in table.Rows)
        {
            if (rowItem is TableRow row)
            {
                yield return RowText(story, row);
            }
        }
    }

    private string RowText(StoryRef story, TableRow row)
    {
        var cells = new List<string>();
        foreach (var item in row.Items)
        {
            switch (item)
            {
                case TableCell cell:
                    cells.Add(string.Join(' ', BlockTexts(story, cell.Blocks)));
                    break;
                case TableRow nested:
                    cells.Add(RowText(story, nested));
                    break;
                default:
                    break;
            }
        }

        return string.Join('\t', cells);
    }

    private IEnumerable<string> BlockTexts(StoryRef story, IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return ParagraphProjection.Build(paragraph, _hosts(story, paragraph)).Text.Replace(ParagraphProjection.ObjectChar.ToString(), string.Empty, StringComparison.Ordinal);
                    break;
                case Table table:
                    foreach (var text in TableRowTexts(story, table))
                    {
                        yield return text;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    // A paragraph of plain text: tabs become tab contents, the paragraph has no properties of its own.
    private static Paragraph TextParagraph(string text)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        foreach (var content in Contents(text))
        {
            run.Content.Add(content);
        }

        if (run.Content.Count > 0)
        {
            paragraph.Inlines.Add(run);
        }

        return paragraph;
    }
}
