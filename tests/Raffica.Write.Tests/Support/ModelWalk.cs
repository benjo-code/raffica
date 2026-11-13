using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>Walks a story's blocks, inlines and run content, including tables, content controls and tracked changes.</summary>
internal static class ModelWalk
{
    public static IEnumerable<Block> Blocks(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            switch (block)
            {
                case Table table:
                    foreach (var inner in RowBlocks(table.Rows))
                    {
                        yield return inner;
                    }

                    break;
                case ContentControlBlock control:
                    foreach (var inner in Blocks(control.Content))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    public static IEnumerable<Inline> Inlines(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            var children = inline switch
            {
                Hyperlink h => h.Inlines,
                SimpleField f => f.Inlines,
                TrackedChange t => t.Inlines,
                ContentControlInline c => c.Inlines,
                _ => null,
            };
            if (children is not null)
            {
                foreach (var inner in Inlines(children))
                {
                    yield return inner;
                }
            }
        }
    }

    public static IEnumerable<RunContent> RunContents(Story story) =>
        Blocks(story.Blocks).OfType<Paragraph>().SelectMany(p => Inlines(p.Inlines)).OfType<Run>().SelectMany(r => r.Content);

    private static IEnumerable<Block> RowBlocks(IEnumerable<RowItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case TableRow row:
                    foreach (var inner in RowBlocks(row.Items))
                    {
                        yield return inner;
                    }

                    break;
                case TableCell cell:
                    foreach (var inner in Blocks(cell.Blocks))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }
}
