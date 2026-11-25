using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The two definitions of a merged region, the model's (<see cref="TableGridMap"/>) and the layout's (<c>TableReader</c> through the object map's <see cref="CellRect"/>), pinned against each other on real tables, the
/// Word-style odd ones included (a continuation with another span than its head, an orphan continuation, a merge in both directions).
/// </summary>
public sealed class TableGridLayoutTests
{
    private const string Restart = "<w:vMerge w:val=\"restart\"/>";
    private const string Continue = "<w:vMerge/>";

    private static string Tc(string text, string props = "") =>
        $"<w:tc>{(props.Length == 0 ? string.Empty : $"<w:tcPr>{props}</w:tcPr>")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Tr(params string[] cells) => $"<w:tr>{string.Concat(cells)}</w:tr>";

    private static string Table(int columns, params string[] rows) =>
        $"<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid>{string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"2000\"/>", columns))}</w:tblGrid>{string.Concat(rows)}</w:tbl>";

    private static string Span(int n) => $"<w:gridSpan w:val=\"{n}\"/>";

    // Every row holds a plain cell with text, so every row has a height of its own and a rectangle covers exactly the rows of its merge.
    public static TheoryData<string, string> Cases() => new()
    {
        { "plain", Table(2, Tr(Tc("a"), Tc("b")), Tr(Tc("c"), Tc("d"))) },
        { "vertical", Table(3, Tr(Tc("a", Restart), Tc("b"), Tc("c")), Tr(Tc(string.Empty, Continue), Tc("d"), Tc("e")), Tr(Tc(string.Empty, Continue), Tc("f"), Tc("g"))) },
        { "span", Table(3, Tr(Tc("a", Span(2)), Tc("b")), Tr(Tc("c"), Tc("d"), Tc("e"))) },
        { "both", Table(3, Tr(Tc("a", Span(2) + Restart), Tc("b")), Tr(Tc(string.Empty, Span(2) + Continue), Tc("c")), Tr(Tc("d"), Tc("e"), Tc("f"))) },
        { "continuation of another span", Table(3, Tr(Tc("a", Restart), Tc("b"), Tc("c")), Tr(Tc("x", Span(2) + Continue), Tc("d")), Tr(Tc("e"), Tc("f"), Tc("g"))) },
        { "orphan continuation", Table(2, Tr(Tc("a", Continue), Tc("b")), Tr(Tc("c"), Tc("d")), Tr(Tc("e", Continue), Tc("f"))) },
        { "continuation after a plain row", Table(2, Tr(Tc("a", Restart), Tc("b")), Tr(Tc("c"), Tc("d")), Tr(Tc(string.Empty, Continue), Tc("e"))) },
        { "two merges side by side", Table(3, Tr(Tc("a", Restart), Tc("b", Restart), Tc("c")), Tr(Tc(string.Empty, Continue), Tc(string.Empty, Continue), Tc("d")), Tr(Tc("e"), Tc("f"), Tc("g"))) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheLayoutDrawsTheCellsOfTheModelGrid(string name, string table)
    {
        var model = LayoutModel.Document(table);
        var layout = await SectionOracleTests.LaidAsync(model);
        var map = TableGridMap.Of((Table)model.Body.Blocks[0]);
        var grid = Assert.Single(layout.Page(0).Objects.Tables);

        Assert.Equal(map.Columns + 1, grid.ColumnBorders.Count);
        Assert.Equal(map.RowIndexes, grid.RowIndexes);
        var heads = map.Cells.Where(c => !c.IsContinuation).ToList();
        Assert.True(heads.Count == grid.Cells.Count, $"{name}: the model has {heads.Count} cells that show, the layout draws {grid.Cells.Count}");
        foreach (var head in heads)
        {
            var rect = grid.Cells.SingleOrDefault(r => r.Row == head.RowIndex && r.Column == head.Column);
            Assert.True(rect is not null, $"{name}: the layout draws no cell at row {head.RowIndex}, column {head.Column}");
            Assert.True(rect.ColumnSpan == head.ColumnSpan, $"{name}: column span at row {head.RowIndex}, column {head.Column}: layout {rect.ColumnSpan}, model {head.ColumnSpan}");
            Assert.True(rect.RowSpan == head.RowSpan, $"{name}: row span at row {head.RowIndex}, column {head.Column}: layout {rect.RowSpan}, model {head.RowSpan}");
        }
    }
}
