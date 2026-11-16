using Raffica.Write.Commands;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The lists of the style and font pickers (Task 11 of milestone 4.3b): the rules of what is listed, and that the font list never loads a face.</summary>
public sealed class StylePickerSourceTests
{
    private sealed class FakeFonts(params string[] families) : ISystemFonts
    {
        public int Matches { get; private set; }

        public FontFace? Match(string family, bool bold, bool italic)
        {
            Matches++;
            return null;
        }

        public IEnumerable<string> Families() => families;
    }

    private static Raffica.Write.Model.WordDocument Model(string? styles, params string[] paragraphs) =>
        DocxFile.Open(StyledDocx.Bytes(styles, paragraphs)).Model;

    [Fact]
    public void ParagraphStylesAreListedByTheRules()
    {
        // Used: UsedPlain (not marked, but a paragraph names it). Not listed: Quote (semi-hidden), Gone (hidden), Latent (not marked, unused).
        var model = Model(StyledDocx.Styles, "<w:pPr><w:pStyle w:val=\"UsedPlain\"/></w:pPr><w:r><w:t>x</w:t></w:r>");

        var items = StylePickerSource.Paragraph(model);

        // The default style first, then uiPriority (9, 9, 10, and 99 for the one without), then the name (heading 1 before Zeta).
        Assert.Equal(["Normal", "Heading1", "Zeta", "Title", "UsedPlain"], items.Select(i => i.Id));
        Assert.Equal(["Normal", "heading 1", "Zeta", "Title", "Used plain"], items.Select(i => i.Label));
    }

    [Fact]
    public void CharacterStylesFollowTheSameRules()
    {
        var model = Model(StyledDocx.Styles, "<w:r><w:rPr><w:rStyle w:val=\"LatentChar\"/></w:rPr><w:t>x</w:t></w:r>", "<w:r><w:t>y</w:t></w:r>");

        var items = StylePickerSource.Character(model);

        // The default character style first, then Strong (22), then the used unmarked one (99); Faint is semi-hidden.
        Assert.Equal(["DefaultParagraphFont", "Strong", "LatentChar"], items.Select(i => i.Id));
    }

    [Fact]
    public void AnUndefinedButUsedStyleIsListedAsItsId()
    {
        var model = Model(
            StyledDocx.Styles,
            "<w:pPr><w:pStyle w:val=\"Ghost\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"GhostChar\"/></w:rPr><w:t>x</w:t></w:r>");

        var paragraph = StylePickerSource.Paragraph(model);
        var character = StylePickerSource.Character(model);

        Assert.Equal(new Raffica.Shell.Frame.PickerItem("Ghost", "Ghost"), paragraph[^1]);
        Assert.Equal(new Raffica.Shell.Frame.PickerItem("GhostChar", "GhostChar"), character[^1]);
        Assert.DoesNotContain(paragraph, i => i.Id == "GhostChar");
    }

    [Fact]
    public void NoStylesPartGivesAnEmptyList()
    {
        var model = Model(null, "<w:pPr><w:pStyle w:val=\"Ghost\"/></w:pPr><w:r><w:t>x</w:t></w:r>");

        Assert.Empty(StylePickerSource.Paragraph(model));
        Assert.Empty(StylePickerSource.Character(model));
    }

    [Fact]
    public void FontListNeverLoadsAFace()
    {
        var system = new FakeFonts("Zebra Sans", "Arial", "calibri", ".Hidden UI", "  ", "Arial");
        var catalog = FontCatalog.CreateShipped(system);

        var items = FontPickerSource.Families(catalog);

        // The shipped families first (Calibri once: the installed one of the same name is a duplicate), then the installed ones alphabetically; names only.
        Assert.Equal(["Calibri", "Cambria", "Arial", "Zebra Sans"], items.Select(i => i.Id));
        Assert.Equal(items.Select(i => i.Id), items.Select(i => i.Label));
        Assert.Equal(0, catalog.LoadedFaces);
        Assert.Equal(0, system.Matches);
    }
}
