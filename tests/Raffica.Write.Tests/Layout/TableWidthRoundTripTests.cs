using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Editing;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The width the editor writes is what the layout reads back (milestone 4.5a, the rule that the first width write converts a table to fixed layout and freezes the width the reader sees). Every table of every kind
/// in the corpus and in the generated documents (autofit, fixed, percentage, merged, and tables wider than the text area, which the layout shrinks) gets, as its FIRST write, a distribution, a table width or column
/// widths, with the widths the layout drew just before; the document is laid out again and the column borders the layout publishes (the result of <c>GridSolver</c>) are the written widths to within a hundredth of a
/// point. A table that is skipped is skipped for a named reason and every table is accounted for.
/// </summary>
public sealed class TableWidthRoundTripTests
{
    private static readonly XNamespace W = LayoutModel.W;

    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    private enum Write
    {
        Distribute,
        TableWidth,
        ColumnWidths,
    }

    /// <summary>What was run and what was left out, per document and summed.</summary>
    private sealed class Tally
    {
        public int Tables { get; set; }

        public int Checked { get; set; }

        // The reasons a table is left out. Every table is checked or has exactly one of these.
        public int Irregular { get; set; }

        public int Inconsistent { get; set; }

        public int ControlWithSeveralTables { get; set; }

        public int NotDrawn { get; set; }

        public int DrawnColumnCollapsed { get; set; }

        public int Autofit { get; set; }

        public int Fixed { get; set; }

        public int Percentage { get; set; }

        public int Merged { get; set; }

        /// <summary>Converted tables that the layout drew narrower than the model says (wider than the text area).</summary>
        public int ConvertedWhileShrunk { get; set; }

        /// <summary>A fixed table that was drawn shrunk and was distributed from its own width: the layout shrinks it again, nothing to compare.</summary>
        public int FixedShrunkDistribute { get; set; }

        public int[,] PerKindAndWrite { get; } = new int[3, 3];

        public int Skipped => Irregular + Inconsistent + ControlWithSeveralTables + NotDrawn + DrawnColumnCollapsed;

        public void Add(Tally other)
        {
            Tables += other.Tables;
            Checked += other.Checked;
            Irregular += other.Irregular;
            Inconsistent += other.Inconsistent;
            ControlWithSeveralTables += other.ControlWithSeveralTables;
            NotDrawn += other.NotDrawn;
            DrawnColumnCollapsed += other.DrawnColumnCollapsed;
            Autofit += other.Autofit;
            Fixed += other.Fixed;
            Percentage += other.Percentage;
            Merged += other.Merged;
            ConvertedWhileShrunk += other.ConvertedWhileShrunk;
            FixedShrunkDistribute += other.FixedShrunkDistribute;
            for (var k = 0; k < 3; k++)
            {
                for (var w = 0; w < 3; w++)
                {
                    PerKindAndWrite[k, w] += other.PerKindAndWrite[k, w];
                }
            }
        }
    }

    private static readonly string[] s_kindNames = ["autofit", "fixed", "percentage"];

    private static string? Type(XElement? element) => (string?)element?.Attribute(W + "type");

