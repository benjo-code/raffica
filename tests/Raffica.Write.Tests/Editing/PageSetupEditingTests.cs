using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Page setup per section, for the whole document or from here on: what the model holds, one history step, the layout against a full layout and the SamePaper rule.</summary>
public sealed class PageSetupEditingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    private static DocumentLayout Lay(WordDocument document) => Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));

    // Six paragraphs, three sections: a1 a2 | b1 b2 | c1 c2 (a2 and b2 carry the breaks, the body's own sectPr ends the last). Letter pages with 1 inch margins.
    private static WordDocument Three() =>
        Document(P("a1") + PEnd("a2", Sect()) + P("b1") + PEnd("b2", Sect()) + P("c1") + P("c2") + Sect());

    private static SectionPageSetup Setup(WordDocument document, int section) => SectionPageSetup.Read(SectionList.Of(document)[section].Properties);

    private static SectionPageSetup A4 => SectionPageSetup.Read(null);

    private static PageSetupChange Size(SectionPageSetup value) => new(value, PageSetupParts.Size);

    private static List<string> Snapshot(WordDocument document) =>
        [.. SectionList.Of(document).Select(s => string.Join("|", s.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting)).Concat(s.Properties.UnknownAttributes.Select(a => a.ToString()))))];

    private static void LayoutAgrees(DocumentLayout before, WordDocument document, string label)
    {
        before.Resection(0);
        LayoutOracle.AssertEqualToFull(before, document, label: label);
    }

    [Fact]
    public void WholeDocumentChangesEverySectionAndTheFinalOne()
    {
        var document = Three();
        var result = Editor(document).ApplyPageSetup(At(document, 2), Size(A4), ApplyScope.WholeDocument);

        Assert.NotNull(result);
        Assert.Equal(3, SectionList.Of(document).Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(11906, Setup(document, i).WidthTwips);
            Assert.Equal(16838, Setup(document, i).HeightTwips);
        }
    }

    [Fact]
    public void ThisSectionChangesOnlyThatSection()
    {
        var document = Three();

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 2), Size(A4), ApplyScope.ThisSection));

        Assert.Equal(12240, Setup(document, 0).WidthTwips);
        Assert.Equal(11906, Setup(document, 1).WidthTwips);
        Assert.Equal(12240, Setup(document, 2).WidthTwips);
    }

    [Fact]
    public void FromHereSplitsAndChangesTheNewAndEveryLaterSection()
    {
        var document = Three();

        // The caret in a2 (the last block of section 0): the section splits before it, so a1 stays alone and a2, b and c follow.
        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 1), Size(A4), ApplyScope.FromHere));

        var sections = SectionList.Of(document);
        Assert.Equal(4, sections.Count);
        Assert.Equal(12240, Setup(document, 0).WidthTwips);
        for (var i = 1; i < 4; i++)
        {
            Assert.Equal(11906, Setup(document, i).WidthTwips);
        }

        Assert.Equal("nextPage", (string?)sections[1].Properties.Get(W + "type")?.Attribute(W + "val"));
    }

    [Fact]
    public void FromHereAtTheStartOfASectionMakesNoSplit()
    {
        var document = Three();

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 2), Size(A4), ApplyScope.FromHere));

        Assert.Equal(3, SectionList.Of(document).Count);
        Assert.Equal(12240, Setup(document, 0).WidthTwips);
        Assert.Equal(11906, Setup(document, 1).WidthTwips);
        Assert.Equal(11906, Setup(document, 2).WidthTwips);
    }

    [Fact]
    public void FromHereOnColumnsUsesAContinuousBreak()
    {
        var document = Three();
        var two = A4 with { Columns = 2, ColumnSpaceTwips = 600, ColumnLine = true };

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 5), new PageSetupChange(two, PageSetupParts.Columns), ApplyScope.FromHere));

        var sections = SectionList.Of(document);
        Assert.Equal(4, sections.Count);
        Assert.Equal("continuous", (string?)sections[3].Properties.Get(W + "type")?.Attribute(W + "val"));
        Assert.Equal(2, Setup(document, 3).Columns);
        Assert.Equal(1, Setup(document, 2).Columns);
        Assert.Equal(12240, Setup(document, 3).WidthTwips);
    }

    [Fact]
    public void OnlyTheChangedPartIsWritten()
    {
        var document = Three();
        var sizeBefore = SectionList.Of(document)[1].Properties.Get(W + "pgSz")!.ToString();
        var margins = A4 with { TopTwips = 700, BottomTwips = 800, LeftTwips = 900, RightTwips = 1000 };

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 2), new PageSetupChange(margins, PageSetupParts.Margins), ApplyScope.ThisSection));

        var properties = SectionList.Of(document)[1].Properties;
        Assert.Equal(sizeBefore, properties.Get(W + "pgSz")!.ToString());
        Assert.Null(properties.Get(W + "cols"));
        Assert.Equal(700, Setup(document, 1).TopTwips);
        Assert.Equal(1000, Setup(document, 1).RightTwips);
        Assert.Equal(1440, Setup(document, 0).TopTwips);
    }

    [Fact]
    public void OrientationTwiceIsTheIdentity()
    {
        var document = Three();
        var editor = Editor(document);
        var original = Setup(document, 1);
        var landscape = new PageSetupChange(original.WithLandscape(true), PageSetupParts.Orientation);

        Assert.NotNull(editor.ApplyPageSetup(At(document, 2), landscape, ApplyScope.ThisSection));
        var turned = Setup(document, 1);
        Assert.True(turned.Landscape);
        Assert.Equal(original.HeightTwips, turned.WidthTwips);
        Assert.Equal(original.WidthTwips, turned.HeightTwips);

        Assert.NotNull(editor.ApplyPageSetup(At(document, 2), new PageSetupChange(turned.WithLandscape(false), PageSetupParts.Orientation), ApplyScope.ThisSection));
        Assert.Equal(original, Setup(document, 1));

        // Asking for landscape again once it is on changes nothing.
        Assert.NotNull(editor.ApplyPageSetup(At(document, 2), landscape, ApplyScope.ThisSection));
        Assert.Null(editor.ApplyPageSetup(At(document, 2), landscape, ApplyScope.ThisSection));
    }

    [Fact]
    public void OrientationKeepsTheSizeOfALaterSectionThatDiffers()
    {
        var document = Document(P("a1") + PEnd("a2", Sect()) + P("b1") + Sect(width: 500f, height: 700f));

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 0), new PageSetupChange(Setup(document, 0).WithLandscape(true), PageSetupParts.Orientation), ApplyScope.WholeDocument));

        Assert.Equal(15840, Setup(document, 0).WidthTwips);
        Assert.Equal(14000, Setup(document, 1).WidthTwips);
        Assert.Equal(10000, Setup(document, 1).HeightTwips);
        Assert.True(Setup(document, 1).Landscape);
    }

    [Fact]
    public void ASectionWithoutPgSzGetsAFullSet()
    {
        var document = Document(P("a") + "<w:sectPr/>");

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 0), new PageSetupChange(A4.WithLandscape(true), PageSetupParts.Size | PageSetupParts.Orientation), ApplyScope.WholeDocument));

        var size = document.Body.FinalSection!.Get(W + "pgSz")!;
        Assert.Equal("16838", (string?)size.Attribute(W + "w"));
        Assert.Equal("11906", (string?)size.Attribute(W + "h"));
        Assert.Equal("landscape", (string?)size.Attribute(W + "orient"));
    }

    [Fact]
    public void ASectionWithoutPgMarGetsAllSevenAttributes()
    {
        var document = Document(P("a") + "<w:sectPr/>");

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 0), new PageSetupChange(A4, PageSetupParts.Margins), ApplyScope.WholeDocument));

        var margins = document.Body.FinalSection!.Get(W + "pgMar")!;
        Assert.Equal(7, margins.Attributes().Count());
    }

    [Fact]
    public void HeaderFooterDistancesAndTheGutterNeverChange()
    {
        var document = Document(P("a") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"500\" w:footer=\"600\" w:gutter=\"123\"/></w:sectPr>");
        var value = A4 with { TopTwips = 100, BottomTwips = 200, LeftTwips = 300, RightTwips = 400 };

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 0), new PageSetupChange(value, PageSetupParts.Margins | PageSetupParts.Size), ApplyScope.WholeDocument));

        var margins = document.Body.FinalSection!.Get(W + "pgMar")!;
        Assert.Equal("500", (string?)margins.Attribute(W + "header"));
        Assert.Equal("600", (string?)margins.Attribute(W + "footer"));
        Assert.Equal("123", (string?)margins.Attribute(W + "gutter"));
        Assert.Equal("100", (string?)margins.Attribute(W + "top"));
    }

    [Fact]
    public void ACustomSizeAndExactMargins()
    {
        var document = Three();
        var custom = A4 with { WidthTwips = 9000, HeightTwips = 13000, TopTwips = 1111, BottomTwips = 1222, LeftTwips = 1333, RightTwips = 1444 };

        Assert.NotNull(Editor(document).ApplyPageSetup(At(document, 5), new PageSetupChange(custom, PageSetupParts.Size | PageSetupParts.Margins), ApplyScope.WholeDocument));

        var read = Setup(document, 2);
        Assert.Equal(9000, read.WidthTwips);
        Assert.Equal(13000, read.HeightTwips);
        Assert.Equal((1111, 1222, 1333, 1444), (read.TopTwips, read.BottomTwips, read.LeftTwips, read.RightTwips));
    }

    [Fact]
    public void PresetsHaveTheSpecValues()
    {
        Assert.Equal((11906, 16838), PagePresets.Size("A4"));
        Assert.Equal((12240, 15840), PagePresets.Size("Letter"));
        Assert.Equal((16838, 23811), PagePresets.Size("A3"));
        Assert.Equal((8391, 11906), PagePresets.Size("A5"));
        Assert.Equal((12240, 20160), PagePresets.Size("Legal"));
        Assert.Throws<ArgumentException>(() => PagePresets.Size("Tabloid"));

        Assert.Equal(new PageMargins(1440, 1440, 1440, 1440), PagePresets.Margins("Normal", inch: true));
        Assert.Equal(new PageMargins(1417, 1417, 1417, 1417), PagePresets.Margins("Normal", inch: false));
        Assert.Equal(new PageMargins(720, 720, 720, 720), PagePresets.Margins("Narrow", inch: true));
        Assert.Equal(new PageMargins(720, 720, 720, 720), PagePresets.Margins("Narrow", inch: false));
        Assert.Equal(new PageMargins(1440, 1440, 1080, 1080), PagePresets.Margins("Moderate", inch: true));
        Assert.Equal(new PageMargins(1417, 1417, 1083, 1083), PagePresets.Margins("Moderate", inch: false));
        Assert.Equal(new PageMargins(1440, 1440, 2880, 2880), PagePresets.Margins("Wide", inch: true));
        Assert.Equal(new PageMargins(1417, 1417, 2835, 2835), PagePresets.Margins("Wide", inch: false));
        Assert.Throws<ArgumentException>(() => PagePresets.Margins("Huge", inch: true));
    }

    [Fact]
    public void UsesInchesFollowsBlankPaper()
    {
        foreach (var name in new[] { "US", "CA", "DE", "GB", "FR", "JP", "MX", "AU" })
        {
            var region = new RegionInfo(name);
            Assert.Equal(BlankPaper.For(region) == PaperSize.Letter, PagePresets.UsesInches(region));
        }

        Assert.True(PagePresets.UsesInches(new RegionInfo("US")));
        Assert.False(PagePresets.UsesInches(new RegionInfo("DE")));
    }

    [Fact]
    public void OneUndoStepPerCommand()
    {
        var document = Three();
        var editor = Editor(document);
        var before = Snapshot(document);
        var blocks = document.Body.Blocks.Count;

        Assert.NotNull(editor.ApplyPageSetup(At(document, 1), Size(A4), ApplyScope.FromHere));
        Assert.Equal(4, SectionList.Of(document).Count);
        var after = Snapshot(document);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(blocks, document.Body.Blocks.Count);
        Assert.Null(editor.History.Undo());
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, Snapshot(document));

        Assert.NotNull(editor.ApplyPageSetup(At(document, 5), Size(A4 with { WidthTwips = 12240, HeightTwips = 15840 }), ApplyScope.WholeDocument));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(after, Snapshot(document));
    }

    [Fact]
    public void AStalePositionThrowsBeforeAnyChange()
    {
        var document = Three();
        var editor = Editor(document);
        var stale = new TextPosition(StoryRef.Body, new Paragraph(), 0);
        var before = Snapshot(document);

        Assert.Throws<StalePositionException>(() => editor.ApplyPageSetup(stale, Size(A4), ApplyScope.FromHere));

        Assert.Equal(before, Snapshot(document));
    }

    [Theory]
    [InlineData(ApplyScope.ThisSection, 2)]
    [InlineData(ApplyScope.WholeDocument, 1)]
    [InlineData(ApplyScope.FromHere, 3)]
    [InlineData(ApplyScope.FromHere, 5)]
    public void TheLayoutEqualsAFullLayoutAfterEach(ApplyScope scope, int block)
    {
        var document = Three();
        var layout = Lay(document);
        var editor = Editor(document);

        Assert.NotNull(editor.ApplyPageSetup(At(document, block), new PageSetupChange(A4 with { Columns = 2 }, PageSetupParts.Size | PageSetupParts.Columns), scope));
        LayoutAgrees(layout, document, "apply " + scope);
        Assert.Equal(Pages(layout).Count, Pages(Lay(document)).Count);

        Assert.NotNull(editor.History.Undo());
        LayoutAgrees(layout, document, "undo " + scope);
        Assert.NotNull(editor.History.Redo());
        LayoutAgrees(layout, document, "redo " + scope);
    }

    [Fact]
    public void AContinuousSectionAfterASizeChangeStartsANewPage()
    {
        var document = Document(P("a1") + P("a2") + P("a3") + Sect());
        var editor = Editor(document);
        Assert.Single(Pages(Lay(document)));

        Assert.NotNull(editor.InsertSectionBreak(At(document, 2), SectionBreakKind.Continuous));
        Assert.Single(Pages(Lay(document)));

        var layout = Lay(document);
        Assert.NotNull(editor.ApplyPageSetup(At(document, 2), Size(A4), ApplyScope.ThisSection));

        LayoutAgrees(layout, document, "size change");
        Assert.Equal(2, Pages(Lay(document)).Count);
    }
}
