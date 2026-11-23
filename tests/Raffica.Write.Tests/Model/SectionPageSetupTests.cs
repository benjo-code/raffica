using System.Xml.Linq;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Model;

public sealed class SectionPageSetupTests
{
    private static readonly XNamespace W = LayoutModel.W;

    private const PageSetupParts All = PageSetupParts.Size | PageSetupParts.Orientation | PageSetupParts.Margins | PageSetupParts.Columns;

    private static SectionProperties Section(string inner) => LayoutModel.Document("<w:p/><w:sectPr>" + inner + "</w:sectPr>").Body.FinalSection!;

    private static string Attr(SectionProperties section, string element, string name) => (string?)section.Get(W + element)?.Attribute(W + name) ?? "<none>";

    [Fact]
    public void ReadWriteRoundTripsEveryField()
    {
        foreach (var setup in new[]
        {
            new SectionPageSetup(15840, 12240, true, 1000, 1100, 1200, 1300, 3, 400, true),
            new SectionPageSetup(12240, 15840, false, 1, 2, 3, 4, 1, 0, false),
        })
        {
            var section = new SectionProperties();
            setup.ApplyTo(section, All);
            Assert.Equal(setup, SectionPageSetup.Read(section));
        }
    }

    [Fact]
    public void OrientationSwapsWidthAndHeightAndWritesOrient()
    {
        var portrait = SectionPageSetup.Read(Section("<w:pgSz w:w=\"12240\" w:h=\"15840\"/>"));
        Assert.False(portrait.Landscape);

        var turned = portrait.WithLandscape(true);
        Assert.Equal((15840, 12240, true), (turned.WidthTwips, turned.HeightTwips, turned.Landscape));
        Assert.Equal(portrait, turned.WithLandscape(false));
        Assert.Same(portrait, portrait.WithLandscape(false));

        var section = Section("<w:pgSz w:w=\"12240\" w:h=\"15840\"/>");
        turned.ApplyTo(section, PageSetupParts.Orientation);
        Assert.Equal(("15840", "12240", "landscape"), (Attr(section, "pgSz", "w"), Attr(section, "pgSz", "h"), Attr(section, "pgSz", "orient")));

        turned.WithLandscape(false).ApplyTo(section, PageSetupParts.Orientation);
        Assert.Equal(("12240", "15840", "portrait"), (Attr(section, "pgSz", "w"), Attr(section, "pgSz", "h"), Attr(section, "pgSz", "orient")));
    }

    [Fact]
    public void UnknownAttributesAndTheGutterSurviveAWrite()
    {
        var section = Section(
            "<w:pgSz w:w=\"12240\" w:h=\"15840\" w:code=\"1\"/>"
            + "<w:pgMar w:top=\"1\" w:right=\"2\" w:bottom=\"3\" w:left=\"4\" w:header=\"567\" w:footer=\"568\" w:gutter=\"99\" w:custom=\"k\"/>"
            + "<w:cols w:num=\"2\" w:space=\"100\" w:equalWidth=\"0\"><w:col w:w=\"1000\"/><w:col w:w=\"2000\"/></w:cols>");
        var setup = SectionPageSetup.Read(section) with { TopTwips = 1440, Columns = 3 };
        setup.ApplyTo(section, All);

        Assert.Equal("1", Attr(section, "pgSz", "code"));
        Assert.Equal(("567", "568", "99", "k", "1440"), (Attr(section, "pgMar", "header"), Attr(section, "pgMar", "footer"), Attr(section, "pgMar", "gutter"), Attr(section, "pgMar", "custom"), Attr(section, "pgMar", "top")));
        Assert.Equal("3", Attr(section, "cols", "num"));
        Assert.Empty(section.Get(W + "cols")!.Elements(W + "col"));
    }

    [Fact]
    public void ANewMarginElementGetsEveryAttributeAndSitsInSchemaOrder()
    {
        var section = Section("<w:cols w:num=\"2\"/><w:titlePg/>");
        new SectionPageSetup(11906, 16838, false, 1, 2, 3, 4, 1, 720, false).ApplyTo(section, PageSetupParts.Size | PageSetupParts.Margins);

        Assert.Equal(["pgSz", "pgMar", "cols", "titlePg"], section.Children.Select(c => c.Name.LocalName));
        Assert.Equal(7, section.Get(W + "pgMar")!.Attributes().Count());
    }

    [Fact]
    public void DefaultsEqualPageGeometryReadOfNull()
    {
        var geometry = PageGeometry.Read(null, "x", []);
        var setup = SectionPageSetup.Read(null);

        Assert.Equal((int)Math.Round(geometry.Width * 20), setup.WidthTwips);
        Assert.Equal((int)Math.Round(geometry.Height * 20), setup.HeightTwips);
        Assert.Equal((int)Math.Round(geometry.Top * 20), setup.TopTwips);
        Assert.Equal((int)Math.Round(geometry.Bottom * 20), setup.BottomTwips);
        Assert.Equal((int)Math.Round(geometry.Left * 20), setup.LeftTwips);
        Assert.Equal((int)Math.Round(geometry.Right * 20), setup.RightTwips);
        Assert.Equal(geometry.Columns.Count, setup.Columns);
        Assert.False(setup.Landscape);
        Assert.False(setup.ColumnLine);

        // A w:cols with no w:space: the layout's gap is the setup's gap.
        var columns = Section("<w:cols w:num=\"2\"/>");
        var read = PageGeometry.Read(columns, "x", []);
        var gap = read.Columns.Lefts[1] - read.Columns.Lefts[0] - read.Columns.Widths[0];
        Assert.Equal((int)Math.Round(gap * 20), SectionPageSetup.Read(columns).ColumnSpaceTwips);
    }

    [Fact]
    public void OnlyTheNamedPartsAreWritten()
    {
        const string Original = "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1\" w:right=\"2\" w:bottom=\"3\" w:left=\"4\" w:header=\"5\" w:footer=\"6\" w:gutter=\"7\"/><w:cols w:num=\"2\" w:space=\"9\"/>";
        var setup = new SectionPageSetup(1, 2, true, 10, 11, 12, 13, 4, 14, true);
        var untouched = Section(Original);

        foreach (var part in new[] { PageSetupParts.Size, PageSetupParts.Orientation, PageSetupParts.Margins, PageSetupParts.Columns, PageSetupParts.None })
        {
            var section = Section(Original);
            setup.ApplyTo(section, part);
            var changed = section.Children.Select(c => c.Name).Where(n => !XNode.DeepEquals(section.Get(n), untouched.Get(n))).Select(n => n.LocalName).ToList();
            string[] expected = part switch
            {
                PageSetupParts.Size or PageSetupParts.Orientation => ["pgSz"],
                PageSetupParts.Margins => ["pgMar"],
                PageSetupParts.Columns => ["cols"],
                _ => [],
            };
            Assert.Equal(expected, changed);
        }

        var sizeOnly = Section(Original);
        setup.ApplyTo(sizeOnly, PageSetupParts.Size);
        Assert.Equal("<none>", Attr(sizeOnly, "pgSz", "orient"));
    }
}
