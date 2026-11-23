using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Own or same as previous, a different first page and left and right pages: what the model holds afterwards, what a laid-out copy draws (the last line of a page is its footer, the first its header),
/// the layout restart against a full layout, the sharing view and the history (one step per command).
/// </summary>
public sealed class HeaderFooterEditingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private sealed class StillClock : TimeProvider
    {
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => 0;
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain, new StillClock());

    private static string Pg(string text) => P(text, "<w:pageBreakBefore/>");

    private static string Foot(string text) => FooterPart(Para(Text(text)));

    private static string Head(string text) => HeaderPart(Para(Text(text)));

    private static Dictionary<string, string> Parts(params (string Id, string Xml)[] parts) => parts.ToDictionary(p => p.Id, p => p.Xml, StringComparer.Ordinal);

    private static DocumentLayout Lay(WordDocument document) => Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));

    private static string[] Feet(WordDocument document) => [.. Pages(Lay(document)).Select(page => Lines(page)[^1])];

    private static string[] Tops(WordDocument document) => [.. Pages(Lay(document)).Select(page => Lines(page)[0])];

    // The incremental layout after the command (the restart the session does) against a full one.
    private static void LayoutAgrees(DocumentLayout before, WordDocument document, string label)
    {
        before.Resection(0);
        LayoutOracle.AssertEqualToFull(before, document, label: label);
    }

    private static List<string> Snapshot(WordDocument document) =>
        [.. SectionList.Of(document).Select(s => string.Join("|", s.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting)).Concat(s.Properties.HeaderFooterReferences.Select(r => $"{r.Kind}{r.Type}{r.RelationshipId}"))))];

    private static string TextOf(WordDocument document, string id) =>
        string.Concat(document.HeadersFooters[id].Content.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content.OfType<TextContent>()).Select(t => t.Text));

    private static HeaderFooterReference? Own(WordDocument document, int section, HeaderFooterKind kind, HeaderFooterType type) =>
        SectionList.Of(document)[section].Properties.HeaderFooterReferences.FirstOrDefault(r => r.Kind == kind && r.Type == type);

    // Two sections of two pages each: the first has the default footer "A" (and header "H"), the second sets nothing.
    private static WordDocument Two() =>
        Document(
            P("p1") + PEnd("p2", Sect(Footer("default", "fA") + Header("default", "hA")), "<w:pageBreakBefore/>") + P("p3") + Pg("p4") + Sect(),
            Parts(("fA", Foot("A")), ("hA", Head("H"))));

    [Fact]
    public void OwnCopiesTheInheritedContentSoNothingChanges()
    {
        var document = Two();
        var layout = Lay(document);
        var feet = Feet(document);
        var tops = Tops(document);
        var editor = Editor(document);

        var result = editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, true);

        Assert.NotNull(result);
        Assert.NotNull(result.Sections);
        var reference = Own(document, 1, HeaderFooterKind.Footer, HeaderFooterType.Default)!;
        Assert.NotEqual("fA", reference.RelationshipId);
        Assert.Equal("A", TextOf(document, reference.RelationshipId));
        Assert.Equal("ftr", document.HeadersFooters[reference.RelationshipId].Content.Root.Name.LocalName);
        Assert.Equal("A", TextOf(document, "fA"));
        Assert.Equal(feet, Feet(document));
        Assert.Equal(tops, Tops(document));
        LayoutAgrees(layout, document, "own footer");
    }

    [Fact]
    public void OwnOfAHeaderMakesAHeaderPart()
    {
        var document = Two();
        var editor = Editor(document);

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Header, HeaderFooterType.Default, true));

        var reference = Own(document, 1, HeaderFooterKind.Header, HeaderFooterType.Default)!;
        Assert.Equal("hdr", document.HeadersFooters[reference.RelationshipId].Content.Root.Name.LocalName);
        Assert.Equal("H", TextOf(document, reference.RelationshipId));
        Assert.Equal(1, SectionList.Of(document)[1].Properties.HeaderFooterReferences.Count(r => r.Kind == HeaderFooterKind.Header));
    }

    [Fact]
    public void SameAsPreviousRemovesTheReferenceAndKeepsThePart()
    {
        var document = Two();
        var editor = Editor(document);
        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, true));
        var id = Own(document, 1, HeaderFooterKind.Footer, HeaderFooterType.Default)!.RelationshipId;
        var layout = Lay(document);
        var parts = document.HeadersFooters.Count;

        var result = editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, false);

        Assert.NotNull(result);
        Assert.Null(Own(document, 1, HeaderFooterKind.Footer, HeaderFooterType.Default));
        Assert.Equal(parts, document.HeadersFooters.Count);
        Assert.True(document.HeadersFooters.ContainsKey(id));
        Assert.Equal(["A", "A", "A", "A"], Feet(document));
        LayoutAgrees(layout, document, "same as previous");
    }

    [Fact]
    public void TheFirstSectionCannotBeSameAsPrevious()
    {
        var document = Two();
        var editor = Editor(document);
        var before = Snapshot(document);

        Assert.Null(editor.SetOwnHeaderFooter(0, HeaderFooterKind.Footer, HeaderFooterType.Default, false));
        Assert.Null(editor.SetOwnHeaderFooter(0, HeaderFooterKind.Footer, HeaderFooterType.Default, true));
        Assert.Null(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, false));
        Assert.Null(editor.SetOwnHeaderFooter(5, HeaderFooterKind.Footer, HeaderFooterType.Default, true));

        Assert.Equal(before, Snapshot(document));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void DifferentFirstPageCreatesAnEmptyFirstPartOnlyWhereNoneResolves()
    {
        var document = Two();
        var layout = Lay(document);
        var editor = Editor(document);
        var parts = document.HeadersFooters.Count;

        Assert.NotNull(editor.SetDifferentFirstPage(0, true));

        var section = SectionList.Of(document)[0].Properties;
        Assert.True(section.GetFlag(W + "titlePg"));
        var footer = Own(document, 0, HeaderFooterKind.Footer, HeaderFooterType.First)!;
        var header = Own(document, 0, HeaderFooterKind.Header, HeaderFooterType.First)!;
        Assert.Equal(parts + 2, document.HeadersFooters.Count);
        Assert.Equal(string.Empty, TextOf(document, footer.RelationshipId));
        Assert.Equal(string.Empty, TextOf(document, header.RelationshipId));
        Assert.Equal([string.Empty, "A", "A", "A"], Feet(document));
        LayoutAgrees(layout, document, "different first page");
    }

    [Fact]
    public void DifferentFirstPageUsesAnInheritedFirstPart()
    {
        var document = Document(
            P("p1") + PEnd("p2", Sect(Footer("first", "fF") + Header("first", "hF") + "<w:titlePg/>"), "<w:pageBreakBefore/>") + P("p3") + Pg("p4") + Sect(),
            Parts(("fF", Foot("F")), ("hF", Head("G"))));
        var editor = Editor(document);
        var parts = document.HeadersFooters.Count;

        Assert.NotNull(editor.SetDifferentFirstPage(1, true));

        Assert.Equal(parts, document.HeadersFooters.Count);
        Assert.Null(Own(document, 1, HeaderFooterKind.Footer, HeaderFooterType.First));
        Assert.Null(Own(document, 1, HeaderFooterKind.Header, HeaderFooterType.First));
        Assert.Equal("F", Feet(document)[2]);
    }

    [Fact]
    public void TurningItOffClearsTitlePgAndKeepsTheParts()
    {
        var document = Two();
        var editor = Editor(document);
        Assert.NotNull(editor.SetDifferentFirstPage(0, true));
        var parts = document.HeadersFooters.Count;
        var layout = Lay(document);

        Assert.NotNull(editor.SetDifferentFirstPage(0, false));

        Assert.False(SectionList.Of(document)[0].Properties.GetFlag(W + "titlePg"));
        Assert.Null(SectionList.Of(document)[0].Properties.Get(W + "titlePg"));
        Assert.Equal(parts, document.HeadersFooters.Count);
        Assert.Equal(["A", "A", "A", "A"], Feet(document));
        Assert.Null(editor.SetDifferentFirstPage(0, false));
        LayoutAgrees(layout, document, "first page off");
    }

    [Fact]
    public void LeftAndRightPagesCopiesTheDefaultOfSectionsWithAnOwnDefaultOnly()
    {
        var document = Two();
        var layout = Lay(document);
        var editor = Editor(document);
        var parts = document.HeadersFooters.Count;

        Assert.NotNull(editor.SetLeftRightPages(true));

        Assert.True(document.Settings!.EvenAndOddHeaders);
        var first = SectionList.Of(document)[0].Properties.HeaderFooterReferences;
        Assert.Equal(2, first.Count(r => r.Type == HeaderFooterType.Even));
        Assert.Empty(SectionList.Of(document)[1].Properties.HeaderFooterReferences);
        Assert.Equal(parts + 2, document.HeadersFooters.Count);
        var even = Own(document, 0, HeaderFooterKind.Footer, HeaderFooterType.Even)!;
        Assert.Equal("A", TextOf(document, even.RelationshipId));
        Assert.NotEqual("fA", even.RelationshipId);
        LayoutAgrees(layout, document, "left and right pages");
    }

    [Fact]
    public void InheritingSectionsInheritTheEvenPart()
    {
        var document = Two();
        var editor = Editor(document);

        Assert.NotNull(editor.SetLeftRightPages(true));

        Assert.Equal(["A", "A", "A", "A"], Feet(document));
        var sharing = editor.SharingOf(HeaderFooterKind.Footer, HeaderFooterType.Even, 1);
        Assert.Equal(0, sharing.OwnerSection);
        Assert.Equal([0, 1], sharing.Sections);
    }

    [Fact]
    public void TurningLeftAndRightPagesOffLeavesTheEvenParts()
    {
        var document = Two();
        var editor = Editor(document);
        Assert.NotNull(editor.SetLeftRightPages(true));
        var parts = document.HeadersFooters.Count;
        var references = Snapshot(document);

        Assert.NotNull(editor.SetLeftRightPages(false));

        Assert.False(document.Settings!.EvenAndOddHeaders);
        Assert.Equal(parts, document.HeadersFooters.Count);
        Assert.Equal(references, Snapshot(document));
        Assert.Null(editor.SetLeftRightPages(false));
    }

    [Fact]
    public async Task AMissingSettingsPartIsCreated()
    {
        using var folder = new TempFolder();
        var source = folder.File("nosettings.docx");
        File.WriteAllBytes(source, WithoutSettings(BlankDocument.Create(PaperSize.A4)));
        using (var original = DocxFile.Open(source))
        {
            Assert.Null(original.Model.Settings);
            var editor = Editor(original.Model);

            Assert.NotNull(editor.SetLeftRightPages(true));
            Assert.True(original.Model.Settings!.EvenAndOddHeaders);
            await original.SaveAsync(folder.File("saved.docx"), TestContext.Current.CancellationToken);
        }

        using var reopened = DocxFile.Open(folder.File("saved.docx"));
        Assert.True(reopened.Model.Settings!.EvenAndOddHeaders);
    }

    [Fact]
    public void SharingListsEverySectionThatResolvesToThePart()
    {
        var document = Document(
            P("a") + PEnd("b", Sect(Footer("default", "fA")), "<w:pageBreakBefore/>") + PEnd("c", Sect(), "<w:pageBreakBefore/>") + PEnd("d", Sect(Footer("default", "fB")), "<w:pageBreakBefore/>") + P("e") + Sect(),
            Parts(("fA", Foot("A")), ("fB", Foot("B"))));
        var editor = Editor(document);

        var shared = editor.SharingOf(HeaderFooterKind.Footer, HeaderFooterType.Default, 1);
        var own = editor.SharingOf(HeaderFooterKind.Footer, HeaderFooterType.Default, 3);
        var none = editor.SharingOf(HeaderFooterKind.Header, HeaderFooterType.Default, 3);

        Assert.Equal(0, shared.OwnerSection);
        Assert.Equal([0, 1], shared.Sections);
        Assert.Equal(2, own.OwnerSection);
        Assert.Equal([2, 3], own.Sections);
        Assert.Equal(-1, none.OwnerSection);
        Assert.Empty(none.Sections);
    }

    [Fact]
    public void EachCommandIsOneUndoStep()
    {
        var document = Two();
        var editor = Editor(document);
        var start = Snapshot(document);
        var parts = document.HeadersFooters.Count;

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, true));
        var afterOwn = Snapshot(document);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, Snapshot(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(afterOwn, Snapshot(document));

        Assert.NotNull(editor.SetDifferentFirstPage(0, true));
        var afterFirst = Snapshot(document);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(afterOwn, Snapshot(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(afterFirst, Snapshot(document));

        Assert.NotNull(editor.SetLeftRightPages(true));
        var afterEven = Snapshot(document);
        Assert.True(document.Settings!.EvenAndOddHeaders);
        Assert.NotNull(editor.History.Undo());
        Assert.False(document.Settings.EvenAndOddHeaders);
        Assert.Equal(afterFirst, Snapshot(document));
        Assert.NotNull(editor.History.Redo());
        Assert.True(document.Settings.EvenAndOddHeaders);
        Assert.Equal(afterEven, Snapshot(document));

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(editor.History.Undo());
        }

        Assert.Equal(start, Snapshot(document));
        Assert.False(editor.History.CanUndo);
        Assert.True(document.HeadersFooters.Count > parts);
    }

    [Fact]
    public void TheLayoutEqualsAFullLayoutAfterEach()
    {
        var document = Two();
        var layout = Lay(document);
        var editor = Editor(document);

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Header, HeaderFooterType.Default, true));
        LayoutAgrees(layout, document, "own header");
        Assert.NotNull(editor.SetDifferentFirstPage(1, true));
        LayoutAgrees(layout, document, "first page");
        Assert.NotNull(editor.SetLeftRightPages(true));
        LayoutAgrees(layout, document, "left and right");
        Assert.NotNull(editor.History.Undo());
        LayoutAgrees(layout, document, "undo left and right");
        Assert.NotNull(editor.History.Undo());
        LayoutAgrees(layout, document, "undo first page");
        Assert.NotNull(editor.History.Undo());
        LayoutAgrees(layout, document, "undo own header");
    }

    [Fact]
    public void ABodyWithoutAFinalSectionGetsOneForTheCommand()
    {
        var document = Document(P("p1") + PEnd("p2", Sect(), "<w:pageBreakBefore/>") + P("p3"));
        Assert.Null(document.Body.FinalSection);
        var editor = Editor(document);

        Assert.NotNull(editor.SetDifferentFirstPage(1, true));

        Assert.NotNull(document.Body.FinalSection);
        Assert.True(document.Body.FinalSection.GetFlag(W + "titlePg"));
        Assert.NotNull(editor.History.Undo());
        Assert.False(document.Body.FinalSection.GetFlag(W + "titlePg"));
    }

    private const string OneCellTable = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

    private static Paragraph CellParagraph(WordDocument document)
    {
        var table = document.Body.Blocks.OfType<Table>().First();
        var cell = ((TableRow)table.Rows[0]).Items.OfType<TableCell>().Single();
        return (Paragraph)cell.Blocks[0];
    }

    [Fact]
    public void UndoAndRedoReturnTheCaretToWhereTheUserWas()
    {
        var document = Two();
        var editor = Editor(document);
        var paragraph = (Paragraph)document.Body.Blocks[2];
        var caret = new TextPosition(StoryRef.Body, paragraph, 1);

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, true, caret));
        Assert.NotNull(editor.SetDifferentFirstPage(1, true, caret));
        Assert.NotNull(editor.SetLeftRightPages(true, caret));

        for (var i = 0; i < 3; i++)
        {
            var undone = editor.History.Undo();
            Assert.Same(paragraph, undone!.Caret.Paragraph);
            Assert.Equal(1, undone.Caret.Offset);
        }

        for (var i = 0; i < 3; i++)
        {
            var redone = editor.History.Redo();
            Assert.Same(paragraph, redone!.Caret.Paragraph);
            Assert.Equal(1, redone.Caret.Offset);
        }
    }

    [Fact]
    public void ACaretInATableCellComesBackOnUndoAndRedo()
    {
        var document = Document(P("a") + OneCellTable + PEnd("b", Sect(Footer("default", "fA")), "<w:pageBreakBefore/>") + P("c") + Sect(), Parts(("fA", Foot("A"))));
        var editor = Editor(document);
        var cell = CellParagraph(document);
        var caret = new TextPosition(StoryRef.Body, cell, 2);

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, true, caret));

        var undone = editor.History.Undo();
        Assert.Same(cell, undone!.Caret.Paragraph);
        Assert.Equal(2, undone.Caret.Offset);
        var redone = editor.History.Redo();
        Assert.Same(cell, redone!.Caret.Paragraph);
        Assert.Equal(2, redone.Caret.Offset);
    }

    [Fact]
    public void AStaleCaretIsRefusedBeforeAnythingChanges()
    {
        var document = Two();
        var editor = Editor(document);
        var before = Snapshot(document);
        var stranger = new TextPosition(StoryRef.Body, new Paragraph(), 0);

        Assert.Throws<StalePositionException>(() => editor.SetDifferentFirstPage(0, true, stranger));

        Assert.Equal(before, Snapshot(document));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void ABodyThatHoldsOnlyATableStillTakesTheCommandWithTheCellForACaret()
    {
        var document = Document(OneCellTable + Sect());
        var editor = Editor(document);
        var cell = CellParagraph(document);

        Assert.NotNull(editor.SetDifferentFirstPage(0, true));

        Assert.True(SectionList.Of(document)[0].Properties.GetFlag(W + "titlePg"));
        var undone = editor.History.Undo();
        Assert.Same(cell, undone!.Caret.Paragraph);
        Assert.False(SectionList.Of(document)[0].Properties.GetFlag(W + "titlePg"));
    }

    [Fact]
    public void ABodyWithNoParagraphAtAllHasNoPlaceForACaretAndDoesNothing()
    {
        var document = Document(Sect());
        var editor = Editor(document);

        Assert.Null(editor.SetDifferentFirstPage(0, true));
        Assert.False(SectionList.Of(document)[0].Properties.GetFlag(W + "titlePg"));
    }

    [Theory]
    [InlineData(HeaderFooterKind.Header, HeaderFooterType.Default)]
    [InlineData(HeaderFooterKind.Footer, HeaderFooterType.First)]
    [InlineData(HeaderFooterKind.Header, HeaderFooterType.Even)]
    [InlineData(HeaderFooterKind.Footer, HeaderFooterType.Even)]
    public void OwnAndSameAsPreviousWorkForEveryKindAndType(HeaderFooterKind kind, HeaderFooterType type)
    {
        var document = Document(P("a") + PEnd("b", Sect(), "<w:pageBreakBefore/>") + P("c") + Sect());
        var editor = Editor(document);
        var parts = document.HeadersFooters.Count;

        Assert.NotNull(editor.SetOwnHeaderFooter(1, kind, type, true));

        var reference = Own(document, 1, kind, type)!;
        Assert.Equal(kind == HeaderFooterKind.Footer ? "ftr" : "hdr", document.HeadersFooters[reference.RelationshipId].Content.Root.Name.LocalName);
        Assert.Equal(parts + 1, document.HeadersFooters.Count);
        Assert.Null(editor.SetOwnHeaderFooter(1, kind, type, true));
        Assert.Equal([1], editor.SharingOf(kind, type, 1).Sections);
        Assert.NotNull(editor.SetOwnHeaderFooter(1, kind, type, false));
        Assert.Null(Own(document, 1, kind, type));
        Assert.Equal(parts + 1, document.HeadersFooters.Count);
        Assert.Equal(-1, editor.SharingOf(kind, type, 1).OwnerSection);
    }

    [Fact]
    public void OwnWithNothingToInheritMakesAnEmptyPart()
    {
        var document = Document(P("a") + PEnd("b", Sect(), "<w:pageBreakBefore/>") + P("c") + Sect());
        var editor = Editor(document);

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Header, HeaderFooterType.Default, true));

        var id = Own(document, 1, HeaderFooterKind.Header, HeaderFooterType.Default)!.RelationshipId;
        Assert.Equal(string.Empty, TextOf(document, id));
        Assert.Null(document.HeadersFooters[id].RelationshipsFrom);
    }

    [Fact]
    public void SameAsPreviousWhenThePreviousSectionHasNoPartLeavesNoHeaderThere()
    {
        var document = Document(P("a") + PEnd("b", Sect(), "<w:pageBreakBefore/>") + P("c") + Sect(Header("default", "hA")), Parts(("hA", Head("own"))));
        var editor = Editor(document);

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Header, HeaderFooterType.Default, false));

        Assert.Equal(-1, editor.SharingOf(HeaderFooterKind.Header, HeaderFooterType.Default, 1).OwnerSection);
        Assert.Equal(["a", "b", "c"], Tops(document));
    }

    [Fact]
    public void OwnForTheDefaultTypeMakesNoEvenCopyWhenLeftAndRightPagesAreOn()
    {
        var document = Two();
        var editor = Editor(document);
        Assert.NotNull(editor.SetLeftRightPages(true));

        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, true));

        Assert.Null(Own(document, 1, HeaderFooterKind.Footer, HeaderFooterType.Even));
        Assert.Equal(0, editor.SharingOf(HeaderFooterKind.Footer, HeaderFooterType.Even, 1).OwnerSection);
    }

    [Fact]
    public void SharingOfAnUnknownSectionIsEmpty()
    {
        var editor = Editor(Two());

        Assert.Equal(-1, editor.SharingOf(HeaderFooterKind.Footer, HeaderFooterType.Default, 9).OwnerSection);
        Assert.Empty(editor.SharingOf(HeaderFooterKind.Footer, HeaderFooterType.Default, -1).Sections);
    }

    // The same package with no settings part: the relationship, the content type and the file are gone.
    private static byte[] WithoutSettings(byte[] package)
    {
        using var input = new MemoryStream(package);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                if (entry.FullName == "word/settings.xml")
                {
                    continue;
                }

                using var read = entry.Open();
                using var text = new MemoryStream();
                read.CopyTo(text);
                var bytes = text.ToArray();
                if (entry.FullName is "word/_rels/document.xml.rels" or "[Content_Types].xml")
                {
                    var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes));
                    xml.Descendants().Where(e => (string?)e.Attribute("Target") == "settings.xml" || (string?)e.Attribute("PartName") == "/word/settings.xml").ToList().ForEach(e => e.Remove());
                    bytes = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
                }

                var created = target.CreateEntry(entry.FullName);
                using var write = created.Open();
                write.Write(bytes);
            }
        }

        return output.ToArray();
    }
}
