using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The Table properties card's single history step (<see cref="TextEditor.SetTableProperties"/>), what the card reads of a table (<see cref="TextEditor.ReadTableValues"/>) and the Tab walk between cells
/// (<see cref="TextEditor.MoveToCell"/>) of milestone 4.5a (Task 8).
/// </summary>
public sealed class TablePropertiesTests
{
    private static readonly XNamespace W = LayoutModel.W;

    private const string Normal = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";

    private static string Tc(string text, string props = "") =>
        $"<w:tc>{(props.Length == 0 ? string.Empty : $"<w:tcPr>{props}</w:tcPr>")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Tr(string props, params string[] cells) => $"<w:tr>{(props.Length == 0 ? string.Empty : $"<w:trPr>{props}</w:trPr>")}{string.Concat(cells)}</w:tr>";

    private static string Dxa(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"dxa\"/>";

    private static string Table3(string rowProps = "") =>
        "<w:tbl><w:tblPr><w:tblW w:w=\"6000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
        string.Concat(Enumerable.Range(0, 3).Select(r => Tr(r == 0 ? rowProps : string.Empty, Tc($"{r}0", Dxa(2000)), Tc($"{r}1", Dxa(2000)), Tc($"{r}2", Dxa(2000))))) +
        "</w:tbl><w:p><w:r><w:t>after</w:t></w:r></w:p>";

    private static WordDocument Document(string body) => LayoutModel.Document(body, LayoutModel.StylesPart(Normal));

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TableRef Ref() => new(StoryRef.Body, 0);

    private static Table TableAt(WordDocument document) => (Table)document.Body.Blocks[0];

    private static string BodyXml(WordDocument document) => ModelSnapshot.Of(document).Elements("part").First(e => (string?)e.Attribute("name") == "document").ToString();

    private static TextPosition Start(WordDocument document, int row, int column)
    {
        var cell = TableGridMap.Of(TableAt(document)).At(row, column)!.Cell;
        return new TextPosition(StoryRef.Body, cell.Blocks.OfType<Paragraph>().First(), 0);
    }

    [Fact]
    public void WidthsRowHeightAndTheHeaderRowAreOneHistoryStepAndOneUndo()
    {
        var document = Document(Table3());
        var editor = Editor(document);
        var before = BodyXml(document);

        var result = editor.SetTableProperties(Ref(), new TableProperties(ColumnTwips: [1000, 2000, 3000], RowHeight: new TableRowHeight(1, 2, 600, true), HeaderRow: true));

        Assert.NotNull(result);
        var table = TableAt(document);
        Assert.Equal([1000, 2000, 3000], table.GridColumns.ToArray());
        var rows = table.Rows.OfType<TableRow>().ToList();
        Assert.Equal("exact", (string?)rows[1].Properties.Get(W + "trHeight")!.Attribute(W + "hRule"));
        Assert.Equal("600", (string?)rows[2].Properties.Get(W + "trHeight")!.Attribute(W + "val"));
        Assert.Null(rows[0].Properties.Get(W + "trHeight"));
        Assert.NotNull(rows[0].Properties.Get(W + "tblHeader"));

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, BodyXml(document));
        Assert.False(editor.History.CanUndo);
        Assert.NotNull(editor.History.Redo());
        Assert.Equal([1000, 2000, 3000], TableAt(document).GridColumns.ToArray());
    }

    [Fact]
    public void TheTableWidthAloneKeepsTheProportionsAndAnEmptyRequestOrABadPartRefusesEverything()
    {
        var document = Document(Table3());
        var editor = Editor(document);
        var before = BodyXml(document);

        Assert.NotNull(editor.SetTableProperties(Ref(), new TableProperties(TableWidthTwips: 3000)));
        Assert.Equal([1000, 1000, 1000], TableAt(document).GridColumns.ToArray());
        Assert.Equal(3000, int.Parse((string)TableAt(document).Properties.Get(W + "tblW")!.Attribute(W + "w")!, System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(editor.History.Undo() is not null);
        Assert.Equal(before, BodyXml(document));

        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetTableProperties(Ref(), new TableProperties()));
        Assert.Null(editor.SetTableProperties(Ref(), new TableProperties()));

        // A part that is refused refuses the whole: the valid header row is not written either.
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetTableProperties(Ref(), new TableProperties(ColumnTwips: [10, 2000, 2000], HeaderRow: true)));
        Assert.Null(editor.SetTableProperties(Ref(), new TableProperties(ColumnTwips: [10, 2000, 2000], HeaderRow: true)));
        Assert.Equal(before, BodyXml(document));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForSetTableProperties(Ref(), new TableProperties(HeaderRow: true), prefixDocument: true));
    }

    [Fact]
    public void TheCardReadsTheModelsOwnNumbers()
    {
        var document = Document(Table3("<w:tblHeader/><w:trHeight w:val=\"500\" w:hRule=\"exact\"/>"));
        var editor = Editor(document);

        var values = editor.ReadTableValues(Ref(), 0)!;
        Assert.Equal([2000, 2000, 2000], values.GridTwips.ToArray());
        Assert.Equal(6000, values.TableWidthTwips);
        Assert.True(values.HeaderRow);
        Assert.Equal((500, true), (values.RowHeightTwips, values.RowHeightExact));

        var other = editor.ReadTableValues(Ref(), 1)!;
        Assert.Equal((0, false), (other.RowHeightTwips, other.RowHeightExact));
        Assert.Null(editor.ReadTableValues(new TableRef(StoryRef.Body, 1), 0));
    }

    [Fact]
    public void TabWalksTheCellsInReadingOrderAndTheLastCellAddsARowInOneStep()
    {
        var document = Document(Table3());
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var forward = editor.MoveToCell(Start(document, 0, 2), +1);
        Assert.NotNull(forward);
        Assert.Empty(forward.Changes);
        Assert.Equal(Start(document, 1, 0).Paragraph, forward.Caret.Paragraph);
        Assert.Equal(revision, document.Edits.Revision);

        var back = editor.MoveToCell(Start(document, 1, 0), -1);
        Assert.Equal(Start(document, 0, 2).Paragraph, back!.Caret.Paragraph);
        Assert.Null(editor.MoveToCell(Start(document, 0, 0), -1));

        var added = editor.MoveToCell(Start(document, 2, 2), +1);
        Assert.NotNull(added);
        Assert.Single(added.Changes);
        Assert.Equal(4, TableGridMap.Of(TableAt(document)).RowCount);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(3, TableGridMap.Of(TableAt(document)).RowCount);
    }

    [Fact]
    public void TabSkipsAMergedContinuationAndIsNothingOutsideATable()
    {
        // Column 0 of the first two rows is one merged cell: Tab from the cell before it goes to the head, and from the head to the next real cell.
        var merged =
            "<w:tbl><w:tblPr><w:tblW w:w=\"4000\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
            Tr(string.Empty, Tc("a", "<w:vMerge w:val=\"restart\"/>"), Tc("b")) +
            Tr(string.Empty, Tc(string.Empty, "<w:vMerge/>"), Tc("c")) +
            "</w:tbl><w:p><w:r><w:t>after</w:t></w:r></w:p>";
        var document = Document(merged);
        var editor = Editor(document);

        var fromB = editor.MoveToCell(Start(document, 0, 1), +1);
        Assert.Equal(Start(document, 1, 1).Paragraph, fromB!.Caret.Paragraph);

        var outside = new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[1], 0);
        Assert.Null(editor.MoveToCell(outside, +1));
    }
}
