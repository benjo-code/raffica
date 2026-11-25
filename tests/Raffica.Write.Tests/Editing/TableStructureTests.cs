using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The table structure operations of milestone 4.5a (insert, add and delete rows and columns, merge, split, move a row, delete the table), one test per rule of the spec: the grid, spans and vertical merges agree after
/// every operation, the content is never lost, and an undo and a redo give the exact bytes.
/// </summary>
public sealed class TableStructureTests
{
    private const string Normal = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";

    private static string Cell(string text = "", string props = "") =>
        $"<w:tc>{(props.Length == 0 ? string.Empty : $"<w:tcPr>{props}</w:tcPr>")}<w:p>{(text.Length == 0 ? string.Empty : $"<w:r><w:t>{text}</w:t></w:r>")}</w:p></w:tc>";

    private static string RowWith(string props, params string[] cells) => $"<w:tr>{(props.Length == 0 ? string.Empty : $"<w:trPr>{props}</w:trPr>")}{string.Concat(cells)}</w:tr>";

    private static string Row(params string[] cells) => RowWith(string.Empty, cells);

    private static string Tbl(int columns, params string[] rows) =>
        $"<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid>{string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"2000\"/>", columns))}</w:tblGrid>{string.Concat(rows)}</w:tbl>";

    private static string Line(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Span(int n) => $"<w:gridSpan w:val=\"{n}\"/>";

    private const string Restart = "<w:vMerge w:val=\"restart\"/>";

    private const string Continue = "<w:vMerge/>";

    private static WordDocument Document(string body, bool styles = true) =>
        LayoutModel.Document(body, styles ? LayoutModel.StylesPart(Normal) : null);

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TableRef Ref(int block = 0) => new(StoryRef.Body, block);

    private static Table TableAt(WordDocument document, int block = 0) => (Table)document.Body.Blocks[block];

    private static string TextOf(TableCell cell) => string.Join("|", cell.Blocks.OfType<Paragraph>().Select(TestEdit.TextOf));

    /// <summary>The table as text: each row is its cells as "text@column+span" (a continuation is "^@column+span"), rows divided by a slash.</summary>
    private static string Shape(Table table)
    {
        var map = TableGridMap.Of(table);
        var rows = new List<string>();
        for (var r = 0; r < map.RowCount; r++)
        {
            rows.Add(string.Join(",", map.CellsOfRow(r).Select(c => (c.IsContinuation ? "^" : TextOf(c.Cell)) + $"@{c.Column}+{c.ColumnSpan}" + (c.RowSpan > 1 ? $"x{c.RowSpan}" : string.Empty))));
        }

        return string.Join("/", rows);
    }

    private static string AllText(Table table) => string.Concat(TableGridMap.Of(table).Cells.Select(c => TextOf(c.Cell)));

    private static string BodyXml(WordDocument document) => ModelSnapshot.Of(document).Elements("part").First(e => (string?)e.Attribute("name") == "document").ToString();

    private static void AssertGridEqualsCells(Table table) => TableGridChecks.AssertGridEqualsCells(table, "table");

    private static string? Attribute(PropertyBag bag, string name) =>
        (string?)bag.Get(System.Xml.Linq.XName.Get(name, LayoutModel.W))?.Attribute(System.Xml.Linq.XName.Get("val", LayoutModel.W));

    [Fact]
    public void TheColumnLimitIsTheLayoutsOwn()
    {
        Assert.Equal(Raffica.Write.Layout.TableLimits.MaximumColumns, TableRules.MaxColumns);
    }

    // ---- insert -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void InsertMakesAFullWidthEqualColumnTableWithAParagraphAfterIt()
    {
        var document = Document(Line("hello"));
        var result = Editor(document).InsertTable(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 5), 3, 2);

        Assert.NotNull(result);
        Assert.Equal(3, document.Body.Blocks.Count);
        var table = Assert.IsType<Table>(document.Body.Blocks[1]);
        Assert.IsType<Paragraph>(document.Body.Blocks[2]);
        Assert.Equal(9026, table.GridColumns.Sum());
        Assert.Equal([3009, 3009, 3008], table.GridColumns);
        Assert.Equal(2, table.Rows.Count);
        AssertGridEqualsCells(table);
        foreach (var cell in TableGridMap.Of(table).Cells)
        {
            Assert.Equal("dxa", (string?)cell.Cell.Properties.Get(System.Xml.Linq.XName.Get("tcW", LayoutModel.W))!.Attribute(System.Xml.Linq.XName.Get("type", LayoutModel.W)));
            Assert.Single(cell.Cell.Blocks);
            Assert.Empty(TestEdit.TextOf((Paragraph)cell.Cell.Blocks[0]));
        }

        Assert.Equal("fixed", (string?)table.Properties.Get(System.Xml.Linq.XName.Get("tblLayout", LayoutModel.W))!.Attribute(System.Xml.Linq.XName.Get("type", LayoutModel.W)));
        Assert.Equal("9026", (string?)table.Properties.Get(System.Xml.Linq.XName.Get("tblW", LayoutModel.W))!.Attribute(System.Xml.Linq.XName.Get("w", LayoutModel.W)));
        Assert.Equal("TableGrid", table.Properties.StyleId);
        Assert.Same(TableGridMap.Of(table).Cells[0].Cell.Blocks[0], result.Caret.Paragraph);
        Assert.Equal(0, result.Caret.Offset);
    }

    [Fact]
    public void InsertAtTheStartGoesBeforeTheParagraphAndInTheMiddleSplitsIt()
    {
        var atStart = Document(Line("hello"));
        Assert.NotNull(Editor(atStart).InsertTable(new TextPosition(StoryRef.Body, (Paragraph)atStart.Body.Blocks[0], 0), 2, 2));
        Assert.Equal(["Table", "Paragraph"], atStart.Body.Blocks.Select(b => b.GetType().Name).ToArray());
        Assert.Equal("hello", TestEdit.TextOf((Paragraph)atStart.Body.Blocks[1]));

        var middle = Document(Line("hello"));
        var editor = Editor(middle);
        Assert.NotNull(editor.InsertTable(new TextPosition(StoryRef.Body, (Paragraph)middle.Body.Blocks[0], 2), 2, 2));
        Assert.Equal(["Paragraph", "Table", "Paragraph"], middle.Body.Blocks.Select(b => b.GetType().Name).ToArray());
        Assert.Equal("he", TestEdit.TextOf((Paragraph)middle.Body.Blocks[0]));
        Assert.Equal("llo", TestEdit.TextOf((Paragraph)middle.Body.Blocks[2]));
        var after = BodyXml(middle);
        Assert.NotNull(editor.History.Undo());
        Assert.Single(middle.Body.Blocks);
        Assert.Equal("hello", TestEdit.TextOf((Paragraph)middle.Body.Blocks[0]));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, BodyXml(middle));
    }

    [Fact]
    public void InsertNeverLeavesTwoTablesTouching()
    {
        var document = Document(Tbl(1, Row(Cell("a"))) + Line("x"));
        Assert.NotNull(Editor(document).InsertTable(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[1], 0), 1, 1));
        // A table, a paragraph that keeps them apart, the new table, then the paragraph the caret was in.
        Assert.Equal(["Table", "Paragraph", "Table", "Paragraph"], document.Body.Blocks.Select(b => b.GetType().Name).ToArray());

        var tableOnly = Document(Line("p") + Tbl(1, Row(Cell("a"))) + Line("x"));
        // At the end of the paragraph before a table the new table needs a paragraph between it and the old one.
        var result = Editor(tableOnly).InsertTable(new TextPosition(StoryRef.Body, (Paragraph)tableOnly.Body.Blocks[0], 1), 1, 1);
        Assert.NotNull(result);
        Assert.Equal(["Paragraph", "Table", "Paragraph", "Table", "Paragraph"], tableOnly.Body.Blocks.Select(b => b.GetType().Name).ToArray());
    }

    [Fact]
    public void TheTableGridStyleIsACompanionOfTheStep()
    {
        var document = Document(Line("a") + Line("b"));
        var editor = Editor(document);
        Assert.Null(document.Styles!.Find("TableGrid"));

        var first = editor.InsertTable(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 1), 2, 2);
        Assert.NotNull(first!.Style);
        var style = document.Styles.Find("TableGrid")!;
        Assert.Equal("table", style.Type);
        Assert.NotNull(style.TableProperties.Get(System.Xml.Linq.XName.Get("tblBorders", LayoutModel.W)));

        var second = editor.InsertTable(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[^1], 1), 2, 2);
        Assert.NotNull(second);
        Assert.Null(second.Style);
        Assert.Equal(1, document.Styles.Styles.Count(s => s.Id == "TableGrid"));

        var bare = Document(Line("a"), styles: false);
        Assert.NotNull(Editor(bare).InsertTable(new TextPosition(StoryRef.Body, (Paragraph)bare.Body.Blocks[0], 1), 2, 1));
        var direct = (Table)bare.Body.Blocks[1];
        Assert.Null(direct.Properties.StyleId);
        Assert.NotNull(direct.Properties.Get(System.Xml.Linq.XName.Get("tblBorders", LayoutModel.W)));
    }

    [Fact]
    public void InsertTakesTheWidthOfTheSectionItLandsIn()
    {
        var landscape = Document(Line("a") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"720\" w:bottom=\"1440\" w:left=\"720\"/></w:sectPr>");
        Assert.NotNull(Editor(landscape).InsertTable(new TextPosition(StoryRef.Body, (Paragraph)landscape.Body.Blocks[0], 1), 2, 1));
        Assert.Equal(12240 - 1440, TableAt(landscape, 1).GridColumns.Sum());

        var columns = Document(Line("a") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/><w:cols w:num=\"2\" w:space=\"720\"/></w:sectPr>");
        Assert.NotNull(Editor(columns).InsertTable(new TextPosition(StoryRef.Body, (Paragraph)columns.Body.Blocks[0], 1), 2, 1));
        Assert.Equal((12240 - 2880 - 720) / 2, TableAt(columns, 1).GridColumns.Sum());
    }

    private const string Margins = "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>";

    private static int WidthOfInsertedTable(WordDocument document, TextPosition at)
    {
        Assert.NotNull(Editor(document).InsertTable(at, 2, 1));
        return FirstTable(document).GridColumns.Sum();
    }

    private static Table FirstTable(WordDocument document) =>
        document.Body.Blocks.SelectMany(b => b is ContentControlBlock c ? c.Content : [b]).OfType<Table>().First();

    [Fact]
    public void InsertAtTheEndOfASectionBreakParagraphTakesTheWidthOfTheNextSection()
    {
        var wide = "<w:sectPr><w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:pgMar w:top=\"1440\" w:right=\"720\" w:bottom=\"1440\" w:left=\"720\"/></w:sectPr>";
        string Body() => $"<w:p><w:pPr><w:sectPr>{Margins}</w:sectPr></w:pPr><w:r><w:t>a</w:t></w:r></w:p>{Line("b")}{wide}";

        var atEnd = Document(Body());
        Assert.Equal(15840 - 1440, WidthOfInsertedTable(atEnd, new TextPosition(StoryRef.Body, (Paragraph)atEnd.Body.Blocks[0], 1)));
        var atStart = Document(Body());
        Assert.Equal(12240 - 2880, WidthOfInsertedTable(atStart, new TextPosition(StoryRef.Body, (Paragraph)atStart.Body.Blocks[0], 0)));
        // In the middle the paragraph is split and the section break moves to the tail: the table is in the first section.
        var middle = Document($"<w:p><w:pPr><w:sectPr>{Margins}</w:sectPr></w:pPr><w:r><w:t>ab</w:t></w:r></w:p>{Line("b")}{wide}");
        Assert.Equal(12240 - 2880, WidthOfInsertedTable(middle, new TextPosition(StoryRef.Body, (Paragraph)middle.Body.Blocks[0], 1)));
    }

    [Fact]
    public void InsertInABlockControlTakesTheWidthOfTheSectionThatHoldsTheControl()
    {
        var wide = "<w:sectPr><w:pgSz w:w=\"15840\" w:h=\"12240\"/><w:pgMar w:top=\"1440\" w:right=\"720\" w:bottom=\"1440\" w:left=\"720\"/></w:sectPr>";
        var document = Document($"<w:p><w:pPr><w:sectPr>{Margins}</w:sectPr></w:pPr><w:r><w:t>a</w:t></w:r></w:p><w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>in</w:t></w:r></w:p></w:sdtContent></w:sdt>{wide}");
        var inside = (Paragraph)((ContentControlBlock)document.Body.Blocks[1]).Content[0];

        Assert.Equal(15840 - 1440, WidthOfInsertedTable(document, new TextPosition(StoryRef.Body, inside, 1)));
    }

    [Fact]
    public void InsertInColumnsTakesTheColumnWidthTheGutterAndTheNarrowestUnequalColumn()
    {
        string Section(string margins, string cols) => $"<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>{margins}{cols}</w:sectPr>";
        const string Plain = "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/>";
        TextPosition Caret(WordDocument d) => new(StoryRef.Body, (Paragraph)d.Body.Blocks[0], 1);

        var equal = Document(Line("a") + Section(Plain, "<w:cols w:num=\"2\" w:space=\"720\"/>"));
        Assert.Equal((12240 - 2880 - 720) / 2, WidthOfInsertedTable(equal, Caret(equal)));

        // The gutter is part of the left margin for the layout, so it is off the text width here too.
        var gutter = Document(Line("a") + Section("<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:gutter=\"360\"/>", string.Empty));
        Assert.Equal(12240 - 2880 - 360, WidthOfInsertedTable(gutter, Caret(gutter)));

        // Unequal columns: the narrowest, so the table fits whichever column the caret is in.
        var unequal = Document(Line("a") + Section(Plain, "<w:cols w:num=\"2\" w:equalWidth=\"0\" w:space=\"720\"><w:col w:w=\"5000\" w:space=\"720\"/><w:col w:w=\"3000\"/></w:cols>"));
        Assert.Equal(3000, WidthOfInsertedTable(unequal, Caret(unequal)));
        var unequalFirst = Document(Line("a") + Section(Plain, "<w:cols w:num=\"2\" w:equalWidth=\"0\" w:space=\"720\"><w:col w:w=\"2500\" w:space=\"720\"/><w:col w:w=\"6000\"/></w:cols>"));
        Assert.Equal(2500, WidthOfInsertedTable(unequalFirst, Caret(unequalFirst)));
    }

    [Fact]
    public void UndoAndRedoPutTheCaretInTheFirstCellOfTheTable()
    {
        var document = Document(Tbl(2, Row(Cell("a"), Cell("b")), Row(Cell("c"), Cell("d"))) + Line("after"));
        var editor = Editor(document);
        var first = (Paragraph)TableGridMap.Of(TableAt(document)).Cells[0].Cell.Blocks[0];

        var result = editor.AddRow(Ref(), 1, after: true);
        Assert.NotNull(result);
        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Equal("a", TestEdit.TextOf(undone.Caret.Paragraph));
        Assert.Equal(0, undone.Caret.Offset);
        Assert.Same(TableGridMap.Of(TableAt(document)).Cells[0].Cell.Blocks[0], undone.Caret.Paragraph);
        var redone = editor.History.Redo();
        Assert.Same(TableGridMap.Of(TableAt(document)).Cells[0].Cell.Blocks[0], redone!.Caret.Paragraph);
        Assert.NotNull(first);
    }

    [Fact]
    public void AGridAndCellsThatDisagreeAreRefusedAndAColumnTooNarrowToShareToo()
    {
        // Two grid columns, cells that span three; a row shorter than the grid; a continuation with no head; a one-twip column.
        var more = Document(Tbl(2, Row(Cell("a", Span(3)))));
        var shorter = Document(Tbl(2, Row(Cell("a"), Cell("b")), Row(Cell("c"))));
        var orphan = Document(Tbl(2, Row(Cell("a", Continue), Cell("b"))));
        var counts = Document("<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1\"/><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr>" + Cell("a") + Cell("b") + "</w:tr></w:tbl>");
        foreach (var document in new[] { more, shorter, orphan })
        {
            var map = TableGridMap.Of(TableAt(document));
            Assert.True(map.IsRegular);
            Assert.False(map.IsConsistent);
            Assert.Equal(TableRefusal.UnsupportedStructure, Editor(document).RefusalForAddRow(Ref(), 0, after: true));
            Assert.Equal(TableRefusal.UnsupportedStructure, Editor(document).RefusalForAddColumn(Ref(), 0, after: true));
            // The whole table can still go.
            Assert.Null(Editor(document).RefusalForDeleteTable(Ref()));
        }

        Assert.True(TableGridMap.Of(TableAt(counts)).IsConsistent);
        Assert.Equal(TableRefusal.WouldBreakTheTable, Editor(counts).RefusalForAddColumn(Ref(), 0, after: true));
        Assert.Null(Editor(counts).RefusalForAddColumn(Ref(), 1, after: true));
    }

    [Fact]
    public void InsertIsRefusedWhereATableDoesNotBelong()
    {
        var document = Document(Tbl(1, Row(Cell("in cell"))) + Line("body"));
        var editor = Editor(document);
        var cellParagraph = (Paragraph)TableGridMap.Of(TableAt(document)).Cells[0].Cell.Blocks[0];
        var body = new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[1], 0);

        Assert.Equal(TableRefusal.NestedTable, editor.RefusalForInsertTable(new TextPosition(StoryRef.Body, cellParagraph, 0), 2, 2));
        Assert.Null(editor.InsertTable(new TextPosition(StoryRef.Body, cellParagraph, 0), 2, 2));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForInsertTable(body, 2, 2, prefixDocument: true));
        Assert.Equal(TableRefusal.InvalidCount, editor.RefusalForInsertTable(body, 0, 2));
        Assert.Equal(TableRefusal.InvalidCount, editor.RefusalForInsertTable(body, 2, -1));
        Assert.Equal(TableRefusal.TooManyColumns, editor.RefusalForInsertTable(body, 64, 2));
        Assert.Equal(TableRefusal.TooManyRows, editor.RefusalForInsertTable(body, 2, 501));
        Assert.Null(editor.RefusalForInsertTable(body, TableRules.MaxColumns, TableRules.MaxRows));
        Assert.Equal(TableRefusal.HeaderOrFooter, editor.RefusalForInsertTable(new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId1"), body.Paragraph, 0), 2, 2));
        Assert.Equal(TableRefusal.Note, editor.RefusalForInsertTable(new TextPosition(new StoryRef(StoryKind.Note, "1"), body.Paragraph, 0), 2, 2));
        Assert.Equal(TableRefusal.Comment, editor.RefusalForInsertTable(new TextPosition(new StoryRef(StoryKind.Comment, "1"), body.Paragraph, 0), 2, 2));
    }

    [Fact]
    public void InsertIsRefusedInAControlThatHoldsTextOnly()
    {
        var document = Document("<w:sdt><w:sdtPr><w:text/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>one</w:t></w:r></w:p></w:sdtContent></w:sdt>" + Line("x"));
        var control = (ContentControlBlock)document.Body.Blocks[0];
        var inside = new TextPosition(StoryRef.Body, (Paragraph)control.Content[0], 1);
        var editor = Editor(document);

        Assert.Equal(TableRefusal.RestrictedControl, editor.RefusalForInsertTable(inside, 2, 2));
        Assert.Null(editor.InsertTable(inside, 2, 2));

        var rich = Document("<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>one</w:t></w:r></w:p></w:sdtContent></w:sdt>" + Line("x"));
        var richInside = new TextPosition(StoryRef.Body, (Paragraph)((ContentControlBlock)rich.Body.Blocks[0]).Content[0], 3);
        Assert.NotNull(Editor(rich).InsertTable(richInside, 2, 2));
        var content = ((ContentControlBlock)rich.Body.Blocks[0]).Content;
        Assert.Equal(["Paragraph", "Table", "Paragraph"], content.Select(b => b.GetType().Name).ToArray());
        Assert.IsType<Table>(new TableRef(StoryRef.Body, 0).Find(rich.Body.Blocks));
    }

    // ---- rows and columns ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void AddRowInsideAVerticalMergeExtendsTheMerge()
    {
        var document = Document(Tbl(2, Row(Cell("a", Restart), Cell("b")), Row(Cell("", Continue), Cell("c")), Row(Cell("", Continue), Cell("d"))));
        var editor = Editor(document);

        Assert.NotNull(editor.AddRow(Ref(), 0, after: true));
        Assert.Equal("a@0+1x4,b@1+1/^@0+1,@1+1/^@0+1,c@1+1/^@0+1,d@1+1", Shape(TableAt(document)));
        AssertGridEqualsCells(TableAt(document));

        // Before the head of the merge the new row is outside it; after its last row too.
        Assert.NotNull(editor.AddRow(Ref(), 0, after: false));
        Assert.Equal("@0+1,@1+1/a@0+1x4,b@1+1/^@0+1,@1+1/^@0+1,c@1+1/^@0+1,d@1+1", Shape(TableAt(document)));
        Assert.NotNull(editor.AddRow(Ref(), 4, after: true));
        Assert.Equal("@0+1,@1+1/a@0+1x4,b@1+1/^@0+1,@1+1/^@0+1,c@1+1/^@0+1,d@1+1/@0+1,@1+1", Shape(TableAt(document)));
        AssertGridEqualsCells(TableAt(document));
    }

    [Fact]
    public void AddRowCopiesTheRowAndKeepsItsMergesAndRefusesPastTheLimit()
    {
        var document = Document(Tbl(2, RowWith("<w:tblHeader/>", Cell("h", Span(2))), Row(Cell("a"), Cell("b"))));
        var editor = Editor(document);
        Assert.NotNull(editor.AddRow(Ref(), 0, after: true));
        var table = TableAt(document);
        Assert.Equal("h@0+2/@0+2/a@0+1,b@1+1", Shape(table));
        // The repeated-header mark does not go to the new row.
        Assert.Null(((TableRow)table.Rows[1]).Properties.Get(System.Xml.Linq.XName.Get("tblHeader", LayoutModel.W)));
        Assert.NotNull(((TableRow)table.Rows[0]).Properties.Get(System.Xml.Linq.XName.Get("tblHeader", LayoutModel.W)));
        Assert.Equal(TableRefusal.OutOfRange, editor.RefusalForAddRow(Ref(), 9, after: false));

        var full = Document(Tbl(1, Enumerable.Range(0, TableRules.MaxRows).Select(_ => Row(Cell("x"))).ToArray()));
        Assert.Equal(TableRefusal.TooManyRows, Editor(full).RefusalForAddRow(Ref(), 0, after: true));
    }

    [Fact]
    public void AddColumnInsideAColumnSpanExtendsTheSpan()
    {
        var document = Document(Tbl(3, Row(Cell("t", Span(3))), Row(Cell("a"), Cell("b"), Cell("c"))));
        var editor = Editor(document);

        Assert.NotNull(editor.AddColumn(Ref(), 1, after: true));
        var table = TableAt(document);
        Assert.Equal("t@0+4/a@0+1,b@1+1,@2+1,c@3+1", Shape(table));
        Assert.Equal(4, table.GridColumns.Count);
        Assert.Equal(6000, table.GridColumns.Sum());
        AssertGridEqualsCells(table);

        // A boundary at the edge of the span is outside it.
        Assert.NotNull(editor.AddColumn(Ref(), 3, after: true));
        Assert.Equal("t@0+4,@4+1/a@0+1,b@1+1,@2+1,c@3+1,@4+1", Shape(TableAt(document)));
        Assert.Equal(TableRefusal.OutOfRange, editor.RefusalForAddColumn(Ref(), 7, after: true));
    }

    [Fact]
    public void AddColumnSharesTheWidthOfTheColumnNextToIt()
    {
        var document = Document(Tbl(2, Row(Cell("a", "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>"), Cell("b", "<w:tcW w:w=\"2000\" w:type=\"dxa\"/>"))));
        Assert.NotNull(Editor(document).AddColumn(Ref(), 0, after: false));
        var table = TableAt(document);
        Assert.Equal([1000, 1000, 2000], table.GridColumns);
        var widths = TableGridMap.Of(table).Cells.Select(c => (string?)c.Cell.Properties.Get(System.Xml.Linq.XName.Get("tcW", LayoutModel.W))!.Attribute(System.Xml.Linq.XName.Get("w", LayoutModel.W))).ToArray();
        Assert.Equal(["1000", "1000", "2000"], widths);

        var wide = Document(Tbl(63, Row(Enumerable.Range(0, 63).Select(_ => Cell("x")).ToArray())));
        Assert.Equal(63, TableGridMap.Of(TableAt(wide)).Columns);
        Assert.Equal(TableRefusal.TooManyColumns, Editor(wide).RefusalForAddColumn(Ref(), 0, after: true));
    }

    [Fact]
    public void DeleteRowPassesTheMergeStartToTheNextRow()
    {
        var document = Document(Tbl(2, Row(Cell("a", Restart), Cell("b")), Row(Cell("", Continue), Cell("c")), Row(Cell("", Continue), Cell("d"))));
        var editor = Editor(document);

        Assert.NotNull(editor.DeleteRows(new CellRange(Ref(), 0, 0, 0, 1)));
        var table = TableAt(document);
        Assert.Equal("@0+1x2,c@1+1/^@0+1,d@1+1", Shape(table));
        Assert.Equal("restart", Attribute(TableGridMap.Of(table).Cells[0].Cell.Properties, "vMerge"));
        AssertGridEqualsCells(table);

        Assert.NotNull(editor.DeleteRows(new CellRange(Ref(), 1, 0, 1, 1)));
        // The merge is left with one row: it is no merge.
        Assert.Equal("@0+1,c@1+1", Shape(TableAt(document)));
        Assert.Null(TableGridMap.Of(TableAt(document)).Cells[0].Cell.Properties.Get(System.Xml.Linq.XName.Get("vMerge", LayoutModel.W)));
    }

    [Fact]
    public void DeleteRowOfAContinuationShrinksTheSpanAbove()
    {
        var document = Document(Tbl(2, Row(Cell("a", Restart), Cell("b")), Row(Cell("", Continue), Cell("c")), Row(Cell("", Continue), Cell("d"))));
        Assert.NotNull(Editor(document).DeleteRows(new CellRange(Ref(), 1, 0, 1, 1)));
        Assert.Equal("a@0+1x2,b@1+1/^@0+1,d@1+1", Shape(TableAt(document)));

        var every = Document(Tbl(1, Row(Cell("a")), Row(Cell("b"))) + Line("after"));
        var editor = Editor(every);
        var result = editor.DeleteRows(new CellRange(Ref(), 0, 0, 1, 0));
        Assert.NotNull(result);
        // Deleting every row deletes the table; the paragraph after it stays.
        Assert.Single(every.Body.Blocks);
        Assert.IsType<Paragraph>(every.Body.Blocks[0]);
    }

    [Fact]
    public void DeleteColumnShrinksASpanAndRemovesASingleColumnCell()
    {
        var document = Document(Tbl(3, Row(Cell("t", Span(3))), Row(Cell("ab", Span(2)), Cell("c")), Row(Cell("a"), Cell("b"), Cell("c"))));
        var editor = Editor(document);

        Assert.NotNull(editor.DeleteColumns(new CellRange(Ref(), 0, 1, 2, 1)));
        var table = TableAt(document);
        Assert.Equal("t@0+2/ab@0+1,c@1+1/a@0+1,c@1+1", Shape(table));
        Assert.Equal(2, table.GridColumns.Count);
        AssertGridEqualsCells(table);

        // The text of the removed single-column cell went with it; nothing else was lost.
        Assert.DoesNotContain("b", AllText(table).Replace("ab", string.Empty, StringComparison.Ordinal));
        Assert.Equal(TableRefusal.OutOfRange, editor.RefusalForDeleteColumns(new CellRange(Ref(), 0, 5, 0, 5)));
    }

    [Fact]
    public void DeleteColumnsOfEveryColumnDeletesTheTable()
    {
        var document = Document(Tbl(2, Row(Cell("a"), Cell("b"))) + Line("x"));
        Assert.NotNull(Editor(document).DeleteColumns(new CellRange(Ref(), 0, 0, 0, 1)));
        Assert.Single(document.Body.Blocks);
    }

    // ---- merge and split -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void MergeConcatenatesNonEmptyParagraphsInOrder()
    {
        var document = Document(Tbl(2, Row(Cell("a"), Cell("")), Row(Cell("c"), Cell("d"))));
        var editor = Editor(document);

        var result = editor.MergeCells(new CellRange(Ref(), 0, 0, 1, 1));
        Assert.NotNull(result);
        var table = TableAt(document);
        Assert.Equal("a|c|d@0+2x2/^@0+2", Shape(table));
        var map = TableGridMap.Of(table);
        Assert.Equal("restart", Attribute(map.Cells[0].Cell.Properties, "vMerge"));
        Assert.Equal("2", Attribute(map.Cells[0].Cell.Properties, "gridSpan"));
        AssertGridEqualsCells(table);

        var onlyEmpty = Document(Tbl(2, Row(Cell(""), Cell(""))));
        Assert.NotNull(Editor(onlyEmpty).MergeCells(new CellRange(Ref(), 0, 0, 0, 1)));
        Assert.Equal("@0+2", Shape(TableAt(onlyEmpty)));
        Assert.Single(TableGridMap.Of(TableAt(onlyEmpty)).Cells[0].Cell.Blocks);

        Assert.Equal(TableRefusal.NothingToMerge, editor.RefusalForMerge(new CellRange(Ref(), 0, 0, 0, 0)));
    }

    [Fact]
    public void MergeThroughAMergedCellIsWidenedToWholeMergedCells()
    {
        // Columns 0-1 merged in the first row; a range over (row 0, column 1) and (row 1, column 1) cuts through it.
        var document = Document(Tbl(3, Row(Cell("ab", Span(2)), Cell("c")), Row(Cell("d"), Cell("e"), Cell("f"))));
        Assert.NotNull(Editor(document).MergeCells(new CellRange(Ref(), 0, 1, 1, 1)));
        // Widened to the columns 0 and 1 of both rows.
        Assert.Equal("ab|d|e@0+2x2,c@2+1/^@0+2,f@2+1", Shape(TableAt(document)));
    }

    [Fact]
    public void SplitRestoresTheOriginalCellsAndKeepsContentInTheFirst()
    {
        var document = Document(Tbl(2, Row(Cell("a"), Cell("b")), Row(Cell("c"), Cell("d"))));
        var editor = Editor(document);
        var original = BodyXml(document);
        Assert.NotNull(editor.MergeCells(new CellRange(Ref(), 0, 0, 1, 1)));

        var result = editor.SplitCell(Ref(), 1, 1);
        Assert.NotNull(result);
        var table = TableAt(document);
        Assert.Equal("a|b|c|d@0+1,@1+1/@0+1,@1+1", Shape(table));
        AssertGridEqualsCells(table);
        Assert.Null(TableGridMap.Of(table).Cells[0].Cell.Properties.Get(System.Xml.Linq.XName.Get("vMerge", LayoutModel.W)));
        Assert.Null(TableGridMap.Of(table).Cells[0].Cell.Properties.Get(System.Xml.Linq.XName.Get("gridSpan", LayoutModel.W)));

        // The merge and the split are two steps; undoing both gives the original bytes.
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(original, BodyXml(document));
    }

    [Fact]
    public void SplitOfAnUnmergedCellIsRefusedWithAReason()
    {
        var document = Document(Tbl(2, Row(Cell("a"), Cell("b"))));
        var editor = Editor(document);
        var before = BodyXml(document);

        Assert.Equal(TableRefusal.NotMerged, editor.RefusalForSplit(Ref(), 0, 0));
        Assert.Null(editor.SplitCell(Ref(), 0, 0));
        Assert.Equal(before, BodyXml(document));
        Assert.False(editor.History.CanUndo);
        Assert.Equal(TableRefusal.OutOfRange, editor.RefusalForSplit(Ref(), 5, 0));
    }

    // ---- move a row ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void MoveRowUpAndDownAreOneStepAndUndo()
    {
        var document = Document(Tbl(1, Row(Cell("a")), Row(Cell("b")), Row(Cell("c"))));
        var editor = Editor(document);
        var original = BodyXml(document);

        Assert.NotNull(editor.MoveRow(Ref(), 0, 1));
        Assert.Equal("b@0+1/a@0+1/c@0+1", Shape(TableAt(document)));
        Assert.NotNull(editor.MoveRow(Ref(), 2, -2));
        Assert.Equal("c@0+1/b@0+1/a@0+1", Shape(TableAt(document)));
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(original, BodyXml(document));
        Assert.False(editor.History.CanUndo);

        Assert.Equal(TableRefusal.CannotMove, editor.RefusalForMoveRow(Ref(), 0, -1));
        Assert.Equal(TableRefusal.CannotMove, editor.RefusalForMoveRow(Ref(), 2, 1));
        Assert.Equal(TableRefusal.CannotMove, editor.RefusalForMoveRow(Ref(), 1, 0));
    }

    [Fact]
    public void MoveRowRefusesToCutAMergeOrCrossTheHeaderRows()
    {
        var document = Document(Tbl(2, RowWith("<w:tblHeader/>", Cell("h"), Cell("i")), Row(Cell("a", Restart), Cell("b")), Row(Cell("", Continue), Cell("c")), Row(Cell("d"), Cell("e"))));
        var editor = Editor(document);

        // A row of a merge, a row that would land inside one, and the border of the repeated header rows.
        Assert.Equal(TableRefusal.WouldCutAMerge, editor.RefusalForMoveRow(Ref(), 1, 1));
        Assert.Equal(TableRefusal.WouldCutAMerge, editor.RefusalForMoveRow(Ref(), 3, -1));
        var headers = Document(Tbl(1, RowWith("<w:tblHeader/>", Cell("h")), Row(Cell("a")), Row(Cell("b"))));
        Assert.Equal(TableRefusal.HeaderRowBoundary, Editor(headers).RefusalForMoveRow(Ref(), 0, 1));
        Assert.Equal(TableRefusal.HeaderRowBoundary, Editor(headers).RefusalForMoveRow(Ref(), 2, -2));
        Assert.Null(Editor(headers).RefusalForMoveRow(Ref(), 1, 1));
        Assert.Null(editor.MoveRow(Ref(), 3, -1));
        Assert.False(editor.History.CanUndo);
    }

    // ---- delete the table, nested and limits -------------------------------------------------------------------------------------------

    [Fact]
    public void ACaretInANestedTableAndAReferenceToNoTableRefuseEveryCommand()
    {
        var nested = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>inner</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = Document("<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc>" + nested + "<w:p/></w:tc></w:tr></w:tbl>" + Line("x"));
        var editor = Editor(document);
        var outerCell = TableGridMap.Of(TableAt(document)).Cells[0].Cell;
        var innerTable = (Table)outerCell.Blocks[0];
        var innerParagraph = (Paragraph)TableGridMap.Of(innerTable).Cells[0].Cell.Blocks[0];
        var outerParagraph = (Paragraph)outerCell.Blocks[1];
        var before = BodyXml(document);

        // The caret in the nested table is in no top-level table, and no table can be made there.
        Assert.Null(editor.TableOf(new TextPosition(StoryRef.Body, innerParagraph, 0)));
        Assert.Equal(TableRefusal.NestedTable, editor.RefusalForInsertTable(new TextPosition(StoryRef.Body, innerParagraph, 0), 2, 2));
        Assert.Equal(TableRefusal.NestedTable, editor.RefusalForInsertTable(new TextPosition(StoryRef.Body, outerParagraph, 0), 2, 2));
        Assert.Null(editor.InsertTable(new TextPosition(StoryRef.Body, innerParagraph, 0), 2, 2));
        // The top-level table has a cell; the paragraph after it is no table.
        Assert.Equal(new TableCellPlace(Ref(), 0, 0), editor.TableOf(new TextPosition(StoryRef.Body, outerParagraph, 0)));
        Assert.Null(editor.TableOf(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[1], 0)));
        // A reference names a place that holds no table (the paragraph), so every command refuses.
        var notATable = Ref(1);
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForAddRow(notATable, 0, true));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForAddColumn(notATable, 0, true));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForDeleteRows(new CellRange(notATable, 0, 0, 0, 0)));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForDeleteColumns(new CellRange(notATable, 0, 0, 0, 0)));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForMerge(new CellRange(notATable, 0, 0, 1, 1)));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForSplit(notATable, 0, 0));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForMoveRow(notATable, 0, 1));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForDeleteTable(notATable));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForAddRow(Ref(), 0, true, prefixDocument: true));
        Assert.Equal(TableRefusal.HeaderOrFooter, editor.RefusalForDeleteTable(new TableRef(new StoryRef(StoryKind.HeaderFooter, "rId1"), 0)));

        // The nested table is content of its cell: deleting the outer row takes it along, the merge of the outer cells would keep it.
        Assert.Equal(before, BodyXml(document));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void AMergeKeepsANestedTableOfAMergedCell()
    {
        var nested = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>inner</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/>";
        var document = Document(Tbl(2, Row("<w:tc><w:p><w:r><w:t>a</w:t></w:r></w:p></w:tc>", "<w:tc>" + nested + "</w:tc>")));
        Assert.NotNull(Editor(document).MergeCells(new CellRange(Ref(), 0, 0, 0, 1)));
        var cell = TableGridMap.Of(TableAt(document)).Cells[0].Cell;
        Assert.Equal(["Paragraph", "Table", "Paragraph"], cell.Blocks.Select(b => b.GetType().Name).ToArray());
        Assert.IsType<Paragraph>(cell.Blocks[^1]);
    }

    [Fact]
    public void TheLastTableAtTheEndOfTheBodyKeepsAParagraphAfterIt()
    {
        var document = Document(Line("a"));
        var editor = Editor(document);
        Assert.NotNull(editor.InsertTable(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 1), 2, 2));
        Assert.IsType<Paragraph>(document.Body.Blocks[^1]);

        // Deleting that table leaves the paragraph before it and the one after it.
        Assert.NotNull(editor.DeleteTable(Ref(1)));
        Assert.Equal(["Paragraph", "Paragraph"], document.Body.Blocks.Select(b => b.GetType().Name).ToArray());

        // A table that is the only block is replaced by a paragraph, so a body never ends empty or in a table.
        var only = Document(Tbl(1, Row(Cell("x"))));
        var result = Editor(only).DeleteTable(Ref());
        Assert.NotNull(result);
        Assert.Equal(["Paragraph"], only.Body.Blocks.Select(b => b.GetType().Name).ToArray());
        Assert.Same(only.Body.Blocks[0], result.Caret.Paragraph);

        var twoTables = Document(Line("a") + Tbl(1, Row(Cell("x"))) + Tbl(1, Row(Cell("y"))));
        Assert.NotNull(Editor(twoTables).DeleteTable(Ref(2)));
        Assert.Equal(["Paragraph", "Table", "Paragraph"], twoTables.Body.Blocks.Select(b => b.GetType().Name).ToArray());
    }

    [Fact]
    public void DeletingATableTakesTheNotesItReferencesWithIt()
    {
        var footnotes = ChromeTestKit.FootnotesPart(ChromeTestKit.Footnote(1, "note text"));
        var cell = "<w:tc><w:p><w:r><w:t>x</w:t></w:r>" + ChromeTestKit.FootnoteRef(1) + "</w:p></w:tc>";
        var document = ChromeTestKit.Document(Tbl(1, Row(cell)) + Line("after"), footnotes: footnotes);
        var editor = Editor(document);

        var result = editor.DeleteTable(Ref());
        Assert.NotNull(result!.Objects);
        Assert.Single(result.Objects.NotesRemoved);
        Assert.Null(document.Footnotes!.Find(1));
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(document.Footnotes.Find(1));
    }

    [Fact]
    public void ATableWithAnUnknownWrapperOrGridBeforeIsNotRestructured()
    {
        var document = Document(Tbl(2, RowWith("<w:gridBefore w:val=\"1\"/>", Cell("a"))));
        var editor = Editor(document);
        Assert.False(TableGridMap.Of(TableAt(document)).IsRegular);
        Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForAddRow(Ref(), 0, true));
        Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForMerge(new CellRange(Ref(), 0, 0, 0, 1)));
        // The whole table can still go.
        Assert.Null(editor.RefusalForDeleteTable(Ref()));
    }

    // ---- history --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void UndoAndRedoRestoreTheExactBytes()
    {
        var document = Document(Line("a") + Tbl(3, Row(Cell("1"), Cell("2"), Cell("3")), Row(Cell("4", Restart), Cell("5"), Cell("6")), Row(Cell("", Continue), Cell("7"), Cell("8"))) + Line("b"));
        var editor = Editor(document);
        var t = Ref(1);
        (string Name, Func<EditResult?> Run)[] steps =
        [
            ("move", () => editor.MoveRow(t, 0, 2)),
            ("add row", () => editor.AddRow(t, 1, after: true)),
            ("add column", () => editor.AddColumn(t, 1, after: false)),
            ("merge", () => editor.MergeCells(new CellRange(t, 0, 0, 0, 1))),
            ("split", () => editor.SplitCell(t, 0, 0)),
            ("delete column", () => editor.DeleteColumns(new CellRange(t, 0, 2, 0, 2))),
            ("delete row", () => editor.DeleteRows(new CellRange(t, 3, 0, 3, 0))),
            ("insert", () => editor.InsertTable(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 1), 2, 2)),
            ("delete table", () => editor.DeleteTable(Ref(3))),
        ];

        var snapshots = new List<string> { BodyXml(document) };
        foreach (var (name, run) in steps)
        {
            Assert.True(run() is not null, name + " was refused");
            snapshots.Add(BodyXml(document));
            Assert.NotEqual(snapshots[^2], snapshots[^1]);
        }

        for (var i = steps.Length - 1; i >= 0; i--)
        {
            Assert.NotNull(editor.History.Undo());
            Assert.Equal(snapshots[i], BodyXml(document));
        }

        for (var i = 0; i < steps.Length; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Assert.Equal(snapshots[i + 1], BodyXml(document));
        }
    }

    [Fact]
    public void ARestructureInAControlUndoesInsideTheControl()
    {
        var document = Document("<w:sdt><w:sdtPr/><w:sdtContent>" + Tbl(2, Row(Cell("a"), Cell("b"))) + "<w:p/></w:sdtContent></w:sdt>");
        var editor = Editor(document);
        var before = BodyXml(document);

        Assert.NotNull(editor.AddColumn(Ref(), 0, after: true));
        var table = (Table)((ContentControlBlock)document.Body.Blocks[0]).Content[0];
        Assert.Equal(3, TableGridMap.Of(table).Columns);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, BodyXml(document));
    }

    [Fact]
    public void ARestructureRaisesTheRevisionOnceAndAnUndoSetsItBack()
    {
        var document = Document(Tbl(2, Row(Cell("a"), Cell("b"))));
        var editor = Editor(document);
        var start = document.Edits.Revision;
        Assert.NotNull(editor.AddRow(Ref(), 0, after: true));
        Assert.Equal(start + 1, document.Edits.Revision);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, document.Edits.Revision);
    }
}