    private static int Int(XElement? element, string name) =>
        int.TryParse((string?)element?.Attribute(W + name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    // Fixed layout and a table width in twips: the widths are read from the model and a width write converts nothing.
    private static bool IsFixedInTwips(Table table) =>
        Type(table.Properties.Get(W + "tblLayout")) == "fixed" && Type(table.Properties.Get(W + "tblW")) is "dxa" or null && Int(table.Properties.Get(W + "tblW"), "w") > 0;

    // 0 autofit, 1 fixed, 2 percentage.
    private static int KindOf(Table table, TableGridMap map)
    {
        if (Type(table.Properties.Get(W + "tblW")) == "pct" || map.Cells.Any(c => Type(c.Cell.Properties.Get(W + "tcW")) == "pct"))
        {
            return 2;
        }

        return Type(table.Properties.Get(W + "tblLayout")) == "fixed" ? 1 : 0;
    }

    private static bool HoldsTable(Block block, int depth = 0) =>
        block is Table || (block is ContentControlBlock control && depth < 32 && control.Content.Any(inner => HoldsTable(inner, depth + 1)));

    private static int[] Weighted(int columns, int total)
    {
        var sum = columns * (columns + 1) / 2;
        var widths = Enumerable.Range(0, columns).Select(i => total * (i + 1) / sum).ToArray();
        widths[^1] += total - widths.Sum();
        return widths;
    }

    private static async Task<Tally> RunAsync(WordDocument model, string label, int rotation)
    {
        var tally = new Tally();
        var before = await SectionOracleTests.LaidAsync(model);
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        var done = new List<(TableRef Reference, Write Write, int Kind, bool Converted, bool Exempt, int[] Drawn, float[] DrawnPoints, int Requested)>();
        var index = 0;
        for (var i = 0; i < model.Body.Blocks.Count; i++)
        {
            var block = model.Body.Blocks[i];
            if (!HoldsTable(block))
            {
                continue;
            }

            tally.Tables++;
            var reference = new TableRef(StoryRef.Body, i);
            if (reference.Find(model.Body.Blocks) is not { } table)
            {
                tally.ControlWithSeveralTables++;
                continue;
            }

            var map = TableGridMap.Of(table);
            if (!map.IsRegular)
            {
                tally.Irregular++;
                continue;
            }

            if (!map.IsConsistent)
            {
                tally.Inconsistent++;
                continue;
            }

            var drawn = DrawnTableWidths.Of(before, reference, map.Columns);
            var points = DrawnTableWidths.Points(before, reference, map.Columns);
            if (drawn is null || points is null)
            {
                tally.NotDrawn++;
                continue;
            }

            if (drawn.Any(w => w <= 0))
            {
                tally.DrawnColumnCollapsed++;
                continue;
            }

            var kind = KindOf(table, map);
            var converted = !IsFixedInTwips(table);
            var write = (Write)((index++ + rotation) % 3);
            var tableWidth = table.Properties.Get(W + "tblW"); var preferred = Type(tableWidth) is "dxa" or null ? Int(tableWidth, "w") : 0; var modelTotal = preferred > 0 ? preferred : table.GridColumns.Sum();
            var drawnTotal = drawn.Sum();
            var shrunk = (modelTotal - drawnTotal) / 20f > 0.5f;
            var exempt = false;
            var requested = 0;
            EditResult? result;
            switch (write)
            {
                case Write.Distribute:
                    // A fixed table is distributed from its own width; when the layout shrank it, it shrinks it again.
                    exempt = !converted && shrunk;
                    result = editor.DistributeColumns(reference, drawn);
                    break;
                case Write.TableWidth:
                    requested = Math.Max(map.Columns * TableRules.MinColumnTwips, (converted ? drawnTotal : Math.Min(modelTotal, drawnTotal)) * 3 / 4);
                    result = editor.SetTableWidth(reference, requested, drawn);
                    break;
                default:
                    requested = Math.Max(map.Columns * 100, drawnTotal * 3 / 4);
                    result = editor.SetColumnWidths(reference, Weighted(map.Columns, requested));
                    break;
            }

            Assert.True(result is not null, $"{label}: table {i} ({write}) was refused: {editor.RefusalForDistributeColumns(reference, drawn)}");
            if (converted && shrunk)
            {
                tally.ConvertedWhileShrunk++;
            }

            if (exempt)
            {
                tally.FixedShrunkDistribute++;
            }

            done.Add((reference, write, kind, converted, exempt, drawn, points, requested));
            switch (kind)
            {
                case 0:
                    tally.Autofit++;
                    break;
                case 1:
                    tally.Fixed++;
                    break;
                default:
                    tally.Percentage++;
                    break;
            }

            if (map.Cells.Any(c => c.ColumnSpan > 1 || c.RowSpan > 1))
            {
                tally.Merged++;
            }
        }

        var after = await SectionOracleTests.LaidAsync(model);
        foreach (var (reference, write, kind, converted, exempt, drawn, drawnPoints, requested) in done)
        {
            var table = reference.Find(model.Body.Blocks)!;
            var columns = table.GridColumns.Count;
            var now = DrawnTableWidths.Points(after, reference, columns);
            Assert.True(now is not null, $"{label}: table {reference.BlockIndex} is drawn on no page after the write");
            TableGridChecks.AssertWidthsAgree(table, $"{label} table {reference.BlockIndex} {write}");

            // Frozen: what was drawn before is what is written (or, for a distribution, shared out equally; for a table width, scaled to the requested sum with the drawn proportions).
            if (converted && write == Write.Distribute)
            {
                Assert.Equal(drawn.Sum(), table.GridColumns.Sum());
                Assert.True(Math.Abs(now.Sum() - drawnPoints.Sum()) < (columns * 0.03f) + 0.1f, $"{label}: table {reference.BlockIndex} was {drawnPoints.Sum():F2} pt wide and is {now.Sum():F2} pt after its first write");
            }

            if (converted && write == Write.TableWidth)
            {
                Assert.Equal(requested, table.GridColumns.Sum());
                for (var c = 0; c < columns; c++)
                {
                    var expected = (double)requested * drawn[c] / drawn.Sum();
                    Assert.True(Math.Abs(table.GridColumns[c] - expected) <= 1.0 || table.GridColumns[c] == TableRules.MinColumnTwips, $"{label}: table {reference.BlockIndex} column {c} is {table.GridColumns[c]} twips, the drawn proportion asks for {expected:F1}");
                }
            }

            if (!exempt)
            {
                for (var c = 0; c < columns; c++)
                {
                    var written = table.GridColumns[c] / 20f;
                    Assert.True(Math.Abs(now[c] - written) < 0.02f, $"{label}: table {reference.BlockIndex} ({write}, kind {kind}, converted {converted}) column {c} is {now[c]:F3} pt in the layout and {written:F3} pt in the model (grid {string.Join(",", table.GridColumns)})");
                }

                tally.Checked++;
                tally.PerKindAndWrite[kind, (int)write]++;
            }
        }

        Assert.Equal(tally.Tables, tally.Checked + tally.FixedShrunkDistribute + tally.Skipped);
        return tally;
    }

    // Tables that are wider than the text area, which the layout shrinks to it: a fixed one, an autofit one, and a percentage one over a hundred percent, each between paragraphs; and a narrow one of each width mode.
    private static string WideBody()
    {
        static string Cell(string text, string width) => $"<w:tc><w:tcPr>{width}</w:tcPr><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";
        static string Row(params string[] cells) => $"<w:tr>{string.Concat(cells)}</w:tr>";
        static string Dxa(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"dxa\"/>";
        static string Pct(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"pct\"/>";
        static string Grid(params int[] w) => $"<w:tblGrid>{string.Concat(w.Select(x => $"<w:gridCol w:w=\"{x}\"/>"))}</w:tblGrid>";
        static string Table(string props, string grid, params string[] rows) => $"<w:tbl><w:tblPr>{props}</w:tblPr>{grid}{string.Concat(rows)}</w:tbl><w:p><w:r><w:t>between</w:t></w:r></w:p>";

        return "<w:p><w:r><w:t>start</w:t></w:r></w:p>"
            + Table("<w:tblW w:w=\"15000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>", Grid(5000, 5000, 5000), Row(Cell("a", Dxa(5000)), Cell("b", Dxa(5000)), Cell("c", Dxa(5000))), Row(Cell("d", Dxa(5000)), Cell("e", Dxa(5000)), Cell("f", Dxa(5000))))
            + Table("<w:tblW w:w=\"0\" w:type=\"auto\"/>", Grid(6000, 6000, 3000), Row(Cell("g", Dxa(6000)), Cell("h", Dxa(6000)), Cell("i", Dxa(3000))), Row(Cell("j", Dxa(6000)), Cell("k", Dxa(6000)), Cell("l", Dxa(3000))))
            + Table("<w:tblW w:w=\"7500\" w:type=\"pct\"/>", Grid(3000, 3000, 3000), Row(Cell("m", Pct(1500)), Cell("n", Pct(2500)), Cell("o", Pct(3500))), Row(Cell("p", Pct(1500)), Cell("q", Pct(2500)), Cell("r", Pct(3500))))
            + Table("<w:tblW w:w=\"0\" w:type=\"auto\"/>", Grid(1000, 3000), Row(Cell("s", Dxa(1000)), Cell("t", Dxa(2000))), Row(Cell("u", Dxa(1000)), Cell("v", Dxa(2000))))
            + Table("<w:tblW w:w=\"2500\" w:type=\"pct\"/>", Grid(2000, 2000), Row(Cell("w", Pct(1000)), Cell("x", Pct(1500))), Row(Cell("y", Pct(1000)), Cell("z", Pct(1500))))
            + Table("<w:tblW w:w=\"4000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>", Grid(1000, 3000), Row(Cell("1", Dxa(1000)), Cell("2", Dxa(3000))), Row(Cell("3", Dxa(1000)), Cell("4", Dxa(3000))));
    }

    [Fact]
    public async Task TheEditorWritesWhatGridSolverReadsBackForEveryTableKindInTheCorpus()
    {
        var total = new Tally();
        var rotation = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            total.Add(await RunAsync(file.Model, entry.Id, rotation++));
        }

        // The generated tables: merges in both directions, the three width modes; and the wide ones.
        using var folder = new TempFolder();
        foreach (var (name, body) in new[] { ("generated", TableCorpusStructureTests.GeneratedBody()), ("wide", WideBody()) })
        {
            for (var shift = 0; shift < 3; shift++)
            {
                var path = folder.File($"{name}{shift}.docx");
                DocxBuilder.WithBody(path, body);
                using var file = DocxFile.Open(path);
                total.Add(await RunAsync(file.Model, $"{name} {shift}", shift));
            }
        }

        var perKind = string.Join("; ", s_kindNames.Select((k, i) => $"{k}: distribute {total.PerKindAndWrite[i, 0]}, table width {total.PerKindAndWrite[i, 1]}, column widths {total.PerKindAndWrite[i, 2]}"));
        TestContext.Current.SendDiagnosticMessage($"{total.Tables} tables, {total.Checked} compared, skipped {total.Skipped} (irregular {total.Irregular}, inconsistent {total.Inconsistent}, control with several tables {total.ControlWithSeveralTables}, not drawn {total.NotDrawn}, a column drawn at no width {total.DrawnColumnCollapsed}), {total.FixedShrunkDistribute} fixed shrunk distributions left out, {total.ConvertedWhileShrunk} converted while shrunk, merged {total.Merged}; {perKind}");

        // Every table of every kind was written as a first write and compared; the floors are what the corpus and the generated tables give today and fall only when a table kind goes missing.
        foreach (var kind in new[] { 0, 2 })
        {
            Assert.True(total.PerKindAndWrite[kind, 0] >= 3, $"kind {kind}: only {total.PerKindAndWrite[kind, 0]} first distributions compared ({perKind})");
            Assert.True(total.PerKindAndWrite[kind, 1] >= 3, $"kind {kind}: only {total.PerKindAndWrite[kind, 1]} first table widths compared ({perKind})");
            Assert.True(total.PerKindAndWrite[kind, 2] >= 3, $"kind {kind}: only {total.PerKindAndWrite[kind, 2]} first column widths compared ({perKind})");
        }

        Assert.True(total.PerKindAndWrite[1, 0] + total.PerKindAndWrite[1, 1] + total.PerKindAndWrite[1, 2] >= 20, $"only a few fixed tables were compared ({perKind})");
        Assert.True(total.ConvertedWhileShrunk >= 6, $"only {total.ConvertedWhileShrunk} autofit or percentage tables wider than the text area were converted");
        Assert.True(total.Merged >= 8, $"only {total.Merged} merged tables were written");
        Assert.True(total.Checked >= 50, $"only {total.Checked} tables were compared with the layout ({perKind})");
        Assert.Equal(0, total.NotDrawn);
        Assert.Equal(total.Tables, total.Checked + total.FixedShrunkDistribute + total.Skipped);
    }

