using System.Xml.Linq;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Model;

public sealed class SectionCopyAndOrientationTests
{
    private static readonly XNamespace W = LayoutModel.W;

    private static SectionProperties Section(string inner, string attributes = "") =>
        LayoutModel.Document("<w:p/><w:sectPr " + attributes + ">" + inner + "</w:sectPr>").Body.FinalSection!;

    private static SectionProperties Source()
    {
        var source = Section("<w:pgSz w:w=\"100\" w:h=\"200\"/><w:custom w:x=\"1\"/>", "w:rsidR=\"AAAA\"");
        source.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Header, HeaderFooterType.First, "rIdS"));
        return source;
    }

    [Fact]
    public void CopyContentFromReplacesTheContentOfTheSameObjectWithClones()
    {
        var source = Source();
        var target = Section("<w:cols w:num=\"2\"/><w:titlePg/>", "w:rsidR=\"BBBB\" w:old=\"x\"");
        target.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Footer, HeaderFooterType.Default, "rIdOld"));
        var targetObject = target;

        target.CopyContentFrom(source);

        Assert.Same(targetObject, target);
        Assert.Equal(["pgSz", "custom"], target.Children.Select(c => c.Name.LocalName));
        Assert.Null(target.Get(W + "cols"));
        Assert.Null(target.Get(W + "titlePg"));
        Assert.Single(target.UnknownAttributes);
        Assert.Equal("AAAA", target.UnknownAttributes[0].Value);
        var reference = Assert.Single(target.HeaderFooterReferences);
        Assert.Equal(("rIdS", HeaderFooterType.First, HeaderFooterKind.Header), (reference.RelationshipId, reference.Type, reference.Kind));

        for (var i = 0; i < source.Children.Count; i++)
        {
            Assert.NotSame(source.Children[i], target.Children[i]);
        }

        Assert.NotSame(source.UnknownAttributes[0], target.UnknownAttributes[0]);
        Assert.NotSame(source.HeaderFooterReferences[0], target.HeaderFooterReferences[0]);

        // The source changes afterwards; the copy does not follow.
        source.Get(W + "custom")!.SetAttributeValue(W + "x", "2");
        source.Set(W + "cols", new XElement(W + "cols"));
        source.HeaderFooterReferences[0].RelationshipId = "rIdChanged";
        source.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Header, HeaderFooterType.Even, "rIdMore"));
        source.UnknownAttributes.Clear();
        Assert.Equal("1", (string?)target.Get(W + "custom")!.Attribute(W + "x"));
        Assert.Null(target.Get(W + "cols"));
        Assert.Equal("rIdS", Assert.Single(target.HeaderFooterReferences).RelationshipId);
        Assert.Single(target.UnknownAttributes);
    }

    [Fact]
    public void CopyingASectionOntoItselfChangesNothing()
    {
        var section = Source();
        var child = section.Children[0];
        var reference = section.HeaderFooterReferences[0];

        section.CopyContentFrom(section);

        Assert.Same(child, section.Children[0]);
        Assert.Same(reference, section.HeaderFooterReferences[0]);
    }

    [Fact]
    public void LandscapeIsReadFromOrientWhenThereIsOneAndFromTheShapeWhenThereIsNot()
    {
        Assert.True(SectionPageSetup.Read(Section("<w:pgSz w:w=\"15840\" w:h=\"12240\"/>")).Landscape);
        Assert.False(SectionPageSetup.Read(Section("<w:pgSz w:w=\"12240\" w:h=\"15840\"/>")).Landscape);
        Assert.False(SectionPageSetup.Read(Section("<w:pgSz w:w=\"12240\" w:h=\"12240\"/>")).Landscape);

        // The attribute wins.
        Assert.False(SectionPageSetup.Read(Section("<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"portrait\"/>")).Landscape);
        Assert.True(SectionPageSetup.Read(Section("<w:pgSz w:w=\"12240\" w:h=\"15840\" w:orient=\"landscape\"/>")).Landscape);

        // A wide page with no orient is already landscape: the command does not swap it.
        var wide = SectionPageSetup.Read(Section("<w:pgSz w:w=\"15840\" w:h=\"12240\"/>"));
        Assert.Same(wide, wide.WithLandscape(true));
        var section = Section("<w:pgSz w:w=\"15840\" w:h=\"12240\"/>");
        wide.ApplyTo(section, PageSetupParts.Orientation);
        Assert.Equal(("15840", "12240", "landscape"), ((string?)section.Get(W + "pgSz")!.Attribute(W + "w"), (string?)section.Get(W + "pgSz")!.Attribute(W + "h"), (string?)section.Get(W + "pgSz")!.Attribute(W + "orient")));

        // No pgSz at all is the A4 portrait default.
        Assert.False(SectionPageSetup.Read(null).Landscape);
        Assert.Equal(PageGeometry.Read(null, "x", []).Width < PageGeometry.Read(null, "x", []).Height, !SectionPageSetup.Read(null).Landscape);
    }
}
