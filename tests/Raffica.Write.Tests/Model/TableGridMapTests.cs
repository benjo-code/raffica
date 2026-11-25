using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Model;

/// <summary>The grid view of a table: which cell covers which column of which row, merged regions, and the tables it does not follow.</summary>
public sealed class TableGridMapTests
{
    private static string Cell(string text, string props = "") =>
        $"<w:tc>{(props.Length == 0 ? string.Empty : $"<w:tcPr>{props}</w:tcPr>")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Row(params string[] cells) => $"<w:tr>{string.Concat(cells)}</w:tr>";

    private static Table Table(int columns, params string[] rows)
    {
        var grid = string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"1000\"/>", columns));
        var document = LayoutModel.Document($"<w:tbl><w:tblPr/><w:tblGrid>{grid}</w:tblGrid>{string.Concat(rows)}</w:tbl>");
        return (Table)document.Body.Blocks[0];
    }

    private static string Name(GridCell cell) => ((Paragraph)cell.Cell.Blocks[0]).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;

    [Fact]
    public void APlainTableHasOneCellPerColumnAndRow()
    {
        var map = TableGridMap.Of(Table(2, Row(Cell("a"), Cell("b")), Row(Cell("c"), Cell("d"))));

        Assert.Equal(2, map.Columns);
        Assert.Equal(2, map.RowCount);
        Assert.True(map.IsRegular);
        Assert.Equal(["a", "b", "c", "d"], map.Cells.Select(Name).ToArray());
        Assert.Equal("d", Name(map.At(1, 1)!));
        Assert.Null(map.At(1, 2));
        Assert.Null(map.At(5, 0));
        Assert.All(map.Cells, c => Assert.False(c.IsContinuation));
    }

    [Fact]
    public void AGridSpanCoversSeveralColumnsAndTheGridIsWidenedToTheWidestRow()
    {
        var map = TableGridMap.Of(Table(2, Row(Cell("a", "<w:gridSpan w:val=\"3\"/>")), Row(Cell("b"), Cell("c"), Cell("d"))));

        Assert.Equal(3, map.Columns);
        var wide = map.At(0, 2)!;
        Assert.Equal("a", Name(wide));
        Assert.Equal(0, wide.Column);
        Assert.Equal(3, wide.ColumnSpan);
        Assert.Equal(2, wide.LastColumn);
        Assert.Same(wide, map.At(0, 0));
    }

    [Fact]
    public void AVerticalMergeHasOneHeadAndContinuationsThatJoinIt()
    {
        var map = TableGridMap.Of(Table(
            2,
            Row(Cell("a", "<w:vMerge w:val=\"restart\"/>"), Cell("b")),
            Row(Cell("", "<w:vMerge/>"), Cell("c")),
            Row(Cell("", "<w:vMerge w:val=\"continue\"/>"), Cell("d"))));

        var head = map.At(0, 0)!;
        Assert.Equal(3, head.RowSpan);
        Assert.False(head.IsContinuation);
        Assert.True(map.At(1, 0)!.IsContinuation);
        Assert.Same(head, map.At(2, 0)!.Head);
        Assert.True(map.MergeCrosses(0));
        Assert.True(map.MergeCrosses(1));
        Assert.False(map.MergeCrosses(2));
    }

    [Fact]
    public void AContinuationOfADifferentSpanDoesNotJoinAndAMergeEndsAtARowThatDoesNotContinueIt()
    {
        var map = TableGridMap.Of(Table(
            2,
            Row(Cell("a", "<w:vMerge w:val=\"restart\"/>"), Cell("b")),
            Row(Cell("", "<w:vMerge/><w:gridSpan w:val=\"2\"/>")),
            Row(Cell("c"), Cell("d")),
            Row(Cell("", "<w:vMerge/>"), Cell("e"))));

        // The wider continuation heads a group of its own; the last row's continuation has nothing above it that continues, so it heads one too.
        Assert.Equal(1, map.At(0, 0)!.RowSpan);
        Assert.False(map.At(1, 0)!.IsContinuation);
        Assert.False(map.At(3, 0)!.IsContinuation);
    }

    [Fact]
    public void RowIndexesAreIndexesOfTheTableRowsWhateverSitsBetweenThem()
    {
        var document = LayoutModel.Document("<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:bookmarkStart w:id=\"1\" w:name=\"x\"/>" + Row(Cell("a")) + "<w:bookmarkEnd w:id=\"1\"/>" + Row(Cell("b")) + "</w:tbl>");
        var table = (Table)document.Body.Blocks[0];
        var map = TableGridMap.Of(table);

        Assert.True(map.IsRegular);
        Assert.Equal([1, 3], map.RowIndexes);
        Assert.Equal("b", Name(map.At(3, 0)!));
        Assert.Equal(1, map.OrdinalOf(3));
        Assert.Equal(-1, map.OrdinalOf(2));
    }

    [Fact]
    public void ATableItDoesNotFollowIsNotRegular()
    {
        Assert.False(TableGridMap.Of(Table(1, "<w:tr><w:trPr><w:gridBefore w:val=\"1\"/></w:trPr>" + Cell("a") + "</w:tr>")).IsRegular);
        Assert.False(TableGridMap.Of(Table(1, "<w:tr><w:trPr><w:gridAfter w:val=\"1\"/></w:trPr>" + Cell("a") + "</w:tr>")).IsRegular);
        Assert.False(TableGridMap.Of(Table(1, "<w:tr><w:trPr><w:del w:id=\"1\" w:author=\"x\"/></w:trPr>" + Cell("a") + "</w:tr>")).IsRegular);
        Assert.False(TableGridMap.Of(Table(1, "<w:sdt><w:sdtPr/><w:sdtContent>" + Row(Cell("a")) + "</w:sdtContent></w:sdt>")).IsRegular);
        Assert.False(TableGridMap.Of(Table(1, "<w:tr><w:sdt><w:sdtPr/><w:sdtContent>" + Cell("a") + "</w:sdtContent></w:sdt></w:tr>")).IsRegular);
    }

    [Fact]
    public void TheGridIsCappedAtSixtyThreeColumns()
    {
        var map = TableGridMap.Of(Table(70, Row(Enumerable.Range(0, 70).Select(i => Cell("x" + i)).ToArray())));

        Assert.Equal(63, map.Columns);
        Assert.Equal(62, map.At(0, 62)!.Column);
    }
}
