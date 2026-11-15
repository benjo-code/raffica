using Raffica.Write.Model;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// Edits of a model the way the editing core will make them (task 10 builds the real one), each returning the <see cref="BlockChange"/> that
/// describes it, for the tests of the incremental layout. The text of a paragraph is the text of its runs (<see cref="TextContent"/> only, also
/// inside hyperlinks, simple fields and content controls); an edit leaves everything else of the paragraph as it is.
/// </summary>
internal static class TestEdit
{
    /// <summary>Every paragraph of the body, inside tables and block content controls too, with the list it is in and its index there.</summary>
    public static List<(Paragraph Paragraph, IList<Block> Container, int Index)> Paragraphs(WordDocument document)
    {
        var found = new List<(Paragraph, IList<Block>, int)>();
        Collect(document.Body.Blocks, found, 0);
        return found;
    }

    private static void Collect(IList<Block> blocks, List<(Paragraph, IList<Block>, int)> found, int depth)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case Paragraph paragraph:
                    found.Add((paragraph, blocks, i));
                    break;
                case ContentControlBlock control when depth < 8:
                    Collect(control.Content, found, depth + 1);
                    break;
                case Table table when depth < 8:
                    foreach (var row in table.Rows)
                    {
                        CollectRow(row, found, depth + 1);
                    }