    [Fact]
    public async Task AWidthFromTheEditorSurvivesASaveAndReopen()
    {
        using var folder = new TempFolder();
        var path = folder.File("tables.docx");
        DocxBuilder.WithBody(path, WideBody());
        using var file = DocxFile.Open(path);
        var layout = await SectionOracleTests.LaidAsync(file.Model);
        var editor = new TextEditor(file.Model, (_, _) => ProjectionHost.Plain);
        var tables = Enumerable.Range(0, file.Model.Body.Blocks.Count).Where(i => file.Model.Body.Blocks[i] is Table).Select(i => new TableRef(StoryRef.Body, i)).ToList();
        // A fixed table is not converted and keeps its own width, which the layout may shrink; only the converted tables are compared with the layout.
        var fixedBefore = tables.Where(r => IsFixedInTwips(r.Find(file.Model.Body.Blocks)!)).ToHashSet();
        foreach (var reference in tables)
        {
            var columns = reference.Find(file.Model.Body.Blocks)!.GridColumns.Count;
            Assert.NotNull(editor.DistributeColumns(reference, DrawnTableWidths.Of(layout, reference, columns)));
        }

        var saved = folder.File("saved.docx");
        await file.SaveAsync(saved, overwriteChangedOriginal: false, CancellationToken.None);
        using var reopened = DocxFile.Open(saved);
        var again = await SectionOracleTests.LaidAsync(reopened.Model);
        foreach (var reference in tables)
        {
            var written = reference.Find(file.Model.Body.Blocks)!;
            var back = reference.Find(reopened.Model.Body.Blocks)!;
            Assert.Equal(written.GridColumns, back.GridColumns);
            TableGridChecks.AssertWidthsAgree(back, "reopened");
            if (fixedBefore.Contains(reference))
            {
                continue;
            }

            var points = DrawnTableWidths.Points(again, reference, back.GridColumns.Count)!;
            for (var c = 0; c < points.Length; c++)
            {
                Assert.True(Math.Abs(points[c] - (back.GridColumns[c] / 20f)) < 0.02f, $"reopened table {reference.BlockIndex} column {c}");
            }
        }
    }
}
