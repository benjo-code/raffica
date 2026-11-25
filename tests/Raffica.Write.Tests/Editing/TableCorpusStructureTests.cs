using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Review focus 1 of milestone 4.5a: every row and column command, at the first, a middle and the last index, on every top-level table of the corpus (the Word-made tables have merged cells in both
/// directions, spans, autofit and percentage widths): the grid equals the cells, no text is lost (a delete loses the text of the cells it deletes and nothing else), an undo gives the table back, and the
/// saved file reads back with the same grid.
/// </summary>
public sealed class TableCorpusStructureTests
{
    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    /// <summary>The corpus files.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id);
        }

        return data;
    }

    private static string Shape(Table table)
    {
        var map = TableGridMap.Of(table);
        var rows = new List<string>();
        for (var r = 0; r < map.RowCount; r++)
        {
            rows.Add(string.Join(",", map.CellsOfRow(r).Select(c => $"{(c.IsContinuation ? "^" : string.Empty)}{c.Column}+{c.ColumnSpan}" + (c.RowSpan > 1 ? $"x{c.RowSpan}" : string.Empty))));
        }

        return map.Columns + ":" + string.Join("/", rows);
    }

    private static List<string> Texts(Table table) =>
        [.. TableGridMap.Of(table).Cells.SelectMany(c => c.Cell.Blocks.OfType<Paragraph>().Select(TestEdit.TextOf)).Where(t => t.Length > 0).Order(StringComparer.Ordinal)];

    // The multiset difference: the texts of <before> that <after> does not have, or null when <after> holds a text <before> did not.
    private static List<string>? Lost(List<string> before, List<string> after)
    {
        var rest = new List<string>(before);
        foreach (var text in after)
        {
            if (!rest.Remove(text))
            {
                return null;
            }
        }

        return rest;
    }

    private static SortedSet<int> Positions(int count) => new SortedSet<int> { 0, count / 2, count - 1 };

    private static void AssertValid(Table table, bool gridMatchedBefore, string label)
    {
        var map = TableGridMap.Of(table);
        Assert.True(map.IsRegular, label + ": the table is no longer regular");
        if (gridMatchedBefore)
        {
            Assert.True(table.GridColumns.Count == map.Columns, $"{label}: the grid has {table.GridColumns.Count} columns, the cells {map.Columns}");
        }

        foreach (var row in table.Rows.OfType<TableRow>())
        {
            Assert.True(row.Items.OfType<TableCell>().Any(), label + ": a row has no cell");
        }

        foreach (var cell in map.Cells)
        {
            Assert.True(cell.Cell.Blocks.Count > 0 && cell.Cell.Blocks[^1] is Paragraph, label + ": a cell has no closing paragraph");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EveryRowAndColumnCommandKeepsTheGridOnTheCorpusTables(string name)
    {
        using var file = DocxFile.Open(Path.Combine(Root, name, name + ".docx"));
        await RunMatrixAsync(file, name);
    }

    // Word-made tables have merges in both directions; the corpus has none with a vertical merge, so these are made by hand: spans, a vertical merge, both in one cell, a repeated header row and the three width modes.
    [Fact]
    public async Task EveryRowAndColumnCommandKeepsTheGridOnGeneratedTablesWithMergesInBothDirections()
    {
        using var folder = new TempFolder();
        var path = folder.File("merged.docx");
        DocxBuilder.WithBody(path, GeneratedBody());
        using var file = DocxFile.Open(path);
        var maps = file.Model.Body.Blocks.OfType<Table>().Select(TableGridMap.Of).ToList();
        Assert.Equal(4, maps.Count);
        Assert.True(maps.All(m => m.IsRegular));
        Assert.Contains(maps, m => m.Cells.Any(c => c.ColumnSpan > 1));
        Assert.Contains(maps, m => m.Cells.Any(c => c.IsContinuation));
        Assert.Contains(maps, m => m.Cells.Any(c => !c.IsContinuation && c.ColumnSpan > 1 && c.RowSpan > 1));
        var ran = await RunMatrixAsync(file, "generated");
        Assert.True(ran > 60, $"only {ran} commands ran");
    }

    private static string Tc(string text, string props = "") =>
        $"<w:tc>{(props.Length == 0 ? string.Empty : $"<w:tcPr>{props}</w:tcPr>")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Tr(string props, params string[] cells) => $"<w:tr>{(props.Length == 0 ? string.Empty : $"<w:trPr>{props}</w:trPr>")}{string.Concat(cells)}</w:tr>";

    internal static string GeneratedBody()
    {
        const string Restart = "<w:vMerge w:val=\"restart\"/>";
        const string Continue = "<w:vMerge/>";
        static string Table(string properties, int[] grid, params string[] rows) =>
            $"<w:tbl><w:tblPr>{properties}</w:tblPr><w:tblGrid>{string.Concat(grid.Select(w => $"<w:gridCol w:w=\"{w}\"/>"))}</w:tblGrid>{string.Concat(rows)}</w:tbl>";
        static string Dxa(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"dxa\"/>";
        static string Pct(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"pct\"/>";
        const string Span2 = "<w:gridSpan w:val=\"2\"/>";

        // Four columns: a header row, a span of two, a vertical merge down two rows, and a cell that is both (a span of two over two rows).
        var merged = Table(
            "<w:tblW w:w=\"0\" w:type=\"auto\"/>",
            [2000, 2000, 2000, 2000],
            Tr("<w:tblHeader/>", Tc("h1", Dxa(2000)), Tc("h2", Dxa(2000)), Tc("h3", Dxa(2000)), Tc("h4", Dxa(2000))),
            Tr(string.Empty, Tc("a", Dxa(4000) + Span2), Tc("b", Dxa(2000)), Tc("c", Dxa(2000))),
            Tr(string.Empty, Tc("d", Dxa(2000) + Restart), Tc("e", Dxa(2000)), Tc("f", Dxa(4000) + Span2 + Restart)),
            Tr(string.Empty, Tc(string.Empty, Dxa(2000) + Continue), Tc("g", Dxa(2000)), Tc(string.Empty, Dxa(4000) + Span2 + Continue)),
            Tr(string.Empty, Tc("i", Dxa(2000)), Tc("j", Dxa(2000)), Tc("k", Dxa(2000)), Tc("l", Dxa(2000))));
        var autofit = Table(
            "<w:tblW w:w=\"0\" w:type=\"auto\"/>",
            [3000, 3000, 3000],
            Tr(string.Empty, Tc("p", Pct(1500)), Tc("q", Pct(1500)), Tc("r", Pct(2000))),
            Tr(string.Empty, Tc("s", Pct(1500) + Restart), Tc("t", Pct(1500)), Tc("u", Pct(2000))),
            Tr(string.Empty, Tc(string.Empty, Pct(1500) + Continue), Tc("v", Pct(1500)), Tc("w", Pct(2000))));
        var percent = Table(
            "<w:tblW w:w=\"5000\" w:type=\"pct\"/>",
            [4000, 4000],
            Tr(string.Empty, Tc("x", Pct(2500)), Tc("y", Pct(2500))),
            Tr(string.Empty, Tc("z", Pct(5000) + Span2)));
        var fixedWidth = Table(
            "<w:tblW w:w=\"6000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>",
            [3000, 3000],
            Tr(string.Empty, Tc("m", Dxa(3000)), Tc("n", Dxa(3000))),
            Tr(string.Empty, Tc("o", Dxa(3000)), Tc("p2", Dxa(3000))));
        static string P(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";
        return P("before") + merged + P("between") + autofit + P("between") + percent + P("between") + fixedWidth + P("after");
    }

    private static async Task<int> RunMatrixAsync(DocxFile file, string name)
    {
        using var folder = new TempFolder();
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        var ran = 0;
        var refused = 0;
        var tables = 0;
        var cancel = TestContext.Current.CancellationToken;

        for (var block = 0; block < model.Body.Blocks.Count; block++)
        {
            var reference = new TableRef(StoryRef.Body, block);
            if (reference.Find(model.Body.Blocks) is not { } original)
            {
                continue;
            }

            var map = TableGridMap.Of(original);
            if (!map.IsRegular)
            {
                Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForAddRow(reference, (map.RowIndexes.Count > 0 ? map.RowIndexes[0] : 0), after: true));
                refused++;
                continue;
            }

            tables++;
            var gridMatched = original.GridColumns.Count == map.Columns;
            var shape = Shape(original);
            var texts = Texts(original);
            var rows = map.RowIndexes;
            var columns = Enumerable.Range(0, map.Columns).ToList();

            var cases = new List<(string Label, Func<EditResult?> Run, Func<Table, TableGridMap, List<string>> Expected, bool RoundTrip)>();
            var middle = rows.Count / 2;
            foreach (var at in Positions(rows.Count))
            {
                var row = rows[at];
                cases.Add(($"add row after {row}", () => editor.AddRow(reference, row, after: true), static (_, _) => [], at == middle));
                cases.Add(($"add row before {row}", () => editor.AddRow(reference, row, after: false), static (_, _) => [], at == middle));
                cases.Add(($"delete row {row}", () => editor.DeleteRows(new CellRange(reference, row, 0, row, 0)), (_, m) => RowTexts(m, at), at == middle));
                if (rows.Count > 1)
                {
                    cases.Add(($"move row {row} down", () => editor.MoveRow(reference, row, 1), static (_, _) => [], at == middle));
                    cases.Add(($"move row {row} up", () => editor.MoveRow(reference, row, -1), static (_, _) => [], at == middle));
                }
            }

            middle = columns.Count / 2;
            foreach (var at in Positions(columns.Count))
            {
                cases.Add(($"add column after {at}", () => editor.AddColumn(reference, at, after: true), static (_, _) => [], at == middle));
                cases.Add(($"add column before {at}", () => editor.AddColumn(reference, at, after: false), static (_, _) => [], at == middle));
                cases.Add(($"delete column {at}", () => editor.DeleteColumns(new CellRange(reference, 0, at, 0, at)), (_, m) => ColumnTexts(m, at), at == middle));
                var lastRow = rows[Math.Min(1, rows.Count - 1)];
                cases.Add(($"merge from {at}", () => editor.MergeCells(new CellRange(reference, rows[0], at, lastRow, Math.Min(at + 1, columns.Count - 1))), static (_, _) => [], at == middle));
                cases.Add(($"split at {at}", () => editor.SplitCell(reference, rows[0], at), static (_, _) => [], at == middle));
            }

            foreach (var (label, run, expected, roundTrip) in cases)
            {
                var caseLabel = $"{name} table {block} {label}";
                var removed = expected(original, map);
                var result = run();
                if (result is null)
                {
                    refused++;
                    Assert.Equal(shape, Shape(reference.Find(model.Body.Blocks)!));
                    continue;
                }

                ran++;
                var after = reference.Find(model.Body.Blocks);
                if (after is null)
                {
                    // Deleting every row or column of a one-cell table deleted it; undo brings it back.
                    Assert.NotNull(editor.History.Undo());
                    Assert.Equal(shape, Shape(reference.Find(model.Body.Blocks)!));
                    continue;
                }

                AssertValid(after, gridMatched, caseLabel);
                var lost = Lost(texts, Texts(after));
                Assert.True(lost is not null, caseLabel + ": a text appeared that the table did not have");
                var expectedLost = new List<string>(removed.Where(t => t.Length > 0).Order(StringComparer.Ordinal));
                Assert.True(lost.Order(StringComparer.Ordinal).SequenceEqual(expectedLost), $"{caseLabel}: lost [{string.Join("|", lost)}], expected [{string.Join("|", expectedLost)}]");

                // The saved file reads back with the same grid (at the middle index, to keep the time of the run: a save writes the whole file).
                if (roundTrip)
                {
                    var saved = folder.File("saved.docx");
                    await file.SaveAsync(saved, cancel);
                    using (var reopened = DocxFile.Open(saved))
                    {
                        var again = new TableRef(StoryRef.Body, block).Find(reopened.Model.Body.Blocks);
                        Assert.True(again is not null, caseLabel + ": the table is not in the saved file");
                        Assert.Equal(Shape(after), Shape(again));
                        Assert.Equal(Texts(after), Texts(again));
                    }

                    File.Delete(saved);
                }

                Assert.NotNull(editor.History.Undo());
                Assert.True(shape == Shape(reference.Find(model.Body.Blocks)!), caseLabel + ": the undo did not restore the grid");
                Assert.Equal(texts, Texts(reference.Find(model.Body.Blocks)!));
            }
        }

        TestContext.Current.SendDiagnosticMessage($"{name}: {tables} tables, {ran} commands ran, {refused} refused");
        return ran;
    }

    // The corpus must hold what review focus 1 names, or the test above proves less than it says.
    [Fact]
    public void TheCorpusHasTablesWithSpansAndTheGeneratedOnesCoverTheMerges()
    {
        int tables = 0, spans = 0, merges = 0, autofit = 0, percent = 0, irregular = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            for (var i = 0; i < file.Model.Body.Blocks.Count; i++)
            {
                if (new TableRef(StoryRef.Body, i).Find(file.Model.Body.Blocks) is not { } table)
                {
                    continue;
                }

                var map = TableGridMap.Of(table);
                tables++;
                irregular += map.IsRegular ? 0 : 1;
                spans += map.Cells.Any(c => c.ColumnSpan > 1) ? 1 : 0;
                merges += map.Cells.Any(c => c.IsContinuation) ? 1 : 0;
                var width = table.Properties.Get(System.Xml.Linq.XName.Get("tblW", LayoutModel.W));
                autofit += table.Properties.Get(System.Xml.Linq.XName.Get("tblLayout", LayoutModel.W)) is null ? 1 : 0;
                percent += (string?)width?.Attribute(System.Xml.Linq.XName.Get("type", LayoutModel.W)) == "pct" ? 1 : 0;
            }
        }

        TestContext.Current.SendDiagnosticMessage($"corpus tables: {tables}, with spans {spans}, with vertical merges {merges}, autofit {autofit}, percentage width {percent}, not regular {irregular}");
        Assert.True(tables >= 10, $"only {tables} top-level tables in the corpus");
        Assert.True(spans > 0, "no table with a grid span");
        Assert.True(irregular < tables, "every table is irregular");
    }

    private static List<string> RowTexts(TableGridMap map, int ordinal) =>
        [.. map.CellsOfRow(ordinal).SelectMany(c => c.Cell.Blocks.OfType<Paragraph>().Select(TestEdit.TextOf))];

    // The texts of the cells that span only the deleted column (they go with it).
    private static List<string> ColumnTexts(TableGridMap map, int column) =>
        [.. map.Cells.Where(c => c.Column == column && c.ColumnSpan == 1).SelectMany(c => c.Cell.Blocks.OfType<Paragraph>().Select(TestEdit.TextOf))];
}
