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
/// Number pages from here, section breaks and removing a marker: what the model holds afterwards, what a laid-out copy draws (the last line of a page is its footer), the independent
/// count of the numbers (<see cref="ReferencePageNumbers"/>), the layout restart against a full layout, and the history.
/// </summary>
public sealed class SectionEditingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly PageNumbering PlainOne = new(1, PageNumberFormat.Arabic, string.Empty);

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    private static string Pg(string text) => P(text, "<w:pageBreakBefore/>");

    private static DocumentLayout Lay(WordDocument document) => Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));

    // The last line of every page: the footer when the page has one, else the page's own text.
    private static string[] Feet(WordDocument document)
    {
        var layout = Lay(document);
        return [.. Pages(layout).Select(page => Lines(page)[^1])];
    }

    private static string[] FirstLines(WordDocument document) => [.. Pages(Lay(document)).Select(page => Lines(page)[0])];

    // What the pages show against the independent count of the sections; returns the numbers.
    private static IReadOnlyList<string> NumbersAgree(WordDocument document)
    {
        var layout = Lay(document);
        var drawn = DrawnPageNumbers.Read(layout, document);
        var reference = ReferencePageNumbers.Numbers(document, [.. drawn.Select(d => d.Section)]);
        Assert.Equal(reference, drawn.Select(d => d.Number));
        return reference;
    }

    // The incremental layout after the command (the restart the session does) against a full one.
    private static void LayoutAgrees(DocumentLayout before, WordDocument document, string label)
    {
        before.Resection(0);
        LayoutOracle.AssertEqualToFull(before, document, label: label);
    }

    private static Paragraph NewPara(string text, bool pageBreak = false)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        run.Content.Add(new TextContent(text));
        paragraph.Inlines.Add(run);
        if (pageBreak)
        {
            paragraph.Properties.Set(W + "pageBreakBefore", new XElement(W + "pageBreakBefore"));
        }

        return paragraph;
    }

    // A blank document of three pages: "p1", "p2", "p3", each after a page break.
    private static (DocxFile File, WordDocument Document) Blank3()
    {
        var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var document = file.Model;
        document.Body.Blocks.Clear();
        document.Body.Blocks.Add(NewPara("p1"));
        document.Body.Blocks.Add(NewPara("p2", true));
        document.Body.Blocks.Add(NewPara("p3", true));
        return (file, document);
    }

    private static WordDocument Doc(string body, Dictionary<string, string>? parts = null, string? settings = null) => Document(body, parts, settingsInner: settings);

    private static Dictionary<string, string> Parts(params (string Id, string Xml)[] parts) => parts.ToDictionary(p => p.Id, p => p.Xml, StringComparer.Ordinal);

    private static string FooterWithPage() => FooterPart(Para(Field("PAGE", "1")));

    private static List<string> Snapshot(WordDocument document) =>
        [.. SectionList.Of(document).Select(s => string.Join("|", s.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting)).Concat(s.Properties.HeaderFooterReferences.Select(r => $"{r.Kind}{r.Type}{r.RelationshipId}")).Concat(s.Properties.UnknownAttributes.Select(a => a.ToString()))))];

    [Fact]
    public void PageThreeShowsOneOnABlankDocument()
    {
        var (file, document) = Blank3();
        using var _ = file;
        var layout = Lay(document);
        var editor = Editor(document);

        var result = editor.NumberPagesFrom(At(document, 2), PlainOne);

        Assert.NotNull(result);
        Assert.Equal(["p1", "p2", "1"], Feet(document));
        Assert.Equal(["1", "2", "1"], NumbersAgree(document));
        Assert.Equal(2, SectionList.Of(document).Count);
        LayoutAgrees(layout, document, "number from page 3");
    }

    [Fact]
    public void TheFirstPageOfTheNewSectionShowsTheNumber()
    {
        var body = P("p1") + Pg("p2") + Pg("p3") + Sect(Footer("first", "fF") + "<w:titlePg/>");
        var document = Doc(body, Parts(("fF", FooterPart(Para("")))));
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));

        var later = SectionList.Of(document)[1].Properties;
        Assert.Null(later.Get(W + "titlePg"));
        Assert.Equal("nextPage", (string?)later.Get(W + "type")!.Attribute(W + "val"));
        Assert.Equal("1", Feet(document)[2]);
        Assert.Equal(string.Empty, Feet(document)[0]);
        Assert.Equal(["1", "2", "1"], NumbersAgree(document));
    }

    [Fact]
    public void AnEmptyFirstFooterGetsTheNumberWhenTheSectionKeepsItsFirstPage()
    {
        // The caret is in the first block of a section that has a first page of its own: the section is edited alone, titlePg stays, the empty first footer resolves to no number.
        var body = P("p1") + Pg("p2") + Sect(Footer("first", "fF") + "<w:titlePg/>");
        var document = Doc(body, Parts(("fF", FooterPart(Para("")))));
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 0), PlainOne));

        Assert.Single(SectionList.Of(document));
        Assert.Equal(["1", "2"], Feet(document));
        Assert.NotNull(SectionList.Of(document)[0].Properties.Get(W + "titlePg"));
    }

    [Fact]
    public void EvenPagesShowTheNumberWhenLeftAndRightPagesAreOn()
    {
        var body = P("p1") + Pg("p2") + Pg("p3") + Pg("p4") + Sect();
        var document = Doc(body, settings: "<w:evenAndOddHeaders/>");
        var layout = Lay(document);
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));

        var section = SectionList.Of(document)[1].Properties;
        Assert.Contains(section.HeaderFooterReferences, r => r.Type == HeaderFooterType.Default);
        Assert.Contains(section.HeaderFooterReferences, r => r.Type == HeaderFooterType.Even);
        Assert.Equal(["p1", "p2", "1", "2"], Feet(document));
        Assert.Equal(["1", "2", "1", "2"], NumbersAgree(document));
        LayoutAgrees(layout, document, "left and right pages");
    }

    [Fact]
    public void ARestartOnADocumentThatAlreadyNumbersOnlyRestartsTheCount()
    {
        var body = P("p1") + Pg("p2") + Pg("p3") + Sect(Footer("default", "fA"));
        var document = Doc(body, Parts(("fA", FooterWithPage())));
        var parts = document.HeadersFooters.Count;
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));

        Assert.Equal(parts, document.HeadersFooters.Count);
        Assert.Equal(["1", "2", "1"], Feet(document));
        Assert.Equal(["1", "2", "1"], NumbersAgree(document));
        Assert.DoesNotContain(SectionList.Of(document)[1].Properties.HeaderFooterReferences, r => r.RelationshipId != "fA");
    }

    [Fact]
    public void AnInlineAndAContentControlPageFieldCountAsANumber()
    {
        var simple = FooterPart(Para("<w:fldSimple w:instr=\" PAGE \"><w:r><w:t>X</w:t></w:r></w:fldSimple>"));
        var control = FooterPart("<w:sdt><w:sdtPr/><w:sdtContent>" + Para(Field("PAGE", "1")) + "</w:sdtContent></w:sdt>");
        foreach (var footer in new[] { simple, control })
        {
            var document = Doc(P("p1") + Pg("p2") + Pg("p3") + Sect(Footer("default", "fA")), Parts(("fA", footer)));
            var parts = document.HeadersFooters.Count;

            Assert.NotNull(Editor(document).NumberPagesFrom(At(document, 2), PlainOne));

            Assert.Equal(parts, document.HeadersFooters.Count);
        }
    }

    [Fact]
    public void AppendixPrefixWritesAnOwnFooterWithAMinusBeforeThePageField()
    {
        var body = P("p1") + Pg("p2") + Pg("p3") + Sect(Footer("default", "fA"));
        var document = Doc(body, Parts(("fA", FooterWithPage())));
        var layout = Lay(document);
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), new PageNumbering(1, PageNumberFormat.Arabic, "A-")));

        Assert.Equal(["1", "2", "A-1"], Feet(document));
        var own = SectionList.Of(document)[1].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Footer);
        Assert.NotEqual("fA", own.RelationshipId);
        Assert.Equal(2, document.HeadersFooters.Count);
        Assert.Contains(SectionList.Of(document)[0].Properties.HeaderFooterReferences, r => r.RelationshipId == "fA");
        LayoutAgrees(layout, document, "appendix");
        // The same prefix again changes nothing: no "A-A-1", no new part, no step.
        var parts = document.HeadersFooters.Count;
        var steps = editor.History.UndoCount;
        Assert.Null(editor.NumberPagesFrom(At(document, 2), new PageNumbering(1, PageNumberFormat.Arabic, "A-")));
        Assert.Equal(parts, document.HeadersFooters.Count);
        Assert.Equal(steps, editor.History.UndoCount);
        Assert.Equal(["1", "2", "A-1"], Feet(document));
    }

    [Fact]
    public void ThePrefixGoesInFrontOfThePageFieldInAFooterWithText()
    {
        var footer = FooterPart(Para(Text("Thesis ") + Field("PAGE", "1")));
        var document = Doc(P("p1") + Pg("p2") + Sect(Footer("default", "fA")), Parts(("fA", footer)));

        Assert.NotNull(Editor(document).NumberPagesFrom(At(document, 1), new PageNumbering(1, PageNumberFormat.Arabic, "A-")));

        Assert.Equal(["Thesis 1", "Thesis A-1"], Feet(document));
    }

    [Fact]
    public void RomanFrontMatterThenArabicBody()
    {
        var (file, document) = Blank3();
        using var _ = file;
        document.Body.Blocks.Add(NewPara("p4", true));
        var layout = Lay(document);
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 0), new PageNumbering(1, PageNumberFormat.LowerRoman, string.Empty)));
        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));

        Assert.Equal(["i", "ii", "1", "2"], Feet(document));
        Assert.Equal(["i", "ii", "1", "2"], NumbersAgree(document));
        LayoutAgrees(layout, document, "roman then arabic");
    }

    [Fact]
    public void LaterSectionsWithTheirOwnSettingsKeepThem()
    {
        var one = Footer("default", "fB");
        var body = P("p1") + Pg("p2") + PEnd("p3", Sect()) + PEnd("p4", Sect(one + "<w:pgNumType w:start=\"7\"/>")) + PEnd("p5", Sect("<w:pgNumType w:fmt=\"upperRoman\"/>")) + P("p6") + Sect();
        var document = Doc(body, Parts(("fB", FooterWithPage())));
        var editor = Editor(document);
        var before = Snapshot(document);

        var facts = editor.ReadSectionFacts(At(document, 1));
        Assert.Equal(0, facts.Section);
        Assert.Equal(3, facts.FollowingSections);
        Assert.Equal(2, facts.FollowingWithOwnSetting);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 1), PlainOne));

        var after = Snapshot(document);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(before.Skip(1), after.Skip(2));
        NumbersAgree(document);
    }

    [Fact]
    public void LaterSectionsThatInheritTheFooterShowTheNumberToo()
    {
        var body = PEnd("p1", Sect()) + PEnd("p2", Sect()) + P("p3") + Sect();
        var document = Doc(body);
        var layout = Lay(document);
        var editor = Editor(document);

        var facts = editor.ReadSectionFacts(At(document, 1));
        Assert.True(facts.AtSectionStart);
        Assert.Equal(1, facts.FollowingSections);
        Assert.Equal(0, facts.FollowingWithOwnSetting);
        Assert.NotNull(editor.NumberPagesFrom(At(document, 1), PlainOne));

        Assert.Equal(3, SectionList.Of(document).Count);
        Assert.Equal(["p1", "1", "2"], Feet(document));
        Assert.Equal(["1", "1", "2"], NumbersAgree(document));
        LayoutAgrees(layout, document, "inherited");
    }

    [Fact]
    public void NoSplitInTheFirstBlockOfASection()
    {
        var document = Doc(P("a") + PEnd("b", Sect()) + P("c") + Sect());
        var editor = Editor(document);

        Assert.True(editor.ReadSectionFacts(At(document, 0)).AtSectionStart);
        Assert.NotNull(editor.NumberPagesFrom(At(document, 0), new PageNumbering(5, PageNumberFormat.Arabic, string.Empty)));
        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), new PageNumbering(9, PageNumberFormat.Arabic, string.Empty)));

        Assert.Equal(2, SectionList.Of(document).Count);
        Assert.Null(editor.InsertSectionBreak(At(document, 0), SectionBreakKind.NextPage));
        Assert.Null(editor.InsertSectionBreak(At(document, 2), SectionBreakKind.Continuous));
        Assert.Equal(2, SectionList.Of(document).Count);
        Assert.Equal("5", (string?)SectionList.Of(document)[0].Properties.Get(W + "pgNumType")!.Attribute(W + "start"));
    }

    private const string Cell = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

    [Fact]
    public void ACaretInATableSplitsBeforeTheTable()
    {
        var document = Doc(P("a") + Cell + P("c") + Sect());
        var table = (Table)document.Body.Blocks[1];
        var inner = ((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks[0];
        var editor = Editor(document);

        var result = editor.NumberPagesFrom(new TextPosition(StoryRef.Body, (Paragraph)inner, 2), PlainOne);

        Assert.NotNull(result);
        var sections = SectionList.Of(document);
        Assert.Equal(2, sections.Count);
        Assert.Equal(1, sections[1].FirstBlock);
        Assert.NotNull(((Paragraph)document.Body.Blocks[0]).SectionBreak);
        Assert.Equal(3, document.Body.Blocks.Count);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.NotNull(editor.History.Undo());
        Assert.Single(SectionList.Of(document));
    }

    [Fact]
    public void ACaretInAContentControlSplitsBeforeTheControl()
    {
        var control = "<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>inside</w:t></w:r></w:p></w:sdtContent></w:sdt>";
        var document = Doc(P("a") + control + P("c") + Sect());
        var inner = ((ContentControlBlock)document.Body.Blocks[1]).Content[0];
        var editor = Editor(document);

        Assert.NotNull(editor.InsertSectionBreak(new TextPosition(StoryRef.Body, (Paragraph)inner, 1), SectionBreakKind.Continuous));

        var sections = SectionList.Of(document);
        Assert.Equal(2, sections.Count);
        Assert.Equal(1, sections[1].FirstBlock);
        Assert.Equal("continuous", (string?)sections[1].Properties.Get(W + "type")!.Attribute(W + "val"));
    }

    [Fact]
    public void ThePreviousPlainParagraphCarriesTheBreakWithoutABlankLine()
    {
        var document = Doc(P("a") + P("b") + P("c") + Sect());
        var editor = Editor(document);

        Assert.NotNull(editor.InsertSectionBreak(At(document, 2), SectionBreakKind.NextPage));

        Assert.Equal(3, document.Body.Blocks.Count);
        Assert.NotNull(((Paragraph)document.Body.Blocks[1]).SectionBreak);
        Assert.Null(((Paragraph)document.Body.Blocks[0]).SectionBreak);
    }

    [Fact]
    public void AnExistingBreakGetsAnEmptyCarrierParagraph()
    {
        // The block before the split is not a plain paragraph (a table here), so the earlier half ends with a new empty paragraph.
        var document = Doc(P("a") + Cell + P("c") + Sect());
        var editor = Editor(document);

        Assert.NotNull(editor.InsertSectionBreak(At(document, 2), SectionBreakKind.NextPage));

        Assert.Equal(4, document.Body.Blocks.Count);
        var carrier = Assert.IsType<Paragraph>(document.Body.Blocks[2]);
        Assert.NotNull(carrier.SectionBreak);
        Assert.Empty(carrier.Inlines);
        Assert.Equal(1, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(3, document.Body.Blocks.Count);
        editor.History.Redo();
        Assert.Equal(4, document.Body.Blocks.Count);
        Assert.NotNull(((Paragraph)document.Body.Blocks[2]).SectionBreak);
    }

    [Theory]
    [InlineData(SectionBreakKind.NextPage, "nextPage")]
    [InlineData(SectionBreakKind.Continuous, "continuous")]
    [InlineData(SectionBreakKind.EvenPage, "evenPage")]
    [InlineData(SectionBreakKind.OddPage, "oddPage")]
    public void EachBreakKindWritesItsType(SectionBreakKind kind, string expected)
    {
        var document = Doc(P("a") + P("b") + Sect("<w:pgNumType w:start=\"3\" w:fmt=\"lowerRoman\"/><w:titlePg/>"));
        var editor = Editor(document);

        Assert.NotNull(editor.InsertSectionBreak(At(document, 1), kind));

        var sections = SectionList.Of(document);
        Assert.Equal(expected, (string?)sections[1].Properties.Get(W + "type")!.Attribute(W + "val"));
        Assert.Null(sections[1].Properties.Get(W + "titlePg"));
        // The earlier half keeps the restart, the later half continues the count and keeps the format.
        Assert.Equal("3", (string?)sections[0].Properties.Get(W + "pgNumType")!.Attribute(W + "start"));
        Assert.Null(sections[1].Properties.Get(W + "pgNumType")!.Attribute(W + "start"));
        Assert.NotNull(sections[0].Properties.Get(W + "titlePg"));
    }

    // Three sections of distinct setups; the middle one has a child the first one lacks and the first has attributes and unknown children the middle lacks.
    private static WordDocument ThreeSetups()
    {
        var first = "<w:sectPr w:rsidR=\"00AA\">" + Footer("default", "fA") + "<w:type w:val=\"nextPage\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgNumType w:start=\"3\" w:fmt=\"upperRoman\"/><w:titlePg/><w:docGrid w:linePitch=\"360\"/></w:sectPr>";
        var second = "<w:sectPr><w:type w:val=\"continuous\"/><w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:cols w:num=\"2\" w:space=\"360\"/></w:sectPr>";
        var body = "<w:p><w:pPr>" + first + "</w:pPr><w:r><w:t>one</w:t></w:r></w:p>" + "<w:p><w:pPr>" + second + "</w:pPr><w:r><w:t>two</w:t></w:r></w:p>" + P("three") + Sect("<w:type w:val=\"evenPage\"/>");
        return Doc(body, Parts(("fA", FooterWithPage())));
    }

    [Fact]
    public void RemovingAMiddleSectionTakesThePreviousSetupWholesale()
    {
        var document = ThreeSetups();
        var editor = Editor(document);
        var before = SectionList.Of(document);
        var earlier = before[0].Properties.Clone();
        var final = before[2].Properties;

        var result = editor.RemoveSectionBreak(1);

        Assert.NotNull(result);
        var after = SectionList.Of(document);
        Assert.Equal(2, after.Count);
        Assert.Null(((Paragraph)document.Body.Blocks[0]).SectionBreak);
        var merged = after[0].Properties;
        Assert.Same(before[1].Properties, merged);
        Assert.Same(final, after[1].Properties);
        Assert.True(SectionSplitter.SameContent(earlier, merged));
        Assert.Null(merged.Get(W + "cols"));
        Assert.NotNull(merged.Get(W + "docGrid"));
        Assert.NotNull(merged.Get(W + "titlePg"));
        Assert.Equal("upperRoman", (string?)merged.Get(W + "pgNumType")!.Attribute(W + "fmt"));
        Assert.Equal("nextPage", (string?)merged.Get(W + "type")!.Attribute(W + "val"));
        Assert.Contains(merged.HeaderFooterReferences, r => r.RelationshipId == "fA");
        Assert.Contains(merged.UnknownAttributes, a => a.Name.LocalName == "rsidR");
        Assert.Equal(1, editor.History.UndoCount);
    }

    private static SectionProperties MergedOf(WordDocument document, int index) => SectionList.Of(document)[index].Properties;

    [Fact]
    public void RemovingTheFinalSectionKeepsTheFinalObject()
    {
        var document = ThreeSetups();
        var editor = Editor(document);
        var final = document.Body.FinalSection!;
        var middle = SectionList.Of(document)[1].Properties.Clone();

        Assert.NotNull(editor.RemoveSectionBreak(2));

        Assert.Same(final, document.Body.FinalSection);
        Assert.True(SectionSplitter.SameContent(middle, final));
        Assert.Equal(2, SectionList.Of(document).Count);
        Assert.Null(((Paragraph)document.Body.Blocks[1]).SectionBreak);
        Assert.Null(editor.RemoveSectionBreak(0));
        Assert.Null(editor.RemoveSectionBreak(2));
    }

    [Fact]
    public void UndoOfARemovalRestoresTheParts()
    {
        foreach (var index in new[] { 1, 2 })
        {
            var document = ThreeSetups();
            var editor = Editor(document);
            var original = Snapshot(document);

            Assert.NotNull(editor.RemoveSectionBreak(index));
            var removed = Snapshot(document);
            Assert.NotEqual(original, removed);
            Assert.NotNull(editor.History.Undo());
            Assert.Equal(original, Snapshot(document));
            Assert.NotNull(editor.History.Redo());
            Assert.Equal(removed, Snapshot(document));
            Assert.NotNull(editor.History.Undo());
            Assert.Equal(original, Snapshot(document));
        }
    }

    [Fact]
    public async Task ARemovedSectionsPartsStayInThePackageUnused()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raffica-sections-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (file, document) = Blank3();
            using var _ = file;
            var editor = Editor(document);
            Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));
            Assert.NotNull(editor.RemoveSectionBreak(1));

            var path = Path.Combine(folder, "removed.docx");
            await file.SaveAsync(path, TestContext.Current.CancellationToken);

            using var reopened = DocxFile.Open(path);
            Assert.Single(SectionList.Of(reopened.Model));
            Assert.Empty(SectionList.Of(reopened.Model)[0].Properties.HeaderFooterReferences);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                Assert.Contains(zip.Entries, entry => entry.FullName.StartsWith("word/footer", StringComparison.Ordinal));
            }

            Assert.Equal(["p1", "p2", "p3"], Feet(reopened.Model));
            Assert.Equal(["1", "2", "3"], NumbersAgree(reopened.Model));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ASavedAndReopenedNumberingGivesTheSameNumbers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raffica-sections-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var (file, document) = Blank3();
            using var _ = file;
            Assert.NotNull(Editor(document).NumberPagesFrom(At(document, 2), new PageNumbering(1, PageNumberFormat.Arabic, "A-")));
            var path = Path.Combine(folder, "numbered.docx");
            await file.SaveAsync(path, TestContext.Current.CancellationToken);

            using var reopened = DocxFile.Open(path);

            Assert.Equal(["p1", "p2", "A-1"], Feet(reopened.Model));
            Assert.Equal(["1", "2", "1"], NumbersAgree(reopened.Model));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void EveryCommandIsOneUndoStep()
    {
        var (file, document) = Blank3();
        using var _ = file;
        var editor = Editor(document);
        var original = Snapshot(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));
        Assert.Equal(1, editor.History.UndoCount);
        var numbered = Snapshot(document);
        Assert.NotNull(editor.InsertSectionBreak(At(document, 1), SectionBreakKind.Continuous));
        Assert.Equal(2, editor.History.UndoCount);
        var broken = Snapshot(document);
        Assert.NotNull(editor.RemoveSectionBreak(1));
        Assert.Equal(3, editor.History.UndoCount);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(broken, Snapshot(document));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(numbered, Snapshot(document));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(original, Snapshot(document));
        Assert.Null(editor.History.Undo());
        Assert.NotNull(editor.History.Redo());
        Assert.NotNull(editor.History.Redo());
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(["p1", "p2", "1"], Feet(document));
    }

    [Fact]
    public void AnEarlierStepOnASectionStillUndoesAfterItsMarkerWasRemovedAndRestored()
    {
        var document = Doc(P("a") + Pg("b") + Pg("c") + Pg("d") + Sect());
        var editor = Editor(document);
        var original = Snapshot(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));
        // A step on the earlier section, whose end properties are the carrier's object.
        Assert.NotNull(editor.NumberPagesFrom(At(document, 0), new PageNumbering(4, PageNumberFormat.Arabic, string.Empty)));
        Assert.NotNull(editor.RemoveSectionBreak(1));
        Assert.Equal(3, editor.History.UndoCount);

        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(original, Snapshot(document));
        Assert.Equal(0, editor.History.UndoCount);
        Assert.True(editor.History.CanRedo);
        Assert.NotNull(editor.History.Redo());
        Assert.NotNull(editor.History.Redo());
        Assert.NotNull(editor.History.Redo());
        Assert.Single(SectionList.Of(document));
    }

    [Fact]
    public void CommandsAreDisabledWithAReasonInAHeaderANoteAndAPrefixDocument()
    {
        var document = Doc(P("a") + P("b") + Sect());
        var editor = Editor(document);
        var paragraph = (Paragraph)document.Body.Blocks[1];
        var before = Snapshot(document);

        var header = new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId1"), paragraph, 0);
        var note = new TextPosition(new StoryRef(StoryKind.Note, "3"), paragraph, 0);
        Assert.Equal(SectionReason.NotInBody, editor.ReadSectionFacts(header).Reason);
        Assert.Equal(SectionReason.NotInBody, editor.ReadSectionFacts(note).Reason);
        Assert.Equal(SectionReason.PrefixDocument, editor.ReadSectionFacts(At(document, 1), prefixDocument: true).Reason);
        Assert.Equal(SectionReason.NoDocument, editor.ReadSectionFacts(default).Reason);
        Assert.Equal(SectionReason.None, editor.ReadSectionFacts(At(document, 1)).Reason);
        Assert.False(editor.ReadSectionFacts(header).InBody);

        Assert.Null(editor.NumberPagesFrom(header, PlainOne));
        Assert.Null(editor.NumberPagesFrom(note, PlainOne));
        Assert.Null(editor.InsertSectionBreak(header, SectionBreakKind.NextPage));
        Assert.Null(editor.NumberPagesFrom(default, PlainOne));
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void AStaleBodyPositionThrowsAndChangesNothing()
    {
        var document = Doc(P("a") + P("b") + Sect());
        var editor = Editor(document);
        var before = Snapshot(document);
        var stale = new TextPosition(StoryRef.Body, new Paragraph(), 0);

        Assert.Throws<StalePositionException>(() => editor.NumberPagesFrom(stale, PlainOne));
        Assert.Throws<StalePositionException>(() => editor.InsertSectionBreak(stale, SectionBreakKind.NextPage));
        Assert.Throws<StalePositionException>(() => editor.ReadSectionFacts(stale));
        Assert.Equal(before, Snapshot(document));
    }

    [Fact]
    public void BadNumberingIsRefusedBeforeAnythingChanges()
    {
        var document = Doc(P("a") + P("b") + Sect());
        var editor = Editor(document);
        var before = Snapshot(document);

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.NumberPagesFrom(At(document, 1), new PageNumbering(100000, PageNumberFormat.Arabic, string.Empty)));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.NumberPagesFrom(At(document, 1), new PageNumbering(-1, PageNumberFormat.Arabic, string.Empty)));
        Assert.Throws<ArgumentException>(() => editor.NumberPagesFrom(At(document, 1), new PageNumbering(1, PageNumberFormat.Arabic, "A\n")));
        Assert.Throws<ArgumentException>(() => editor.NumberPagesFrom(At(document, 1), new PageNumbering(1, PageNumberFormat.Arabic, new string('x', 33))));
        Assert.Equal(before, Snapshot(document));
        Assert.NotNull(editor.NumberPagesFrom(At(document, 1), new PageNumbering(0, PageNumberFormat.Arabic, string.Empty)));
        Assert.NotNull(editor.NumberPagesFrom(At(document, 1), new PageNumbering(null, PageNumberFormat.LowerLetter, "x")));
    }

    [Fact]
    public void ASplitNeverMakesAnEmptySection()
    {
        var shapes = new[]
        {
            P("a") + Cell + P("c") + Cell + Sect(),
            P("a") + PEnd("b", Sect()) + Cell + P("d") + Sect(),
            Cell + P("x") + Sect(),
            P("a") + "<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>i</w:t></w:r></w:p><w:p><w:pPr>" + Sect() + "</w:pPr></w:p></w:sdtContent></w:sdt>" + P("z") + Sect(),
        };
        foreach (var shape in shapes)
        {
            var count = Doc(shape).Body.Blocks.Count;
            for (var index = 0; index < count; index++)
            {
                var document = Doc(shape);
                var block = document.Body.Blocks[index];
                var paragraph = block as Paragraph ?? FirstParagraph(block);
                var sections = SectionList.Of(document).Count;

                var result = Editor(document).InsertSectionBreak(new TextPosition(StoryRef.Body, paragraph, 0), SectionBreakKind.NextPage);

                var after = SectionList.Of(document);
                Assert.Equal(result is null ? sections : sections + 1, after.Count);
                Assert.All(after, s => Assert.True(s.LastBlock >= s.FirstBlock, "empty section at block " + index));
            }
        }
    }

    private static Paragraph FirstParagraph(Block block) => block switch
    {
        Paragraph p => p,
        ContentControlBlock c => FirstParagraph(c.Content[0]),
        Table t => (Paragraph)((TableCell)((TableRow)t.Rows[0]).Items[0]).Blocks[0],
        _ => throw new InvalidOperationException(),
    };

    [Fact]
    public void ABodyWithNoFinalSectionGetsOneAndUndoLeavesAnEmptyOne()
    {
        var document = Doc(P("a") + P("b"));
        Assert.Null(document.Body.FinalSection);
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 1), PlainOne));

        Assert.NotNull(document.Body.FinalSection);
        Assert.Equal("1", (string?)document.Body.FinalSection!.Get(W + "pgNumType")!.Attribute(W + "start"));
        Assert.NotNull(editor.History.Undo());
        Assert.Single(SectionList.Of(document));
        Assert.Empty(document.Body.FinalSection!.Children);
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(2, SectionList.Of(document).Count);
    }

    [Fact]
    public void AnUnchangedRestartIsNotAStep()
    {
        var document = Doc(P("a") + Sect(Footer("default", "fA") + "<w:pgNumType w:start=\"1\"/>"), Parts(("fA", FooterWithPage())));
        var editor = Editor(document);

        Assert.Null(editor.NumberPagesFrom(At(document, 0), PlainOne));

        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void SectionResultsAreAttributedWithTheirCarriers()
    {
        // A section result must be attributed with its carriers in one call (EditResult.Attribute); the plain call would serve stale bytes of a swapped break.
        var root = Raffica.TestSupport.TestData.Root;
        var source = File.ReadAllText(Path.Combine(root, "src", "Raffica.Write.Editing", "TextEditor.Sections.cs"));

        Assert.DoesNotContain("Edits.Attribute(", source, StringComparison.Ordinal);
        Assert.Contains("result.Attribute(_document)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithAThousandSectionsAndABreakParagraphAsTheLastBlockNeverThrows()
    {
        var many = Doc(string.Concat(Enumerable.Range(0, 1000).Select(i => PEnd("s" + i, Sect()))) + P("end") + Sect());
        var editor = Editor(many);
        var facts = editor.ReadSectionFacts(At(many, 500));
        Assert.Equal(500, facts.Section);
        Assert.Equal(500, facts.FollowingSections);
        Assert.NotNull(editor.NumberPagesFrom(At(many, 999), new PageNumbering(1, PageNumberFormat.Arabic, string.Empty)));
        Assert.NotNull(editor.RemoveSectionBreak(500));
        Assert.Equal(1000, SectionList.Of(many).Count);

        // The last block is the paragraph of a section break and the final section is empty: a split before it, a number on it and the removal all work.
        var last = Doc(P("a") + PEnd("b", Sect()) + Sect());
        var lastEditor = Editor(last);
        Assert.NotNull(lastEditor.InsertSectionBreak(At(last, 1), SectionBreakKind.NextPage));
        Assert.Equal(3, SectionList.Of(last).Count);
        Assert.All(SectionList.Of(last).Take(2), s => Assert.True(s.LastBlock >= s.FirstBlock));
        Assert.NotNull(lastEditor.RemoveSectionBreak(2));
        Assert.NotNull(lastEditor.RemoveSectionBreak(1));
        Assert.Single(SectionList.Of(last));
        Assert.NotNull(lastEditor.NumberPagesFrom(At(last, 1), PlainOne));
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    [InlineData(0xFFFF)]
    [InlineData(0xFFFE)]
    [InlineData(9)]
    public void APrefixTheDocumentCannotStoreIsRefused(int code)
    {
        var document = Doc(P("a") + P("b") + Sect());
        var editor = Editor(document);
        var before = Snapshot(document);

        Assert.Throws<ArgumentException>(() => editor.NumberPagesFrom(At(document, 1), new PageNumbering(1, PageNumberFormat.Arabic, "a" + (char)code)));

        Assert.Equal(before, Snapshot(document));
        Assert.NotNull(editor.NumberPagesFrom(At(document, 1), new PageNumbering(1, PageNumberFormat.Arabic, "\U0001F600-")));
    }

    [Fact]
    public void AFooterWithTextAndAPageFieldInAHyperlinkKeepsItsTextAndGetsThePrefix()
    {
        // The field sits inside a hyperlink: the prefix goes in front of the hyperlink and nothing of the footer is dropped.
        var footer = FooterPart(Para(Text("Thesis | ") + "<w:hyperlink w:anchor=\"top\">" + Field("PAGE", "1") + "</w:hyperlink>" + Text(" |")));
        var document = Doc(P("p1") + Pg("p2") + Sect(Footer("default", "fA")), Parts(("fA", footer)));

        Assert.NotNull(Editor(document).NumberPagesFrom(At(document, 1), new PageNumbering(1, PageNumberFormat.Arabic, "A-")));

        Assert.Equal(["Thesis | 1 |", "Thesis | A-1 |"], Feet(document));
    }

    [Fact]
    public void AFooterWithTextAndNoPageFieldKeepsItsTextAndGetsTheNumberBelow()
    {
        var document = Doc(P("p1") + Pg("p2") + Sect(Footer("default", "fA")), Parts(("fA", FooterPart(Para(Text("Confidential"))))));

        Assert.NotNull(Editor(document).NumberPagesFrom(At(document, 1), PlainOne));

        var lines = Pages(Lay(document)).Select(page => string.Join("/", Lines(page))).ToArray();
        Assert.Equal("p1/Confidential", lines[0]);
        Assert.Equal("p2/Confidential/1", lines[1]);
    }

    [Fact]
    public void ARepeatedCommandAddsNoPartAndNoStep()
    {
        var (file, document) = Blank3();
        using var _ = file;
        var editor = Editor(document);
        var numbering = new PageNumbering(1, PageNumberFormat.Arabic, "A-");

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), numbering));
        var parts = document.HeadersFooters.Count;
        var steps = editor.History.UndoCount;
        var shape = Snapshot(document);

        Assert.Null(editor.NumberPagesFrom(At(document, 2), numbering));

        Assert.Equal(parts, document.HeadersFooters.Count);
        Assert.Equal(steps, editor.History.UndoCount);
        Assert.Equal(shape, Snapshot(document));
        Assert.Equal(["p1", "p2", "A-1"], Feet(document));
    }

    [Fact]
    public void TheCaretStaysWhereTheUserHadIt()
    {
        var document = Doc(P("a") + P("b") + Cell + P("d") + Sect());
        var editor = Editor(document);
        var at = At(document, 1, 1);

        var result = editor.NumberPagesFrom(at, PlainOne);

        Assert.NotNull(result);
        Assert.Equal(at, result.Caret);
        Assert.True(result.KeepSelection);
        // In a table the history needs a top-level paragraph of the changed list: the carrier; the session keeps its own selection (KeepSelection).
        var table = document.Body.Blocks.OfType<Table>().Single();
        var inner = (Paragraph)((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks[0];
        var inTable = editor.InsertSectionBreak(new TextPosition(StoryRef.Body, inner, 1), SectionBreakKind.NextPage);
        Assert.NotNull(inTable);
        Assert.True(inTable.KeepSelection);
        Assert.Equal(2, editor.History.UndoCount);
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
    }

    [Fact]
    public void AParagraphThatEndsInAPageBreakCarriesTheBreakLikeAnyOther()
    {
        // The layout puts the paragraph mark behind a trailing page break on the next page (it already draws an empty line there before the command), so a next-page section break after it
        // shows that page empty. A different carrier would sit on that page too, so the splitter does not special-case it; the layout restart equals a full layout.
        var body = P("p1") + "<w:p><w:r><w:t>p2</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r></w:p>" + P("p3") + Sect();
        var document = Doc(body);
        var layout = Lay(document);
        var pagesBefore = layout.LaidOutPages;
        var editor = Editor(document);

        Assert.NotNull(editor.NumberPagesFrom(At(document, 2), PlainOne));

        Assert.Equal(pagesBefore + 1, Lay(document).LaidOutPages);
        Assert.Equal(["p1/p2", string.Empty, "p3/1"], Pages(Lay(document)).Select(page => page.Text.Replace("\n", "/")).ToArray());
        LayoutAgrees(layout, document, "page break before the split");
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(pagesBefore, Lay(document).LaidOutPages);
    }

    private static string ThesisPath => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");

    [Fact]
    public void TheThesisAppendixGivesAMinusAndThePageNumberFromOne()
    {
        using var file = DocxFile.Open(ThesisPath);
        var document = file.Model;
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var appendix = SectionList.Of(document)[^1];
        var heading = (Paragraph)document.Body.Blocks[appendix.FirstBlock];
        var at = new TextPosition(StoryRef.Body, heading, 0);
        var numbering = new PageNumbering(1, PageNumberFormat.Arabic, "A-");

        // The file already has this appendix: the command changes nothing there (no part, no step).
        var original = document.HeadersFooters.Count;
        Assert.Null(editor.NumberPagesFrom(at, numbering));
        Assert.Equal(original, document.HeadersFooters.Count);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal("A-1", AppendixFooter(document, heading));

        // Take the appendix section away and make it again with the one command: the same footer, "A-1" on its first page and the numbers of the reference everywhere.
        Assert.NotNull(editor.RemoveSectionBreak(SectionList.Of(document).Count - 1));
        Assert.NotNull(editor.NumberPagesFrom(at, numbering));
        Assert.Equal(original + 1, document.HeadersFooters.Count);
        Assert.Equal("A-1", AppendixFooter(document, heading));
        var layout = Lay(document);
        var drawn = DrawnPageNumbers.Read(layout, document);
        Assert.Equal(ReferencePageNumbers.Numbers(document, [.. drawn.Select(d => d.Section)]), drawn.Select(d => d.Number));

        // A repeat is a no-op: no new part, no step.
        var steps = editor.History.UndoCount;
        Assert.Null(editor.NumberPagesFrom(at, numbering));
        Assert.Equal(original + 1, document.HeadersFooters.Count);
        Assert.Equal(steps, editor.History.UndoCount);
    }

    // The footer line of the page the heading starts on.
    private static string AppendixFooter(WordDocument document, Paragraph heading)
    {
        var layout = Lay(document);
        var page = layout.Locate(document.Body.Blocks.IndexOf(heading))!.Value.Page;
        return Lines(layout.Page(page))[^1];
    }
}
