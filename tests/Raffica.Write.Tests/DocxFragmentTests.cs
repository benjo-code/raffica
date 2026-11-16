using System.Text;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class DocxFragmentTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    private static string Wrap(string body) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"{W.NamespaceName}\"><w:body>{body}</w:body></w:document>";

    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    [Fact]
    public void RoundTripsParagraphsWithDirectFormatting()
    {
        var paragraph = new Paragraph();
        paragraph.Properties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "center")));
        var bold = new Run();
        bold.Properties.SetFlag(W + "b", true);
        bold.Content.Add(new TextContent("Bold "));
        var plain = new Run();
        plain.Content.Add(new TextContent("plain"));
        plain.Content.Add(new TabContent());
        plain.Content.Add(new UnknownRunContent(new UnknownContent(new XElement(W + "sym", new XAttribute(W + "font", "Wingdings"), new XAttribute(W + "char", "F028")))));
        paragraph.Inlines.Add(bold);
        paragraph.Inlines.Add(plain);
        paragraph.Inlines.Add(new UnknownInline(new UnknownContent(new XElement(W + "proofErr", new XAttribute(W + "type", "spellStart")))));

        var read = DocxFragment.Read(DocxFragment.Write([paragraph]));

        var back = Assert.Single(read);
        Assert.Equal("center", (string?)back.Properties.Get(W + "jc")?.Attribute(W + "val"));
        Assert.Equal(3, back.Inlines.Count);
        var firstRun = Assert.IsType<Run>(back.Inlines[0]);
        Assert.True(firstRun.Properties.Bold);
        Assert.Equal("Bold ", Assert.IsType<TextContent>(Assert.Single(firstRun.Content)).Text);
        var secondRun = Assert.IsType<Run>(back.Inlines[1]);
        Assert.IsType<TextContent>(secondRun.Content[0]);
        Assert.IsType<TabContent>(secondRun.Content[1]);
        var sym = Assert.IsType<UnknownRunContent>(secondRun.Content[2]);
        Assert.Equal("F028", (string?)sym.Content.Element.Attribute(W + "char"));
        var unknown = Assert.IsType<UnknownInline>(back.Inlines[2]);
        Assert.Equal("proofErr", unknown.Content.Element.Name.LocalName);
    }

    [Fact]
    public void NeverWritesSectionBreaksOrParagraphIds()
    {
        var paragraph = new Paragraph { SectionBreak = new SectionProperties() };
        paragraph.UnknownAttributes.Add(new XAttribute(W14 + "paraId", "1A2B3C4D"));
        paragraph.UnknownAttributes.Add(new XAttribute(W14 + "textId", "77777777"));
        paragraph.UnknownAttributes.Add(new XAttribute(W + "rsidR", "00112233"));
        var run = new Run();
        run.Content.Add(new TextContent("x"));
        paragraph.Inlines.Add(run);

        var bytes = DocxFragment.Write([paragraph]);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("sectPr", text, StringComparison.Ordinal);
        Assert.DoesNotContain("paraId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("textId", text, StringComparison.Ordinal);
        Assert.Contains("rsidR", text, StringComparison.Ordinal);
        Assert.Null(Assert.Single(DocxFragment.Read(bytes)).SectionBreak);
    }

    [Fact]
    public void ReadDropsSectionBreaksParagraphIdsAndNonParagraphBlocks()
    {
        var xml = Wrap($"<w:p xmlns:w14=\"{W14.NamespaceName}\" w14:paraId=\"AAAA0001\"><w:pPr><w:sectPr/></w:pPr><w:r><w:t>a</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl><w:p/>");

        var read = DocxFragment.Read(Bytes(xml));

        Assert.Equal(2, read.Count);
        Assert.Null(read[0].SectionBreak);
        Assert.DoesNotContain(read[0].UnknownAttributes, a => a.Name.LocalName is "paraId" or "textId");
    }

    [Fact]
    public void AnEmptyListOfParagraphsRoundTrips() => Assert.Empty(DocxFragment.Read(DocxFragment.Write([])));

    [Fact]
    public void ADoctypeWithAnEntityIsRefused()
    {
        var xml = $"<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY e \"boom\">]><w:document xmlns:w=\"{W.NamespaceName}\"><w:body><w:p><w:r><w:t>&e;</w:t></w:r></w:p></w:body></w:document>";
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(Bytes(xml)));
    }

    [Fact]
    public void ADoctypeWithAnExternalEntityIsRefused()
    {
        var xml = $"<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><w:document xmlns:w=\"{W.NamespaceName}\"><w:body><w:p><w:r><w:t>&e;</w:t></w:r></w:p></w:body></w:document>";
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(Bytes(xml)));
    }

    [Fact]
    public void DeepNestingIsRefusedWithoutOverflowingTheStack()
    {
        var depth = 20000;
        var xml = Wrap("<w:p>" + string.Concat(Enumerable.Repeat("<w:hyperlink>", depth)) + string.Concat(Enumerable.Repeat("</w:hyperlink>", depth)) + "</w:p>");
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(Bytes(xml)));
    }

    [Fact]
    public void NestingJustWithinTheLimitIsRead()
    {
        var depth = DocxFragment.MaxDepth - 6;
        var xml = Wrap("<w:p>" + string.Concat(Enumerable.Repeat("<w:hyperlink>", depth)) + "<w:r><w:t>x</w:t></w:r>" + string.Concat(Enumerable.Repeat("</w:hyperlink>", depth)) + "</w:p>");
        Assert.Single(DocxFragment.Read(Bytes(xml)));
    }

    [Fact]
    public void AnOversizeFragmentIsRefused()
    {
        var bytes = new byte[DocxFragment.MaxBytes + 1];
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(bytes));
    }

    [Fact]
    public void TooManyParagraphsAreRefused()
    {
        var xml = Wrap(string.Concat(Enumerable.Repeat("<w:p/>", DocxFragment.MaxParagraphs + 1)));
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(Bytes(xml)));
    }

    private static void Refused(string xml) => Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(Bytes(xml)));

    [Fact]
    public void AnAttributeFloodOnOneElementIsRefused()
    {
        var attributes = string.Concat(Enumerable.Range(0, DocxFragment.MaxAttributesPerElement + 1).Select(i => $" a{i}=\"1\""));
        Refused(Wrap($"<w:p{attributes}/>"));
    }

    [Fact]
    public void ANamespaceDeclarationFloodOnOneElementIsRefused()
    {
        var declarations = string.Concat(Enumerable.Range(0, DocxFragment.MaxAttributesPerElement + 1).Select(i => $" xmlns:n{i}=\"urn:n{i}\""));
        Refused(Wrap($"<w:p{declarations}/>"));
    }

    [Fact]
    public void TheTotalOfAttributesAndDeclarationsIsCapped()
    {
        var perElement = DocxFragment.MaxAttributesPerElement / 2;
        var elements = (DocxFragment.MaxAttributes / perElement) + 1;
        var attributes = string.Concat(Enumerable.Range(0, perElement).Select(i => $" a{i}=\"1\""));
        Assert.True(elements < DocxFragment.MaxParagraphs);
        Refused(Wrap(string.Concat(Enumerable.Repeat($"<w:p{attributes}/>", elements))));
    }

    [Fact]
    public void TooManyElementsAreRefused()
    {
        var xml = Wrap("<w:p>" + string.Concat(Enumerable.Repeat("<w:x/>", DocxFragment.MaxElements + 1)) + "</w:p>");
        Assert.True(xml.Length < DocxFragment.MaxBytes);
        Refused(xml);
    }

    [Fact]
    public void ATextNodeBeyondTheCharacterLimitIsRefused()
    {
        var xml = Wrap("<w:p><w:r><w:t>" + new string('x', DocxFragment.MaxCharacters + 1) + "</w:t></w:r></w:p>");
        Assert.True(xml.Length < DocxFragment.MaxBytes);
        Refused(xml);
    }

    [Fact]
    public void ALargeRealisticCopyIsStillRead()
    {
        var paragraphs = new List<Paragraph>();
        for (var i = 0; i < 5000; i++)
        {
            var paragraph = new Paragraph();
            paragraph.Properties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "left")));
            var run = new Run();
            run.Properties.SetFlag(W + "b", true);
            run.Content.Add(new TextContent(new string('t', 300)));
            paragraph.Inlines.Add(run);
            paragraphs.Add(paragraph);
        }

        var bytes = DocxFragment.Write(paragraphs);

        Assert.Equal(5000, DocxFragment.Read(bytes).Count);
    }

    [Fact]
    public void TruncatedXmlIsRefused()
    {
        var good = DocxFragment.Write([new Paragraph()]);
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(good.AsSpan(0, good.Length - 12)));
    }

    [Fact]
    public void EmptyBytesAreRefused() => Assert.Throws<FragmentFormatException>(() => DocxFragment.Read([]));

    [Fact]
    public void GarbageBytesAreRefused() => Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x02 }));

    [Fact]
    public void AWrongRootIsRefused()
    {
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(Bytes($"<w:hdr xmlns:w=\"{W.NamespaceName}\"><w:p/></w:hdr>")));
        Assert.Throws<FragmentFormatException>(() => DocxFragment.Read(Bytes("<document><body><p/></body></document>")));
    }

    [Fact]
    public void ARefusedFragmentIsAFormatExceptionWithoutTheText()
    {
        var thrown = Assert.ThrowsAny<FormatException>(() => DocxFragment.Read(Bytes("not xml at all with secret text")));
        Assert.DoesNotContain("secret", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCodecWritesAndReadsThroughTheModelInterface()
    {
        var codec = new DocxFragmentCodec();
        Assert.IsAssignableFrom<IFragmentCodec>(codec);
        var paragraph = new Paragraph();
        var run = new Run();
        run.Content.Add(new TextContent("via the interface"));
        paragraph.Inlines.Add(run);

        var read = codec.Read(codec.Write([paragraph]));

        Assert.Equal("via the interface", Assert.IsType<TextContent>(Assert.IsType<Run>(Assert.Single(read).Inlines[0]).Content[0]).Text);
    }
}
