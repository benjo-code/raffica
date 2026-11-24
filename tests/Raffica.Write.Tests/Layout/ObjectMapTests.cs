using System.Globalization;
using System.Text;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using WriteCorpus;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The object map of a laid-out page (milestone 4.5a): the boxes of the body's pictures and the grids of its tables, and the hit test that reads them.</summary>
public sealed class ObjectMapTests
{
    private const float Tolerance = 0.06f;
    private const float Left = 72f;
    private const string Compat15 = "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat>";

    private const string PictureNamespaces =
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"";

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

    private static string Text(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string Para(params string[] runs) => $"<w:p>{string.Concat(runs)}</w:p>";

    private static string Inline(float width, float height, string graphic) =>
        $"<w:r><w:drawing {PictureNamespaces}><wp:inline><wp:extent cx=\"{Emus(width)}\" cy=\"{Emus(height)}\"/><wp:docPr id=\"1\" name=\"p\"/>{graphic}</wp:inline></w:drawing></w:r>";

    private static string Emus(float points) => ((long)(points * 12700f)).ToString(CultureInfo.InvariantCulture);

    private static string PictureGraphic(string spPr = "") =>
        $"<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip r:embed=\"rId1\"/></pic:blipFill>{spPr}</pic:pic></a:graphicData></a:graphic>";

    private static string Picture(float width, float height) => Inline(width, height, PictureGraphic());

    private static string Cell(string content, string tcPr = "") => $"<w:tc><w:tcPr>{tcPr}</w:tcPr>{(content.StartsWith("<w:p", StringComparison.Ordinal) ? content : Para(Text(content)))}</w:tc>";

    private static string Row(string cells, string trPr = "") => $"<w:tr><w:trPr>{trPr}</w:trPr>{cells}</w:tr>";

    private static string Grid(params int[] twips) =>
        "<w:tblGrid>" + string.Concat(twips.Select(w => string.Create(CultureInfo.InvariantCulture, $"<w:gridCol w:w=\"{w}\"/>"))) + "</w:tblGrid>";

    private static string Tbl(string grid, params string[] rows) =>
        $"<w:tbl><w:tblPr><w:tblLayout w:type=\"fixed\"/></w:tblPr>{grid}{string.Concat(rows)}</w:tbl>";

    private static WordDocument Model(string body)
    {
        var document = LayoutModel.Document(body.Contains("<w:sectPr", StringComparison.Ordinal) ? body : body + Sect());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"<w:settings xmlns:w=\"{LayoutModel.W}\">{Compat15}</w:settings>"));
        document.Settings = Raffica.Write.Docx.Xml.SettingsXml.Read(stream);
        return document;
    }

    private static DocumentLayout Lay(WordDocument document, LayoutOptions? options = null) =>
        Finish(DocumentLayout.Create(document, Fonts, options ?? new LayoutOptions()));

    [Fact]
    public void AnInlinePictureGetsABoxWithItsIdentity()
    {
        var document = Model(Para(Text("first")) + Para(Text("second "), Picture(100f, 50f)));
        var page = Lay(document).Page(0);

        var box = Assert.Single(page.Objects.Pictures);
        Assert.Equal(new ObjectId(StoryRef.Body, 1, 1, 0), box.Id);
        Assert.Equal(100f, box.Width, Tolerance);
        Assert.Equal(50f, box.Height, Tolerance);
        Assert.False(box.Floating);
        Assert.True(box.Resizable);
        var item = Assert.Single(page.Items.OfType<ImageItem>());
        Assert.Equal(item.X, box.X, Tolerance);
        Assert.Equal(item.Y, box.Y, Tolerance);

        // The identity names the drawing again in the model, after the layout is done with it.
        var found = ObjectIds.Find(document.Body.Blocks, box.Id!);
        Assert.NotNull(found);
        Assert.Same(document.Body.Blocks[1], found!.Value.Paragraph);
        Assert.Null(ObjectIds.Find(document.Body.Blocks, new ObjectId(StoryRef.Body, 1, 0, 0)));
    }

    [Fact]
    public void AFloatingPictureIsMarkedFloating()
    {
        var anchored = $"<w:r><w:drawing {PictureNamespaces}><wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" behindDoc=\"0\"><wp:simplePos x=\"0\" y=\"0\"/>" +
            "<wp:positionH relativeFrom=\"column\"><wp:posOffset>127000</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
            $"<wp:extent cx=\"{Emus(80f)}\" cy=\"{Emus(40f)}\"/><wp:wrapSquare wrapText=\"bothSides\"/><wp:docPr id=\"2\" name=\"f\"/>{PictureGraphic()}</wp:anchor></w:drawing></w:r>";
        var page = Lay(Model(Para(anchored, Text("text beside the picture")))).Page(0);

        var box = Assert.Single(page.Objects.Pictures);
        Assert.True(box.Floating);
        Assert.True(box.Resizable);
        Assert.Equal(80f, box.Width, Tolerance);
        Assert.Equal(40f, box.Height, Tolerance);
        Assert.Equal(Left + 10f, box.X, Tolerance);
        Assert.Equal(new ObjectId(StoryRef.Body, 0, 0, 0), box.Id);
    }

    [Fact]
    public void ARotatedPictureIsNotResizable()
    {
        var rotated = Inline(60f, 60f, PictureGraphic("<pic:spPr><a:xfrm rot=\"5400000\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"762000\" cy=\"762000\"/></a:xfrm></pic:spPr>"));
        var straight = Inline(60f, 60f, PictureGraphic("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"762000\" cy=\"762000\"/></a:xfrm></pic:spPr>"));
        var page = Lay(Model(Para(rotated) + Para(straight))).Page(0);

        Assert.Equal(2, page.Objects.Pictures.Count);
        Assert.False(page.Objects.Pictures[0].Resizable);
        Assert.True(page.Objects.Pictures[1].Resizable);
    }

    [Fact]
    public void AShapeIsNeverAPictureBox()
    {
        // A shape that is filled with a picture, a floating text box and a drawing that is not a picture: none is a box. The plain picture next to them is.
        var shapeWithPicture = Inline(50f, 50f, "<a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><wps:wsp><wps:spPr><a:blipFill><a:blip r:embed=\"rId1\"/></a:blipFill></wps:spPr></wps:wsp></a:graphicData></a:graphic>");
        var chart = Inline(50f, 50f, "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/></a:graphic>");
        var body = Para(shapeWithPicture) + Para(chart) + Para(FloatLayoutTests.TextBoxRun(), Text("text")) + Para(Picture(40f, 40f));
        var page = Lay(Model(body)).Page(0);

        var box = Assert.Single(page.Objects.Pictures);
        Assert.Equal(new ObjectId(StoryRef.Body, 3, 0, 0), box.Id);
    }

    [Fact]
    public void ATableListsEveryColumnBorderAndRowEdge()
    {
        var page = Lay(Model(Tbl(Grid(2000, 3000, 1000), Row(Cell("a") + Cell("b") + Cell("c")), Row(Cell("d") + Cell("e") + Cell("f"))))).Page(0);

        var grid = Assert.Single(page.Objects.Tables);
        Assert.Equal(StoryRef.Body, grid.Story);
        Assert.Equal(0, grid.BlockIndex);
        Assert.Equal(4, grid.ColumnBorders.Count);
        Assert.Equal([Left, Left + 100f, Left + 250f, Left + 300f], grid.ColumnBorders.ToArray(), new FloatComparer());
        Assert.Equal(3, grid.RowEdges.Count);
        Assert.Equal(grid.Y, grid.RowEdges[0], Tolerance);
        Assert.Equal(grid.Y + grid.Height, grid.RowEdges[^1], Tolerance);
        Assert.Equal(Left, grid.X, Tolerance);
        Assert.Equal(300f, grid.Width, Tolerance);
        Assert.Equal([0, 1], grid.RowIndexes.ToArray());
        Assert.Equal(6, grid.Cells.Count);
        var e = grid.Cells.Single(c => c.Row == 1 && c.Column == 1);
        Assert.Equal(Left + 100f, e.X, Tolerance);
        Assert.Equal(150f, e.Width, Tolerance);
        Assert.Equal(grid.RowEdges[1], e.Y, Tolerance);
        Assert.Equal(1, e.RowSpan);
        Assert.Equal(1, e.ColumnSpan);
    }

    [Fact]
    public void AMergedCellIsOneRectOfItsSpan()
    {
        var page = Lay(Model(Tbl(
            Grid(2000, 2000, 2000),
            Row(Cell("A", "<w:vMerge w:val=\"restart\"/>") + Cell("B", "<w:gridSpan w:val=\"2\"/>")),
            Row(Cell(string.Empty, "<w:vMerge/>") + Cell("C") + Cell("D"))))).Page(0);

        var grid = Assert.Single(page.Objects.Tables);
        Assert.Equal(4, grid.Cells.Count);
        var a = grid.Cells.Single(c => c.Column == 0);
        Assert.Equal(2, a.RowSpan);
        Assert.Equal(1, a.ColumnSpan);
        Assert.Equal(grid.RowEdges[2] - grid.RowEdges[0], a.Height, Tolerance);
        var b = grid.Cells.Single(c => c.Row == 0 && c.Column == 1);
        Assert.Equal(2, b.ColumnSpan);
        Assert.Equal(1, b.RowSpan);
        Assert.Equal(200f, b.Width, Tolerance);
        Assert.Equal(2, grid.Cells.Count(c => c.Row == 1));
    }

    [Fact]
    public void ARepeatedHeaderRowIsReportedOncePerPageItAppearsOn()
    {
        var rows = new StringBuilder();
        rows.Append(Row(Cell("HEAD"), "<w:tblHeader/>"));
        for (var i = 1; i <= 12; i++)
        {
            rows.Append(Row(Cell("r" + i.ToString(CultureInfo.InvariantCulture))));
        }

        var layout = Lay(Model(Tbl(Grid(2000), rows.ToString()) + TallFor(5)));
        var pages = Pages(layout);
        Assert.True(pages.Count >= 3);
        var tablePages = pages.Where(p => p.Objects.Tables.Count > 0).ToList();
        Assert.True(tablePages.Count >= 3);
        var previousLast = -1;
        foreach (var page in tablePages)
        {
            var grid = Assert.Single(page.Objects.Tables);
            Assert.Equal(0, grid.RowIndexes[0]);
            Assert.Equal(1, grid.Cells.Count(c => c.Row == 0));
            Assert.Equal(grid.RowIndexes.Count + 1, grid.RowEdges.Count);
            if (previousLast >= 0)
            {
                // The header has its own rect on the continuation page and the rows go on where the page before ended.
                Assert.Equal(previousLast + 1, grid.RowIndexes[1]);
            }

            previousLast = grid.RowIndexes[^1];
        }

        Assert.Equal(12, previousLast);
    }

    [Fact]
    public void HitObjectPrefersAHandleOverThePictureOverTheBorder()
    {
        var document = Model(Tbl(Grid(4000, 4000), Row(Cell(Para(Picture(60f, 40f))) + Cell("b"))));
        var layout = Lay(document);
        var grid = Assert.Single(layout.Page(0).Objects.Tables);
        var box = Assert.Single(layout.Page(0).Objects.Pictures);
        var id = box.Id!;
        const float reach = 6f;

        // The corner of the selected picture is a handle, although the picture, a cell and (close to it) the border lie under the point.
        var corner = layout.HitObject(0, box.X, box.Y, reach, id);
        var handle = Assert.IsType<ObjectHit.Handle>(corner);
        Assert.Equal(HandleSlot.TopLeft, handle.Slot);
        Assert.Equal(HandleSlot.BottomRight, Assert.IsType<ObjectHit.Handle>(layout.HitObject(0, box.X + box.Width, box.Y + box.Height, reach, id)).Slot);
        Assert.Equal(HandleSlot.Right, Assert.IsType<ObjectHit.Handle>(layout.HitObject(0, box.X + box.Width + 1f, box.Y + (box.Height / 2f), reach, id)).Slot);

        // The same point without a selection is the picture; so is a point near the cell border but inside the picture.
        Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, box.X + 1f, box.Y + 1f, reach));
        Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, box.X + 0.5f, box.Y + (box.Height / 2f), reach));
        Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, box.X + 30f, box.Y + 20f, reach, id));

        // On a border with nothing else there: the border; between borders: the cell; outside the table, left and above: the margins.
        var mid = grid.Y + (grid.Height / 2f);
        var border = Assert.IsType<ObjectHit.ColumnBorder>(layout.HitObject(0, grid.ColumnBorders[1] + 2f, mid, reach));
        Assert.Equal(1, border.Index);
        var edge = Assert.IsType<ObjectHit.RowEdge>(layout.HitObject(0, grid.X + 150f, grid.RowEdges[1] - 1f, reach));
        Assert.Equal(1, edge.Index);
        var cell = Assert.IsType<ObjectHit.Cell>(layout.HitObject(0, grid.ColumnBorders[1] + 40f, mid, reach));
        Assert.Equal(1, cell.Rect.Column);
        var row = Assert.IsType<ObjectHit.Margin>(layout.HitObject(0, grid.X - 12f, mid, reach));
        Assert.Equal((TableMarginKind.Row, 0), (row.Kind, row.Index));
        var column = Assert.IsType<ObjectHit.Margin>(layout.HitObject(0, grid.ColumnBorders[1] + 40f, grid.Y - 12f, reach));
        Assert.Equal((TableMarginKind.Column, 1), (column.Kind, column.Index));
        var corner2 = Assert.IsType<ObjectHit.Margin>(layout.HitObject(0, grid.X - 12f, grid.Y - 12f, reach));
        Assert.Equal(TableMarginKind.Table, corner2.Kind);
        Assert.Null(layout.HitObject(0, grid.X - 60f, mid, reach));
        Assert.Null(layout.HitObject(99, 1f, 1f, reach));
    }

    [Fact]
    public void ADocumentWithoutObjectsHasTheSharedEmptyMap()
    {
        var layout = Lay(Model(string.Concat(Enumerable.Repeat(Para(Text("only text")), 120))));
        var pages = Pages(layout);
        Assert.True(pages.Count >= 2);
        foreach (var page in pages)
        {
            Assert.Same(ObjectMap.Empty, page.Objects);
        }

        Assert.Null(layout.HitObject(0, 100f, 100f, 6f));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheMapOfAnIncrementalLayoutEqualsTheMapOfAFullLayoutOnEveryCorpusFile(string id)
    {
        using var file = DocxFile.Open(Path.Combine(Root, id, id + ".docx"));
        var model = file.Model;
        var layout = await SectionOracleTests.LaidAsync(model);
        var plain = TestEdit.Paragraphs(model).Where(p => p.Paragraph.SectionBreak is null && ReferenceEquals(p.Container, model.Body.Blocks)).ToList();
        if (plain.Count == 0)
        {
            return;
        }

        // A split adds a block in front of every table below it (their block indexes move), a text insert re-lays one page; each ends equal to a full layout, maps included.
        var target = plain[0].Paragraph;
        var split = TestEdit.SplitParagraph(model, target, 0);
        layout.ApplyEdit(split, 1);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "split " + id);

        var after = TestEdit.Paragraphs(model).Where(p => p.Paragraph.SectionBreak is null && ReferenceEquals(p.Container, model.Body.Blocks)).ToList();
        var insert = TestEdit.InsertText(model, after[Math.Min(1, after.Count - 1)].Paragraph, 0, "q");
        layout.ApplyEdit(insert, 1);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "insert " + id);
    }

    [Fact]
    public async Task EveryPictureOfTheCorpusResolvesToItsDrawing()
    {
        var pictures = 0;
        var tables = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var layout = await SectionOracleTests.LaidAsync(file.Model);
            for (var i = 0; i < layout.LaidOutPages; i++)
            {
                var objects = layout.Page(i).Objects;
                tables += objects.Tables.Count;
                foreach (var box in objects.Pictures)
                {
                    pictures++;
                    Assert.NotNull(box.Id);
                    var found = ObjectIds.Find(file.Model.Body.Blocks, box.Id!);
                    Assert.True(found is not null, $"{entry.Id}: picture {box.Id} is not found again");
                    Assert.Same(box.Anchor.Picture, found!.Value.Picture);
                }
            }
        }

        Assert.True(pictures > 0, "the corpus has no picture in a box");
        Assert.True(tables > 0, "the corpus has no table in a grid");
        TestContext.Current.SendDiagnosticMessage($"{pictures} picture boxes and {tables} table grids over the corpus");
    }

    [Fact]
    public void HitObjectResolvesTheSelectionOnceAndReadsNoIdentity()
    {
        var layout = Lay(Model(string.Concat(Enumerable.Range(0, 25).Select(_ => Para(Picture(20f, 20f))))));
        var page = layout.Page(0);
        Assert.True(page.Objects.Pictures.Count >= 20);
        var selected = page.Objects.Pictures[15];
        var id = selected.Id!;
        var drawing = selected.Anchor.Picture;
        var x = selected.X + 10f;
        var y = selected.Y + 10f;

        var reads = PictureAnchor.IdentityReads;
        var resolves = layout.HitResolves;
        for (var i = 0; i < 50; i++)
        {
            Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, x, y, 4f, id));
        }

        Assert.Equal(reads, PictureAnchor.IdentityReads);
        Assert.Equal(resolves + 50, layout.HitResolves);

        // Without a selection and with the drawing in hand the model is not read at all, and the handle is still found.
        resolves = layout.HitResolves;
        Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, x, y, 4f));
        Assert.Equal(HandleSlot.TopLeft, Assert.IsType<ObjectHit.Handle>(layout.HitObject(0, selected.X, selected.Y, 4f, drawing)).Slot);
        Assert.Equal(resolves, layout.HitResolves);
        Assert.Equal(HandleSlot.TopLeft, Assert.IsType<ObjectHit.Handle>(layout.HitObject(0, selected.X, selected.Y, 4f, id)).Slot);
        Assert.Equal(resolves + 1, layout.HitResolves);
        Assert.Equal(reads, PictureAnchor.IdentityReads);

        // A page whose pictures cannot be resized never resolves the selection.
        var rotated = Inline(30f, 30f, PictureGraphic("<pic:spPr><a:xfrm rot=\"5400000\"/></pic:spPr>"));
        var turned = Lay(Model(Para(rotated)));
        var turnedBox = Assert.Single(turned.Page(0).Objects.Pictures);
        var turnedId = turnedBox.Id!;
        resolves = turned.HitResolves;
        Assert.IsType<ObjectHit.Picture>(turned.HitObject(0, turnedBox.X + 5f, turnedBox.Y + 5f, 4f, turnedId));
        Assert.Equal(resolves, turned.HitResolves);
    }

    [Fact]
    public void ABehindTextPictureIsListedFirstAndDoesNotWinOverAnInlinePicture()
    {
        var behind = FloatLayoutTests.Anchored(120f, 120f, wrap: "<wp:wrapNone/>", hFrom: "column", vFrom: "paragraph", behind: true);
        var layout = Lay(Model(Para(behind, FloatLayoutTests.InlinePicture(40f, 30f))));
        var page = layout.Page(0);

        Assert.Equal(2, page.Objects.Pictures.Count);
        var back = page.Objects.Pictures[0];
        var front = page.Objects.Pictures[1];
        Assert.True(back.BehindText);
        Assert.False(front.BehindText);
        var insideX = front.X + 5f;
        var insideY = front.Y + 5f;
        Assert.Equal(front, Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, insideX, insideY, 2f)).Box);
        Assert.Equal(front, Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, insideX, insideY, 2f, includeBehindText: true)).Box);

        // Where only the picture behind the text lies: nothing by default, the picture when asked for.
        var onlyX = back.X + 100f;
        var onlyY = back.Y + 100f;
        Assert.Null(layout.HitObject(0, onlyX, onlyY, 2f));
        Assert.Equal(back, Assert.IsType<ObjectHit.Picture>(layout.HitObject(0, onlyX, onlyY, 2f, includeBehindText: true)).Box);
    }

    [Fact]
    public async Task ARowWithATrackedDeletionKeepsTheModelRowNumbers()
    {
        const string deleted = "<w:del w:id=\"7\" w:author=\"a\" w:date=\"2020-01-01T00:00:00Z\"/>";
        var document = Model(Tbl(Grid(3000), Row(Cell("zero")), Row(Cell("one"), deleted), Row(Cell("two")), Row(Cell("three"))));
        var layout = Lay(document);

        var grid = Assert.Single(layout.Page(0).Objects.Tables);
        Assert.Equal([0, 2, 3], grid.RowIndexes.ToArray());
        Assert.Equal(4, grid.RowEdges.Count);
        Assert.Equal([0, 2, 3], grid.Cells.Select(c => c.Row).OrderBy(r => r).ToArray());
        var table = Assert.IsType<Table>(document.Body.Blocks[0]);
        Assert.Equal(4, table.Rows.Count);
        var margin = Assert.IsType<ObjectHit.Margin>(layout.HitObject(0, grid.X - 8f, grid.RowEdges[1] + 3f, 4f));
        Assert.Equal((TableMarginKind.Row, 2), (margin.Kind, margin.Index));

        // The incremental layout after an edit equals a full one, maps included.
        var target = TestEdit.Paragraphs(document).First(p => p.Paragraph.SectionBreak is null && !ReferenceEquals(p.Container, document.Body.Blocks)).Paragraph;
        layout.ApplyEdit(TestEdit.InsertText(document, target, 0, "q"), 1);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, document, label: "tracked deletion row");
    }

    [Fact]
    public void ATableInABlockContentControlNamesTheControlAsItsBlock()
    {
        var control = $"<w:sdt><w:sdtPr/><w:sdtContent>{Tbl(Grid(3000), Row(Cell("inside")))}</w:sdtContent></w:sdt>";
        var layout = Lay(Model(Para(Text("first")) + control + Para(Text("middle")) + Tbl(Grid(3000), Row(Cell("top level")))));

        var tables = layout.Page(0).Objects.Tables;
        Assert.Equal(2, tables.Count);
        Assert.Equal(1, tables[0].BlockIndex);
        Assert.Equal(3, tables[1].BlockIndex);
    }

    [Fact]
    public void AnUnreadableInlinePictureStaysAMarkedBoxWithNoPictureBox()
    {
        var options = new LayoutOptions(ImageBytes: _ => new byte[] { 1, 2, 3 });
        AssertMarkedBoxOnly(Lay(Model(Para(Text("a "), Picture(60f, 40f))), options));
        AssertMarkedBoxOnly(Lay(Model(Para(Picture(60f, 40f))), new LayoutOptions(ImageBytes: _ => ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public void AnUnreadableFloatingPictureStaysAMarkedBoxWithNoPictureBox()
    {
        var options = new LayoutOptions(ImageBytes: _ => new byte[] { 1, 2, 3 });
        AssertMarkedBoxOnly(Lay(Model(Para(FloatLayoutTests.Anchored(80f, 40f), Text("beside it"))), options));
    }

    [Fact]
    public void APictureWithoutAnEmbedGetsNoPictureBox()
    {
        const string noEmbed = "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip/></pic:blipFill></pic:pic></a:graphicData></a:graphic>";
        AssertMarkedBoxOnly(Lay(Model(Para(Inline(50f, 30f, noEmbed)))));
        var floating = $"<w:r><w:drawing {PictureNamespaces}><wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" behindDoc=\"0\"><wp:simplePos x=\"0\" y=\"0\"/>" +
            "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
            $"<wp:extent cx=\"{Emus(50f)}\" cy=\"{Emus(30f)}\"/><wp:wrapNone/><wp:docPr id=\"2\" name=\"f\"/>{noEmbed}</wp:anchor></w:drawing></w:r>";
        AssertMarkedBoxOnly(Lay(Model(Para(floating, Text("text")))));
    }

    // The picture is a placeholder in the items, has no box, and a click on it hits nothing.
    private static void AssertMarkedBoxOnly(DocumentLayout layout)
    {
        var page = layout.Page(0);
        Assert.Empty(page.Objects.Pictures);
        Assert.Empty(page.Items.OfType<ImageItem>());
        var marked = Assert.Single(page.Items.OfType<PlaceholderItem>());
        Assert.Null(layout.HitObject(0, marked.X + (marked.Width / 2f), marked.Y + (marked.Height / 2f), 4f));
    }

    private sealed class FloatComparer : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) <= Tolerance;

        public int GetHashCode(float obj) => 0;
    }
}
