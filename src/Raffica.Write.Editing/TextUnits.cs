using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What a pointer selects and where it may select: the word and paragraph around a position (the same word rule as the word moves and word deletes of
/// <see cref="TextEditor"/>), and the container of blocks a paragraph is in (a selection never spans two containers: a table cell, a cell of a nested
/// table and the top level of a story are three).
/// </summary>
public static class TextUnits
{
    /// <summary>
    /// The word under the pointer at <paramref name="position"/>: the run of letters and digits, or of blanks, or of one kind of punctuation, that the character under the pointer is in.
    /// A hit test returns the nearest boundary between two characters: when the pointer was on the later half of a character (<paramref name="trailing"/>, <c>TextHit.Trailing</c>)
    /// the character under it is the one before the position, otherwise the one at the position (at the end of the paragraph always the one before). A field result or another
    /// atomic unit is selected whole. The range is in the paragraph of the position.
    /// </summary>
    public static TextRange WordAt(TextPosition position, ParagraphProjection projection, bool trailing)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var under = trailing && position.Offset > 0 ? position.Offset - 1 : position.Offset;
        var (start, end) = TextBoundaries.RunAt(projection.Text, under);
        if (projection.AtomicAt(start) is var (atomicStart, _) && start > atomicStart)
        {
            start = atomicStart;
        }

        if (projection.AtomicAt(end) is var (atomicBegin, atomicEnd) && end > atomicBegin)
        {
            end = atomicEnd;
        }

        return new TextRange(position with { Offset = start }, position with { Offset = end });
    }

    /// <summary>The whole paragraph of <paramref name="position"/>, from offset 0 to the end of its text.</summary>
    public static TextRange ParagraphAt(TextPosition position, ParagraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new TextRange(position with { Offset = 0 }, position with { Offset = projection.Text.Length });
    }

    /// <summary>The blocks of a story of the document (the body, a header or footer, a note, a comment), or null when the document has no such story.</summary>
    public static IList<Block>? BlocksOf(WordDocument document, StoryRef story)
    {
        ArgumentNullException.ThrowIfNull(document);
        return StoryResolver.TryBlocks(document, story);
    }

    /// <summary>The list of blocks that holds a paragraph of a story (the story's own list at the top level, the blocks of a cell inside a table, the content of a content control), or null when it is not in <paramref name="blocks"/>.</summary>
    public static IList<Block>? ContainerOf(IList<Block> blocks, Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(paragraph);
        return TextEditor.FindIn(blocks, paragraph)?.Container;
    }

    /// <summary>
    /// The index in <paramref name="container"/> of the block that is, or holds (a table), <paramref name="paragraph"/>; -1 when the paragraph is not under the container. A hit in a
    /// table or a content control that lies in the container maps to its index, so a pointer that passes over a table can stop beside it.
    /// </summary>
    public static int IndexUnder(IList<Block> container, Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(paragraph);
        for (var i = 0; i < container.Count; i++)
        {
            if (ReferenceEquals(container[i], paragraph))
            {
                return i;
            }
        }

        for (var i = 0; i < container.Count; i++)
        {
            switch (container[i])
            {
                case ContentControlBlock control when TextEditor.FindIn(control.Content, paragraph) is not null:
                    return i;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        if (TextEditor.FindInRowItem(row, paragraph) is not null)
                        {
                            return i;
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return -1;
    }
}
