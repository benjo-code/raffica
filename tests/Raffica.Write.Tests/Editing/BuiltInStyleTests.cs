using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Support.GermanDocuments;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The fields know Word's built-in styles by their name, not by their id, as Word does (probed with Word 16, <c>Docs/write-word-check.md</c>): headings, the style outline level, the entry styles of a table of contents and of a
/// list of figures, and the caption style. Every expectation here is Word's.
/// </summary>
public sealed class BuiltInStyleTests
{
    private const string Custom = " w:customStyle=\"1\"";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The two switch forms of a table of contents over headings: without and with the outline levels of paragraphs.</summary>
    public static TheoryData<string> Switches() => new() { "TOC \\o \"1-9\" \\h \\z", "TOC \\o \"1-9\" \\h \\z \\u" };

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static Paragraph Para(WordDocument document, int block) => (Paragraph)document.Body.Blocks[block];

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, Para(document, block), offset);

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static FieldRefreshResult Refresh(TextEditor editor, WordDocument document, FakeFieldHost? host = null) =>
        editor.RefreshFields(host ?? new FakeFieldHost(document), RefreshScope.OrderBased);

    // The entries of a table of contents as "<name of the entry style>: <title>".
    private static List<string> Listed(WordDocument document, int toc = 0) =>
        [.. TocDocuments.Entries(document, toc).Select(p => $"{document.Styles!.Find(p.Properties.StyleId!)?.Name ?? "undefined " + p.Properties.StyleId}: {TocDocuments.Line(p).Split('|')[0]}")];

    private static List<string> Styles(WordDocument document, int toc = 0) => [.. TocDocuments.Entries(document, toc).Select(p => p.Properties.StyleId!)];

    private static List<string> HeadingTargets(WordDocument document) =>
        [.. CrossReferences.Targets(document, new FakeFieldHost(document), CrossRefTargetKind.Heading).Select(t => t.Display)];

    // The stored result of each field of the body that is not part of a table of contents, in order.
    private static List<string> Results(WordDocument document) =>
        [.. document.Body.Blocks.OfType<Paragraph>().SelectMany(p => FieldView.InParagraph(p).Select((f, i) => (Paragraph: p, Field: f, Ordinal: i)))
            .Where(f => f.Field.Support.Kind is FieldKind.Seq or FieldKind.StyleRef).Select(f => ReferenceFields.Stored(f.Paragraph, f.Ordinal)!)];

    private static string ThreeHeadings => Text("Alpha", "berschrift1") + Text("Beta", "berschrift2") + Text("Gamma", "berschrift3") + Text("The end.");

    // ---- a German document ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("TOC \\o \"1-3\" \\h \\z")]
    [InlineData("TOC \\o \"1-3\" \\h \\z \\u")]
    public void AGermanTableOfContentsListsItsHeadingsInItsOwnStyles(string instruction)
    {
        var document = Document(StaleToc(instruction, "Verzeichnis1") + ThreeHeadings);
        var editor = Editor(document);

        Refresh(editor, document);

        Assert.Equal(["toc 1: Alpha", "toc 2: Beta", "toc 3: Gamma"], Listed(document));
        Assert.Equal(["Verzeichnis1", "Verzeichnis2", "TOC3"], Styles(document));

        // The level the file has no style for gets one, based on the default paragraph style of the file.
        var made = document.Styles!.Find("TOC3")!;
        Assert.Equal(("paragraph", "toc 3", "Standard", "Standard"), (made.Type, made.Name, made.BasedOn, made.Next));
        Assert.Equal(["toc 1", "toc 2", "toc 3"], document.Styles.Styles.Select(s => s.Name!).Where(n => n.StartsWith("toc ", StringComparison.Ordinal)).Order());

        var again = Refresh(editor, document);
        Assert.Empty(again.Content);
        Assert.Null(again.Structural);
    }

    [Fact]
    public void AGermanListOfFiguresKeepsItsStyle()
    {
        var document = Document(StaleToc("TOC \\h \\z \\c \"Abbildung\"", "Abbildungsverzeichnis") + P(Run("Abbildung ") + Field("SEQ Abbildung") + Run(": Erste"), "Beschriftung"));
        var editor = Editor(document);
        var count = document.Styles!.Styles.Count;

        Refresh(editor, document);

        Assert.Equal(["table of figures: Abbildung 1: Erste"], Listed(document));
        Assert.Equal(["Abbildungsverzeichnis"], Styles(document));
        Assert.Equal(count, document.Styles.Styles.Count);
        Assert.Empty(Refresh(editor, document).Content);
    }

    [Fact]
    public void SequencesRestartAndTheChapterNumberIsReadInAGermanDocument()
    {
        var caption = P(Run("Abbildung ") + Field("STYLEREF 1 \\s") + Run("-") + Field("SEQ Abbildung \\s 1"), "Beschriftung");
        var document = Document(Text("Alpha", "berschrift1") + caption + caption + Text("Beta", "berschrift1") + caption, numbered: true);

        Refresh(Editor(document), document);

        Assert.Equal(["1", "1", "1", "2", "2", "1"], Results(document));
    }

    [Fact]
    public void TheHeadingsOfAGermanDocumentAreCrossReferenceTargets()
    {
        var document = Document(ThreeHeadings + Text("see "), numbered: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        var targets = CrossReferences.Targets(document, host, CrossRefTargetKind.Heading);

        Assert.Equal(["1 Alpha", "1.1 Beta", "1.1.1 Gamma"], targets.Select(t => t.Display));
        Assert.NotNull(editor.InsertCrossReference(At(document, 4, 4), targets[1], CrossRefKind.Text, host));
        Assert.Equal("see Beta", TextOf(Para(document, 4)));
    }

    [Fact]
    public void ACaptionOfAGermanDocumentTakesTheChapterNumberAndTheCaptionStyle()
    {
        var document = Document(Text("Alpha", "berschrift1") + Text("Beta", "berschrift1") + Text("pic"), numbered: true);
        var editor = Editor(document);
        var count = document.Styles!.Styles.Count;
        Assert.Null(editor.RefusalForCaption(At(document, 2), chapterNumber: true, place: CaptionPlace.Below));

        var result = editor.InsertCaption(At(document, 2), "Figure", string.Empty, CaptionPlace.Below, true, "Abbildung", new FakeFieldHost(document));

        Assert.NotNull(result);
        Assert.Equal("Abbildung 2-1", TextOf(Para(document, 3)));
        Assert.Equal("Beschriftung", Para(document, 3).Properties.StyleId);
        Assert.Equal(count, document.Styles.Styles.Count);
    }

    [Fact]
    public void ANewTableOfContentsOfAGermanDocumentUsesTheGermanStyles()
    {
        var document = Document(Text("Inhalt") + ThreeHeadings + P(Run("Figure ") + Field("SEQ Figure", "1"), "Beschriftung") + Text("Bilder"));
        var editor = Editor(document);

        Assert.NotNull(editor.InsertToc(At(document, 0, 6), new TocOptions(1, 3, true, true, TocLeader.Dots, null), new FakeFieldHost(document)));

        Assert.Equal(["Verzeichnis1", "Verzeichnis2", "TOC3"], Styles(document));
        Assert.Equal("toc 3", document.Styles!.Find("TOC3")!.Name);
        Assert.Equal("Standard", document.Styles.Find("TOC3")!.BasedOn);
        Assert.Null(document.Styles.Find("TOC1"));
        Assert.Null(document.Styles.Find("TOC2"));

        var last = document.Body.Blocks.Count - 1;
        var count = document.Styles.Styles.Count;
        Assert.NotNull(editor.InsertToc(At(document, last, 6), new TocOptions(1, 1, true, true, TocLeader.Dots, "Figure"), new FakeFieldHost(document)));
        Assert.Equal(["Abbildungsverzeichnis"], Styles(document, 1));
        Assert.Equal(count, document.Styles.Styles.Count);
    }

    // A list as Word writes it in a German file (entry styles by their German ids, a stop behind the number, links, page references), everything current: a refresh writes no block and adds no style.
    [Theory]
    [InlineData("TOC \\o \"1-3\" \\h \\z")]
    [InlineData("TOC \\o \"1-3\" \\h \\z \\u")]
    public void ARefreshOfAGermanListWithNothingChangedWritesNothing(string instruction)
    {
        static string Entry(string style, string anchor, string number, string title, string stop, string before = "", string after = "") =>
            "<w:p><w:pPr><w:pStyle w:val=\"" + style + "\"/><w:tabs><w:tab w:val=\"left\" w:pos=\"" + stop + "\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs></w:pPr>" + before
            + $"<w:hyperlink w:anchor=\"{anchor}\" w:history=\"1\">" + Run(number) + "<w:r><w:tab/></w:r>" + Run(title) + "<w:r><w:tab/></w:r>" + Field($"PAGEREF {anchor} \\h", "1") + "</w:hyperlink>" + after + "</w:p>";
        static string Heading(string style, string anchor, int id, string title) =>
            P($"<w:bookmarkStart w:id=\"{id}\" w:name=\"{anchor}\"/>" + Run(title) + $"<w:bookmarkEnd w:id=\"{id}\"/>", style);
        var begin = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> " + instruction + " </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>";
        var document = Document(
            Entry("Verzeichnis1", "_Toc1", "1", "Alpha", "440", before: begin)
            + Entry("Verzeichnis2", "_Toc2", "1.1", "Beta", "880")
            + P("<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>")
            + Heading("berschrift1", "_Toc1", 1, "Alpha") + Heading("berschrift2", "_Toc2", 2, "Beta"),
            numbered: true);
        var host = new FakeFieldHost(document);
        host.Pages[Para(document, 3)] = (0, "1");
        host.Pages[Para(document, 4)] = (0, "1");
        var count = document.Styles!.Styles.Count;
        var before = TocDocument.Dump(document);

        var result = Editor(document).RefreshFields(host, RefreshScope.WithPages);

        Assert.Empty(result.Content);
        Assert.Null(result.Structural);
        Assert.Equal(0, result.Written);
        Assert.Equal(count, document.Styles.Styles.Count);
        Assert.Equal(before, TocDocument.Dump(document));
    }

    // A list whose entries name a style the file does not define keeps that: a refresh that changes nothing in the list adds no style behind its back.
    [Fact]
    public void ARefreshAddsNoStyleToAListThatAlreadyNamesTheMissingOne()
    {
        var document = Document(StaleToc("TOC \\o \"1-1\" \\n", "TOC1") + Text("Alpha", "berschrift1"), styles: Standard() + Headings(numbered: false));
        var editor = Editor(document);
        var count = document.Styles!.Styles.Count;

        Refresh(editor, document);

        Assert.Equal(["TOC1"], Styles(document));
        Assert.Equal(count, document.Styles.Styles.Count);
    }

    // ---- an empty list, and what comes into it later ----------------------------------------------------------------------------------------

    private static string OneHeadingStyles => Standard() + Headings(numbered: false) + Style("Beschriftung", "caption");

    // Every paragraph of the saved file names a style the file defines, or none.
    private static void AssertNoUndefinedStyle(WordDocument document) =>
        Assert.All(ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().Select(p => p.Properties.StyleId).OfType<string>(), id => Assert.NotNull(document.Styles?.Find(id)));

    private static async Task<DocxFile> SavedAndReadAgainAsync(DocxFile file, TempFolder folder, string name)
    {
        var path = folder.File(name);
        await file.SaveAsync(path, TestContext.Current.CancellationToken);
        return DocxFile.Open(path);
    }

    [Theory]
    [InlineData("TOC \\o \"1-3\" \\h \\z", false)]
    [InlineData("TOC \\h \\z \\c \"Figure\"", true)]
    public async Task AListRefreshedWhileEmptyStillGetsItsEntryStyleLater(string instruction, bool figures)
    {
        using var folder = new TempFolder();
        var path = folder.File("empty.docx");
        DocxBuilder.WithStyles(path, StaleToc(instruction) + Text("Alpha"), LayoutModel.StylesPart(OneHeadingStyles));
        using var file = DocxFile.Open(path);
        var editor = Editor(file.Model);

        // Nothing to list: the sentence stands there, and it names no style the file lacks.
        Refresh(editor, file.Model);

        Assert.Equal(ReferenceToc.Sentence, TextOf(TocDocuments.Entries(file.Model)[0]));
        using (var empty = await SavedAndReadAgainAsync(file, folder, "still-empty.docx"))
        {
            AssertNoUndefinedStyle(empty.Model);
        }

        var last = file.Model.Body.Blocks.Count - 1;
        Assert.NotNull(figures
            ? editor.InsertCaption(At(file.Model, last), "Figure", string.Empty, CaptionPlace.Below, false, "Figure")
            : editor.ApplyParagraphStyle(new TextRange(At(file.Model, last), At(file.Model, last)), "berschrift1"));
        Refresh(editor, file.Model);

        using var again = await SavedAndReadAgainAsync(file, folder, "filled.docx");
        AssertNoUndefinedStyle(again.Model);
        var entry = Assert.Single(TocDocuments.Entries(again.Model));
        var style = again.Model.Styles!.Find(entry.Properties.StyleId!)!;
        Assert.Equal(figures ? "table of figures" : "toc 1", style.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AListInsertedWhileEmptyGetsItsEntryStyle(bool figures)
    {
        using var folder = new TempFolder();
        var path = folder.File("insert.docx");
        DocxBuilder.WithStyles(path, Text("here") + Text("Alpha"), LayoutModel.StylesPart(OneHeadingStyles));
        using var file = DocxFile.Open(path);
        var editor = Editor(file.Model);

        Assert.NotNull(editor.InsertToc(At(file.Model, 0, 4), new TocOptions(1, 3, true, true, TocLeader.Dots, figures ? "Figure" : null), new FakeFieldHost(file.Model)));
        var last = file.Model.Body.Blocks.Count - 1;
        Assert.NotNull(figures
            ? editor.InsertCaption(At(file.Model, last), "Figure", string.Empty, CaptionPlace.Below, false, "Figure")
            : editor.ApplyParagraphStyle(new TextRange(At(file.Model, last), At(file.Model, last)), "berschrift1"));
        Refresh(editor, file.Model);

        using var again = await SavedAndReadAgainAsync(file, folder, "filled.docx");
        AssertNoUndefinedStyle(again.Model);
        var entry = Assert.Single(TocDocuments.Entries(again.Model));
        Assert.Equal(figures ? "table of figures" : "toc 1", again.Model.Styles!.Find(entry.Properties.StyleId!)!.Name);
    }

    // Without a style sheet no style can be made: the sentence of an empty list then names none.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSentenceOfAListInADocumentWithoutStylesNamesNoStyle(bool figures)
    {
        var inserted = TocDocument.Document(TocDocument.Plain("here"));
        Assert.NotNull(Editor(inserted).InsertToc(At(inserted, 0, 4), new TocOptions(1, 3, true, true, TocLeader.Dots, figures ? "Figure" : null), new FakeFieldHost(inserted)));
        Assert.Null(TocDocuments.Entries(inserted)[0].Properties.StyleId);

        var refreshed = LayoutModel.Document(StaleToc(figures ? "TOC \\h \\c \"Figure\"" : "TOC \\o \"1-3\" \\h") + Text("Alpha"));
        Refresh(Editor(refreshed), refreshed);
        Assert.Null(TocDocuments.Entries(refreshed)[0].Properties.StyleId);
    }

    // A pass that added a style and failed before it changed a block has changed the document all the same: whatever keys on the stamp must read again.
    [Fact]
    public void AFailedPassThatAddedAStyleMovesTheStamp()
    {
        var document = Document(
            StaleToc("TOC \\o \"1-1\" \\h", "plain") + P("<w:bookmarkStart w:id=\"1\" w:name=\"_Toc1\"/>" + Run("Alpha") + "<w:bookmarkEnd w:id=\"1\"/>", "berschrift1"),
            styles: OneHeadingStyles + Style("plain", "Plain"));
        var before = document.Edits.Stamp;

        var failure = Assert.Throws<FieldRefreshException>(() => Editor(document).RefreshFields(new NoPageHost(new FakeFieldHost(document)), RefreshScope.WithPages));

        Assert.Empty(failure.Content);
        Assert.True(failure.StylesAdded);
        Assert.NotNull(document.Styles!.Find("TOC1"));
        Assert.NotEqual(before, document.Edits.Stamp);
        Assert.Equal(before.Revision, document.Edits.Revision);
    }

    // A host whose pages cannot be read: the entries of a list fail after its styles were added.
    private sealed class NoPageHost(FakeFieldHost inner) : IFieldHost
    {
        public bool IsComplete => true;

        public string? NumberLabel(Paragraph paragraph) => inner.NumberLabel(paragraph);

        public string? ChapterNumber(Paragraph paragraph) => inner.ChapterNumber(paragraph);

        public bool LabelEndsInTab(Paragraph paragraph) => inner.LabelEndsInTab(paragraph);

        public LineGeometry MeasureLine(Paragraph paragraph, string text) => inner.MeasureLine(paragraph, text);

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => throw new InvalidOperationException("test");

        public string? NoteMarkText(int noteId, NoteType type) => inner.NoteMarkText(noteId, type);
    }

    // ---- rule 1: which styles are heading styles --------------------------------------------------------------------------------------------

    private static WordDocument HeadingNames() => Document(
        StaleToc("TOC \\o \"1-9\"")
        + Text("OtherId", "zz1") + Text("CapitalName", "Heading2") + Text("OnlyId", "Heading1") + Text("NoName", "Heading3") + Text("Undefined", "Heading4") + Text("WithAlias", "alias")
        + Text("CustomName", "own") + Text("BasedOnHeading", "child") + Text("CharacterStyle", "chr") + Text("NoType", "untyped") + Text("see "),
        styles: Standard() + Style("zz1", "heading 1") + Style("Heading2", "Heading 2") + Style("Heading1", "Kapitel") + Style("Heading3", null) + Style("alias", "heading 5,Kapitel")
            + Style("own", "heading 6", attributes: Custom) + Style("child", "Mine", "zz1") + Style("chr", "heading 7", type: "character") + Style("untyped", "heading 8", type: null));

    [Fact]
    public void AHeadingStyleIsAStyleNamedHeadingNWhateverItsId()
    {
        var document = HeadingNames();

        Assert.Equal(["OtherId", "CapitalName", "NoType"], HeadingTargets(document));
    }

    [Theory]
    [MemberData(nameof(Switches))]
    public void ATableOfContentsListsTheHeadingStylesByName(string instruction)
    {
        var document = HeadingNames();
        ((Paragraph)document.Body.Blocks[0]).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<InstructionText>().Single().Text = instruction;

        Refresh(Editor(document), document);

        // The style based on a heading is no heading, but it has the heading's outline level (rule 3).
        Assert.Equal(["toc 1: OtherId", "toc 2: CapitalName", "toc 1: BasedOnHeading", "toc 8: NoType"], Listed(document));
    }

    [Fact]
    public void ADefaultParagraphStyleThatIsAHeadingMakesUnstyledAndUnknownStylesHeadings()
    {
        var document = Document(
            Text("NoStyle") + Text("Undefined", "Nowhere"),
            styles: "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"zz1\"><w:name w:val=\"heading 1\"/></w:style>");

        Assert.Equal(["NoStyle", "Undefined"], HeadingTargets(document));
    }

    // ---- rule 2: sequences and the chapter number look at heading styles only ----------------------------------------------------------------

    [Fact]
    public void OnlyAHeadingStyleRestartsASequenceAndNamesTheChapter()
    {
        var one = P(Field("SEQ A \\s 1") + Run(" ") + Field("STYLEREF 1 \\s"));
        var document = Document(
            Text("Alpha", "berschrift1") + one + one
            + Text("CustomLevel", "own") + one
            + Text("BasedOnHeading", "child") + one
            + Text("DirectLevel", extra: Level(0)) + one
            + Text("Beta", "berschrift1") + one,
            styles: Standard() + Headings(numbered: true) + Style("own", "Own", "Standard", "<w:numPr><w:numId w:val=\"1\"/></w:numPr>" + Level(0), Custom) + Style("child", "Child", "berschrift1"));

        Refresh(Editor(document), document);

        // The custom numbered paragraph and the one based on the heading are numbered 2 and 3 in the list, and the chapter number stays that of the last heading.
        Assert.Equal(["1", "1", "2", "1", "3", "1", "4", "1", "5", "1", "1", "4"], Results(document));
    }

    // ---- rules 3 to 5: the outline level and the switches -----------------------------------------------------------------------------------

    private static (string Text, string? Style, int? Direct, int? Without, int? With)[] LevelCases() =>
    [
        ("Heading", "berschrift1", null, 1, 1),
        ("HeadingDirectBody", "berschrift1", 9, 1, 1),
        ("HeadingDirectThree", "berschrift1", 2, 1, 1),
        ("OwnLevel", "own", null, 1, 1),
        ("ChildOfOwn", "child", null, 1, 1),
        ("DeepLevel", "deep", null, 5, 5),
        ("BasedButBody", "off", null, null, null),
        ("BasedButTwo", "two", null, 2, 2),
        ("BasedNoLevel", "based", null, 1, 1),
        ("BareHeading", "zz4", null, 4, 4),
        ("ChildOfBare", "bare", null, 4, 4),
        ("OwnDirectBody", "own", 9, 1, null),
        ("OwnDirectTwo", "own", 1, 1, 2),
        ("DirectOnly", null, 2, null, 3),
        ("PlainDirectBody", "Standard", 9, null, null),
        ("Plain", null, null, null, null),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLevelOfAParagraphUnderBothSwitches(bool outline)
    {
        var body = StaleToc("TOC \\o \"1-9\"" + (outline ? " \\u" : string.Empty))
            + string.Concat(LevelCases().Select(c => Text(c.Text, c.Style, c.Direct is { } level ? Level(level) : string.Empty)));
        var document = Document(
            body,
            styles: Standard() + Headings(numbered: false) + Style("own", "Own", "Standard", Level(0), Custom) + Style("child", "Child", "own") + Style("deep", "Deep", "Standard", Level(4)) + Style("off", "Off", "berschrift1", Level(9))
                + Style("two", "Two", "berschrift1", Level(1)) + Style("based", "Based", "berschrift1") + Style("zz4", "heading 4") + Style("bare", "ChildOfBare", "zz4"));

        Refresh(Editor(document), document);

        Assert.Equal(
            LevelCases().Select(c => (c.Text, Level: outline ? c.With : c.Without)).Where(c => c.Level is not null).Select(c => $"toc {c.Level}: {c.Text}"),
            Listed(document));
    }

    [Theory]
    [MemberData(nameof(Switches))]
    public void AParagraphWithoutAKnownStyleHasTheLevelOfTheDefaultStyle(string instruction)
    {
        var document = Document(
            StaleToc(instruction, "plain") + Text("NoStyle") + Text("Undefined", "Nowhere") + Text("Named", "Standard") + Text("Other", "plain"),
            styles: Standard(Level(1)) + Style("plain", "Plain", null));

        Refresh(Editor(document), document);

        Assert.Equal(["toc 2: NoStyle", "toc 2: Undefined", "toc 2: Named"], Listed(document));
    }

    // ---- rules 6 and 7: the styles Write gives ----------------------------------------------------------------------------------------------

    private static string TakenIds() =>
        Standard() + Headings(numbered: false) + Style("TOC1", "Kapitel eins", attributes: Custom) + Style("zz1", "toc 1") + Style("zz2", "toc 2", attributes: Custom) + Style("TOC3", "Drei")
        + Style("TableofFigures", "Bilder") + Style("zzf", "Table of Figures");

    [Fact]
    public void ARefreshGivesEntriesTheStyleNamedForThemAndMakesAMissingOneUnderAFreeId()
    {
        var document = Document(StaleToc("TOC \\o \"1-3\"") + ThreeHeadings + StaleToc("TOC \\c \"Figure\"") + P(Run("Figure ") + Field("SEQ Figure", "1")), styles: TakenIds());
        var editor = Editor(document);

        Refresh(editor, document);

        Assert.Equal(["zz1", "TOC2", "TOC31"], Styles(document));
        Assert.Equal(["zzf"], Styles(document, 1));
        Assert.Equal("toc 2", document.Styles!.Find("TOC2")!.Name);
        Assert.Equal("toc 3", document.Styles.Find("TOC31")!.Name);
        Assert.Equal("Drei", document.Styles.Find("TOC3")!.Name);
        Assert.Empty(Refresh(editor, document).Content);
    }

    [Fact]
    public void ANewListGivesEntriesTheStyleNamedForThemAndMakesAMissingOneUnderAFreeId()
    {
        var document = Document(Text("here") + ThreeHeadings, styles: TakenIds());
        var editor = Editor(document);

        Assert.NotNull(editor.InsertToc(At(document, 0, 4), new TocOptions(1, 3, true, true, TocLeader.Dots, null), new FakeFieldHost(document)));

        Assert.Equal(["zz1", "TOC2", "TOC31"], Styles(document));
        Assert.Equal("toc 3", document.Styles!.Find("TOC31")!.Name);
        Assert.Single(document.Styles.Styles, s => s.Id == "TOC3");
    }

    [Theory]
    [InlineData("TOC1", "Kapitel", "")]
    [InlineData("zz9", "caption", " w:customStyle=\"1\"")]
    [InlineData("Caption", "Kapitel", "")]
    public void AStyleThatIsNotNamedCaptionIsNotTheCaptionStyle(string id, string name, string attributes)
    {
        var document = Document(Text("pic"), styles: Standard() + Style(id, name, attributes: attributes));

        Assert.NotNull(Editor(document).InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));

        var made = document.Styles!.Find(Para(document, 1).Properties.StyleId!)!;
        Assert.NotEqual(id, made.Id);
        Assert.Equal(("caption", "Standard", "Standard"), (made.Name, made.BasedOn, made.Next));
        Assert.Equal(id == "Caption" ? "Caption1" : "Caption", made.Id);
    }

    [Fact]
    public void TheStyleNamedCaptionIsTheCaptionStyleBeforeAStyleWithThatId()
    {
        var document = Document(Text("pic"), styles: Standard() + Style("Caption", "Kapitel") + Style("zz1", "Caption"));

        Assert.NotNull(Editor(document).InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));

        Assert.Equal("zz1", Para(document, 1).Properties.StyleId);
        Assert.Equal(3, document.Styles!.Styles.Count);
    }

    // ---- the chapter number needs a numbered heading 1 ----------------------------------------------------------------------------------------

    [Fact]
    public void AChapterNumberNeedsTheStyleNamedHeading1ToBeNumbered()
    {
        var numberedOther = Document(
            Text("Alpha", "Heading1") + Text("pic"),
            styles: Standard() + Style("Heading1", "Kapitel", "Standard", "<w:numPr><w:numId w:val=\"1\"/></w:numPr>"));
        Assert.Equal(CaptionRefusal.ChapterNeedsNumberedHeadings, CaptionRules.Refusal(numberedOther, At(numberedOther, 1), chapterNumber: true));

        var plain = Document(Text("Alpha", "berschrift1") + Text("pic"));
        Assert.Equal(CaptionRefusal.ChapterNeedsNumberedHeadings, CaptionRules.Refusal(plain, At(plain, 1), chapterNumber: true));

        var numbered = Document(Text("Alpha", "berschrift1") + Text("pic"), numbered: true);
        Assert.Null(CaptionRules.Refusal(numbered, At(numbered, 1), chapterNumber: true));
    }
}
