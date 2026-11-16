using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// The effective character formatting of a run, read through the layout's own <see cref="StyleResolver"/> so the editor and the page agree.
/// The resolver snapshots the styles when it is made: make a new instance after a style change. A paragraph in a table cell is resolved
/// with the cell's format as the layout does it; the enclosing cell is found by walking the story on every call (nothing is cached).
/// </summary>
public sealed class LayoutEffectiveFormat(WordDocument document) : IEffectiveFormat
{
    private readonly WordDocument _document = document ?? throw new ArgumentNullException(nameof(document));
    private readonly StyleResolver _resolver = new(document.Styles, document.Settings, document.Numbering);

    public EffectiveRun ForRun(StoryRef story, Paragraph paragraph, Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var resolved = Resolve(story, paragraph);
        return Map(_resolver.ForRun(resolved, run));
    }

    public EffectiveRun ForMark(StoryRef story, Paragraph paragraph)
    {
        var resolved = Resolve(story, paragraph);
        return Map(_resolver.ForParagraphMark(resolved));
    }

    private ResolvedParagraph Resolve(StoryRef story, Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var blocks = BlocksOf(story);
        ResolvedCellFormat? cell = null;
        if (blocks is not null)
        {
            Find(blocks, paragraph, ref cell);
        }

        return _resolver.ForParagraph(paragraph, cell);
    }

    private IList<Block>? BlocksOf(StoryRef story) => story.Kind switch
    {
        StoryKind.Body => _document.Body.Blocks,
        StoryKind.HeaderFooter => story.Key is not null && _document.HeadersFooters.TryGetValue(story.Key, out var part) ? part.Content.Blocks : null,
        StoryKind.Note when story.Key is not null && story.Key.StartsWith("endnote:", StringComparison.Ordinal) =>
            int.TryParse(story.Key.AsSpan("endnote:".Length), out var endId) ? _document.Endnotes?.Find(endId)?.Content.Blocks : null,
        StoryKind.Note => int.TryParse(story.Key, out var noteId) ? _document.Footnotes?.Find(noteId)?.Content.Blocks : null,
        StoryKind.Comment => int.TryParse(story.Key, out var commentId) ? _document.Comments?.Find(commentId)?.Content.Blocks : null,
        _ => null,
    };

    // Walks tables and content controls; sets the format of the innermost cell around the paragraph.
    private bool Find(IList<Block> blocks, Paragraph paragraph, ref ResolvedCellFormat? cell)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p when ReferenceEquals(p, paragraph):
                    return true;
                case ContentControlBlock control when Find(control.Content, paragraph, ref cell):
                    return true;
                case Table table when FindInTable(table, paragraph, ref cell):
                    return true;
            }
        }

        return false;
    }

    private bool FindInTable(Table table, Paragraph paragraph, ref ResolvedCellFormat? cell)
    {
        var rows = new List<TableRow>();
        foreach (var item in table.Rows)
        {
            if (item is TableRow row && row.Properties.Get(Ooxml.W + "del") is null)
            {
                rows.Add(row);
            }
        }

        // The grid width as the layout reads it: the grid, widened to the widest row.
        var columns = table.GridColumns.Count;
        var geometry = new List<(int Start, TableCell? Cell)[]>(rows.Count);
        foreach (var row in rows)
        {
            var at = Math.Min(Math.Max(TableReader.Value(row.Properties.Get(Ooxml.W + "gridBefore")) ?? 0, 0), TableLimits.MaximumColumns - 1);
            var cells = new List<(int, TableCell?)>();
            foreach (var part in row.Items)
            {
                if (part is TableCell cell1)
                {
                    var span = Math.Clamp(TableReader.Value(cell1.Properties.Get(Ooxml.W + "gridSpan")) ?? 1, 1, TableLimits.MaximumColumns);
                    var start = Math.Min(at, TableLimits.MaximumColumns - 1);
                    span = Math.Min(span, TableLimits.MaximumColumns - start);
                    cells.Add((start, cell1));
                    at = start + span;
                    columns = Math.Max(columns, at);
                }
                else if (part is UnknownRowItem unknown && TableReader.Takes(unknown))
                {
                    cells.Add((Math.Min(at, TableLimits.MaximumColumns - 1), null));
                    at = Math.Min(at, TableLimits.MaximumColumns - 1) + 1;
                    columns = Math.Max(columns, at);
                }
            }

            geometry.Add([.. cells]);
        }

        columns = Math.Max(columns, 1);
        for (var r = 0; r < rows.Count; r++)
        {
            foreach (var (start, tableCell) in geometry[r])
            {
                if (tableCell is null || !Find(tableCell.Blocks, paragraph, ref cell))
                {
                    continue;
                }

                if (cell is null)
                {
                    cell = _resolver.ForTable(table).CellFormat(r, start, rows.Count, columns, tableCell);
                }

                return true;
            }
        }

        return false;
    }

    private static EffectiveRun Map(ResolvedRun run) => new(
        run.Bold,
        run.Italic,
        run.Underline != UnderlineKind.None,
        run.Strike,
        run.SmallCaps,
        run.Caps,
        run.VerticalAlignment switch
        {
            RunVerticalAlignment.Superscript => VerticalPosition.Superscript,
            RunVerticalAlignment.Subscript => VerticalPosition.Subscript,
            _ => VerticalPosition.Baseline,
        });
}
