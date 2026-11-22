using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>What <see cref="DrawnLabels"/> reads from pages: a repeated table header row is the one case of a paragraph drawn twice that it collapses.</summary>
public sealed class DrawnLabelsTests
{
    private const string Numbering =
        "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum>"
        + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";

    private static string Numbered(string text) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    // A table whose first row is a header row holding a numbered paragraph, then enough one-line rows for the table to run over a page break.
    private static string Table(bool header, int rows) =>
        "<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"5000\"/></w:tblGrid>"
        + $"<w:tr><w:trPr>{(header ? "<w:tblHeader/>" : string.Empty)}</w:trPr><w:tc><w:tcPr><w:tcW w:w=\"5000\" w:type=\"dxa\"/></w:tcPr>{Numbered("Header")}</w:tc></w:tr>"
        + string.Concat(Enumerable.Range(0, rows).Select(i => $"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"5000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>row {i}</w:t></w:r></w:p></w:tc></w:tr>"))
        + "</w:tbl><w:p><w:r><w:t>after</w:t></w:r></w:p>";

    private static async Task<(DocumentLayout Layout, WordDocument Model)> LaidAsync(string body)
    {
        var model = LayoutModel.Document(body, LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>"), LayoutModel.NumberingPart(Numbering));
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, TestContext.Current.CancellationToken);
        Assert.True(layout.IsComplete);
        return (layout, model);
    }

    private static int LabelLinesOf(DocumentLayout layout, Paragraph paragraph) =>
        Enumerable.Range(0, layout.LaidOutPages).Sum(i => layout.Page(i).TextLines.Count(l => ReferenceEquals(l.Paragraph, paragraph) && l.Runs.Any(r => r.IsLabel)));

    [Fact]
    public async Task ARepeatedHeaderRowDrawsItsNumberedParagraphAgainAndTheReaderCountsItOnce()
    {
        var (layout, model) = await LaidAsync(Table(header: true, rows: 120));
        var heading = ReferenceNumbering.Labels(model).Single().Paragraph;

        // The paragraph is drawn on every page the table covers, each time with its label ...
        Assert.True(layout.LaidOutPages >= 2, "the table does not run over a page break");
        Assert.True(LabelLinesOf(layout, heading) >= 2, "the header row was not drawn again");

        // ... the reader finds the same label each time, counts the paragraph once and says so.
        var tally = new LabelTally();
        var drawn = DrawnLabels.Read(layout, "header", model, tally, tolerateUnreadablePages: false);
        Assert.Equal([("1.", heading)], drawn.Select(d => (d.Label, d.Paragraph)));
        Assert.True(tally.RepeatedHeaderLabels >= 1);
        Assert.Equal(LabelLinesOf(layout, heading) - 1, tally.RepeatedHeaderLabels);
        DrawnLabels.AssertEqualToReference(layout, model, "header");
    }

    [Fact]
    public async Task WithoutAHeaderRowAParagraphIsNotDrawnTwiceAndNothingIsCollapsed()
    {
        var (layout, model) = await LaidAsync(Table(header: false, rows: 120));
        var heading = ReferenceNumbering.Labels(model).Single().Paragraph;
        Assert.True(layout.LaidOutPages >= 2);
        Assert.Equal(1, LabelLinesOf(layout, heading));

        var tally = new LabelTally();
        Assert.Single(DrawnLabels.Read(layout, "no header", model, tally, tolerateUnreadablePages: false));
        Assert.Equal(0, tally.RepeatedHeaderLabels);
    }
}
