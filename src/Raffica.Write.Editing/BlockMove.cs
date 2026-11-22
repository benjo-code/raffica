using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// A range of top-level blocks of a story that moved: <paramref name="Count"/> blocks from <paramref name="First"/> to the place that was <paramref name="Target"/> in the list
/// <em>before</em> the move. Legal when <c>Target &lt;= First</c> or <c>Target &gt;= First + Count</c> (a drop inside the range is no move). The blocks are the same objects after the
/// move (paragraph ids, bookmarks and comment anchors ride along), so the step stores indices only.
/// </summary>
public sealed record BlockMove(StoryRef Story, int First, int Count, int Target)
{
    /// <summary>Where the range starts after the move.</summary>
    internal int NewFirst => Target >= First + Count ? Target - Count : Target;

    /// <summary>The move that undoes this one (the range now starts at <see cref="NewFirst"/>).</summary>
    internal BlockMove Inverse => new(Story, NewFirst, Count, First > NewFirst ? First + Count : First);

    /// <summary>True when the move is legal for a list of <paramref name="length"/> blocks and changes the order.</summary>
    internal bool IsValidFor(int length) =>
        Count > 0 && First >= 0 && First + Count <= length && Target >= 0 && Target <= length && (Target < First || Target > First + Count);

    /// <summary>The first block and the number of blocks whose place changes (the range and everything it passes over).</summary>
    internal (int First, int Count) Span => (Math.Min(First, Target), Math.Max(First + Count, Target) - Math.Min(First, Target));

    /// <summary>Rotates the range in place (no block is copied) and returns the blocks of the span as they were before.</summary>
    internal Block[] ApplyTo(IList<Block> blocks)
    {
        var (spanFirst, spanCount) = Span;
        var old = new Block[spanCount];
        for (var i = 0; i < spanCount; i++)
        {
            old[i] = blocks[spanFirst + i];
        }

        var moved = old.AsSpan(First - spanFirst, Count).ToArray();
        if (blocks is List<Block> list)
        {
            list.RemoveRange(First, Count);
            list.InsertRange(NewFirst, moved);
        }
        else
        {
            for (var i = Count - 1; i >= 0; i--)
            {
                blocks.RemoveAt(First + i);
            }

            for (var i = 0; i < Count; i++)
            {
                blocks.Insert(NewFirst + i, moved[i]);
            }
        }

        return old;
    }

    /// <summary>The result of this move having just been applied to <paramref name="blocks"/>: one change over the span and the caret at the start of the first moved paragraph (a range without a paragraph: the paragraph nearest to where it went).</summary>
    internal EditResult ResultIn(IList<Block> blocks, Block[] old)
    {
        var (spanFirst, spanCount) = Span;
        var paragraph = blocks.Skip(NewFirst).Take(Count).OfType<Paragraph>().FirstOrDefault()
            ?? NearestParagraph(blocks, NewFirst, Count)
            ?? throw new InvalidOperationException("The body holds no paragraph.");
        return new EditResult(new TextPosition(Story, paragraph, 0), [new BlockChange(Story, blocks, spanFirst, spanCount, spanCount, old)], Move: this);
    }

    // The paragraph closest to the moved range [first, first + count): looking outward, the block before it first.
    private static Paragraph NearestParagraph(IList<Block> blocks, int first, int count)
    {
        for (var distance = 0; distance < blocks.Count; distance++)
        {
            if (first - 1 - distance is >= 0 and var before && blocks[before] is Paragraph beforeParagraph)
            {
                return beforeParagraph;
            }

            if (first + count + distance < blocks.Count && blocks[first + count + distance] is Paragraph afterParagraph)
            {
                return afterParagraph;
            }
        }

        throw new InvalidOperationException("The body holds no paragraph.");
    }
}