                    break;
            }
        }
    }

    private static void CollectRow(RowItem item, List<(Paragraph, IList<Block>, int)> found, int depth)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var inner in row.Items)
                {
                    CollectRow(inner, found, depth);
                }

                break;
            case TableCell cell:
                Collect(cell.Blocks, found, depth);
                break;
        }
    }

    /// <summary>The paragraph's own place: the list that holds it and its index.</summary>
    public static (IList<Block> Container, int Index) Find(WordDocument document, Paragraph paragraph)
    {
        foreach (var (candidate, container, index) in Paragraphs(document))
        {
            if (ReferenceEquals(candidate, paragraph))
            {
                return (container, index);
            }
        }

        throw new InvalidOperationException("The paragraph is not in the body.");
    }

    /// <summary>The index of the top-level block of the body that holds the paragraph.</summary>
    public static int TopLevelIndex(WordDocument document, Paragraph paragraph)
    {
        for (var i = 0; i < document.Body.Blocks.Count; i++)
        {
            if (ReferenceEquals(document.Body.Blocks[i], paragraph) || Paragraphs(document.Body.Blocks[i]).Any(p => ReferenceEquals(p, paragraph)))
            {
                return i;
            }
        }

        throw new InvalidOperationException("The paragraph is not in the body.");
    }

    private static IEnumerable<Paragraph> Paragraphs(Block block)
    {
        var found = new List<(Paragraph, IList<Block>, int)>();
        Collect([block], found, 0);
        return found.Select(f => f.Item1);
    }

    public static string TextOf(Paragraph paragraph) => string.Concat(Texts(paragraph.Inlines).Select(t => t.Text));

    private static IEnumerable<TextContent> Texts(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content.OfType<TextContent>())
                    {
                        yield return content;
                    }

                    break;
                case Hyperlink link:
                    foreach (var text in Texts(link.Inlines))
                    {
                        yield return text;
                    }

                    break;
                case SimpleField field:
                    foreach (var text in Texts(field.Inlines))
                    {
                        yield return text;
                    }

                    break;
                case ContentControlInline control:
                    foreach (var text in Texts(control.Inlines))
                    {
                        yield return text;
                    }

                    break;
            }
        }
    }

    private static void InsertInto(Paragraph paragraph, int offset, string text)
    {
        var at = 0;
        TextContent? last = null;
        foreach (var content in Texts(paragraph.Inlines))
        {
            if (offset <= at + content.Text.Length)
            {
                var local = offset - at;
                content.Text = content.Text.Insert(local, text);
                return;
            }

            at += content.Text.Length;
            last = content;
        }

        if (last is not null)
        {
            last.Text += text;
            return;
        }

        var run = new Run();
        run.Content.Add(new TextContent(text));
        paragraph.Inlines.Add(run);
    }

    private static void RemoveFrom(Paragraph paragraph, int offset, int length)
    {
        var at = 0;
        foreach (var content in Texts(paragraph.Inlines).ToList())
        {
            var size = content.Text.Length;
            var start = Math.Max(offset - at, 0);
            var end = Math.Min(offset + length - at, size);
            if (end > start)
            {
                content.Text = content.Text.Remove(start, end - start);
            }

            at += size;
        }
    }

    /// <summary>
    /// Inserts <paramref name="text"/> at a text offset of the paragraph. With <paramref name="replace"/> the paragraph is replaced in its list by an edited
    /// copy (a new block object), otherwise it is changed in place.
    /// </summary>
    public static BlockChange InsertText(WordDocument document, Paragraph paragraph, int offset, string text, bool replace = false)
    {
        var (container, index) = Find(document, paragraph);
        var before = paragraph.Clone();
        var target = replace ? paragraph.Clone() : paragraph;
        InsertInto(target, Math.Clamp(offset, 0, TextOf(target).Length), text);
        if (replace)
        {
            container[index] = target;
        }

        return new BlockChange(StoryRef.Body, container, index, 1, 1, [before]);
    }

    /// <summary>Deletes <paramref name="length"/> characters of the paragraph's text from <paramref name="offset"/>, in place.</summary>
    public static BlockChange DeleteText(WordDocument document, Paragraph paragraph, int offset, int length)
    {
        var (container, index) = Find(document, paragraph);
        var before = paragraph.Clone();
        var size = TextOf(paragraph).Length;
        offset = Math.Clamp(offset, 0, size);
        RemoveFrom(paragraph, offset, Math.Min(length, size - offset));
        return new BlockChange(StoryRef.Body, container, index, 1, 1, [before]);
    }

    /// <summary>Puts <paramref name="block"/> in <paramref name="container"/> at <paramref name="index"/>.</summary>
    public static BlockChange InsertBlock(IList<Block> container, int index, Block block)
    {
        container.Insert(index, block);
        return new BlockChange(StoryRef.Body, container, index, 0, 1, []);
    }

    /// <summary>Removes the block at <paramref name="index"/>.</summary>
    public static BlockChange RemoveBlock(IList<Block> container, int index)
    {
        var before = container[index].Clone();
        container.RemoveAt(index);
        return new BlockChange(StoryRef.Body, container, index, 1, 0, [before]);
    }

    /// <summary>Replaces the paragraph by two paragraphs, the text before <paramref name="offset"/> and the text from it (new block objects).</summary>
    public static BlockChange SplitParagraph(WordDocument document, Paragraph paragraph, int offset)
    {
        var (container, index) = Find(document, paragraph);
        var before = paragraph.Clone();
        var size = TextOf(paragraph).Length;
        offset = Math.Clamp(offset, 0, size);
        var head = paragraph.Clone();
        var tail = paragraph.Clone();
        head.SectionBreak = null;
        RemoveFrom(head, offset, size - offset);
        RemoveFrom(tail, 0, offset);
        container[index] = head;
        container.Insert(index + 1, tail);
        return new BlockChange(StoryRef.Body, container, index, 1, 2, [before]);
    }

    /// <summary>Replaces the paragraph and the block after it by one paragraph with both texts (a new block object).</summary>
    public static BlockChange MergeWithNext(WordDocument document, Paragraph paragraph)
    {
        var (container, index) = Find(document, paragraph);
        var next = (Paragraph)container[index + 1];
        var before = new Block[] { paragraph.Clone(), next.Clone() };
        var merged = paragraph.Clone();
        merged.SectionBreak = next.SectionBreak?.Clone();
        InsertInto(merged, TextOf(merged).Length, TextOf(next));
        container[index] = merged;
        container.RemoveAt(index + 1);
        return new BlockChange(StoryRef.Body, container, index, 2, 1, before);
    }

    /// <summary>Appends a run that holds <paramref name="reference"/> (a footnote or endnote reference) to the paragraph, in place.</summary>
    public static BlockChange AddNoteReference(WordDocument document, Paragraph paragraph, RunContent reference)
    {
        var (container, index) = Find(document, paragraph);
        var before = paragraph.Clone();
        var run = new Run();
        run.Content.Add(reference);
        paragraph.Inlines.Add(run);
        return new BlockChange(StoryRef.Body, container, index, 1, 1, [before]);
    }

    /// <summary>Removes every footnote and endnote reference from the paragraph's runs, in place.</summary>
    public static BlockChange RemoveNoteReferences(WordDocument document, Paragraph paragraph)
    {
        var (container, index) = Find(document, paragraph);
        var before = paragraph.Clone();
        foreach (var run in paragraph.Inlines.OfType<Run>().ToList())
        {
            foreach (var content in run.Content.Where(c => c is FootnoteReference or EndnoteReference).ToList())
            {
                run.Content.Remove(content);
            }
        }

        return new BlockChange(StoryRef.Body, container, index, 1, 1, [before]);
    }
}
