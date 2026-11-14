using System.Globalization;
using System.Text;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Layout;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class TableLayoutTests
{
    // Word 2013 and later: the border of the first cell sits at the indent, so column boundaries are easy to predict.
    private const string Compat15 = "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat>";
    private const float Left = 72f;
    private const float Margin = 5.4f;
    private const float Tolerance = 0.06f;

    private static DocumentLayout Lay(string body, string settings = Compat15, string? styles = null, LayoutOptions? options = null)
    {
        var document = LayoutModel.Document(body, styles);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"<w:settings xmlns:w=\"{LayoutModel.W}\">{settings}</w:settings>"));
        document.Settings = SettingsXml.Read(stream);
        return Finish(DocumentLayout.Create(document, Fonts, options ?? new LayoutOptions()));
    }

    private static string Cell(string text, string tcPr = "") =>
        $"<w:tc><w:tcPr>{tcPr}</w:tcPr><w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p></w:tc>";

    private static string Cells(string tcPr, params string[] paragraphs) =>
        $"<w:tc><w:tcPr>{tcPr}</w:tcPr>{string.Concat(paragraphs.Select(t => $"<w:p><w:r><w:t xml:space=\"preserve\">{t}</w:t></w:r></w:p>"))}</w:tc>";

    private static string Row(string cells, string trPr = "") => $"<w:tr><w:trPr>{trPr}</w:trPr>{cells}</w:tr>";

    private static string Grid(params int[] twips) =>
        "<w:tblGrid>" + string.Concat(twips.Select(w => string.Create(CultureInfo.InvariantCulture, $"<w:gridCol w:w=\"{w}\"/>"))) + "</w:tblGrid>";

    private static string Tbl(string tblPr, string grid, params string[] rows) =>
        $"<w:tbl><w:tblPr>{tblPr}</w:tblPr>{grid}{string.Concat(rows)}</w:tbl>";

    private static string Fixed => "<w:tblLayout w:type=\"fixed\"/>";

    private static string Shade(string rgb) => $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{rgb}\"/>";

    private static List<FillItem> Fills(LaidOutPage page) => page.Items.OfType<FillItem>().OrderBy(f => f.Y).ThenBy(f => f.X).ToList();

    private static List<RuleItem> RulesOf(LaidOutPage page) => page.Items.OfType<RuleItem>().ToList();

    private static float TextX(LaidOutPage page, int run) => Runs(page).OrderBy(r => r.Baseline).ThenBy(r => r.X).ElementAt(run).X;

    [Fact]
    public void FixedWidthColumns()
    {
        var layout = Lay(
            Tbl(Fixed, Grid(2000, 3000), Row(Cell("a", Shade("FF0000")) + Cell("b", Shade("00FF00")))) + Sect());
        var page = layout.Page(0);
        var fills = Fills(page);
        Assert.Equal(2, fills.Count);
        Assert.Equal(Left, fills[0].X, Tolerance);
        Assert.Equal(100f, fills[0].Width, Tolerance);
        Assert.Equal(Left + 100f, fills[1].X, Tolerance);
        Assert.Equal(150f, fills[1].Width, Tolerance);
        Assert.Equal(0xFFFF0000u, fills[0].Color);
        Assert.Equal(0xFF00FF00u, fills[1].Color);
        Assert.Equal(LineHeight(), fills[0].Height, Tolerance);

        // Text starts one cell margin inside each cell.
        Assert.Equal(Left + Margin, TextX(page, 0), Tolerance);
        Assert.Equal(Left + 100f + Margin, TextX(page, 1), Tolerance);
        Assert.Equal(0, layout.Locate(0)!.Value.Page);
    }

    [Fact]
    public void ABorderOfAnOlderDocumentSitsOneCellMarginLeftOfTheText()
    {
        var layout = Lay(Tbl(Fixed, Grid(2000), Row(Cell("a", Shade("FF0000")))) + Sect(), settings: string.Empty);
        var page = layout.Page(0);
        Assert.Equal(Left - Margin, Fills(page)[0].X, Tolerance);
        Assert.Equal(Left, TextX(page, 0), Tolerance);
    }

    [Fact]
    public void TableIndentAndAlignmentMoveTheTable()
    {
        var indented = Lay(Tbl(Fixed + "<w:tblInd w:w=\"400\" w:type=\"dxa\"/>", Grid(2000), Row(Cell("a", Shade("FF0000")))) + Sect());
        Assert.Equal(Left + 20f, Fills(indented.Page(0))[0].X, Tolerance);

        var centred = Lay(Tbl(Fixed + "<w:jc w:val=\"center\"/>", Grid(2000), Row(Cell("a", Shade("FF0000")))) + Sect());
        Assert.Equal(Left + ((468f - 100f) / 2f), Fills(centred.Page(0))[0].X, Tolerance);

        var right = Lay(Tbl(Fixed + "<w:jc w:val=\"right\"/>", Grid(2000), Row(Cell("a", Shade("FF0000")))) + Sect());
        Assert.Equal(Left + 468f - 100f, Fills(right.Page(0))[0].X, Tolerance);
    }

    [Fact]
    public void AutoFitFollowsContent()
    {
        const string Short = "a";
        const string Long = "a considerably longer piece of text in this cell";
        var plain = Lay(Tbl(string.Empty, Grid(0, 0), Row(Cell(Short, Shade("FF0000")) + Cell(Long, Shade("00FF00")))) + Sect());
        var fills = Fills(plain.Page(0));
        Assert.Equal(2, fills.Count);
        Assert.True(fills[1].Width > fills[0].Width * 3f);
        Assert.True(fills[0].Width + fills[1].Width < 468f);
        Assert.Equal(fills[0].X + fills[0].Width, fills[1].X, Tolerance);
        var short1 = fills[0].Width;

        // Far more text than the page holds: the table takes the page, the short column keeps what its content asks for.
        var text = string.Join(" ", Enumerable.Repeat("many words that need room", 20));
        var shrunk = Lay(Tbl(string.Empty, Grid(0, 0), Row(Cell(Short, Shade("FF0000")) + Cell(text, Shade("00FF00")))) + Sect());
        var f2 = Fills(shrunk.Page(0));
        Assert.Equal(short1, f2[0].Width, Tolerance);
        Assert.Equal(468f, f2[0].Width + f2[1].Width, Tolerance);
        Assert.True(shrunk.Page(0).Text.Split('\n').Length > 3);
    }

    [Fact]
    public void AGridThatIsGivenIsKeptByAnAutoFitTable()
    {
        var layout = Lay(Tbl(string.Empty, Grid(3000, 1000), Row(Cell("a", Shade("FF0000")) + Cell("b", Shade("00FF00")))) + Sect());
        var fills = Fills(layout.Page(0));
        Assert.Equal(150f, fills[0].Width, Tolerance);
        Assert.Equal(50f, fills[1].Width, Tolerance);
    }

    [Fact]
    public void PercentAndAutoPreferredWidths()
    {
        // The table is 100 % wide: a 40 % column and a column that has no width take the rest.
        var layout = Lay(
            Tbl(
                "<w:tblW w:w=\"5000\" w:type=\"pct\"/>",
                Grid(0, 0),
                Row(Cell("a", "<w:tcW w:w=\"2000\" w:type=\"pct\"/>" + Shade("FF0000")) + Cell("b", "<w:tcW w:w=\"0\" w:type=\"auto\"/>" + Shade("00FF00")))) + Sect());
        var fills = Fills(layout.Page(0));
        Assert.Equal(468f * 0.4f, fills[0].Width, Tolerance);
        Assert.Equal(468f * 0.6f, fills[1].Width, Tolerance);

        // A width in twips on the table and on one cell.
        var dxa = Lay(
            Tbl(
                "<w:tblW w:w=\"6000\" w:type=\"dxa\"/>",
                Grid(0, 0),
                Row(Cell("a", "<w:tcW w:w=\"1000\" w:type=\"dxa\"/>" + Shade("FF0000")) + Cell("b", Shade("00FF00")))) + Sect());
        var f2 = Fills(dxa.Page(0));
        Assert.Equal(50f, f2[0].Width, Tolerance);
        Assert.Equal(250f, f2[1].Width, Tolerance);

        // Percent of the table in a table of fixed width.
        var pct = Lay(
            Tbl(
                "<w:tblW w:w=\"4000\" w:type=\"dxa\"/>",
                Grid(0, 0),
                Row(Cell("a", "<w:tcW w:w=\"2500\" w:type=\"pct\"/>" + Shade("FF0000")) + Cell("b", "<w:tcW w:w=\"2500\" w:type=\"pct\"/>" + Shade("00FF00")))) + Sect());
        var f3 = Fills(pct.Page(0));
        Assert.Equal(f3[0].Width, f3[1].Width, Tolerance);
        Assert.Equal(200f, f3[0].Width + f3[1].Width, Tolerance);
    }

    [Fact]
    public void GridSpanAndVerticalMerge()
    {
        var lh = LineHeight();
        var layout = Lay(
            Tbl(
                Fixed,
                Grid(2000, 2000, 2000),
                Row(Cell("A", "<w:vMerge w:val=\"restart\"/>" + Shade("FF0000")) + Cell("B", "<w:gridSpan w:val=\"2\"/>" + Shade("00FF00"))),
                Row(Cell("hidden", "<w:vMerge/>") + Cell("C", Shade("0000FF")) + Cell("D", Shade("FFFF00")))) + Sect());
        var page = layout.Page(0);
        var fills = Fills(page);
        Assert.Equal(4, fills.Count);
        var merged = fills.Single(f => f.Color == 0xFFFF0000u);
        Assert.Equal(Left, merged.X, Tolerance);
        Assert.Equal(100f, merged.Width, Tolerance);
        Assert.Equal(2f * lh, merged.Height, Tolerance);
        var span = fills.Single(f => f.Color == 0xFF00FF00u);
        Assert.Equal(Left + 100f, span.X, Tolerance);
        Assert.Equal(200f, span.Width, Tolerance);
        var d = fills.Single(f => f.Color == 0xFFFFFF00u);
        Assert.Equal(Left + 200f, d.X, Tolerance);
        Assert.Equal(lh, d.Y - span.Y, Tolerance);

        // What a continuing cell holds is not shown; the merged cell's text is.
        Assert.Equal(["A", "B", "C", "D"], Lines(page));
    }

    [Fact]
    public void AMergedCellTallerThanItsRowsMakesTheLastRowGrow()
    {
        var lh = LineHeight();
        var layout = Lay(
            Tbl(
                Fixed,
                Grid(2000, 2000),
                Row(Cells("<w:vMerge w:val=\"restart\"/>" + Shade("FF0000"), "a1", "a2", "a3", "a4") + Cell("B", Shade("00FF00"))),
                Row(Cell(string.Empty, "<w:vMerge/>") + Cell("C", Shade("0000FF")))) + Sect());
        var page = layout.Page(0);
        var fills = Fills(page);
        Assert.Equal(4f * lh, fills.Single(f => f.Color == 0xFFFF0000u).Height, Tolerance);
        Assert.Equal(lh, fills.Single(f => f.Color == 0xFF00FF00u).Height, Tolerance);
        Assert.Equal(3f * lh, fills.Single(f => f.Color == 0xFF0000FFu).Height, Tolerance);
    }

    [Fact]
    public void CellMarginsAndRowHeightRules()
    {
        var lh = LineHeight();
        const string Mar = "<w:tblCellMar><w:top w:w=\"100\" w:type=\"dxa\"/><w:left w:w=\"200\" w:type=\"dxa\"/><w:bottom w:w=\"100\" w:type=\"dxa\"/><w:right w:w=\"200\" w:type=\"dxa\"/></w:tblCellMar>";
        var layout = Lay(
            Tbl(
                Fixed + Mar,
                Grid(2000),
                Row(Cell("auto", Shade("FF0000"))),
                Row(Cell("least", Shade("00FF00")), "<w:trHeight w:val=\"1000\" w:hRule=\"atLeast\"/>"),
                Row(Cell("small", Shade("0000FF")), "<w:trHeight w:val=\"200\" w:hRule=\"atLeast\"/>"),
                Row(Cell("exact", Shade("FFFF00")), "<w:trHeight w:val=\"400\" w:hRule=\"exact\"/>"),
                Row(Cell("own", Shade("00FFFF") + "<w:tcMar><w:left w:w=\"400\" w:type=\"dxa\"/></w:tcMar>"))) + Sect());
        var page = layout.Page(0);
        var fills = Fills(page);
        Assert.Equal(lh + 10f, fills[0].Height, Tolerance);
        Assert.Equal(50f, fills[1].Height, Tolerance);
        Assert.Equal(lh + 10f, fills[2].Height, Tolerance);
        Assert.Equal(20f, fills[3].Height, Tolerance);
        Assert.Equal(lh + 10f, fills[4].Height, Tolerance);
        Assert.Equal(fills[0].Y + fills[0].Height, fills[1].Y, Tolerance);

        // The text sits inside the margins; a cell's own margin wins over the table's.
        Assert.Equal(Left + 10f, TextX(page, 0), Tolerance);
        Assert.Equal(Left + 20f, TextX(page, 4), Tolerance);
    }

    [Fact]
    public void HeaderRowsRepeatOnEachPage()
    {
        var rows = new StringBuilder();
        rows.Append(Row(Cell("HEAD"), "<w:tblHeader/>"));
        for (var i = 1; i <= 12; i++)
        {
            rows.Append(Row(Cell("r" + i.ToString(CultureInfo.InvariantCulture))));
        }

        var layout = Lay(Tbl(Fixed, Grid(2000), rows.ToString()) + TallFor(5));
        var pages = Pages(layout);
        Assert.True(pages.Count >= 3);
        foreach (var page in pages)
        {
            Assert.Equal("HEAD", Lines(page)[0]);
        }

        var all = pages.SelectMany(p => Lines(p).Skip(1)).ToList();
        Assert.Equal(Enumerable.Range(1, 12).Select(i => "r" + i.ToString(CultureInfo.InvariantCulture)), all);
        Assert.Equal(pages.Count, pages.Sum(p => Lines(p).Count(l => l == "HEAD")));
    }

    [Fact]
    public void AHeaderRowTallerThanThePageDoesNotRepeatAndEndsTheLayout()
    {
        var tall = Cells(string.Empty, Enumerable.Range(1, 30).Select(i => "h" + i.ToString(CultureInfo.InvariantCulture)).ToArray());
        var layout = Lay(Tbl(Fixed, Grid(2000), Row(tall, "<w:tblHeader/>"), Row(Cell("body1")), Row(Cell("body2"))) + TallFor(5));
        var pages = Pages(layout);
        Assert.InRange(pages.Count, 2, 20);
        var text = string.Join("\n", pages.Select(p => p.Text));
        for (var i = 1; i <= 30; i++)
        {
            Assert.Contains("\nh" + i.ToString(CultureInfo.InvariantCulture) + "\n", "\n" + text + "\n", StringComparison.Ordinal);
        }

        Assert.Contains("body1", text, StringComparison.Ordinal);
        Assert.Contains("body2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RowsDoNotSplitWhenCantSplitIsSet()
    {
        var paragraphs = Enumerable.Range(1, 8).Select(i => "a" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
        string Build(string trPr) => Tbl(Fixed, Grid(2000), Row(Cell("x")), Row(Cells(string.Empty, paragraphs), trPr)) + TallFor(8);

        var splitting = Pages(Lay(Build(string.Empty)));
        Assert.Equal(2, splitting.Count);
        Assert.Equal(["x", "a1", "a2", "a3", "a4", "a5", "a6", "a7"], Lines(splitting[0]));
        Assert.Equal(["a8"], Lines(splitting[1]));

        var whole = Pages(Lay(Build("<w:cantSplit/>")));
        Assert.Equal(2, whole.Count);
        Assert.Equal(["x"], Lines(whole[0]));
        Assert.Equal(paragraphs, Lines(whole[1]));
    }

    [Fact]
    public void AnExactRowIsNeverCutAndAMergedGroupStaysTogether()
    {
        var exact = Pages(Lay(Tbl(Fixed, Grid(2000), Row(Cell("x")), Row(Cell("e"), "<w:trHeight w:val=\"2000\" w:hRule=\"exact\"/>")) + TallFor(3)));
        Assert.Equal(["x"], Lines(exact[0]));
        Assert.Equal(["e"], Lines(exact[1]));

        var merged = Pages(Lay(
            Tbl(
                Fixed,
                Grid(2000, 2000),
                Row(Cell("p1") + Cell("q1")),
                Row(Cell("p2") + Cell("q2")),
                Row(Cell("p3") + Cell("q3")),
                Row(Cell("p4") + Cell("m", "<w:vMerge w:val=\"restart\"/>")),
                Row(Cell("p5") + Cell(string.Empty, "<w:vMerge/>"))) + TallFor(4)));
        // Rows 4 and 5 share a merged cell: the page breaks before row 4 although row 4 alone would still fit.
        Assert.Equal(["p1", "q1", "p2", "q2", "p3", "q3"], Lines(merged[0]));
        Assert.Equal(["p4", "m", "p5"], Lines(merged[1]));
    }

    [Fact]
    public void BordersAndShadingResolve()
    {
        const string Borders = "<w:tblBorders><w:top w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/><w:left w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/>"
            + "<w:bottom w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/><w:right w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/>"
            + "<w:insideH w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/><w:insideV w:val=\"single\" w:sz=\"8\" w:color=\"FF0000\"/></w:tblBorders>";
        const string Thick = "<w:tcBorders><w:right w:val=\"single\" w:sz=\"24\" w:color=\"0000FF\"/></w:tcBorders>";
        var lh = LineHeight();
        var layout = Lay(
            Tbl(
                Fixed + Borders,
                Grid(2000, 2000),
                Row(Cell("a", Thick + Shade("FFFF00")) + Cell("b")),
                Row(Cell("c") + Cell("d"))) + Sect());
        var page = layout.Page(0);
        var rules = RulesOf(page);

        var vertical = rules.Where(r => r.Height > r.Width).ToList();
        var horizontal = rules.Where(r => r.Width > r.Height).ToList();

        // The border between the cells of row 1: the cell's 3 pt blue border is wider than the table's 1 pt red one, so it wins, once.
        var between1 = vertical.Where(r => MathF.Abs(r.X + (r.Width / 2f) - (Left + 100f)) < 0.2f && r.Y < 72f + lh).ToList();
        var thick = Assert.Single(between1);
        Assert.Equal(3f, thick.Width, Tolerance);
        Assert.Equal(0xFF0000FFu, thick.Color);
        Assert.Equal(72f, thick.Y, Tolerance);
        Assert.Equal(lh, thick.Height, Tolerance);

        // Row 2 has no cell border: the table's inside border is drawn there, also once.
        var between2 = vertical.Where(r => MathF.Abs(r.X + (r.Width / 2f) - (Left + 100f)) < 0.2f && r.Y >= 72f + lh - 0.1f).ToList();
        var thin = Assert.Single(between2);
        Assert.Equal(1f, thin.Width, Tolerance);
        Assert.Equal(0xFFFF0000u, thin.Color);

        // The outer edges: one rule each, as long as the table.
        var outerLeft = Assert.Single(vertical, r => MathF.Abs(r.X + (r.Width / 2f) - Left) < 0.2f);
        Assert.Equal(2f * lh, outerLeft.Height, Tolerance);
        Assert.Equal(3, horizontal.Count);
        Assert.All(horizontal, r => Assert.Equal(200f, r.Width, Tolerance));
        Assert.All(horizontal, r => Assert.Equal(1f, r.Height, Tolerance));
        Assert.Equal([72f - 0.5f, 72f + lh - 0.5f, 72f + (2f * lh) - 0.5f], horizontal.Select(r => r.Y).OrderBy(y => y).ToArray(), new FloatComparer());

        // Shading is drawn before the text and the rules after it.
        var fill = Assert.Single(page.Items.OfType<FillItem>());
        Assert.Equal(0xFFFFFF00u, fill.Color);
        var order = page.Items.Select(i => i.GetType()).ToList();
        Assert.True(order.IndexOf(typeof(FillItem)) < order.IndexOf(typeof(GlyphRunItem)));
        Assert.True(order.LastIndexOf(typeof(GlyphRunItem)) < order.IndexOf(typeof(RuleItem)));
    }

    [Fact]
    public void ABorderOfADoubleLineIsTwoRulesAndNoBorderIsNoRule()
    {
        var twice = Lay(Tbl("<w:tblBorders><w:top w:val=\"double\" w:sz=\"4\" w:color=\"auto\"/></w:tblBorders>" + Fixed, Grid(2000), Row(Cell("a"))) + Sect());
        Assert.Equal(2, RulesOf(twice.Page(0)).Count);

        var none = Lay(Tbl(Fixed, Grid(2000), Row(Cell("a"))) + Sect());
        Assert.Empty(RulesOf(none.Page(0)));
    }

    [Fact]
    public void NestedTable()
    {
        var inner = Tbl(Fixed, Grid(1000, 1000), Row(Cell("n1", Shade("FF0000")) + Cell("n2", Shade("00FF00"))));
        var outer = Tbl(
            Fixed,
            Grid(4000),
            Row($"<w:tc><w:tcPr/><w:p><w:r><w:t>outer</w:t></w:r></w:p>{inner}<w:p><w:r><w:t>after</w:t></w:r></w:p></w:tc>"));
        var lh = LineHeight();
        var layout = Lay(outer + Sect());
        var page = layout.Page(0);
        Assert.Equal(["outer", "n1", "n2", "after"], Lines(page));

        // The inner table starts at the text edge of the outer cell; its columns are 50 pt wide.
        var fills = Fills(page);
        Assert.Equal(Left + Margin, fills[0].X, Tolerance);
        Assert.Equal(50f, fills[0].Width, Tolerance);
        Assert.Equal(Left + Margin + 50f, fills[1].X, Tolerance);
        Assert.Equal(lh, fills[0].Y - 72f, Tolerance);
        Assert.Equal(Left + Margin + Margin, TextX(page, 1), Tolerance);
    }

    [Fact]
    public void ANestedTableIsCutAtItsRowsWithTheOuterOne()
    {
        var innerRows = string.Concat(Enumerable.Range(1, 10).Select(i => Row(Cell("n" + i.ToString(CultureInfo.InvariantCulture)))));
        var inner = Tbl(Fixed, Grid(1000), innerRows);
        var outer = Tbl(Fixed, Grid(4000), Row($"<w:tc><w:tcPr/>{inner}<w:p><w:r><w:t>end</w:t></w:r></w:p></w:tc>"));
        var pages = Pages(Lay(outer + TallFor(4)));
        Assert.True(pages.Count >= 3);
        var all = pages.SelectMany(Lines).ToList();
        Assert.Equal(Enumerable.Range(1, 10).Select(i => "n" + i.ToString(CultureInfo.InvariantCulture)).Append("end"), all);
    }

    [Fact]
    public void OversizedContentTerminates()
    {
        // A table wider than the page is scaled to the text width.
        var wide = Lay(Tbl(Fixed, Grid(8000, 8000, 8000), Row(Cell("a", Shade("FF0000")) + Cell("b", Shade("00FF00")) + Cell("c", Shade("0000FF")))) + Sect());
        var fills = Fills(wide.Page(0));
        Assert.Equal(468f, fills.Sum(f => f.Width), 0.2f);
        Assert.Equal(Left, fills[0].X, Tolerance);
        Assert.Equal(Left + 468f, fills[2].X + fills[2].Width, 0.2f);

        // A row taller than a page splits inside the row, at the line boundaries of its cells.
        var lines = Enumerable.Range(1, 30).Select(i => "t" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var other = Enumerable.Range(1, 12).Select(i => "u" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var tall = Lay(Tbl(Fixed, Grid(2000, 2000), Row(Cells(string.Empty, lines) + Cells(string.Empty, other))) + TallFor(5));
        var pages = Pages(tall);
        Assert.InRange(pages.Count, 6, 8);
        var text = pages.SelectMany(Lines).ToList();
        Assert.Equal(lines.Length + other.Length, text.Count);
        Assert.Equal(lines, text.Where(l => l[0] == 't'));
        Assert.Equal(other, text.Where(l => l[0] == 'u'));

        // An unbreakable word in a cell too narrow for it overflows; the layout does not loop on it.
        var narrow = Lay(Tbl(Fixed, Grid(200, 2000), Row(Cell("Unbreakable") + Cell("next"))) + Sect());
        Assert.Equal(["Unbreakable", "next"], Lines(narrow.Page(0)));
        Assert.Single(Pages(narrow));

        // The same in a table that fits itself to its content.
        var auto = Lay(Tbl(string.Empty, Grid(0, 0), Row(Cell(string.Join(string.Empty, Enumerable.Repeat("W", 400))) + Cell("next"))) + Sect());
        Assert.Contains("next", auto.Page(0).Text, StringComparison.Ordinal);
        Assert.InRange(auto.LaidOutPages, 1, 3);
    }

    [Fact]
    public void DegenerateTablesNeitherThrowNorLoopNorDropText()
    {
        const string Empty = "<w:tbl><w:tblPr/><w:tblGrid/></w:tbl>";
        const string NoCells = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:trPr/></w:tr></w:tbl>";
        var spanning = Tbl(string.Empty, Grid(2000), Row(Cell("span", "<w:gridSpan w:val=\"100\"/>") + Cell("extra")));
        var orphan = Tbl(Fixed, Grid(2000), Row(Cell("orphan", "<w:vMerge/>")));
        var absurd = Tbl("<w:tblW w:w=\"99999999999\" w:type=\"dxa\"/>" + "<w:tblInd w:w=\"-99999999\" w:type=\"dxa\"/>", Grid(2147483647, 0, 5), Row(Cell("absurd", "<w:tcW w:w=\"2147483647\" w:type=\"dxa\"/>") + Cell("y") + Cell("z")));
        var noParagraph = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/></w:tc></w:tr></w:tbl>";
        var layout = Lay(P("start") + Empty + NoCells + spanning + orphan + absurd + noParagraph + P("end") + Sect());
        var text = string.Join("\n", Pages(layout).Select(p => p.Text));
        foreach (var word in new[] { "start", "span", "extra", "orphan", "absurd", "y", "z", "end" })
        {
            Assert.Contains(word, text.Split('\n'));
        }

        Assert.True(layout.IsComplete);
        Assert.InRange(layout.LaidOutPages, 1, 3);
        Assert.All(Pages(layout), page => Assert.All(page.Items.OfType<FillItem>(), f => Assert.True(float.IsFinite(f.X) && float.IsFinite(f.Width))));
    }

    [Fact]
    public void TableAfterTableKeepsAnEmptyParagraph()
    {
        var lh = LineHeight();
        var layout = Lay(
            Tbl(Fixed, Grid(2000), Row(Cell("a"))) + "<w:p/>" + Tbl(Fixed, Grid(2000), Row(Cell("b"))) + Sect());
        var page = layout.Page(0);
        Assert.Equal(["a", string.Empty, "b"], Lines(page));
        var baselines = Baselines(page);
        Assert.Equal(2, baselines.Count);
        Assert.Equal(2f * lh, baselines[1] - baselines[0], Tolerance);
    }

    [Fact]
    public void CellTextComesRowByRowAndCellByCell()
    {
        var layout = Lay(
            P("before")
            + Tbl(Fixed, Grid(2000, 2000), Row(Cells(string.Empty, "r1c1a", "r1c1b") + Cell("r1c2")), Row(Cell("r2c1") + Cell("r2c2")))
            + P("after") + Sect());
        Assert.Equal(["before", "r1c1a", "r1c1b", "r1c2", "r2c1", "r2c2", "after"], Lines(layout.Page(0)));
    }

    [Fact]
    public void ARowWithATrackedDeletionIsHiddenAndAnInsertedRowShows()
    {
        var layout = Lay(
            Tbl(
                Fixed,
                Grid(2000),
                Row(Cell("keep")),
                Row(Cell("gone"), "<w:del w:id=\"1\" w:author=\"a\"/>"),
                Row(Cell("new"), "<w:ins w:id=\"2\" w:author=\"a\"/>")) + Sect());
        Assert.Equal(["keep", "new"], Lines(layout.Page(0)));
    }

    [Fact]
    public void TheTableStyleAppliesToCellParagraphs()
    {
        const string Styles = "<w:style w:type=\"table\" w:styleId=\"Big\"><w:name w:val=\"Big\"/><w:pPr><w:spacing w:before=\"200\" w:after=\"200\"/></w:pPr><w:tblPr><w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>";
        var lh = LineHeight();
        var layout = Lay(
            Tbl("<w:tblStyle w:val=\"Big\"/>" + Fixed, Grid(2000), Row(Cell("a", Shade("FF0000")))) + Sect(),
            styles: LayoutModel.StylesPart(Styles));
        var page = layout.Page(0);
        Assert.Equal(lh + 20f, Fills(page)[0].Height, Tolerance);
        Assert.Equal(Left, TextX(page, 0), Tolerance);
    }

    [Fact]
    public void AFootnoteInACellGetsItsRoom()
    {
        var table = Tbl(Fixed, Grid(2000), Row("<w:tc><w:tcPr/><w:p><w:r><w:t>cell</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p></w:tc>"));
        var document = ChromeTestKit.Document(
            table + Sect(),
            footnotes: ChromeTestKit.FootnotesPart(ChromeTestKit.Footnote(1, "note text")),
            settingsInner: Compat15);
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        var lines = Lines(layout.Page(0));
        Assert.Contains(lines, l => l.Contains("note text", StringComparison.Ordinal));
        Assert.Equal(1, layout.LaidOutPages);
    }

    [Fact]
    public void ATableInAHeaderIsLaidOut()
    {
        var header = ChromeTestKit.HeaderPart(Tbl(Fixed, Grid(2000, 2000), Row(Cell("left") + Cell("right"))));
        var document = ChromeTestKit.Document(
            P("body") + "<w:sectPr>" + ChromeTestKit.Header("default", "rId1") + Pgsz(612f, 792f) + Margins(72f, 72f, 72f, 72f) + "</w:sectPr>",
            parts: new Dictionary<string, string> { ["rId1"] = header },
            settingsInner: Compat15);
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        Assert.Equal(["left", "right", "body"], Lines(layout.Page(0)));
    }

    [Fact]
    public void TheWritingViewLaysATableOutAtItsTextWidth()
    {
        var layout = Lay(
            Tbl("<w:tblW w:w=\"5000\" w:type=\"pct\"/>", Grid(1000, 1000), Row(Cell("a", Shade("FF0000")) + Cell("b", Shade("00FF00")))) + Sect(),
            options: new LayoutOptions(LayoutMode.Flow, 400f));
        var fills = Fills(layout.Page(0));
        Assert.Equal(400f, fills.Sum(f => f.Width), 0.2f);
    }

    private static string Sdt(string inner) => $"<w:sdt><w:sdtPr/><w:sdtContent>{inner}</w:sdtContent></w:sdt>";

    [Fact]
    public void ARowInAnUnknownWrapperIsAMarkedBoxAndTheRestStillLaysOut()
    {
        var lh = LineHeight();
        var layout = Lay(
            Tbl(Fixed, Grid(2000, 2000), Row(Cell("a", Shade("FF0000")) + Cell("b")), Sdt(Row(Cell("hidden") + Cell("text"))), Row(Cell("c", Shade("00FF00")) + Cell("d"))) + Sect());
        var page = layout.Page(0);
        var box = Assert.Single(page.Items.OfType<PlaceholderItem>());
        Assert.Equal(Left + Margin, box.X, Tolerance);
        Assert.Equal(200f - (2f * Margin), box.Width, Tolerance);
        var fills = Fills(page);
        Assert.Equal(box.Y, fills[0].Y + fills[0].Height, Tolerance);
        Assert.Equal(box.Y + box.Height + 8f, fills[1].Y, Tolerance);
        Assert.Equal(["a", "b", "c", "d"], Lines(page));
        Assert.Equal(lh, fills[0].Height, Tolerance);
    }

    [Fact]
    public void ACellInAnUnknownWrapperIsAMarkedCellAndKeepsTheGridOfTheOthers()
    {
        var layout = Lay(
            Tbl(Fixed, Grid(2000, 2000, 2000), Row(Cell("a", Shade("FF0000")) + Sdt(Cell("hidden")) + Cell("c", Shade("00FF00")))) + Sect());
        var page = layout.Page(0);
        var box = Assert.Single(page.Items.OfType<PlaceholderItem>());
        Assert.Equal(Left + Margin + 100f, box.X, Tolerance);
        var fills = Fills(page);
        Assert.Equal(Left, fills[0].X, Tolerance);
        Assert.Equal(Left + 200f, fills[1].X, Tolerance);
        Assert.Equal(["a", "c"], Lines(page));
    }

    [Fact]
    public void ARowMarkBetweenRowsAndCellsTakesNoRoom()
    {
        var layout = Lay(
            Tbl(Fixed, Grid(2000), "<w:bookmarkStart w:id=\"1\" w:name=\"x\"/>" + Row(Cell("a") + "<w:bookmarkEnd w:id=\"1\"/>")) + Sect());
        Assert.Empty(layout.Page(0).Items.OfType<PlaceholderItem>());
    }

    [Fact]
    public void ACellThatSpansThreePagesAddsNoSpaceBeforeOnTheLaterPages()
    {
        // One-line paragraphs with 10 pt before: the cut falls between paragraphs, so each later page starts with a whole paragraph.
        var paragraphs = string.Concat(Enumerable.Range(1, 18).Select(i => $"<w:p><w:pPr><w:spacing w:before=\"200\"/></w:pPr><w:r><w:t>p{i}</w:t></w:r></w:p>"));
        var layout = Lay(Tbl(Fixed, Grid(2000), Row($"<w:tc><w:tcPr/>{paragraphs}</w:tc>")) + TallFor(5));
        var pages = Pages(layout);
        Assert.True(pages.Count >= 3);
        var first = Baselines(pages[0])[0];
        Assert.Equal(10f, first - Baselines(pages[1])[0], Tolerance);
        Assert.Equal(Baselines(pages[1])[0], Baselines(pages[2])[0], Tolerance);
        Assert.Equal(18, pages.Sum(p => Lines(p).Length));
    }

    [Fact]
    public void ManyRowsLayOutQuickly()
    {
        var rows = string.Concat(Enumerable.Range(0, 3000).Select(i => Row(string.Concat(Enumerable.Range(0, 5).Select(c => Cell("r" + i.ToString(CultureInfo.InvariantCulture) + "c" + c.ToString(CultureInfo.InvariantCulture)))))));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var layout = Lay(Tbl(string.Empty, Grid(0, 0, 0, 0, 0), rows) + Sect());
        watch.Stop();
        Assert.True(layout.IsComplete);
        Assert.InRange(layout.LaidOutPages, 40, 120);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    private sealed class FloatComparer : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) < 0.1f;

        public int GetHashCode(float obj) => 0;
    }
}
