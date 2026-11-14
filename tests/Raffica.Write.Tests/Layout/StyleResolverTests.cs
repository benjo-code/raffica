using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class StyleResolverTests
{
    private const string Defaults =
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault>"
        + "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>"
        + "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
        + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr><w:tblCellMar><w:left w:w=\"108\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>";

    private static (WordDocument Document, StyleResolver Resolver) Setup(string styles, string body)
    {
        var document = LayoutModel.Document(body, LayoutModel.StylesPart(Defaults + styles));
        return (document, new StyleResolver(document.Styles, document.Settings));
    }

    [Fact]
    public void BasedOnChainResolves()
    {
        var (doc, resolver) = Setup(
            "<w:style w:type=\"paragraph\" w:styleId=\"A\"><w:basedOn w:val=\"Normal\"/><w:pPr><w:jc w:val=\"center\"/><w:ind w:left=\"720\"/></w:pPr></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"B\"><w:basedOn w:val=\"A\"/><w:pPr><w:spacing w:before=\"240\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr></w:style>",
            "<w:p><w:pPr><w:pStyle w:val=\"B\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        var resolved = resolver.ForParagraph(p);
        Assert.Equal(ParagraphAlignment.Center, resolved.Alignment);
        Assert.Equal(36f, resolved.LeftIndent);
        Assert.Equal(12f, resolved.SpaceBefore);
        Assert.Equal(8f, resolved.SpaceAfter);
        var run = resolver.ForRun(resolved, (Run)p.Inlines[0]);
        Assert.True(run.Bold);
        Assert.Equal(14f, run.SizePoints);
        Assert.Equal("Calibri", run.FontName);
        Assert.False(run.ParagraphMarkOnly);
    }

    [Fact]
    public void CyclicBasedOnTerminates()
    {
        var (doc, resolver) = Setup(
            "<w:style w:type=\"paragraph\" w:styleId=\"X\"><w:basedOn w:val=\"Y\"/><w:pPr><w:jc w:val=\"right\"/></w:pPr></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Y\"><w:basedOn w:val=\"X\"/><w:pPr><w:ind w:right=\"200\"/></w:pPr></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Self\"><w:basedOn w:val=\"Self\"/></w:style>",
            "<w:p><w:pPr><w:pStyle w:val=\"X\"/></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val=\"Self\"/></w:pPr></w:p>");
        var first = resolver.ForParagraph(LayoutModel.Para(doc, 0));
        Assert.Equal(ParagraphAlignment.Right, first.Alignment);
        Assert.Equal(10f, first.RightIndent);
        Assert.Equal(8f, resolver.ForParagraph(LayoutModel.Para(doc, 1)).SpaceAfter);
    }

    [Fact]
    public void DirectFormattingWinsOverStyle()
    {
        var (doc, resolver) = Setup(
            "<w:style w:type=\"paragraph\" w:styleId=\"C\"><w:pPr><w:jc w:val=\"center\"/><w:spacing w:after=\"0\"/></w:pPr><w:rPr><w:sz w:val=\"20\"/><w:i/></w:rPr></w:style>",
            "<w:p><w:pPr><w:pStyle w:val=\"C\"/><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:sz w:val=\"40\"/><w:i w:val=\"0\"/><w:color w:val=\"FF0000\"/><w:u w:val=\"single\"/><w:vertAlign w:val=\"superscript\"/></w:rPr><w:t>x</w:t></w:r>"
            + "<w:r><w:t>y</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        var resolved = resolver.ForParagraph(p);
        Assert.Equal(ParagraphAlignment.Right, resolved.Alignment);
        Assert.Equal(0f, resolved.SpaceAfter);
        var run = resolver.ForRun(resolved, (Run)p.Inlines[0]);
        Assert.Equal(20f, run.SizePoints);
        Assert.False(run.Italic);
        Assert.Equal(0xFFFF0000u, run.Color);
        Assert.Equal(UnderlineKind.Solid, run.Underline);
        Assert.Equal(RunVerticalAlignment.Superscript, run.VerticalAlignment);
        var plain = resolver.ForRun(resolved, (Run)p.Inlines[1]);
        Assert.Equal(10f, plain.SizePoints);
        Assert.True(plain.Italic);
    }

    [Fact]
    public void ToggleBoldTwiceIsOff()
    {
        var (doc, resolver) = Setup(
            "<w:style w:type=\"paragraph\" w:styleId=\"Strong1\"><w:rPr><w:b/></w:rPr></w:style>"
            + "<w:style w:type=\"character\" w:styleId=\"BoldChar\"><w:rPr><w:b/></w:rPr></w:style>",
            "<w:p><w:pPr><w:pStyle w:val=\"Strong1\"/></w:pPr>"
            + "<w:r><w:rPr><w:rStyle w:val=\"BoldChar\"/></w:rPr><w:t>twice</w:t></w:r>"
            + "<w:r><w:t>once</w:t></w:r>"
            + "<w:r><w:rPr><w:rStyle w:val=\"BoldChar\"/><w:b/></w:rPr><w:t>direct</w:t></w:r>"
            + "<w:r><w:rPr><w:b w:val=\"0\"/></w:rPr><w:t>off</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        var resolved = resolver.ForParagraph(p);
        Assert.False(resolver.ForRun(resolved, (Run)p.Inlines[0]).Bold);
        Assert.True(resolver.ForRun(resolved, (Run)p.Inlines[1]).Bold);
        Assert.True(resolver.ForRun(resolved, (Run)p.Inlines[2]).Bold);
        Assert.False(resolver.ForRun(resolved, (Run)p.Inlines[3]).Bold);
    }

    [Fact]
    public void DocDefaultsApplyToUnstyledParagraphs()
    {
        var (doc, resolver) = Setup(string.Empty, "<w:p><w:r><w:t>x</w:t></w:r></w:p>");
        var p = LayoutModel.Para(doc, 0);
        var resolved = resolver.ForParagraph(p);
        Assert.Equal(8f, resolved.SpaceAfter);
        Assert.Equal(LineSpacingRule.Auto, resolved.LineRule);
        Assert.Equal(259f / 240f, resolved.LineValue, 3);
        Assert.True(resolved.WidowControl);
        var run = resolver.ForRun(resolved, (Run)p.Inlines[0]);
        Assert.Equal("Calibri", run.FontName);
        Assert.Equal(11f, run.SizePoints);
        Assert.Equal("en-US", run.Language);
        Assert.False(run.Bold);
        Assert.Null(run.Color);
    }

    [Fact]
    public void MissingStyleIdFallsBackToDefault()
    {
        var (doc, resolver) = Setup(string.Empty, "<w:p><w:pPr><w:pStyle w:val=\"Nope\"/></w:pPr></w:p><w:p/>");
        var missing = resolver.ForParagraph(LayoutModel.Para(doc, 0));
        var plain = resolver.ForParagraph(LayoutModel.Para(doc, 1));
        Assert.Equal(plain.SpaceAfter, missing.SpaceAfter);
        Assert.Equal(plain.LineValue, missing.LineValue);

        var bare = new StyleResolver(null, null).ForParagraph(LayoutModel.Para(doc, 0));
        Assert.Equal(0f, bare.SpaceAfter);
        Assert.Equal(ParagraphAlignment.Left, bare.Alignment);
    }

    [Fact]
    public void CachesOneParagraphPerStyle()
    {
        var (doc, resolver) = Setup(
            "<w:style w:type=\"paragraph\" w:styleId=\"A\"><w:pPr><w:jc w:val=\"center\"/></w:pPr></w:style>",
            "<w:p><w:pPr><w:pStyle w:val=\"A\"/></w:pPr></w:p><w:p><w:pPr><w:pStyle w:val=\"A\"/></w:pPr></w:p>");
        Assert.Same(resolver.ForParagraph(LayoutModel.Para(doc, 0)), resolver.ForParagraph(LayoutModel.Para(doc, 1)));
    }

    private const string GridStyle =
        "<w:style w:type=\"table\" w:styleId=\"Grid\"><w:basedOn w:val=\"TableNormal\"/><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"8\" w:color=\"112233\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"AAAAAA\"/></w:tblBorders></w:tblPr>"
        + "<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/></w:rPr><w:tcPr><w:shd w:val=\"clear\" w:fill=\"4472C4\"/></w:tcPr></w:tblStylePr>"
        + "<w:tblStylePr w:type=\"band1Horz\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"D9E2F3\"/></w:tcPr></w:tblStylePr></w:style>";

    private static string Rows(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => "<w:tr><w:tc><w:p><w:r><w:t>c" + i + "</w:t></w:r></w:p></w:tc></w:tr>"));

    private static string TableBody(string look) =>
        "<w:tbl><w:tblPr><w:tblStyle w:val=\"Grid\"/>" + look + "</w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid>" + Rows(4) + "</w:tbl>";

    [Fact]
    public void TableStyleFirstRowAndBands()
    {
        var (doc, resolver) = Setup(GridStyle, TableBody("<w:tblLook w:firstRow=\"1\" w:noHBand=\"0\" w:noVBand=\"1\"/>"));
        var table = (Table)doc.Body.Blocks[0];
        var resolved = resolver.ForTable(table);
        Assert.Equal(0xFF4472C4u, resolved.CellFormat(0, 0, 4, 1).Fill);
        Assert.Equal(0xFFD9E2F3u, resolved.CellFormat(1, 0, 4, 1).Fill);
        Assert.Null(resolved.CellFormat(2, 0, 4, 1).Fill);
        Assert.Equal(0xFFD9E2F3u, resolved.CellFormat(3, 0, 4, 1).Fill);
        Assert.Equal(5.4f, resolved.CellMarginLeft);
        Assert.Equal(0f, resolved.CellMarginTop);

        var header = resolver.ForParagraph(new Paragraph(), resolved.CellFormat(0, 0, 4, 1));
        Assert.True(resolver.ForRun(header, new Run()).Bold);
        var body = resolver.ForParagraph(new Paragraph(), resolved.CellFormat(2, 0, 4, 1));
        Assert.False(resolver.ForRun(body, new Run()).Bold);

        var top = resolved.CellFormat(0, 0, 4, 1).Top;
        Assert.NotNull(top);
        Assert.Equal(1f, top.WidthPoints);
        Assert.Equal(0xFF112233u, top.Color);
        Assert.Equal(0.5f, resolved.CellFormat(2, 0, 4, 1).Top!.WidthPoints);
    }

    [Fact]
    public void TableLookSwitchesConditionsOff()
    {
        var (doc, resolver) = Setup(GridStyle, TableBody("<w:tblLook w:firstRow=\"0\" w:noHBand=\"1\"/>"));
        var resolved = resolver.ForTable((Table)doc.Body.Blocks[0]);
        for (var row = 0; row < 4; row++)
        {
            Assert.Null(resolved.CellFormat(row, 0, 4, 1).Fill);
        }

        var (doc2, resolver2) = Setup(GridStyle, TableBody("<w:tblLook w:val=\"0620\"/>"));
        var resolved2 = resolver2.ForTable((Table)doc2.Body.Blocks[0]);
        Assert.Equal(0xFF4472C4u, resolved2.CellFormat(0, 0, 4, 1).Fill);
        Assert.Null(resolved2.CellFormat(1, 0, 4, 1).Fill);
    }

    [Fact]
    public void UnknownTableStyleResolvesToDefaults()
    {
        var (doc, resolver) = Setup(string.Empty, "<w:tbl><w:tblPr><w:tblStyle w:val=\"Nope\"/></w:tblPr><w:tblGrid/></w:tbl>");
        var resolved = resolver.ForTable((Table)doc.Body.Blocks[0]);
        Assert.Equal(5.4f, resolved.CellMarginRight);
        Assert.Null(resolved.CellFormat(0, 0, 1, 1).Fill);
    }
}
