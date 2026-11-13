using System.Xml.Linq;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class XmlEquivalenceTests
{
    private const string Ns = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    private static XElement Parse(string xml) => XElement.Parse(xml);

    [Fact]
    public void EqualIgnoringAttributeOrderAndNamespaceDeclarationPlacement()
    {
        var a = Parse($"<w:p {Ns} w:x=\"1\" w:y=\"2\"><w:r/></w:p>");
        var b = Parse($"<w:p w:y=\"2\" w:x=\"1\" {Ns}><w:r {Ns}/></w:p>");
        Assert.True(XmlEquivalence.AreEqual(a, b));
        Assert.Null(XmlEquivalence.Explain(a, b));
    }

    [Fact]
    public void UnequalForAChangedAttributeValue()
    {
        var a = Parse($"<w:p {Ns} w:x=\"1\"/>");
        var b = Parse($"<w:p {Ns} w:x=\"2\"/>");
        Assert.False(XmlEquivalence.AreEqual(a, b));
    }

    [Fact]
    public void UnequalForASwappedChildOrder()
    {
        var a = Parse($"<w:p {Ns}><w:b/><w:i/></w:p>");
        var b = Parse($"<w:p {Ns}><w:i/><w:b/></w:p>");
        Assert.False(XmlEquivalence.AreEqual(a, b));
    }

    [Fact]
    public void UnequalForALeadingSpaceInText()
    {
        var a = Parse($"<w:t {Ns} xml:space=\"preserve\"> a</w:t>");
        var b = Parse($"<w:t {Ns} xml:space=\"preserve\">a</w:t>");
        Assert.False(XmlEquivalence.AreEqual(a, b));
    }

    [Fact]
    public void WhitespaceBetweenElementsIsIgnored()
    {
        var a = Parse($"<w:p {Ns}>\n  <w:r/>\n</w:p>");
        var b = Parse($"<w:p {Ns}><w:r/></w:p>");
        Assert.True(XmlEquivalence.AreEqual(a, b));
    }

    [Fact]
    public void IdenticalMixedContentIsEqual()
    {
        var a = Parse($"<w:p {Ns}>x<w:b/>y</w:p>");
        var b = Parse($"<w:p {Ns}>x<w:b/>y</w:p>");
        Assert.True(XmlEquivalence.AreEqual(a, b));
    }

    [Fact]
    public void DifferingMixedContentTextIsUnequal()
    {
        var a = Parse($"<w:p {Ns}>x<w:b/></w:p>");
        var b = Parse($"<w:p {Ns}>z<w:b/></w:p>");
        Assert.False(XmlEquivalence.AreEqual(a, b));
    }

    [Fact]
    public void ExplainNamesThePathOfTheDifference()
    {
        var a = Parse($"<w:document {Ns}><w:body><w:p/><w:p><w:r><w:t>a</w:t></w:r></w:p></w:body></w:document>");
        var b = Parse($"<w:document {Ns}><w:body><w:p/><w:p><w:r><w:t>b</w:t></w:r></w:p></w:body></w:document>");
        var explanation = XmlEquivalence.Explain(a, b);
        Assert.NotNull(explanation);
        Assert.StartsWith("/w:document[1]/w:body[1]/w:p[2]/w:r[1]/w:t[1]", explanation);
    }

    [Fact]
    public void RedundantPreserveOnOneSideIsIgnoredForPlainText()
    {
        var a = Parse($"<w:t {Ns} xml:space=\"preserve\">a</w:t>");
        var b = Parse($"<w:t {Ns}>a</w:t>");
        Assert.True(XmlEquivalence.AreEqual(a, b));
        Assert.True(XmlEquivalence.AreEqual(b, a));
    }

    [Fact]
    public void MissingPreserveIsStillADifferenceWhenTheTextHasEdgeSpace()
    {
        var a = Parse($"<w:t {Ns} xml:space=\"preserve\"> a</w:t>");
        var b = Parse($"<w:t {Ns}> a</w:t>");
        Assert.False(XmlEquivalence.AreEqual(a, b));
        Assert.False(XmlEquivalence.AreEqual(b, a));
    }
}
