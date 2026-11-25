using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Editing;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The width the editor writes is what the layout reads back (milestone 4.5a, the rule that the first width write converts a table to fixed layout): a table of every kind in the corpus (autofit, fixed, percentage,
/// merged) gets column widths, a table width and a distribution through the editor, the document is laid out, and the column borders the layout publishes (the result of <c>GridSolver</c>) are the widths of the
/// grid to within a hundredth of a point.
/// </summary>
public sealed class TableWidthRoundTripTests
{
    private static readonly XNamespace W = LayoutModel.W;

    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    // A narrow table fits any text area the corpus has (a section with several columns included), so the solver never shrinks it to the room.
    private const int Narrow = 1500;

    private const int MostColumns = 15;

    /// <summary>The kinds of width a table had before the editor wrote it.</summary>
    private sealed class Kinds
    {
        public int Autofit { get; set; }

        public int Fixed { get; set; }

        public int Percentage { get; set; }

        public int Merged { get; set; }

        public int Checked { get; set; }

        public int Skipped { get; set; }

        public void Add(Kinds other)
        {
            Autofit += other.Autofit;
            Fixed += other.Fixed;
            Percentage += other.Percentage;
            Merged += other.Merged;
            Checked += other.Checked;
            Skipped += other.Skipped;
        }
    }

    private static string? Type(XElement? element) => (string?)element?.Attribute(W + "type");

    private static void Classify(Table table, Kinds kinds)
    {
        var width = table.Properties.Get(W + "tblW");
        var layout = table.Properties.Get(W + "tblLayout");
        var map = TableGridMap.Of(table);
        var percentage = Type(width) == "pct" || map.Cells.Any(c => Type(c.Cell.Properties.Get(W + "tcW")) == "pct");
        if (percentage)
        {
            kinds.Percentage++;
        }
        else if (Type(layout) == "fixed")
        {
            kinds.Fixed++;
        }
        else
        {
            kinds.Autofit++;
        }

        if (map.Cells.Any(c => c.ColumnSpan > 1 || c.RowSpan > 1))
        {
            kinds.Merged++;
        }
    }

    // The tables the editor can write: top-level, regular and consistent, with few columns. A reference, never the table: every operation replaces the table by a changed copy.
    private static List<TableRef> Candidates(WordDocument model, Kinds kinds)
    {
        var found = new List<TableRef>();
        for (var i = 0; i < model.Body.Blocks.Count; i++)
        {
            var reference = new TableRef(StoryRef.Body, i);
            if (reference.Find(model.Body.Blocks) is not { } table)
            {
                continue;
            }

            var map = TableGridMap.Of(table);
            if (!map.IsRegular || !map.IsConsistent || map.Columns > MostColumns)
            {
                kinds.Skipped++;
                continue;
            }

            Classify(table, kinds);
            found.Add(reference);
        }

        return found;
    }

    private static Table Fresh(WordDocument model, TableRef reference) => reference.Find(model.Body.Blocks)!;

    private static async Task<int> AssertDrawnAsync(WordDocument model, IEnumerable<TableRef> references, string label)
    {
        var layout = await SectionOracleTests.LaidAsync(model);
        var compared = 0;
        foreach (var reference in references)
        {
            var table = Fresh(model, reference);
            TableGrid? grid = null;
            for (var p = 0; p < layout.LaidOutPages && grid is null; p++)
            {
                grid = layout.Page(p).Objects.Tables.FirstOrDefault(t => t.BlockIndex == reference.BlockIndex && t.ColumnBorders.Count == table.GridColumns.Count + 1);
            }

            if (grid is null)
            {
                // A table that the layout draws on no page of the laid-out part (an empty table) has nothing to read back.
                continue;
            }

            for (var c = 0; c < table.GridColumns.Count; c++)
            {
                var drawn = grid.ColumnBorders[c + 1] - grid.ColumnBorders[c];
                var written = table.GridColumns[c] / 20f;
                Assert.True(Math.Abs(drawn - written) < 0.02f, $"{label}: table {reference.BlockIndex} column {c} is {drawn:F3} pt in the layout and {written:F3} pt in the model (grid {string.Join(",", table.GridColumns)})");
            }

            compared++;
        }

        return compared;
    }

    private static async Task<Kinds> RunAsync(WordDocument model, string label)
    {
        var kinds = new Kinds();
        var tables = Candidates(model, kinds);
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        var random = new Random(label.Length * 31);

        // 1. Distinct column widths that add up to about the narrow width.
        foreach (var reference in tables)
        {
            var columns = Fresh(model, reference).GridColumns.Count;
            var widths = Enumerable.Range(0, columns).Select(i => (Narrow / columns) + ((i * 37) % 80)).ToList();
            Assert.NotNull(editor.SetColumnWidths(reference, widths));
        }

        kinds.Checked += await AssertDrawnAsync(model, tables, label + " widths");

        // 2. A table width: proportions kept, sum exact.
        foreach (var reference in tables)
        {
            var target = Math.Max(Fresh(model, reference).GridColumns.Count * 100, Narrow + random.Next(0, 400));
            Assert.NotNull(editor.SetTableWidth(reference, target));
            Assert.Equal(target, Fresh(model, reference).GridColumns.Sum());
        }

        kinds.Checked += await AssertDrawnAsync(model, tables, label + " table width");

        // 3. Distribute.
        foreach (var reference in tables)
        {
            Assert.NotNull(editor.DistributeColumns(reference));
        }

        kinds.Checked += await AssertDrawnAsync(model, tables, label + " distribute");
        return kinds;
    }

    [Fact]
    public async Task TheEditorWritesWhatGridSolverReadsBackForEveryTableKindInTheCorpus()
    {
        var total = new Kinds();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            total.Add(await RunAsync(file.Model, entry.Id));
        }

        // The generated tables: merges in both directions, the three width modes.
        using var folder = new TempFolder();
        var path = folder.File("tables.docx");
        DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
        using (var file = DocxFile.Open(path))
        {
            total.Add(await RunAsync(file.Model, "generated"));
        }

        TestContext.Current.SendDiagnosticMessage($"autofit {total.Autofit}, fixed {total.Fixed}, percentage {total.Percentage}, merged {total.Merged}; {total.Checked} table reads compared, {total.Skipped} tables skipped");
        Assert.True(total.Autofit > 0, "no autofit table was written");
        Assert.True(total.Fixed > 0, "no fixed table was written");
        Assert.True(total.Percentage > 0, "no percentage table was written");
        Assert.True(total.Merged > 0, "no merged table was written");
        Assert.True(total.Checked > 30, $"only {total.Checked} table reads were compared with the layout");
    }

    [Fact]
    public async Task AWidthFromTheEditorSurvivesASaveAndReopen()
    {
        using var folder = new TempFolder();
        var path = folder.File("tables.docx");
        DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
        using var file = DocxFile.Open(path);
        var tables = Candidates(file.Model, new Kinds());
        var editor = new TextEditor(file.Model, (_, _) => ProjectionHost.Plain);
        foreach (var reference in tables)
        {
            var columns = Fresh(file.Model, reference).GridColumns.Count;
            Assert.NotNull(editor.SetColumnWidths(reference, Enumerable.Range(0, columns).Select(i => 300 + (i * 50)).ToList()));
        }

        var saved = folder.File("saved.docx");
        await file.SaveAsync(saved, overwriteChangedOriginal: false, CancellationToken.None);
        using var reopened = DocxFile.Open(saved);
        foreach (var reference in tables)
        {
            var written = Fresh(file.Model, reference);
            var again = Fresh(reopened.Model, reference);
            Assert.Equal(written.GridColumns, again.GridColumns);
            Assert.Equal(written.Properties.Get(W + "tblW")!.ToString(), again.Properties.Get(W + "tblW")!.ToString());
        }

        await AssertDrawnAsync(reopened.Model, tables, "reopened");
    }
}
