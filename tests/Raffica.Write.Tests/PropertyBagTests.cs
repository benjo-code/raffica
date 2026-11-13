using System.Xml.Linq;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class PropertyBagTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    private static PropertyBag Bag(PropertyKind kind, params XElement[] children)
    {
        var bag = new PropertyBag(kind);
        foreach (var child in children)
        {
            bag.AddLoaded(child);
        }

        return bag;
    }

    private static string[] Names(PropertyBag bag) => bag.Children.Select(c => c.Name.LocalName).ToArray();

    [Fact]
    public void SetInsertsInSchemaOrder()
    {
        var bag = Bag(PropertyKind.Run, new XElement(W + "b"), new XElement(W + "lang"));
        bag.Set(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", "24")));
        Assert.Equal(["b", "sz", "lang"], Names(bag));
    }

    [Fact]
    public void UnknownChildKeepsItsPlace()
    {
        var bag = Bag(PropertyKind.Run, new XElement(W + "b"), new XElement(W14 + "ligatures"), new XElement(W + "lang"));
        bag.Set(W + "i", new XElement(W + "i"));
        var names = Names(bag);
        Assert.Equal(4, names.Length);
        Assert.True(Array.IndexOf(names, "b") < Array.IndexOf(names, "ligatures"));
        Assert.True(Array.IndexOf(names, "ligatures") < Array.IndexOf(names, "lang"));
        Assert.Equal(["b", "ligatures", "i", "lang"], names);
    }

    [Fact]
    public void RemoveLeavesTheOthers()
    {
        var bag = Bag(PropertyKind.Run, new XElement(W + "b"), new XElement(W + "i"), new XElement(W + "lang"));
        Assert.True(bag.Remove(W + "i"));
        Assert.False(bag.Remove(W + "i"));
        Assert.Equal(["b", "lang"], Names(bag));
    }

    [Fact]
    public void SetReplacesAnExistingChildInPlace()
    {
        var bag = Bag(PropertyKind.Run, new XElement(W + "b"), new XElement(W + "sz", new XAttribute(W + "val", "20")), new XElement(W + "lang"));
        bag.Set(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", "28")));
        Assert.Equal(["b", "sz", "lang"], Names(bag));
        Assert.Equal("28", (string?)bag.Get(W + "sz")!.Attribute(W + "val"));
    }

    [Fact]
    public void StyleIdReadsByKind()
    {
        var paragraph = Bag(PropertyKind.Paragraph, new XElement(W + "pStyle", new XAttribute(W + "val", "Heading1")));
        var run = Bag(PropertyKind.Run, new XElement(W + "rStyle", new XAttribute(W + "val", "Strong")));
        var table = Bag(PropertyKind.Table, new XElement(W + "tblStyle", new XAttribute(W + "val", "Grid")));
        Assert.Equal("Heading1", paragraph.StyleId);
        Assert.Equal("Strong", run.StyleId);
        Assert.Equal("Grid", table.StyleId);
        Assert.Null(Bag(PropertyKind.Run, new XElement(W + "pStyle", new XAttribute(W + "val", "x"))).StyleId);
    }

    [Fact]
    public void BoldIsFalseForValOff()
    {
        Assert.False(Bag(PropertyKind.Run).Bold);
        Assert.True(Bag(PropertyKind.Run, new XElement(W + "b")).Bold);
        Assert.False(Bag(PropertyKind.Run, new XElement(W + "b", new XAttribute(W + "val", "0"))).Bold);
        Assert.False(Bag(PropertyKind.Run, new XElement(W + "b", new XAttribute(W + "val", "false"))).Bold);
        Assert.True(Bag(PropertyKind.Run, new XElement(W + "b", new XAttribute(W + "val", "1"))).Bold);
    }

    [Fact]
    public void NumberingReadsIdAndLevel()
    {
        var bag = Bag(
            PropertyKind.Paragraph,
            new XElement(
                W + "numPr",
                new XElement(W + "ilvl", new XAttribute(W + "val", "2")),
                new XElement(W + "numId", new XAttribute(W + "val", "7"))));
        Assert.Equal(7, bag.NumberingId);
        Assert.Equal(2, bag.NumberingLevel);
        Assert.Null(Bag(PropertyKind.Paragraph).NumberingId);
    }

    [Fact]
    public void ParagraphMarkChildrenComeLastInOrder()
    {
        var bag = Bag(PropertyKind.Paragraph);
        bag.Set(W + "pPrChange", new XElement(W + "pPrChange"));
        bag.Set(W + "sectPr", new XElement(W + "sectPr"));
        bag.Set(W + "rPr", new XElement(W + "rPr"));
        bag.Set(W + "jc", new XElement(W + "jc"));
        bag.Set(W + "pStyle", new XElement(W + "pStyle"));
        Assert.Equal(["pStyle", "jc", "rPr", "sectPr", "pPrChange"], Names(bag));
    }

    [Fact]
    public void SchemaOrderPinsKnownOrderings()
    {
        Assert.True(SchemaOrder.IndexOf(PropertyKind.Paragraph, W + "numPr") < SchemaOrder.IndexOf(PropertyKind.Paragraph, W + "spacing"));
        Assert.True(SchemaOrder.IndexOf(PropertyKind.Run, W + "sz") < SchemaOrder.IndexOf(PropertyKind.Run, W + "lang"));
        Assert.True(SchemaOrder.IndexOf(PropertyKind.TableCell, W + "tcW") < SchemaOrder.IndexOf(PropertyKind.TableCell, W + "gridSpan"));
        Assert.Equal(
            SchemaOrder.IndexOf(PropertyKind.Section, W + "headerReference"),
            SchemaOrder.IndexOf(PropertyKind.Section, W + "footerReference"));
        Assert.True(SchemaOrder.IndexOf(PropertyKind.Section, W + "pgSz") < SchemaOrder.IndexOf(PropertyKind.Section, W + "pgMar"));
        Assert.Null(SchemaOrder.IndexOf(PropertyKind.Run, W14 + "ligatures"));
    }

    [Fact]
    public void UnknownContentKeepsThePrefixesOfItsSource()
    {
        var root = XElement.Parse(
            "<w:p xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w14:thing w14:id=\"1\"/></w:p>");
        var kept = new UnknownContent(root.Elements().Single());
        Assert.Contains("w14:thing", kept.Element.ToString());
        Assert.Contains("xmlns:w14=", kept.Element.ToString());
        Assert.DoesNotContain("p1:", kept.Element.ToString());
        Assert.Null(kept.Element.Parent);
    }
}
