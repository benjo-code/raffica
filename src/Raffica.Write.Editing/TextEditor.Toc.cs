using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Why the last typing, split, paste, delete or cut was refused because it touches a table of contents (null when it was not, and at the start of every one of those calls). A table of contents updates itself, so it is changed
    /// by its headings only and removed whole: an edit inside it, or over part of it, changes nothing and leaves this reason, which the caller shows (<see cref="FieldRefusal.Reason"/>). The edges of the field count as inside.
    /// </summary>
    public FieldRefusal? LastFieldRefusal { get; private set; }

    /// <summary>Forgets <see cref="LastFieldRefusal"/>; a caller that runs several kinds of operation through one place clears it before each, so an operation that does not look at tables of contents cannot report an older refusal.</summary>
    public void ClearFieldRefusal() => LastFieldRefusal = null;

    /// <summary>
    /// The place a caret may rest at: a position inside a table of contents (a field that spans paragraphs, so a caret in its middle paragraphs would sit in text the field owns) moves to the nearer edge of the field, the start of
    /// its first paragraph or the end of its last (the end when both are equally near); every other position is returned as it is.
    /// </summary>
    public TextPosition SnapCaret(TextPosition position)
    {
        var (story, container, index) = Locate(position);
        if (TocRanges.Around(container, index) is not { } toc)
        {
            return position;
        }

        var lastLength = TextLengthOf(story, container, toc.Last);
        if ((index == toc.First && position.Offset <= 0) || (index == toc.Last && position.Offset >= lastLength))
        {
            return position;
        }

        return index - toc.First < toc.Last - index
            ? new TextPosition(story, (Paragraph)container[toc.First], 0)
            : new TextPosition(story, (Paragraph)container[toc.Last], lastLength);
    }

    private int TextLengthOf(StoryRef story, IList<Block> container, int index) => Map(story, (Paragraph)container[index]).Projection.Text.Length;

    // The refusal for an edit over the span: null when it touches no table of contents or covers each one it touches whole (then the field goes with the span). A caret is inside, a selection that takes only part of the field is part of it.
    private FieldRefusal? TocRefusalOf(Span span)
    {
        if (TocRanges.In(span.Container) is not { } ranges)
        {
            return null;
        }

        foreach (var toc in ranges)
        {
            if (span.FirstIndex > toc.Last || span.LastIndex < toc.First)
            {
                continue;
            }

            if (!span.IsCollapsed && CoversWhole(span, toc))
            {
                continue;
            }

            return new FieldRefusal(span.IsCollapsed ? FieldRefusalKind.InsideToc : FieldRefusalKind.PartOfToc);
        }

        return null;
    }

    private bool CoversWhole(Span span, TocRange toc) =>
        (span.FirstIndex < toc.First || (span.FirstIndex == toc.First && span.FirstOffset <= 0))
        && (span.LastIndex > toc.Last || (span.LastIndex == toc.Last && span.LastOffset >= TextLengthOf(span.Story, span.Container, toc.Last)));

    // A refused edit: the reason is kept for the caller and nothing changed.
    private EditResult RefuseToc(Span span, FieldRefusal refusal)
    {
        LastFieldRefusal = refusal;
        return NoChange(span.Story, span.First, span.FirstOffset);
    }

    // A first Backspace or Delete next to a table of contents selects the whole field (the caller deletes the selection next), as it does for a field in one paragraph. Null when the caret is not next to one.
    private EditResult? SelectTocNextTo(TextPosition caret, StoryRef story, IList<Block> container, int index, int offset, int length, bool forward)
    {
        if (TocRanges.In(container) is not { } ranges)
        {
            return null;
        }

        foreach (var toc in ranges)
        {
            var next = forward
                ? (index == toc.First && offset <= 0) || (index == toc.First - 1 && offset >= length)
                : (index == toc.Last && offset >= length) || (index == toc.Last + 1 && offset <= 0);
            if (next)
            {
                var first = new TextPosition(story, (Paragraph)container[toc.First], 0);
                var last = new TextPosition(story, (Paragraph)container[toc.Last], TextLengthOf(story, container, toc.Last));
                return new EditResult(caret, [], new TextRange(first, last));
            }
        }

        return null;
    }

    // A span that covers a table of contents whole takes it with it. The field marks of the paragraphs at the ends of the span are taken away first (the text delete of a paragraph sees only the visible text and would leave
    // the begin, the instruction or the end behind); a paragraph in the middle of the span goes whole. Returns the span with the end offset of the stripped last paragraph.
    private Span UnwrapWholeTocs(Span span)
    {
        if (span.IsCollapsed || TocRanges.In(span.Container) is not { } ranges)
        {
            return span;
        }

        foreach (var toc in ranges)
        {
            if (span.FirstIndex > toc.Last || span.LastIndex < toc.First || !CoversWhole(span, toc))
            {
                continue;
            }

            if (span.FirstIndex >= toc.First && span.FirstIndex <= toc.Last)
            {
                StripFieldMarks(span.First.Inlines);
            }

            if (span.LastIndex >= toc.First && span.LastIndex <= toc.Last)
            {
                StripFieldMarks(span.Last.Inlines);
                span = span with { LastOffset = Map(span.Story, span.Last).Projection.Text.Length };
            }
        }

        return span;
    }

    // Removes the field marks (begin, instruction, separate, end) of every field in the inlines, entered containers included, and the runs and links that held nothing else.
    private static bool StripFieldMarks(IList<Inline> list)
    {
        var any = false;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is Run run)
            {
                var removed = false;
                for (var c = run.Content.Count - 1; c >= 0; c--)
                {
                    if (run.Content[c] is FieldChar or InstructionText)
                    {
                        run.Content.RemoveAt(c);
                        removed = true;
                    }
                }

                any |= removed;
                if (removed && run.Content.Count == 0)
                {
                    list.RemoveAt(i);
                }
            }
            else if (ParagraphMap.ChildrenOf(list[i]) is { } children && StripFieldMarks(children))
            {
                any = true;
                if (children.Count == 0 && list[i] is not ContentControlInline)
                {
                    list.RemoveAt(i);
                }
            }
        }

        return any;
    }
}
