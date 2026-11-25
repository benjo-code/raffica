using System.Diagnostics;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// What the ribbon asks of the editor about a big table each time it refreshes the table commands (milestone 4.5a, the facts of <c>TableFactsSource</c>): one refusal check per command of the Table tab, on a table of 500 rows.
/// The checks work on a copy of the table, so the cost grows with the table and with the number of commands.
/// </summary>
public sealed class TableFactsCostTests
{
    private static string Row(int r) => $"<w:tr><w:tc><w:p><w:r><w:t>a{r}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>b{r}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>c{r}</w:t></w:r></w:p></w:tc></w:tr>";

    private static double MedianMilliseconds(Action refresh, int rounds)
    {
        var times = new List<double>();
        for (var i = 0; i < rounds + 3; i++)
        {
            var start = Stopwatch.GetTimestamp();
            refresh();
            if (i >= 3)
            {
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        }

        times.Sort();
        return times[times.Count / 2];
    }

    [Fact]
    public void TheFactsOfTheTableTabOnATableOf500RowsStayWithinABound()
    {
        var table = "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" + string.Concat(Enumerable.Range(0, 500).Select(Row)) + "</w:tbl>";
        var document = LayoutModel.Document(table + "<w:p><w:r><w:t>after</w:t></w:r></w:p>");
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var reference = new TableRef(StoryRef.Body, 0);
        var middle = new CellRange(reference, 250, 1, 250, 1);
        var two = new CellRange(reference, 250, 0, 250, 1);

        // The question of every command of the Table tab, as TableFactsSource.Evaluate asks it at a caret in the middle of the table.
        void Refresh()
        {
            _ = editor.RefusalForAddRow(reference, 250, after: false);
            _ = editor.RefusalForAddRow(reference, 250, after: true);
            _ = editor.RefusalForAddColumn(reference, 1, after: false);
            _ = editor.RefusalForAddColumn(reference, 1, after: true);
            _ = editor.RefusalForDeleteRows(middle);
            _ = editor.RefusalForDeleteColumns(middle);
            _ = editor.RefusalForDeleteTable(reference);
            _ = editor.RefusalForMerge(two);
            _ = editor.RefusalForSplit(reference, 250, 1);
            _ = editor.RefusalForSetBorders(middle, BorderSpec.All());
            _ = editor.RefusalForSetShading(middle, null);
            _ = editor.RefusalForSetHeaderRow(reference, true);
            _ = editor.RefusalForSetTableProperties(reference, new TableProperties(HeaderRow: true));
            _ = editor.RefusalForDistributeColumns(reference);
            _ = editor.RefusalForMoveRow(reference, 250, -1);
            _ = editor.RefusalForMoveRow(reference, 250, 1);
        }

        var median = MedianMilliseconds(Refresh, 15);
        // The number is recorded in the decision (about 42 ms for the 16 checks on the Windows PC, 2.6 ms for each: one clone of the table and two grid maps per check). The bound only catches a check that has gone quadratic.
        TestContext.Current.SendDiagnosticMessage($"Table facts refresh on 500 rows: median {median:F2} ms");
        Assert.True(median < 2000, $"The facts of the Table tab took {median:F1} ms on 500 rows.");
    }
}
