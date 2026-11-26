using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Why the last edit was refused because it touches a table of contents (null when it was not, and at the start of every call that looks at tables of contents). A table of contents updates itself, so it is changed by its
    /// headings only and removed whole: an edit inside it, or over part of it, changes nothing and leaves this reason, which the caller shows (<see cref="FieldRefusal.Reason"/>). The edges of the field count as inside.
    /// Refused: typing, Enter, paste and every other insert at or in it, a delete or cut over part of it, a picture, note, table, bookmark or section break in it, a list command or an outline shift over it, and a move of blocks that
    /// separates its paragraphs or lands inside it. Not refused: character and paragraph formatting and styles (they change no content or structure, and a refresh gives the entries their style again).
    /// </summary>
    public FieldRefusal? LastFieldRefusal { get; private set; }

    /// <summary>Forgets <see cref="LastFieldRefusal"/>; a caller that runs several kinds of operation through one place clears it before each, so an operation that does not look at tables of contents cannot report an older refusal.</summary>
    public void ClearFieldRefusal() => LastFieldRefusal = null;

    /// <summary>
    /// The place a caret may rest at: a position inside a table of contents (a field that spans paragraphs, so a caret in its middle paragraphs would sit in text the field owns) moves to an edge of the field, the start of
    /// its first paragraph or the end of its last. With <paramref name="from"/> (where the caret was, for a key that moves it) the edge is the one in the direction of the move, so a key steps over the field; without it (a click) it is the
    /// nearer edge (the end when both are equally near). Every other position is returned as it is, and a position that is not in the document too.
    /// </summary>
    public TextPosition SnapCaret(TextPosition position, TextPosition? from = null)
    {
        if (!TryLocate(position, out var story, out var container, out var index) || _tocs.Around(container, index) is not { } toc)
        {
            return position;
        }

        var lastLength = TextLengthOf(story, container, toc.Last);
        if ((index == toc.First && position.Offset <= 0) || (index == toc.Last && position.Offset >= lastLength))
        {
            return position;
        }

        var forward = from is { } origin ? DirectionOf(origin, position, container, index) : null;
        var toEnd = forward ?? (index - toc.First >= toc.Last - index);
        return toEnd
            ? new TextPosition(story, (Paragraph)container[toc.Last], lastLength)
            : new TextPosition(story, (Paragraph)container[toc.First], 0);
    }

    // True when the position is after where the caret was, false when before, null when that is unknown or the same.
    private bool? DirectionOf(TextPosition from, TextPosition position, IList<Block> container, int index)
    {
        if (ReferenceEquals(from.Paragraph, position.Paragraph))
        {
            return from.Offset == position.Offset ? null : position.Offset > from.Offset;
        }

        if (!TryLocate(from, out _, out var fromContainer, out var fromIndex) || !ReferenceEquals(fromContainer, container))
        {
            return null;
        }

        return index > fromIndex;
    }

    // The tables of contents of a list of blocks, kept between edits (see TocIndex); the answer the tests compare with a fresh search.
    internal List<TocRange>? TocRangesOf(IList<Block> container) => _tocs.RangesOf(container);

    private bool TryLocate(TextPosition position, out StoryRef story, out IList<Block> container, out int index)
    {
        story = position.Story;
        container = null!;
        index = -1;
        if (position.Paragraph is null || StoryResolver.TryStory(_document, position.Story) is not { } resolved || FindIn(resolved.Blocks, position.Paragraph) is not { } found)
        {
            return false;
        }

        container = found.Container;
        index = found.Index;
        return true;
    }

    private int TextLengthOf(StoryRef story, IList<Block> container, int index) => Map(story, (Paragraph)container[index]).Projection.Text.Length;

    // A caret or an insert point: refused when it is in a table of contents (the edges included). Sets LastFieldRefusal.
    private bool TocRefusesAt(TextPosition at)
    {
        LastFieldRefusal = null;
        if (!TryLocate(at, out _, out var container, out var index) || _tocs.Around(container, index) is null)
        {
            return false;
        }

        LastFieldRefusal = new FieldRefusal(FieldRefusalKind.InsideToc);
        return true;
    }

    // A selection: refused when it takes any paragraph of a table of contents, whole or not (for commands that change the structure or add content, which have no "the field goes with it" meaning).
    private bool TocRefusesAny(TextRange selection)
    {
        LastFieldRefusal = null;
        if (!TryLocate(selection.Start, out _, out var container, out var first) || !TryLocate(selection.End, out _, out var endContainer, out var last) || !ReferenceEquals(container, endContainer))
        {
            return false;
        }

        return TocRefusesIndices(container, Math.Min(first, last), Math.Max(first, last), selection.IsEmpty ? FieldRefusalKind.InsideToc : FieldRefusalKind.PartOfToc);
    }

    // The blocks [first, last] of a list: refused when any of them is a paragraph of a table of contents.
    private bool TocRefusesIndices(IList<Block> container, int first, int last, FieldRefusalKind kind)
    {
        LastFieldRefusal = null;
        if (_tocs.RangesOf(container) is not { } ranges)
        {
            return false;
        }

        foreach (var toc in ranges)
        {
            if (last >= toc.First && first <= toc.Last)
            {
                LastFieldRefusal = new FieldRefusal(kind);
                return true;
            }
        }

        return false;
    }

    // The refusal for an edit over the span: null when it touches no table of contents or covers each one it touches whole (then the field goes with the span). A caret is inside, a selection that takes only part of the field is part of it.
    private FieldRefusal? TocRefusalOf(Span span)
    {
        if (_tocs.RangesOf(span.Container) is not { } ranges)
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

    // A section break goes before a top-level block: refused when that is between two paragraphs of a table of contents (before its first paragraph and after its last it is outside the field).
    private bool TocRefusesSplitBefore(int topLevelIndex)
    {
        LastFieldRefusal = null;
        if (_tocs.RangesOf(_document.Body.Blocks) is not { } ranges)
        {
            return false;
        }

        foreach (var toc in ranges)
        {
            if (topLevelIndex > toc.First && topLevelIndex <= toc.Last)
            {
                LastFieldRefusal = new FieldRefusal(FieldRefusalKind.InsideToc);
                return true;
            }
        }

        return false;
    }

    // The paragraphs of the tables of contents in the list of blocks the span lies in (null for none): a command that changes the structure of paragraphs leaves them alone.
    private HashSet<Paragraph>? TocParagraphsIn(Span span)
    {
        if (_tocs.RangesOf(span.Container) is not { } ranges)
        {
            return null;
        }

        var set = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var toc in ranges)
        {
            for (var i = toc.First; i <= toc.Last; i++)
            {
                if (span.Container[i] is Paragraph paragraph)
                {
                    set.Add(paragraph);
                }
            }
        }

        return set;
    }

    // A move of top-level blocks never separates the paragraphs of a table of contents and never lands inside one: the range holds a whole field or none of it, and the place it lands in is not between two of its paragraphs.
    private bool TocRefusesMove(int first, int count, int target)
    {
        LastFieldRefusal = null;
        var body = _document.Body.Blocks;
        if (_tocs.RangesOf(body) is not { } ranges)
        {
            return false;
        }

        var last = first + count - 1;
        foreach (var toc in ranges)
        {
            var overlaps = last >= toc.First && first <= toc.Last;
            var whole = first <= toc.First && last >= toc.Last;
            var landsInside = !overlaps && target > toc.First && target <= toc.Last;
            if ((overlaps && !whole) || landsInside)
            {
                LastFieldRefusal = new FieldRefusal(FieldRefusalKind.PartOfToc);
                return true;
            }
        }

        return false;
    }

    // A first Backspace or Delete next to a table of contents selects the whole field (the caller deletes the selection next), as it does for a field in one paragraph. Null when the caret is not next to one.
    private EditResult? SelectTocNextTo(TextPosition caret, StoryRef story, IList<Block> container, int index, int offset, int length, bool forward)
    {
        if (_tocs.RangesOf(container) is not { } ranges)
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
        if (span.IsCollapsed || _tocs.RangesOf(span.Container) is not { } ranges)
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
