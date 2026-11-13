using System.Text;
using System.Xml.Linq;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class BlockXmlTests
{
    private static readonly XNamespace W = XmlSnippets.W;
    private static readonly XNamespace Test = "urn:test";

    private static (Story Story, XElement Written) RoundTrip(string xml)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var story = DocumentXml.Read(input);
        using var output = new MemoryStream();
        DocumentXml.Write(story, output);
        var text = Encoding.UTF8.GetString(output.ToArray());
        var difference = XmlEquivalence.Explain(XElement.Parse(xml), XElement.Parse(text));
        Assert.True(difference is null, difference + "\n" + text);
        return (story, XElement.Parse(text));
    }

    [Fact]
    public void TablesKeepGridRowsCellsAndMerges()
    {
        var (story, _) = RoundTrip(XmlSnippets.Document(
            """
            <w:tbl><w:tblPr><w:tblStyle w:val="Grid"/><w:tblW w:w="0" w:type="auto"/></w:tblPr>
            <w:tblGrid><w:gridCol w:w="2000"/><w:gridCol w:w="3000"/><w:gridCol w:w="4000"/></w:tblGrid>
            <w:tr><w:trPr><w:tblHeader/></w:trPr>
              <w:tc><w:tcPr><w:gridSpan w:val="2"/></w:tcPr><w:p><w:r><w:t>head</w:t></w:r></w:p></w:tc>
              <w:tc><w:tcPr><w:vMerge w:val="restart"/></w:tcPr><w:p/></w:tc></w:tr>
            <w:tr><w:tc><w:p/></w:tc><w:tc><w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="500"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>nested</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/></w:tc>
              <w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc></w:tr>
            </w:tbl><w:p/>
            """));

        var table = Assert.IsType<Table>(story.Blocks[0]);
        Assert.Equal("Grid", table.Properties.StyleId);
        Assert.Equal([2000, 3000, 4000], table.GridColumns);
        Assert.Equal(2, table.Rows.Count);
        var header = Assert.IsType<TableRow>(table.Rows[0]);
        Assert.NotNull(header.Properties.Get(W + "tblHeader"));
        Assert.Equal(2, header.Items.Count);
        var first = Assert.IsType<TableCell>(header.Items[0]);
        Assert.Equal("2", (string?)first.Properties.Get(W + "gridSpan")!.Attribute(W + "val"));
        Assert.IsType<Paragraph>(Assert.Single(first.Blocks));
        var second = Assert.IsType<TableCell>(Assert.IsType<TableRow>(table.Rows[1]).Items[1]);
        Assert.Equal(2, second.Blocks.Count);
        var nested = Assert.IsType<Table>(second.Blocks[0]);
        Assert.Equal([500], nested.GridColumns);
        var vMerge = Assert.IsType<TableCell>(header.Items[1]);
        Assert.Equal("restart", (string?)vMerge.Properties.Get(W + "vMerge")!.Attribute(W + "val"));
    }

    [Fact]
    public void TableGridKeepsWhatTheModelDoesNotType()
    {
        var (story, written) = RoundTrip(XmlSnippets.Document(
            """<w:tbl><w:tblPr/><w:tblGrid w:x="1"><w:gridCol w:w="100" w:extra="e"/><w:gridCol w:w="200"/><w:tblGridChange w:id="3"><w:tblGrid><w:gridCol w:w="50"/></w:tblGrid></w:tblGridChange></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>"""));

        var table = Assert.IsType<Table>(story.Blocks[0]);
        Assert.Equal([100, 200], table.GridColumns);
        table.GridColumns[1] = 250;
        using var output = new MemoryStream();
        DocumentXml.Write(story, output);
        var edited = XElement.Parse(Encoding.UTF8.GetString(output.ToArray()));
        var grid = edited.Descendants(W + "tblGrid").First();
        Assert.Equal("250", (string?)grid.Elements(W + "gridCol").ElementAt(1).Attribute(W + "w"));
        Assert.NotNull(grid.Element(W + "tblGridChange"));
        Assert.Equal("1", (string?)grid.Attribute(W + "x"));
        Assert.NotNull(written);
    }

    [Fact]
    public void SectionsAreFoundOnParagraphsAndAtTheEnd()
    {
        var (story, written) = RoundTrip(XmlSnippets.Document(
            """
            <w:p><w:pPr><w:pStyle w:val="A"/><w:sectPr w:rsidR="01"><w:headerReference w:type="default" r:id="rId1"/><w:footerReference w:type="first" r:id="rId2"/><w:pgNumType w:start="1"/><w:titlePg/></w:sectPr></w:pPr></w:p>
            <w:p><w:pPr><w:sectPr><w:headerReference w:type="even" r:id="rId3"/><w:pgSz w:w="11906" w:h="16838"/></w:sectPr></w:pPr></w:p>
            <w:p/>
            <w:sectPr w:rsidR="02"><w:footerReference w:type="default" r:id="rId4"/><w:pgSz w:w="12240" w:h="15840"/></w:sectPr>
            """));

        var first = Assert.IsType<Paragraph>(story.Blocks[0]);
        Assert.Equal("A", first.Properties.StyleId);
        var section = Assert.IsType<SectionProperties>(first.SectionBreak);
        Assert.Equal(2, section.HeaderFooterReferences.Count);
        var header = section.HeaderFooterReferences[0];
        Assert.Equal((HeaderFooterKind.Header, HeaderFooterType.Default, "rId1"), (header.Kind, header.Type, header.RelationshipId));
        var footer = section.HeaderFooterReferences[1];
        Assert.Equal((HeaderFooterKind.Footer, HeaderFooterType.First, "rId2"), (footer.Kind, footer.Type, footer.RelationshipId));
        Assert.Equal("1", (string?)section.Get(W + "pgNumType")!.Attribute(W + "start"));
        Assert.Equal(2, section.Children.Count);
        Assert.Contains(section.UnknownAttributes, a => a.Name == W + "rsidR");

        var second = Assert.IsType<Paragraph>(story.Blocks[1]);
        Assert.Equal(HeaderFooterType.Even, Assert.Single(second.SectionBreak!.HeaderFooterReferences).Type);
        Assert.Null(Assert.IsType<Paragraph>(story.Blocks[2]).SectionBreak);

        Assert.Equal(3, story.Blocks.Count);
        var final = Assert.IsType<SectionProperties>(story.FinalSection);
        Assert.Equal("rId4", Assert.Single(final.HeaderFooterReferences).RelationshipId);
        Assert.Equal(W + "sectPr", written.Element(W + "body")!.Elements().Last().Name);
    }

    [Fact]
    public void SectionReferencesKeepTheirPlaceAmongOtherChildren()
    {
        RoundTrip(XmlSnippets.Document(
            """<w:p/><w:sectPr><w:pgSz w:w="1" w:h="2"/><w:headerReference w:type="default" r:id="rId1" x:a="1" xmlns:x="urn:test"/><w:footerReference w:type="bogus" r:id="rId2"/></w:sectPr>"""));
    }

    [Fact]
    public void ContentControlsKeepTheirElement()
    {
        var (story, _) = RoundTrip(XmlSnippets.Document(
            """
            <w:sdt w:rsid="1"><w:sdtPr><w:alias w:val="Name"/><w:tag w:val="t"/><w:id w:val="42"/></w:sdtPr><w:sdtEndPr><w:b/></w:sdtEndPr><w:sdtContent w:x="y"><w:p><w:r><w:t>block</w:t></w:r></w:p><w:tbl><w:tblGrid/></w:tbl></w:sdtContent></w:sdt>
            <w:p><w:r><w:t>a</w:t></w:r><w:sdt><w:sdtPr><w:alias w:val="Inline"/></w:sdtPr><w:sdtContent><w:r><w:t>in</w:t></w:r></w:sdtContent></w:sdt></w:p>
            """));

        var block = Assert.IsType<ContentControlBlock>(story.Blocks[0]);
        Assert.NotNull(block.Properties.Get(W + "alias"));
        Assert.Equal(2, block.Content.Count);
        Assert.IsType<Table>(block.Content[1]);
        Assert.NotNull(block.Frame.EndProperties);
        var paragraph = Assert.IsType<Paragraph>(story.Blocks[1]);
        var inline = Assert.IsType<ContentControlInline>(paragraph.Inlines[1]);
        Assert.Equal("Inline", (string?)inline.Properties.Get(W + "alias")!.Attribute(W + "val"));
        Assert.IsType<Run>(Assert.Single(inline.Inlines));
    }

    [Fact]
    public void WrappersAroundRowsAndCellsStayUnknownRowItems()
    {
        var (story, _) = RoundTrip(XmlSnippets.Document(
            """<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="1"/></w:tblGrid><w:sdt><w:sdtContent><w:tr><w:tc><w:p/></w:tc></w:tr></w:sdtContent></w:sdt><w:tr><w:tc><w:p/></w:tc><w:ins w:id="1" w:author="A"><w:tc><w:p/></w:tc></w:ins></w:tr></w:tbl>"""));

        var table = Assert.IsType<Table>(story.Blocks[0]);
        Assert.IsType<UnknownRowItem>(table.Rows[0]);
        var row = Assert.IsType<TableRow>(table.Rows[1]);
        Assert.IsType<TableCell>(row.Items[0]);
        Assert.IsType<UnknownRowItem>(row.Items[1]);
    }

    [Fact]
    public void UnknownElementsBetweenKnownSiblingsInRowsAndCellsKeepTheirPlace()
    {
        var (story, written) = RoundTrip(XmlSnippets.Document(
            """<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="1"/></w:tblGrid><w:tr><w:trPr/><w:tc><w:p/><x:foo xmlns:x="urn:test"/><w:p/></w:tc><x:bar xmlns:x="urn:test"/><w:tc><w:tcPr/><x:baz xmlns:x="urn:test"/><w:p/></w:tc></w:tr></w:tbl>"""));

        var row = Assert.IsType<TableRow>(Assert.IsType<Table>(story.Blocks[0]).Rows[0]);
        Assert.Equal(3, row.Items.Count);
        Assert.IsType<UnknownRowItem>(row.Items[1]);
        var cell = Assert.IsType<TableCell>(row.Items[0]);
        Assert.IsType<UnknownBlock>(cell.Blocks[1]);
        Assert.Equal(["trPr", "tc", "bar", "tc"], written.Descendants(W + "tr").Single().Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void NewerWordPropertiesInATableSurvive()
    {
        var (story, written) = RoundTrip(XmlSnippets.Document(
            """<w:tbl><w:tblPr><w:tblStyle w:val="G"/><w14:tblFoo w14:val="1"/><x:other xmlns:x="urn:test"/></w:tblPr><w:tblGrid><w:gridCol w:w="1"/></w:tblGrid><w:tr w14:paraId="0A0B0C0D" w14:textId="77777777"><w:tc><w:p/></w:tc></w:tr></w:tbl>"""));

        var table = Assert.IsType<Table>(story.Blocks[0]);
        Assert.Equal(3, table.Properties.Children.Count);
        Assert.Equal("G", table.Properties.StyleId);
        Assert.Single(written.Descendants(Test + "other"));
    }

    [Theory]
    [InlineData("tbl")]
    [InlineData("tblPr")]
    [InlineData("tblGrid")]
    [InlineData("tr")]
    [InlineData("trPr")]
    [InlineData("tc")]
    [InlineData("tcPr")]
    [InlineData("sdt")]
    [InlineData("sdtPr")]
    [InlineData("sdtContent")]
    [InlineData("sectPr")]
    [InlineData("sdtInline")]
    public void UnknownContentSurvivesInBlockContainers(string container)
    {
        const string Attribute = """xmlns:x="urn:test" x:bar="1" """;
        const string Element = """<x:foo xmlns:x="urn:test" a="1"><w:t>deep</w:t></x:foo>""";
        var body = container switch
        {
            "tbl" => $"<w:tbl {Attribute}>{Element}<w:tblGrid/></w:tbl>",
            "tblPr" => $"<w:tbl><w:tblPr {Attribute}>{Element}</w:tblPr></w:tbl>",
            "tblGrid" => $"<w:tbl><w:tblGrid {Attribute}>{Element}<w:gridCol w:w=\"5\"/></w:tblGrid></w:tbl>",
            "tr" => $"<w:tbl><w:tr {Attribute}>{Element}<w:tc><w:p/></w:tc></w:tr></w:tbl>",
            "trPr" => $"<w:tbl><w:tr><w:trPr {Attribute}>{Element}</w:trPr></w:tr></w:tbl>",
            "tc" => $"<w:tbl><w:tr><w:tc {Attribute}>{Element}<w:p/></w:tc></w:tr></w:tbl>",
            "tcPr" => $"<w:tbl><w:tr><w:tc><w:tcPr {Attribute}>{Element}</w:tcPr><w:p/></w:tc></w:tr></w:tbl>",
            "sdt" => $"<w:sdt {Attribute}>{Element}<w:sdtPr/><w:sdtContent><w:p/></w:sdtContent></w:sdt>",
            "sdtPr" => $"<w:sdt><w:sdtPr {Attribute}>{Element}</w:sdtPr><w:sdtContent><w:p/></w:sdtContent></w:sdt>",
            "sdtContent" => $"<w:sdt><w:sdtContent {Attribute}>{Element}<w:p/></w:sdtContent></w:sdt>",
            "sectPr" => $"<w:p><w:pPr><w:sectPr {Attribute}>{Element}<w:headerReference w:type=\"default\" r:id=\"rId1\" xmlns:x=\"urn:test\" x:bar=\"2\"/></w:sectPr></w:pPr></w:p>",
            _ => $"<w:p><w:sdt {Attribute}>{Element}<w:sdtContent {Attribute}>{Element}<w:r><w:t>a</w:t></w:r></w:sdtContent></w:sdt></w:p>",
        };
        var (_, written) = RoundTrip(XmlSnippets.Document(body));
        Assert.NotEmpty(written.Descendants(Test + "foo"));
        Assert.Contains(written.DescendantsAndSelf().SelectMany(e => e.Attributes()), a => a.Name == Test + "bar");
    }
}
