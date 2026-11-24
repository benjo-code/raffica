namespace Raffica.Write.Model;

/// <summary>
/// The identity of one drawing (a picture) in a story, as three ordinals: <paramref name="Paragraph"/> is the paragraph's position in the
/// story's paragraphs in document order (a table's cells row by row and a content control's blocks included, to a depth of 32; the first
/// paragraph is 0), <paramref name="Run"/> the position of the run in that paragraph among all its runs in document order (the runs inside
/// hyperlinks, fields, content controls and tracked changes count, in the order they are met) and <paramref name="Content"/> the position of
/// the drawing in the run's content. Like a <see cref="TextPosition"/> it names a place and not an object: any edit can move it, so a
/// holder re-reads it (<see cref="ObjectIds.Find"/> resolves it again) after every edit.
/// </summary>
public sealed record ObjectId(StoryRef Story, int Paragraph, int Run, int Content);

/// <summary>Makes and resolves <see cref="ObjectId"/>s: the one definition of the ordinals.</summary>
public static class ObjectIds
{
    private const int MaximumDepth = 32;

    /// <summary>The identity of <paramref name="content"/> in <paramref name="run"/> of <paramref name="paragraph"/>, or null when any of them is not in <paramref name="blocks"/>.</summary>
    public static ObjectId? Of(StoryRef story, IList<Block> blocks, Paragraph paragraph, Run run, RunContent content)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);
        var index = 0;
        foreach (var candidate in Paragraphs(blocks, 0))
        {
            if (ReferenceEquals(candidate, paragraph))
            {
                var ordinal = 0;
                return RunOrdinal(paragraph.Inlines, run, ref ordinal) && ContentIndex(run, content) is var at and >= 0 ? new ObjectId(story, index, ordinal, at) : null;
            }

            index++;
        }

        return null;
    }

    /// <summary>The paragraph, run and drawing an identity names, or null when the story has no such place (or the place holds no <see cref="Picture"/>).</summary>
    public static (Paragraph Paragraph, Run Run, Picture Picture)? Find(IList<Block> blocks, ObjectId id)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(id);
        var index = 0;
        foreach (var paragraph in Paragraphs(blocks, 0))
        {
            if (index++ != id.Paragraph)
            {
                continue;
            }

            var ordinal = 0;
            var run = RunAt(paragraph.Inlines, id.Run, ref ordinal);
            return run is not null && id.Content >= 0 && id.Content < run.Content.Count && run.Content[id.Content] is Picture picture ? (paragraph, run, picture) : null;
        }

        return null;
    }

    private static IEnumerable<Paragraph> Paragraphs(IList<Block> blocks, int depth)
    {
        if (depth > MaximumDepth)
        {
            yield break;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case ContentControlBlock control:
                    foreach (var inner in Paragraphs(control.Content, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var inner in Paragraphs(row, depth + 1))
                        {
                            yield return inner;
                        }
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static IEnumerable<Paragraph> Paragraphs(RowItem item, int depth)
    {
        if (depth > MaximumDepth)
        {
            yield break;
        }

        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    foreach (var inner in Paragraphs(child, depth + 1))
                    {
                        yield return inner;
                    }
                }

                break;
            case TableCell cell:
                foreach (var inner in Paragraphs(cell.Blocks, depth + 1))
                {
                    yield return inner;
                }

                break;
            default:
                break;
        }
    }

    // Counts the runs in document order until the one asked for; true when it was met (ordinal then is its position).
    private static bool RunOrdinal(IList<Inline> inlines, Run wanted, ref int ordinal)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                if (ReferenceEquals(run, wanted))
                {
                    return true;
                }

                ordinal++;
            }
            else if (Children(inline) is { } children && RunOrdinal(children, wanted, ref ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Run? RunAt(IList<Inline> inlines, int wanted, ref int ordinal)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                if (ordinal++ == wanted)
                {
                    return run;
                }
            }
            else if (Children(inline) is { } children && RunAt(children, wanted, ref ordinal) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static IList<Inline>? Children(Inline inline) => inline switch
    {
        Hyperlink link => link.Inlines,
        SimpleField field => field.Inlines,
        ContentControlInline control => control.Inlines,
        TrackedChange change => change.Inlines,
        _ => null,
    };

    private static int ContentIndex(Run run, RunContent content)
    {
        for (var i = 0; i < run.Content.Count; i++)
        {
            if (ReferenceEquals(run.Content[i], content))
            {
                return i;
            }
        }

        return -1;
    }
}
