using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class InlineXmlTests
{
    private static readonly XNamespace W = XmlSnippets.W;
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace Test = "urn:test";

    /// <summary>Reads the part, writes it again, checks the bytes are UTF-8 without BOM with a standalone declaration and that the result is equivalent to the source.</summary>
    private static (Story Story, XElement Written) RoundTrip(string xml)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var story = DocumentXml.Read(input);
        using var output = new MemoryStream();
        DocumentXml.Write(story, output);
        var bytes = output.ToArray();
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>", text, StringComparison.Ordinal);
        var written = XElement.Parse(text);
        var difference = XmlEquivalence.Explain(XElement.Parse(xml), written);
        Assert.True(difference is null, difference + "\n" + text);
        return (story, written);
    }

    private static Paragraph FirstParagraph(Story story) => Assert.IsType<Paragraph>(story.Blocks[0]);

    [Fact]
    public void ReadsParagraphsRunsAndContent()
    {
        var (story, _) = RoundTrip(XmlSnippets.Document(
            """<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve"> a </w:t><w:tab/><w:br w:type="page"/></w:r></w:p>"""));

        var paragraph = FirstParagraph(story);
        Assert.Equal("Heading1", paragraph.Properties.StyleId);
        var run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines));
        Assert.True(run.Properties.Bold);
        Assert.Equal(3, run.Content.Count);
        Assert.Equal(" a ", Assert.IsType<TextContent>(run.Content[0]).Text);
        Assert.IsType<TabContent>(run.Content[1]);
        Assert.Equal(BreakKind.Page, Assert.IsType<BreakContent>(run.Content[2]).Kind);
    }

    [Fact]
    public void TextSurvivesExactly()
    {
        var texts = new[] { " lead", "trail ", "two  spaces", "<b>&</b>", "\U0001F600", "plain" };
        var body = new StringBuilder("<w:p>");
        foreach (var text in texts)
        {
            var preserve = text.StartsWith(' ') || text.EndsWith(' ') ? " xml:space=\"preserve\"" : string.Empty;
            body.Append("<w:r><w:t").Append(preserve).Append('>').Append(new XText(text)).Append("</w:t></w:r>");
        }

        body.Append("<w:r><w:t>a</w:t><w:tab/><w:br/><w:t xml:space=\"preserve\"> b</w:t></w:r></w:p>");
        var (story, written) = RoundTrip(XmlSnippets.Document(body.ToString()));

        var runs = FirstParagraph(story).Inlines.Cast<Run>().ToList();
        Assert.Equal(texts, runs.Take(texts.Length).Select(r => Assert.IsType<TextContent>(Assert.Single(r.Content)).Text));
        Assert.Equal(["a", null, null, " b"], runs[^1].Content.Select(c => (c as TextContent)?.Text));

        foreach (var t in written.Descendants(W + "t"))
        {
            var preserve = (string?)t.Attribute(XNamespace.Xml + "space");
            var needs = t.Value.StartsWith(' ') || t.Value.EndsWith(' ');
            Assert.Equal(needs ? "preserve" : null, preserve);
        }
    }

    [Fact]
    public void HyperlinkBookmarkAndCommentRangesSurvive()
    {
        var (story, _) = RoundTrip(XmlSnippets.Document(
            """<w:p><w:bookmarkStart w:id="0" w:name="_Toc1"/><w:hyperlink r:id="rId7" w:history="1"><w:r><w:t>web</w:t></w:r></w:hyperlink><w:hyperlink w:anchor="_Toc1"><w:r><w:t>local</w:t></w:r></w:hyperlink><w:bookmarkEnd w:id="0"/><w:commentRangeStart w:id="3"/><w:r><w:t>c</w:t></w:r><w:commentRangeEnd w:id="3"/><w:r><w:rPr><w:rStyle w:val="CommentReference"/></w:rPr><w:commentReference w:id="3"/></w:r></w:p>"""));

        var inlines = FirstParagraph(story).Inlines;
        Assert.Equal(8, inlines.Count);
        var start = Assert.IsType<BookmarkStart>(inlines[0]);
        Assert.Equal(0, start.Id);
        Assert.Equal("_Toc1", start.Name);
        var external = Assert.IsType<Hyperlink>(inlines[1]);
        Assert.Equal("rId7", external.RelationshipId);
        Assert.Null(external.Anchor);
        Assert.Single(external.Inlines);
        var local = Assert.IsType<Hyperlink>(inlines[2]);
        Assert.Equal("_Toc1", local.Anchor);
        Assert.Null(local.RelationshipId);
        Assert.Equal(0, Assert.IsType<BookmarkEnd>(inlines[3]).Id);
        Assert.Equal(3, Assert.IsType<CommentRangeStart>(inlines[4]).Id);
        Assert.IsType<Run>(inlines[5]);
        Assert.Equal(3, Assert.IsType<CommentRangeEnd>(inlines[6]).Id);
        Assert.IsType<Run>(inlines[7]);
    }

    [Fact]
    public void ReferenceRunsKeepTheirIds()
    {
        var (story, _) = RoundTrip(XmlSnippets.Document(
            """<w:p><w:r><w:commentReference w:id="3"/></w:r><w:r><w:footnoteReference w:id="2"/></w:r><w:r><w:endnoteReference w:id="4"/></w:r></w:p>"""));

        var runs = FirstParagraph(story).Inlines.Cast<Run>().ToList();
        Assert.Equal(3, Assert.IsType<CommentReference>(Assert.Single(runs[0].Content)).Id);
        Assert.Equal(2, Assert.IsType<FootnoteReference>(Assert.Single(runs[1].Content)).Id);
        Assert.Equal(4, Assert.IsType<EndnoteReference>(Assert.Single(runs[2].Content)).Id);
    }

    [Fact]
    public void TrackedChangesAreWrappersAndListed()
    {
        const string Date = "2024-05-01T10:00:00Z";
        var (story, _) = RoundTrip(XmlSnippets.Document(
            $"""<w:p><w:pPr><w:rPr><w:ins w:id="1" w:author="Ann" w:date="{Date}"/></w:rPr></w:pPr><w:ins w:id="2" w:author="Ann" w:date="{Date}"><w:r><w:t>new</w:t></w:r></w:ins><w:del w:id="3" w:author="Bob" w:date="{Date}"><w:r><w:delText>old</w:delText></w:r></w:del><w:moveFrom w:id="4" w:author="Bob" w:date="{Date}"><w:r><w:t>m</w:t></w:r></w:moveFrom><w:moveTo w:id="5" w:author="Bob" w:date="{Date}"><w:r><w:t>m</w:t></w:r></w:moveTo><w:r><w:rPr><w:b/><w:rPrChange w:id="6" w:author="Cy" w:date="{Date}"><w:rPr/></w:rPrChange></w:rPr><w:t>fmt</w:t></w:r></w:p>"""));

        var inlines = FirstParagraph(story).Inlines;
        var deleted = Assert.IsType<TrackedChange>(inlines[1]);
        Assert.Equal(TrackedChangeKind.Delete, deleted.Kind);
        var run = Assert.IsType<Run>(Assert.Single(deleted.Inlines));
        Assert.Equal("old", Assert.IsType<DeletedText>(Assert.Single(run.Content)).Text);

        var records = TrackedChanges.List(story);
        Assert.Equal(
            [RevisionKind.ParagraphMark, RevisionKind.Insert, RevisionKind.Delete, RevisionKind.MoveFrom, RevisionKind.MoveTo, RevisionKind.PropertyChange],
            records.Select(r => r.Kind));
        Assert.Equal([1, 2, 3, 4, 5, 6], records.Select(r => r.Id));
        Assert.Equal(["Ann", "Ann", "Bob", "Bob", "Bob", "Cy"], records.Select(r => r.Author));
        Assert.All(records, r => Assert.Equal(DateTimeOffset.Parse(Date, CultureInfo.InvariantCulture), r.Date));
        Assert.Equal(TrackedChangeKind.Insert, records[0].Change);
    }

    [Fact]
    public void FieldScannerFindsComplexAndSimpleFields()
    {
        const string Begin = """<w:r><w:fldChar w:fldCharType="begin"/></w:r>""";
        const string Separate = """<w:r><w:fldChar w:fldCharType="separate"/></w:r>""";
        const string End = """<w:r><w:fldChar w:fldCharType="end"/></w:r>""";
        var (story, _) = RoundTrip(XmlSnippets.Document(
            "<w:p>" + Begin + """<w:r><w:instrText xml:space="preserve"> TOC \o "1-3" </w:instrText></w:r>""" + Separate
            + Begin + """<w:r><w:instrText xml:space="preserve"> PAGEREF _Toc1 \h </w:instrText></w:r>""" + Separate + "<w:r><w:t>1</w:t></w:r>" + End
            + End
            + """<w:fldSimple w:instr=" PAGE "><w:r><w:t>2</w:t></w:r></w:fldSimple>"""
            + Begin + "<w:r><w:instrText>SEQ x</w:instrText></w:r></w:p>"));

        var fields = FieldScanner.Scan(FirstParagraph(story).Inlines);
        Assert.Equal(4, fields.Count);
        Assert.Equal("TOC \\o \"1-3\"", fields[0].Instruction);
        Assert.True(fields[0].IsComplete);
        Assert.Null(fields[0].Parent);
        Assert.Equal("PAGEREF _Toc1 \\h", fields[1].Instruction);
        Assert.Same(fields[0], fields[1].Parent);
        Assert.True(fields[1].IsComplete);
        Assert.Equal("PAGE", fields[2].Instruction);
        Assert.True(fields[2].IsComplete);
        Assert.Null(fields[2].Parent);
        Assert.Equal("SEQ x", fields[3].Instruction);
        Assert.False(fields[3].IsComplete);
    }

    [Fact]
    public void PicturesKeepTheirXmlAndExposeSize()
    {
        const string Inline = """<w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="952500" cy="476250"/><wp:docPr id="1" name="Picture 1" descr="A cat"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:blipFill><a:blip r:embed="rId5"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>""";
        const string Anchor = """<w:r><w:drawing><wp:anchor behindDoc="0"><wp:extent cx="100" cy="200"/><wp:docPr id="2" name="Picture 2"/><a:graphic><a:graphicData uri="x"><a:blip r:embed="rId6"/></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>""";
        var (story, _) = RoundTrip(XmlSnippets.Document("<w:p>" + Inline + Anchor + "</w:p>"));

        var runs = FirstParagraph(story).Inlines.Cast<Run>().ToList();
        var inline = Assert.IsType<Picture>(Assert.Single(runs[0].Content));
        Assert.True(inline.Inline);
        Assert.Equal(952500, inline.WidthEmu);
        Assert.Equal(476250, inline.HeightEmu);
        Assert.Equal("rId5", inline.EmbedRelationshipId);
        Assert.Equal("A cat", inline.AltText);
        var anchor = Assert.IsType<Picture>(Assert.Single(runs[1].Content));
        Assert.False(anchor.Inline);
        Assert.Equal("rId6", anchor.EmbedRelationshipId);
        Assert.Null(anchor.AltText);
    }

    [Fact]
    public void NewerWordContentSurvives()
    {
        var source = XmlSnippets.Document(
            """<w:p w14:paraId="1A2B3C4D" w14:textId="77777777"><w:pPr><w:pStyle w:val="Heading1" w15:collapsed="1"/></w:pPr><w:r><mc:AlternateContent><mc:Choice Requires="w14"><w:txbxContent><w:p><w:r><w:t>boxed</w:t></w:r></w:p></w:txbxContent></mc:Choice><mc:Fallback><w:t>fallback</w:t></mc:Fallback></mc:AlternateContent></w:r></w:p>""");
        var (story, written) = RoundTrip(source);

        Assert.Equal("w14 w15 wp14", (string?)written.Attribute(Mc + "Ignorable"));
        var expected = XElement.Parse(source);
        foreach (var prefix in new[] { "w14", "w15", "wp14", "mc", "w", "r", "wp", "a", "pic" })
        {
            Assert.NotNull(written.GetNamespaceOfPrefix(prefix));
            Assert.Equal(expected.GetNamespaceOfPrefix(prefix), written.GetNamespaceOfPrefix(prefix));
            Assert.NotNull(written.Attribute(XNamespace.Xmlns + prefix));
        }

        var paragraph = written.Descendants(W + "p").First();
        Assert.Equal("1A2B3C4D", (string?)paragraph.Attribute(W14 + "paraId"));
        Assert.Equal("77777777", (string?)paragraph.Attribute(W14 + "textId"));
        Assert.Equal("1", (string?)written.Descendants(W + "pStyle").Single().Attribute(W15 + "collapsed"));
        var choice = written.Descendants(Mc + "Choice").Single();
        Assert.Equal(W14.NamespaceName, choice.GetNamespaceOfPrefix("w14")?.NamespaceName);
        Assert.Contains(FirstParagraph(story).UnknownAttributes, a => a.Name == W14 + "paraId" && a.Value == "1A2B3C4D");
    }

    [Theory]
    [InlineData("body")]
    [InlineData("p")]
    [InlineData("pPr")]
    [InlineData("r")]
    [InlineData("rPr")]
    [InlineData("hyperlink")]
    [InlineData("ins")]
    [InlineData("del")]
    public void UnknownContentSurvivesInInlineContainers(string container)
    {
        const string Attribute = """xmlns:x="urn:test" x:bar="1" """;
        const string Element = """<x:foo xmlns:x="urn:test" a="1"><w:t>deep</w:t></x:foo>""";
        const string Revision = """ w:id="9" w:author="A" w:date="2024-01-01T00:00:00Z" """;
        var body = container switch
        {
            "body" => "<w:p/>",
            "p" => $"<w:p {Attribute}>{Element}</w:p>",
            "pPr" => $"<w:p><w:pPr {Attribute}>{Element}</w:pPr></w:p>",
            "r" => $"<w:p><w:r {Attribute}>{Element}</w:r></w:p>",
            "rPr" => $"<w:p><w:r><w:rPr {Attribute}>{Element}</w:rPr></w:r></w:p>",
            "hyperlink" => $"<w:p><w:hyperlink {Attribute}>{Element}</w:hyperlink></w:p>",
            _ => $"<w:p><w:{container}{Revision}{Attribute}>{Element}</w:{container}></w:p>",
        };
        var xml = XmlSnippets.Document(body);
        if (container == "body")
        {
            xml = xml.Replace("<w:body>", $"<w:body {Attribute}>" + Element, StringComparison.Ordinal);
        }

        var (_, written) = RoundTrip(xml);
        Assert.Single(written.Descendants(Test + "foo"));
        Assert.Contains(written.DescendantsAndSelf().SelectMany(e => e.Attributes()), a => a.Name == Test + "bar");
    }

    [Fact]
    public void BodySectionPropertiesSurvive()
    {
        var (story, written) = RoundTrip(XmlSnippets.Document(
            """<w:p><w:r><w:t>x</w:t></w:r></w:p><w:sectPr w:rsidR="00AA"><w:pgSz w:w="11906" w:h="16838"/></w:sectPr>"""));

        Assert.Equal(2, story.Blocks.Count);
        Assert.Equal(W + "sectPr", written.Element(W + "body")!.Elements().Last().Name);
    }

    [Fact]
    public void NonBodyChildrenOfTheRootSurvive()
    {
        var (_, written) = RoundTrip(XmlSnippets.Document("<w:p/>").Replace("<w:body>", "<w:background w:color=\"FFFF00\"/><w:body>", StringComparison.Ordinal));

        Assert.Equal(W + "background", written.Elements().First().Name);
    }
}
