using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Where a position goes when a field refresh rewrote the results in its paragraph. A result is atomic, so a caret is never inside one: a position inside a replaced result goes to the start of that result, a position before it stays and a
/// position after it moves by the change of its length (the caret stays behind the same character). Every other position is returned as it is.
/// </summary>
public static class FieldPositions
{
    /// <summary>
    /// The position that follows <paramref name="position"/> through <paramref name="content"/> (the <see cref="FieldRefreshResult.Content"/> of a refresh, whose <see cref="BlockChange.OldBlocks"/> are clones taken before the rewrite and whose live blocks hold
    /// the new results). Offsets are those of the plain projection of the paragraph (the stored results), the layout's own for a paragraph with no field the layout computes itself.
    /// </summary>
    public static TextPosition Follow(TextPosition position, IReadOnlyList<BlockChange> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (position.Story.Kind != StoryKind.Body)
        {
            return position;
        }

        foreach (var change in content)
        {
            if (change.Story.Kind != StoryKind.Body)
            {
                continue;
            }

            for (var i = 0; i < change.Added && i < change.OldBlocks.Count && change.First + i < change.Container.Count; i++)
            {
                var live = BookmarkOperations.ParagraphsOf([change.Container[change.First + i]]).ToList();
                var index = live.FindIndex(p => ReferenceEquals(p, position.Paragraph));
                if (index < 0)
                {
                    continue;
                }

                var old = BookmarkOperations.ParagraphsOf([change.OldBlocks[i]]).ToList();
                return old.Count != live.Count ? position : new TextPosition(position.Story, position.Paragraph, Map(old[index], live[index], position.Offset));
            }
        }

        return position;
    }

    private static int Map(Paragraph before, Paragraph after, int offset)
    {
        var was = Results(before);
        var now = Results(after);
        if (was.Count != now.Count)
        {
            return offset;
        }

        var shift = 0;
        for (var i = 0; i < was.Count; i++)
        {
            if (offset <= was[i].Start)
            {
                break;
            }

            if (offset < was[i].End)
            {
                return now[i].Start;
            }

            shift += now[i].Length - was[i].Length;
        }

        return offset + shift;
    }

    private static List<AtomicPiece> Results(Paragraph paragraph) =>
        [.. ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Pieces.OfType<AtomicPiece>().Where(p => p.Atomic == AtomicKind.FieldResult)];
}
