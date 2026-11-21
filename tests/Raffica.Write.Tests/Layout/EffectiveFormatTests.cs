using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class EffectiveFormatTests
{
    private const string Base =
        "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults>"
        + "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
        + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr/></w:style>";

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static (WordDocument Document, LayoutEffectiveFormat Format) Setup(string styles, string body)
    {
        var document = LayoutModel.Document(body, LayoutModel.StylesPart(Base + styles));
        return (document, new LayoutEffectiveFormat(document));
    }

    private static Run FirstRun(Paragraph paragraph) => (Run)paragraph.Inlines[0];

    [Fact]
    public void BoldFromTheParagraphStyleIsEffective()
    {
        var (doc, format) = Setup(
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:basedOn w:val=\"Normal\"/><w:rPr><w:b/></w:rPr></w:style>",
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        Assert.True(format.ForRun(StoryRef.Body, p, FirstRun(p)).Bold);
    }

    [Fact]
    public void ExplicitOffInARunCancelsTheStyle()
    {
        var (doc, format) = Setup(
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:rPr><w:b/></w:rPr></w:style>",
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:rPr><w:b w:val=\"0\"/></w:rPr><w:t>x</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        Assert.False(format.ForRun(StoryRef.Body, p, FirstRun(p)).Bold);
    }

    [Fact]
    public void CharacterStyleAndBasedOnChainAreRead()
    {
        var (doc, format) = Setup(
            "<w:style w:type=\"character\" w:styleId=\"Base\"><w:rPr><w:i/></w:rPr></w:style>"
            + "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:basedOn w:val=\"Base\"/><w:rPr><w:b/></w:rPr></w:style>",
            "<w:p><w:r><w:rPr><w:rStyle w:val=\"Strong\"/></w:rPr><w:t>x</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        var effective = format.ForRun(StoryRef.Body, p, FirstRun(p));
        Assert.True(effective.Bold);
        Assert.True(effective.Italic);
        Assert.False(effective.Strike);
    }

    [Fact]
    public void BoldFromATableStyleHeaderRowIsEffectiveInsideTheCell()
    {
        var (doc, format) = Setup(
            "<w:style w:type=\"table\" w:styleId=\"Head\"><w:basedOn w:val=\"TableNormal\"/><w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr></w:tblStylePr></w:style>",
            "<w:tbl><w:tblPr><w:tblStyle w:val=\"Head\"/><w:tblLook w:firstRow=\"1\" w:noHBand=\"1\" w:noVBand=\"1\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid>"
            + "<w:tr><w:tc><w:p><w:r><w:t>h</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>b</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
            + "<w:p><w:r><w:t>out</w:t></w:r></w:p>");
        var table = (Table)doc.Body.Blocks[0];
        var head = (Paragraph)((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks[0];
        var body = (Paragraph)((TableCell)((TableRow)table.Rows[1]).Items[0]).Blocks[0];
        var outside = LayoutModel.Para(doc, 1);
        Assert.True(format.ForRun(StoryRef.Body, head, FirstRun(head)).Bold);
        Assert.False(format.ForRun(StoryRef.Body, body, FirstRun(body)).Bold);
        Assert.False(format.ForRun(StoryRef.Body, outside, FirstRun(outside)).Bold);
    }

    [Fact]
    public void UnderlineOfAnyKindIsOn()
    {
        var (doc, format) = Setup(
            string.Empty,
            "<w:p><w:r><w:rPr><w:u w:val=\"wave\"/></w:rPr><w:t>a</w:t></w:r><w:r><w:rPr><w:u w:val=\"none\"/></w:rPr><w:t>b</w:t></w:r><w:r><w:t>c</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        Assert.True(format.ForRun(StoryRef.Body, p, (Run)p.Inlines[0]).Underline);
        Assert.False(format.ForRun(StoryRef.Body, p, (Run)p.Inlines[1]).Underline);
        Assert.False(format.ForRun(StoryRef.Body, p, (Run)p.Inlines[2]).Underline);
    }

    [Fact]
    public void SuperscriptAndSubscriptMapToPosition()
    {
        var (doc, format) = Setup(
            string.Empty,
            "<w:p><w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:t>a</w:t></w:r><w:r><w:rPr><w:vertAlign w:val=\"subscript\"/></w:rPr><w:t>b</w:t></w:r><w:r><w:t>c</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        Assert.Equal(VerticalPosition.Superscript, format.ForRun(StoryRef.Body, p, (Run)p.Inlines[0]).Position);
        Assert.Equal(VerticalPosition.Subscript, format.ForRun(StoryRef.Body, p, (Run)p.Inlines[1]).Position);
        Assert.Equal(VerticalPosition.Baseline, format.ForRun(StoryRef.Body, p, (Run)p.Inlines[2]).Position);
    }

    [Fact]
    public void ForMarkReadsTheParagraphMark()
    {
        var (doc, format) = Setup(
            string.Empty,
            "<w:p><w:pPr><w:rPr><w:b/><w:i/></w:rPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p><w:p><w:r><w:t>y</w:t></w:r></w:p>");
        var marked = format.ForMark(StoryRef.Body, LayoutModel.Para(doc, 0));
        Assert.True(marked.Bold);
        Assert.True(marked.Italic);
        Assert.False(format.ForMark(StoryRef.Body, LayoutModel.Para(doc, 1)).Bold);
    }

    [Fact]
    public void EveryRunOfEveryCorpusFileMatchesTheLayoutsResolvedRun()
    {
        var runs = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var docx = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var model = docx.Model;
            var format = new LayoutEffectiveFormat(model);
            var resolver = new StyleResolver(model.Styles, model.Settings, model.Numbering);
            foreach (var block in model.Body.Blocks)
            {
                if (block is not Paragraph paragraph)
                {
                    continue;
                }

                var resolved = resolver.ForParagraph(paragraph);
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is not Run run)
                    {
                        continue;
                    }

                    var expected = resolver.ForRun(resolved, run);
                    var actual = format.ForRun(StoryRef.Body, paragraph, run);
                    Assert.True(
                        expected.Bold == actual.Bold && expected.Italic == actual.Italic && (expected.Underline != UnderlineKind.None) == actual.Underline
                        && expected.Strike == actual.Strike && expected.SmallCaps == actual.SmallCaps && expected.Caps == actual.Caps
                        && (int)expected.VerticalAlignment == (int)actual.Position,
                        entry.Id);
                    runs++;
                }
            }
        }

        Assert.True(runs > 1000, $"only {runs} runs compared");
    }

    [Fact]
    public void ForParagraphEqualsThePlannerForEveryParagraphOfTheCorpus()
    {
        var compared = 0;
        var inCells = 0;
        var listed = 0;
        var plannerCompared = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var docx = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var model = docx.Model;
            var format = new LayoutEffectiveFormat(model);
            var styles = new StyleResolver(model.Styles, model.Settings, model.Numbering);
            var planner = new BlockPlanner(styles, new NumberingCounter(model.Numbering), new ParagraphLayouter(PageTestKit.Fonts), model.Settings);
            foreach (var (paragraph, inTable) in AllParagraphs(model.Body.Blocks))
            {
                // This comparison only pins the field mapping (ForParagraph and ResolveParagraph share Resolve); the planner comparisons below carry the proof.
                var resolved = format.ResolveParagraph(StoryRef.Body, paragraph);
                var actual = format.ForParagraph(StoryRef.Body, paragraph);
                Assert.Equal((resolved.StyleId, resolved.NumberingId, resolved.NumberingLevel, resolved.OutlineLevel), (actual.StyleId, actual.NumberingId, actual.NumberingLevel, actual.OutlineLevel));
                compared++;
                listed += actual.NumberingId > 0 ? 1 : 0;
            }

            // The planner's own reading of the top-level paragraphs, and of the paragraphs of the cells of top-level tables (the cell format of the table style enters there).
            // A table nested in a cell is not reachable through the table plan's model, so its paragraphs are not compared with the planner here.
            var flat = FlatBlocks.From(model.Body.Blocks);
            foreach (var item in flat)
            {
                if (item.Block is Paragraph top && planner.PlanFor(item) is ParagraphPlan plan)
                {
                    AssertSameAsPlanner(plan.Input.Paragraph, format.ForParagraph(StoryRef.Body, top), entry.Id);
                    plannerCompared++;
                }
                else if (item.Block is Table table && planner.PlanFor(item) is TablePlan tablePlan)
                {
                    var geometry = TableGeometry.Of(table);
                    for (var r = 0; r < geometry.Rows.Count; r++)
                    {
                        for (var c = 0; c < geometry.Rows[r].Cells.Count; c++)
                        {
                            if (geometry.Rows[r].Cells[c] is not { } cell)
                            {
                                continue;
                            }

                            foreach (var paragraph in cell.Blocks.OfType<Paragraph>())
                            {
                                var expected = styles.ForParagraph(paragraph, tablePlan.Model.Rows[r].Cells[c].Format);
                                AssertSameAsPlanner(expected, format.ForParagraph(StoryRef.Body, paragraph), entry.Id);
                                plannerCompared++;
                                inCells++;
                            }
                        }
                    }
                }
            }
        }

        Assert.True(compared > 1000, $"only {compared} paragraphs compared");
        Assert.True(plannerCompared > 1000, $"only {plannerCompared} paragraphs compared with the planner");
        Assert.True(inCells > 0, "no paragraph in a table cell compared with the planner");
        Assert.True(listed > 0, "no listed paragraph");
    }

    private static void AssertSameAsPlanner(ResolvedParagraph expected, EffectiveParagraph actual, string id) =>
        Assert.True(
            (expected.StyleId, expected.NumberingId, expected.NumberingLevel, expected.OutlineLevel) == (actual.StyleId, actual.NumberingId, actual.NumberingLevel, actual.OutlineLevel),
            id);

    [Fact]
    public void AParagraphInACellTakesListAndOutlineLevelFromTheTableStyle()
    {
        const string style = "<w:style w:type=\"table\" w:styleId=\"Banded\"><w:basedOn w:val=\"TableNormal\"/>"
            + "<w:tblStylePr w:type=\"firstRow\"><w:pPr><w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"3\"/></w:numPr><w:outlineLvl w:val=\"2\"/></w:pPr></w:tblStylePr></w:style>";
        var document = LayoutModel.Document(
            "<w:p><w:r><w:t>before</w:t></w:r></w:p>" + Tbl("<w:tr>" + Cell("h1") + Cell("h2") + "</w:tr><w:tr>" + Cell("b1") + Cell("b2") + "</w:tr>"),
            LayoutModel.StylesPart(Base + style));
        var styles = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var planner = new BlockPlanner(styles, new NumberingCounter(document.Numbering), new ParagraphLayouter(PageTestKit.Fonts), document.Settings);
        var tablePlan = (TablePlan)planner.PlanFor(FlatBlocks.From(document.Body.Blocks)[1])!;
        var table = (Table)document.Body.Blocks[1];
        var format = new LayoutEffectiveFormat(document);
        var header = (Paragraph)((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks[0];
        var body = (Paragraph)((TableCell)((TableRow)table.Rows[1]).Items[0]).Blocks[0];
        var expectedHeader = styles.ForParagraph(header, tablePlan.Model.Rows[0].Cells[0].Format);
        var actualHeader = format.ForParagraph(StoryRef.Body, header);

        // The plan and the editor agree, and the header row's cell format really reaches the fields.
        AssertSameAsPlanner(expectedHeader, actualHeader, "header");
        Assert.Equal(2, actualHeader.OutlineLevel);
        Assert.Equal(3, actualHeader.NumberingId);
        Assert.Equal(1, actualHeader.NumberingLevel);
        Assert.Equal(0, format.ForParagraph(StoryRef.Body, body).NumberingId);
        Assert.Null(format.ForParagraph(StoryRef.Body, body).OutlineLevel);
    }

    private const string BandedStyle =
        "<w:style w:type=\"table\" w:styleId=\"Banded\"><w:basedOn w:val=\"TableNormal\"/>"
        + "<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr></w:tblStylePr>"
        + "<w:tblStylePr w:type=\"lastRow\"><w:rPr><w:i/></w:rPr></w:tblStylePr>"
        + "<w:tblStylePr w:type=\"band1Horz\"><w:rPr><w:u w:val=\"single\"/></w:rPr></w:tblStylePr>"
        + "<w:tblStylePr w:type=\"firstCol\"><w:rPr><w:strike/></w:rPr></w:tblStylePr></w:style>";

    private const string Look = "<w:tblLook w:firstRow=\"1\" w:lastRow=\"1\" w:firstColumn=\"1\" w:noHBand=\"0\" w:noVBand=\"1\"/>";

    private static string Cell(string text, string props = "") => $"<w:tc><w:tcPr>{props}</w:tcPr><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Tbl(string rows, string grid = "<w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/>") =>
        $"<w:tbl><w:tblPr><w:tblStyle w:val=\"Banded\"/>{Look}</w:tblPr><w:tblGrid>{grid}</w:tblGrid>{rows}</w:tbl>";

    private static void AssertCellsMatchTheLayout(WordDocument document, int tableIndex)
    {
        var styles = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var planner = new BlockPlanner(styles, new NumberingCounter(document.Numbering), new ParagraphLayouter(PageTestKit.Fonts), document.Settings);
        var flat = FlatBlocks.From(document.Body.Blocks);
        var plan = (TablePlan)planner.PlanFor(flat[tableIndex])!;
        var table = (Table)document.Body.Blocks[tableIndex];
        var geometry = TableGeometry.Of(table);
        var format = new LayoutEffectiveFormat(document);
        var compared = 0;
        for (var r = 0; r < geometry.Rows.Count; r++)
        {
            for (var c = 0; c < geometry.Rows[r].Cells.Count; c++)
            {
                if (geometry.Rows[r].Cells[c] is not { } cell)
                {
                    continue;
                }

                foreach (var paragraph in cell.Blocks.OfType<Paragraph>())
                {
                    var resolved = styles.ForParagraph(paragraph, plan.Model.Rows[r].Cells[c].Format);
                    foreach (var run in paragraph.Inlines.OfType<Run>())
                    {
                        var expected = styles.ForRun(resolved, run);
                        var actual = format.ForRun(StoryRef.Body, paragraph, run);
                        Assert.Equal(
                            (expected.Bold, expected.Italic, expected.Underline != UnderlineKind.None, expected.Strike),
                            (actual.Bold, actual.Italic, actual.Underline, actual.Strike));
                        compared++;
                    }
                }
            }
        }

        Assert.True(compared > 0);
    }

    private static WordDocument TableDocument(string table) => LayoutModel.Document("<w:p><w:r><w:t>before</w:t></w:r></w:p>" + table, LayoutModel.StylesPart(Base + BandedStyle));

    [Fact]
    public void MergedCellsUnderConditionalRegionsMatchTheLayout()
    {
        var rows = "<w:tr>" + Cell("h1") + Cell("h2") + "</w:tr>"
            + "<w:tr>" + Cell("m", "<w:vMerge w:val=\"restart\"/>") + Cell("b1") + "</w:tr>"
            + "<w:tr>" + Cell("", "<w:vMerge/>") + Cell("b2") + "</w:tr>"
            + "<w:tr>" + Cell("span", "<w:gridSpan w:val=\"2\"/>") + "</w:tr>";
        AssertCellsMatchTheLayout(TableDocument(Tbl(rows)), 1);
    }

    [Fact]
    public void ARowLevelContentControlBeforeTheRowTakesARowIndex()
    {
        // The sdt around a row is row 0 in the layout, so the real first row is row 1: not the header, banded as the second row.
        var rows = "<w:sdt><w:sdtContent><w:tr>" + Cell("wrapped") + Cell("w2") + "</w:tr></w:sdtContent></w:sdt>"
            + "<w:tr>" + Cell("first") + Cell("f2") + "</w:tr>" + "<w:tr>" + Cell("last") + Cell("l2") + "</w:tr>";
        var document = TableDocument(Tbl(rows));
        AssertCellsMatchTheLayout(document, 1);
        var table = (Table)document.Body.Blocks[1];
        var first = (Paragraph)((TableCell)((TableRow)table.Rows[1]).Items[0]).Blocks[0];
        Assert.False(new LayoutEffectiveFormat(document).ForRun(StoryRef.Body, first, FirstRun(first)).Bold);
    }

    [Fact]
    public void ANestedTableUsesItsOwnInnermostCell()
    {
        var inner = Tbl("<w:tr>" + Cell("in1") + Cell("in2") + "</w:tr><w:tr>" + Cell("in3") + Cell("in4") + "</w:tr>");
        var outer = "<w:tc><w:p><w:r><w:t>o</w:t></w:r></w:p>" + inner + "<w:p/></w:tc>";
        var rows = "<w:tr>" + Cell("h1") + Cell("h2") + "</w:tr><w:tr>" + outer + Cell("b") + "</w:tr><w:tr>" + Cell("l1") + Cell("l2") + "</w:tr>";
        var document = TableDocument(Tbl(rows));
        AssertCellsMatchTheLayout(document, 1);
        var outerCell = (TableCell)((TableRow)((Table)document.Body.Blocks[1]).Rows[1]).Items[0];
        var nested = (Table)outerCell.Blocks[1];
        var innerParagraph = (Paragraph)((TableCell)((TableRow)nested.Rows[0]).Items[0]).Blocks[0];
        var effective = new LayoutEffectiveFormat(document).ForRun(StoryRef.Body, innerParagraph, FirstRun(innerParagraph));
        Assert.True(effective.Bold);
        Assert.True(effective.Strike);
    }

    [Fact]
    public void MoreGridColumnsThanTheLayoutKeepMatchTheLayout()
    {
        var grid = string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"100\"/>", 80));
        var rows = "<w:tr>" + Cell("h") + Cell("h2") + "</w:tr><w:tr>" + Cell("b") + Cell("b2") + "</w:tr><w:tr>" + Cell("l") + Cell("l2") + "</w:tr>";
        AssertCellsMatchTheLayout(TableDocument(Tbl(rows, grid)), 1);
    }

    [Fact]
    public void EveryRunOfTheThesisIsReadFromOneWalkOfTheStory()
    {
        using var docx = DocxFile.Open(Path.Combine(Root, "thesis", "thesis.docx"));
        var model = docx.Model;
        var format = new LayoutEffectiveFormat(model);
        var runs = 0;
        foreach (var (paragraph, _) in AllParagraphs(model.Body.Blocks))
        {
            foreach (var run in paragraph.Inlines.OfType<Run>())
            {
                _ = format.ForRun(StoryRef.Body, paragraph, run);
                runs++;
            }

            _ = format.ForMark(StoryRef.Body, paragraph);
        }

        Assert.True(runs > 5000, $"only {runs} runs read");

        // The paragraphs of the story itself are answered by a search and the index holds the paragraphs of tables only: one walk for the revision, never one per paragraph.
        Assert.Equal(1, format.StoryWalks);
    }

    private static IEnumerable<(Paragraph Paragraph, bool InTable)> AllParagraphs(IEnumerable<Block> blocks, bool inTable = false)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    yield return (p, inTable);
                    break;
                case ContentControlBlock control:
                    foreach (var item in AllParagraphs(control.Content, inTable))
                    {
                        yield return item;
                    }

                    break;
                case Table table:
                    foreach (var row in table.Rows.OfType<TableRow>())
                    {
                        foreach (var cell in row.Items.OfType<TableCell>())
                        {
                            foreach (var item in AllParagraphs(cell.Blocks, true))
                            {
                                yield return item;
                            }
                        }
                    }

                    break;
            }
        }
    }

    [Fact]
    public void AStructureChangeThatRaisesTheRevisionDropsTheIndex()
    {
        var document = TableDocument(Tbl("<w:tr>" + Cell("h1") + Cell("h2") + "</w:tr><w:tr>" + Cell("b1") + Cell("b2") + "</w:tr>"));
        var format = new LayoutEffectiveFormat(document);
        var table = (Table)document.Body.Blocks[1];
        var header = (Paragraph)((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks[0];
        Assert.True(format.ForRun(StoryRef.Body, header, FirstRun(header)).Bold);

        // The header paragraph leaves its cell and becomes a body paragraph: a raised revision makes the format see it outside the table.
        ((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks.Remove(header);
        document.Body.Blocks.Add(header);
        document.Edits.Raise();

        Assert.False(format.ForRun(StoryRef.Body, header, FirstRun(header)).Bold);

        // It is a block of the body now: answered without a walk.
        Assert.Equal(1, format.StoryWalks);
    }

    [Fact]
    public void AParagraphTheIndexDoesNotKnowIsFoundByOneMoreWalkPerRevision()
    {
        var document = TableDocument(Tbl("<w:tr>" + Cell("h1") + Cell("h2") + "</w:tr>"));
        var format = new LayoutEffectiveFormat(document);
        var table = (Table)document.Body.Blocks[1];
        var header = (Paragraph)((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks[0];
        Assert.True(format.ForRun(StoryRef.Body, header, FirstRun(header)).Bold);

        var added = LayoutModel.Para(LayoutModel.Document("<w:p><w:r><w:t>new</w:t></w:r></w:p>", LayoutModel.StylesPart(Base)), 0);
        document.Body.Blocks.Add(added);
        Assert.False(format.ForRun(StoryRef.Body, added, FirstRun(added)).Bold);

        // A block of the body is no miss: no walk.
        Assert.Equal(1, format.StoryWalks);

        // A paragraph that is in no story costs one more walk for the revision, not one per call.
        var stranger = LayoutModel.Para(LayoutModel.Document("<w:p><w:r><w:t>x</w:t></w:r></w:p>", LayoutModel.StylesPart(Base)), 0);
        for (var i = 0; i < 20; i++)
        {
            _ = format.ForRun(StoryRef.Body, stranger, FirstRun(stranger));
        }

        Assert.Equal(2, format.StoryWalks);
    }
}
