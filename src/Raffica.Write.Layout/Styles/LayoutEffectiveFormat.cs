using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// The effective character formatting of a run, read through the layout's own <see cref="StyleResolver"/> so the editor and the page agree.
/// The resolver snapshots the styles when it is made: make a new instance after a style change. A paragraph in a table cell is resolved
/// with the cell's format as the layout does it. The enclosing cell of every paragraph that is in a table is found by one walk of the tables of that story
/// (<see cref="TableGeometry.Of"/> once per table; a paragraph outside every table is not stored, its absence is the answer: the walk of the thesis
/// stored every one of its thousands of paragraphs per revision, which was most of the 3.5 ms of a formatting operation and its garbage collections on a laptop) and remembered for as long as <see cref="DocumentEdits.Revision"/> stays the same:
/// every edit that restructures blocks (insert, delete, split, merge, table change, undo, redo) raises the revision and drops the index.
/// A formatting operation changes properties in place and never moves a block, so one operation reads one index. A paragraph the index
/// does not know is looked up once more with a fresh walk (once per revision, not when the index was made by this very call) before it counts as outside every table.
/// The instance is safe to call from several threads.
/// </summary>
public sealed class LayoutEffectiveFormat(WordDocument document) : IEffectiveFormat
{
    private readonly WordDocument _document = document ?? throw new ArgumentNullException(nameof(document));
    private readonly StyleResolver _resolver = new(document.Styles, document.Settings, document.Numbering);
    private readonly object _gate = new();
    private readonly Dictionary<StoryRef, Dictionary<Paragraph, Place>> _stories = [];
    private int _revision = int.MinValue;
    private bool _rewalked;

    /// <summary>How many story walks were made (for tests: the walks are one per story and revision, not one per call).</summary>
    internal int StoryWalks { get; private set; }

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

    /// <summary>The fully resolved formatting of a run, or of the paragraph mark when <paramref name="run"/> is null (what the page shows; the ribbon reads its font, size and colours).</summary>
    public ResolvedRun ResolveRun(StoryRef story, Paragraph paragraph, Run? run)
    {
        var resolved = Resolve(story, paragraph);
        return run is null ? _resolver.ForParagraphMark(resolved) : _resolver.ForRun(resolved, run);
    }

    /// <summary>The fully resolved formatting of a paragraph (styles, numbering and table style applied).</summary>
    public ResolvedParagraph ResolveParagraph(StoryRef story, Paragraph paragraph) => Resolve(story, paragraph);

    private ResolvedParagraph Resolve(StoryRef story, Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return _resolver.ForParagraph(paragraph, CellFormatOf(story, paragraph));
    }

    private ResolvedCellFormat? CellFormatOf(StoryRef story, Paragraph paragraph)
    {
        lock (_gate)
        {
            // A paragraph that is a block of the story itself has no cell, and that is nearly every paragraph: it is found by a plain search (no allocation) and never earns a walk. The walk of the
            // tables of the thesis allocated 600 KB each time, twice for every formatting operation (the revision is new after each), and the garbage collections of that were the heavy tail of the operation.
            if (IsTopLevel(story, paragraph))
            {
                return null;
            }

            var revision = _document.Edits.Revision;
            if (revision != _revision)
            {
                _stories.Clear();
                _revision = revision;
                _rewalked = false;
            }

            var made = false;
            if (!_stories.TryGetValue(story, out var index))
            {
                index = Walk(story);
                made = true;
            }

            if (!index.TryGetValue(paragraph, out var place) && !_rewalked && !made)
            {
                // A structure change that did not raise the revision (a hand-made model): walk once more per revision.
                _rewalked = true;
                index = Walk(story);
                index.TryGetValue(paragraph, out place);
            }

            return place.Table?.CellOf(place.Cell!)?.Format(_resolver);
        }
    }

    private bool IsTopLevel(StoryRef story, Paragraph paragraph)
    {
        if (BlocksOf(story) is not { } blocks)
        {
            return false;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], paragraph))
            {
                return true;
            }
        }

        return false;
    }

    private Dictionary<Paragraph, Place> Walk(StoryRef story)
    {
        StoryWalks++;
        var index = new Dictionary<Paragraph, Place>(ReferenceEqualityComparer.Instance);
        if (BlocksOf(story) is { } blocks)
        {
            Collect(blocks, default, index);
        }

        _stories[story] = index;
        return index;
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

    // Walks tables and content controls without reading any table geometry; a paragraph in a table is remembered with the table and the cell around it (the innermost), any other paragraph is
    // not remembered (it has no cell). The geometry of a table is read when a paragraph of that table is first asked for, so the format of a paragraph in one table never reads the others (the
    // geometry of all tables of the thesis allocated 600 KB for every walk, twice for every formatting operation).
    private static void Collect(IList<Block> blocks, Place cell, Dictionary<Paragraph, Place> index)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p when cell.Table is not null:
                    index.TryAdd(p, cell);
                    break;
                case ContentControlBlock control:
                    Collect(control.Content, cell, index);
                    break;
                case Table table:
                    CollectTable(table, index);
                    break;
            }
        }
    }

    private static void CollectTable(Table table, Dictionary<Paragraph, Place> index)
    {
        var entry = new TableEntry(table);
        foreach (var rowItem in table.Rows)
        {
            if (rowItem is not TableRow row)
            {
                continue;
            }

            foreach (var item in row.Items)
            {
                if (item is TableCell tableCell)
                {
                    Collect(tableCell.Blocks, new Place(entry, tableCell), index);
                }
            }
        }
    }

    // A paragraph's place: the table and the cell of it (no table: outside every table).
    private readonly record struct Place(TableEntry? Table, TableCell? Cell);

    // A table whose geometry is read on the first question about one of its cells (the rows, starts and column count come from the layout's own reader, so the conditional regions resolve as they
    // do on the page); a cell the geometry does not hold (a hidden or unknown one) has no cell format.
    private sealed class TableEntry(Table table)
    {
        private TableGeometry? _geometry;
        private Dictionary<TableCell, CellEntry?>? _cells;

        public CellEntry? CellOf(TableCell cell)
        {
            _cells ??= new Dictionary<TableCell, CellEntry?>(ReferenceEqualityComparer.Instance);
            if (_cells.TryGetValue(cell, out var known))
            {
                return known;
            }

            _geometry ??= TableGeometry.Of(table);
            CellEntry? found = null;
            for (var r = 0; r < _geometry.Rows.Count && found is null; r++)
            {
                var cells = _geometry.Rows[r].Cells;
                for (var c = 0; c < cells.Count; c++)
                {
                    if (ReferenceEquals(cells[c], cell))
                    {
                        found = new CellEntry(table, _geometry, r, c, cell);
                        break;
                    }
                }
            }

            _cells[cell] = found;
            return found;
        }
    }

    // The place of a cell; its format is resolved on the first paragraph that asks and then kept for the revision.
    private sealed class CellEntry(Table table, TableGeometry geometry, int row, int column, TableCell cell)
    {
        private ResolvedCellFormat? _format;

        public ResolvedCellFormat? Format(StyleResolver resolver)
        {
            return _format ??= resolver.ForTable(table).CellFormat(row, geometry.Starts[row][column], geometry.Rows.Count, geometry.Columns, cell);
        }
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
