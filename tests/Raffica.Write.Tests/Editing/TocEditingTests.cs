using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Tables of contents and lists of figures and tables (milestone 4.5b): the entries and their rules, the shape of the field, the hidden bookmarks, the empty sentence, the styles, the refusals, remove and the history.</summary>
public sealed class TocEditingTests
{
    private const string Section = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string P(string inner, string? style = null, string extra = "") =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}{extra}</w:pPr>{inner}</w:p>";

    private static string Text(string text, string? style = null) => P(Run(text), style);

    private static string Heading(int level, string text) => Text(text, "Heading" + level);

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run(result)
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    // Heading1 to Heading9 (outline levels in the styles), Normal, optionally Caption; numbered headings give Heading1 and Heading2 a list (1. and 1.1.).
    private static WordDocument Doc(string body, bool numbered = false, string? language = null, bool outlineStyle = false)
    {
        var lang = language is null ? string.Empty : $"<w:rPr><w:lang w:val=\"{language}\"/></w:rPr>";
        var styles = $"<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/>{lang}</w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            var number = numbered && i <= 2 ? $"<w:numPr><w:ilvl w:val=\"{i - 1}\"/><w:numId w:val=\"1\"/></w:numPr>" : string.Empty;
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr>{number}<w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        if (outlineStyle)
        {
            styles += "<w:style w:type=\"paragraph\" w:styleId=\"Chapterish\"><w:name w:val=\"Chapterish\"/><w:pPr><w:outlineLvl w:val=\"2\"/></w:pPr></w:style>";
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>"
            + "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(body + Section, LayoutModel.StylesPart(styles), LayoutModel.NumberingPart(numbering));
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static Paragraph Para(WordDocument document, int block) => (Paragraph)document.Body.Blocks[block];

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, Para(document, block), offset);

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static string Bytes(WordDocument document) => ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);

    // The body alone: the styles a step made in place stay after an undo, by design.
    private static string BodyBytes(WordDocument document) =>
        string.Concat(ModelSnapshot.Of(document).Elements().Where(p => (string?)p.Attribute("name") == "document").Select(p => p.ToString(SaveOptions.DisableFormatting)));

    private static readonly TocOptions s_default = new(1, 3, true, true, TocLeader.Dots, null);

    private static readonly string[] s_tocIds = ["TOC1", "TOC2", "TOC3"];

    // The paragraphs of the table of contents that starts at the block (up to and including the one with the end mark), through the grammar the editor itself uses.
    // The entry paragraphs of the first table of contents: a paragraph that holds only the end mark is not one of them.
    private static List<Paragraph> TocParagraphs(WordDocument document)
    {
        if (TocRefresh.Find(document.Body.Blocks) is not [var site, ..])
        {
            return [];
        }

        var last = TocRefresh.Analyze(site)?.TailParagraph == true ? site.Last - 1 : site.Last;
        return [.. Enumerable.Range(site.First, last - site.First + 1).Select(i => (Paragraph)site.Container[i])];
    }

    // What the stored text of every entry says, tabs shown as '|'.
    private static List<string> Lines(WordDocument document) =>
        [.. TocParagraphs(document).Select(p => string.Concat(ModelWalk.Inlines(p.Inlines).OfType<Run>().SelectMany(r => r.Content).Select(c => c switch { TextContent t => t.Text, TabContent => "|", _ => string.Empty })))];

    private static EditResult Insert(TextEditor editor, WordDocument document, int block, TocOptions? options = null, FakeFieldHost? host = null)
    {
        var result = editor.InsertToc(At(document, block), options ?? s_default, host ?? new FakeFieldHost(document));
        Assert.NotNull(result);
        return result;
    }

    [Fact]
    public void EntriesFollowTheHeadingsWithTheirNumberLabels()
    {
        var document = Doc(Heading(1, "Intro") + Text("body") + Heading(2, "Scope") + Heading(1, "Method") + Text(string.Empty), numbered: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        Insert(editor, document, 4, host: host);

        Assert.Equal(["1.|Intro|?", "1.1.|Scope|?", "2.|Method|?"], Lines(document));
        Assert.True(ReferenceToc.AssertEqualToExpected(document, host, "insert") == 3);
        Assert.Equal(["TOC1", "TOC2", "TOC1"], TocParagraphs(document).Select(p => p.Properties.StyleId));
    }

    [Fact]
    public void LevelsOneToThreeByDefaultAndUpToNine()
    {
        var body = Heading(1, "A") + Heading(2, "B") + Heading(3, "C") + Heading(4, "D") + Heading(9, "I") + Text(string.Empty);
        var document = Doc(body);
        var editor = Editor(document);
        Insert(editor, document, 5);
        Assert.Equal(["A|?", "B|?", "C|?"], Lines(document));

        var wide = Doc(body);
        var wideEditor = Editor(wide);
        Insert(wideEditor, wide, 5, new TocOptions(1, 9, true, true, TocLeader.Dots, null));
        Assert.Equal(["A|?", "B|?", "C|?", "D|?", "I|?"], Lines(wide));

        var narrow = Doc(body);
        Insert(Editor(narrow), narrow, 5, new TocOptions(2, 3, true, true, TocLeader.Dots, null));
        Assert.Equal(["B|?", "C|?"], Lines(narrow));
    }

    [Fact]
    public void OutlineLevelsAreIncludedWithU()
    {
        // A paragraph with its own outline level, one whose style has one, and Heading 1; body text (level 10) is not an entry.
        var body = Heading(1, "Chapter")
            + P(Run("Own level"), extra: "<w:outlineLvl w:val=\"1\"/>")
            + Text("From style", "Chapterish")
            + P(Run("Body text"), extra: "<w:outlineLvl w:val=\"9\"/>")
            + Text(string.Empty);
        var document = Doc(body, outlineStyle: true);
        var host = new FakeFieldHost(document);

        var result = Insert(Editor(document), document, 4, host: host);

        Assert.Equal(["Chapter|?", "Own level|?", "From style|?"], Lines(document));
        Assert.Equal(["TOC1", "TOC2", "TOC3"], TocParagraphs(document).Select(p => p.Properties.StyleId));
        Assert.Contains("\\u", Instruction(document), StringComparison.Ordinal);
        Assert.NotNull(result);
        ReferenceToc.AssertEqualToExpected(document, host, "outline");
    }

    [Fact]
    public void HiddenEmptyAndTrackedDeletedHeadingsAreLeftOut()
    {
        var hidden = P("<w:r><w:rPr><w:vanish/></w:rPr><w:t>secret</w:t></w:r>", "Heading1");
        var deleted = P("<w:del w:id=\"7\" w:author=\"a\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:delText>gone</w:delText></w:r></w:del>", "Heading1");
        var partly = P(Run("kept ") + "<w:r><w:rPr><w:vanish/></w:rPr><w:t>hidden part</w:t></w:r>" + "<w:del w:id=\"8\" w:author=\"a\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:delText>old</w:delText></w:r></w:del>" + Run("text"), "Heading1");
        var body = hidden + deleted + P(string.Empty, "Heading1") + Text("   ", "Heading1") + partly + Heading(1, "Real") + Text(string.Empty);
        var document = Doc(body);
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 6, host: host);

        Assert.Equal(["kept text|?", "Real|?"], Lines(document));
        ReferenceToc.AssertEqualToExpected(document, host, "left out");
    }

    [Fact]
    public void ALineBreakInAHeadingBecomesASpace()
    {
        var document = Doc(P("<w:r><w:t>Line one</w:t><w:br/><w:t>line two</w:t><w:tab/><w:t>end</w:t></w:r>", "Heading1") + Text(string.Empty));
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 1, host: host);

        Assert.Equal(["Line one line two end|?"], Lines(document));
        ReferenceToc.AssertEqualToExpected(document, host, "break");
    }

    [Fact]
    public void AHeadingInATableOrControlIsIncluded()
    {
        var cell = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr>" + Heading(2, "In a cell") + "</w:tc></w:tr></w:tbl>";
        var control = "<w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val=\"x\"/></w:docPartObj></w:sdtPr><w:sdtContent>" + Heading(1, "In a control") + Text("inside") + "</w:sdtContent></w:sdt>";
        var document = Doc(Heading(1, "Top") + cell + control + Text(string.Empty));
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, document.Body.Blocks.Count - 1, host: host);

        Assert.Equal(["Top|?", "In a cell|?", "In a control|?"], Lines(document));
        ReferenceToc.AssertEqualToExpected(document, host, "nested");
    }

    [Fact]
    public void AHeadingWithoutANumberLabelHasNoLabelInItsEntry()
    {
        // Heading 2 of a numbered document is numbered, Heading 3 is not: its entry has no label and no tab before the text.
        var document = Doc(Heading(1, "One") + Heading(3, "Three") + Text(string.Empty), numbered: true);
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 2, host: host);

        Assert.Equal(["1.|One|?", "Three|?"], Lines(document));
        ReferenceToc.AssertEqualToExpected(document, host, "labels");
    }

    [Fact]
    public void ListOfFiguresUsesCLabelAndTableofFiguresStyle()
    {
        var document = Doc(Text("picture") + Text("other") + Text(string.Empty));
        var editor = Editor(document);
        Assert.NotNull(editor.InsertCaption(At(document, 0), "Figure", ": First", CaptionPlace.Below, false, "Figure"));
        Assert.NotNull(editor.InsertCaption(At(document, 2), "Table", ": Numbers", CaptionPlace.Above, false, "Table"));
        Assert.NotNull(editor.InsertCaption(At(document, 3), "Figure", ": Second", CaptionPlace.Below, false, "Figure"));
        var host = new FakeFieldHost(document);

        Insert(editor, document, document.Body.Blocks.Count - 1, new TocOptions(1, 3, true, true, TocLeader.Dots, "Figure"), host);

        Assert.Equal(["Figure 1: First|?", "Figure 2: Second|?"], Lines(document));
        Assert.All(TocParagraphs(document), p => Assert.Equal("TableofFigures", p.Properties.StyleId));
        var instruction = Instruction(document);
        Assert.Contains("\\c \"Figure\"", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("\\o", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", instruction, StringComparison.Ordinal);
        ReferenceToc.AssertEqualToExpected(document, host, "figures");
    }

    [Fact]
    public void NoPageNumbersWritesN()
    {
        var document = Doc(Heading(1, "A") + Heading(2, "B") + Text(string.Empty));
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 2, new TocOptions(1, 3, false, true, TocLeader.Dots, null), host);

        Assert.Equal(["A", "B"], Lines(document));
        var instruction = Instruction(document);
        Assert.Contains("\\n", instruction, StringComparison.Ordinal);
        Assert.Contains("\\h \\z", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("PAGEREF", Bytes(document), StringComparison.Ordinal);
        Assert.All(TocParagraphs(document), p => Assert.Null(p.Properties.Get(XName.Get("tabs", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"))));
        ReferenceToc.AssertEqualToExpected(document, host, "no pages");
    }

    [Fact]
    public void RightAlignAndTheLeaderAreTheTabStopOfEveryEntry()
    {
        var document = Doc(Heading(1, "A") + Text(string.Empty));
        Insert(Editor(document), document, 1, new TocOptions(1, 3, true, true, TocLeader.Hyphens, null));

        var tabs = TocParagraphs(document)[0].Properties.Get(XName.Get("tabs", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"));
        var tab = Assert.Single(tabs!.Elements());
        var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        Assert.Equal("right", (string?)tab.Attribute(w + "val"));
        Assert.Equal("hyphen", (string?)tab.Attribute(w + "leader"));
        Assert.Equal("9360", (string?)tab.Attribute(w + "pos"));
    }

    [Fact]
    public void AnEmptyTocSaysSoInTheDocumentLanguage()
    {
        var english = Doc(Text("no headings here") + Text(string.Empty));
        var host = new FakeFieldHost(english);
        Insert(Editor(english), english, 1, host: host);
        Assert.Equal(["No table of contents entries found."], Lines(english));
        ReferenceToc.AssertEqualToExpected(english, host, "english");

        var german = Doc(Text("keine Überschrift") + Text(string.Empty), language: "de-DE");
        var germanHost = new FakeFieldHost(german);
        Insert(Editor(german), german, 1, host: germanHost);
        Assert.Equal(["Es wurden keine Einträge für das Inhaltsverzeichnis gefunden."], Lines(german));
        ReferenceToc.AssertEqualToExpected(german, germanHost, "german");
    }

    [Fact]
    public void TheHiddenTocBookmarksAreCreatedOnceAndReused()
    {
        var document = Doc(Heading(1, "A") + Heading(2, "B") + Text(string.Empty) + Text(string.Empty));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Insert(editor, document, 2, host: host);

        var first = BookmarkOperations.AllNames(document).Where(n => n.StartsWith("_Toc", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, first.Count);
        Assert.All(first, n => Assert.Matches("^_Toc[0-9]{9}$", n));
        Assert.Equal(first.Count, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // A refresh makes nothing new and a second list over the same headings reuses the names.
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
        var at = document.Body.Blocks.Count - 1;
        Assert.NotNull(editor.InsertToc(At(document, at), new TocOptions(1, 2, false, true, TocLeader.Dots, null), host));
        var second = BookmarkOperations.AllNames(document).Where(n => n.StartsWith("_Toc", StringComparison.Ordinal)).ToList();
        Assert.Equal(first.Order(), second.Order());
        Assert.Empty(BookmarkOperations.Visible(document));
    }

    [Fact]
    public void RemoveTocLeavesNoFieldNoTocBookmarkEntryAndOneUndoStep()
    {
        var document = Doc(Heading(1, "A") + Heading(2, "B") + Text("tail"));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var before = BodyBytes(document);
        Assert.NotNull(editor.InsertToc(At(document, 2, 0), s_default, host));
        Assert.Equal(2, TocParagraphs(document).Count);
        var afterInsert = BodyBytes(document);
        var steps = editor.History.UndoCount;

        var result = editor.RemoveToc(At(document, 2, 0));

        Assert.NotNull(result);
        Assert.Equal(steps + 1, editor.History.UndoCount);
        var xml = Bytes(document);
        Assert.DoesNotContain("TOC \\o", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("_Toc", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("PAGEREF", xml, StringComparison.Ordinal);
        Assert.Empty(TocRefresh.Find(document.Body.Blocks));
        Assert.Equal(["A", "B", "tail"], document.Body.Blocks.OfType<Paragraph>().Select(TextOf));

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(afterInsert, BodyBytes(document));
        Assert.NotNull(editor.History.Redo());
        Assert.DoesNotContain("TOC \\o", Bytes(document), StringComparison.Ordinal);
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, BodyBytes(document));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void TheFileHasNoDirtyOrUpdateFields()
    {
        var document = Doc(Heading(1, "A") + Text(string.Empty));
        Insert(Editor(document), document, 1);

        var xml = Bytes(document);
        Assert.DoesNotContain("w:dirty", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("updateFields", xml, StringComparison.Ordinal);
        Assert.Contains("w:history=\"1\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFieldIsOneMultiParagraphFieldWithTheMarksInTheFirstAndLastParagraph()
    {
        var document = Doc(Heading(1, "A") + Heading(1, "B") + Heading(1, "C") + Text(string.Empty));
        Insert(Editor(document), document, 3);

        var range = Assert.Single(TocRanges.In(document.Body.Blocks)!);
        Assert.Equal((3, 5), (range.First, range.Last));
        Assert.Equal("TOC \\o \"1-3\" \\h \\z \\u", range.Instruction.Trim());
        var first = (Paragraph)document.Body.Blocks[range.First];
        var last = (Paragraph)document.Body.Blocks[range.Last];
        Assert.Contains(first.Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is FieldChar { Kind: FieldCharKind.Begin });
        Assert.Contains(last.Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is FieldChar { Kind: FieldCharKind.End });
        Assert.All(TocParagraphs(document), p => Assert.NotNull(ModelWalk.Inlines(p.Inlines).OfType<Hyperlink>().Single().Anchor));
    }

    [Fact]
    public void TheStylesAreMadeAsCompanionsOfTheStep()
    {
        var document = Doc(Heading(1, "A") + Heading(2, "B") + Text(string.Empty));
        Assert.Null(document.Styles!.Find("TOC1"));
        var editor = Editor(document);

        var result = Insert(editor, document, 2);

        Assert.NotNull(result.Style);
        Assert.NotNull(document.Styles.Find("TOC1"));
        Assert.NotNull(document.Styles.Find("TOC2"));
        Assert.NotNull(document.Styles.Find("TOC3"));
        Assert.Null(document.Styles.Find("TableofFigures"));
        Assert.Equal(["toc 1", "toc 2", "toc 3"], s_tocIds.Select(id => document.Styles.Find(id)!.Name));
    }

    [Fact]
    public void TheRefusalsChangeNothing()
    {
        var document = Doc(Heading(1, "A") + Heading(1, "A2") + Text("x") + Text(string.Empty));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var before = BodyBytes(document);

        Assert.Null(editor.InsertToc(At(document, 2), new TocOptions(3, 2, true, true, TocLeader.Dots, null), host));
        Assert.Equal(TocRefusal.LevelRange, editor.LastTocRefusal);
        Assert.Null(editor.InsertToc(At(document, 2), new TocOptions(0, 3, true, true, TocLeader.Dots, null), host));
        Assert.Equal(TocRefusal.LevelRange, editor.LastTocRefusal);
        Assert.Null(editor.InsertToc(At(document, 2), new TocOptions(1, 3, true, true, TocLeader.Dots, "bad label"), host));
        Assert.Equal(TocRefusal.LabelNotAllowed, editor.LastTocRefusal);
        Assert.Null(editor.InsertToc(At(document, 2), s_default, host, prefixDocument: true));
        Assert.Equal(TocRefusal.Prefix, editor.LastTocRefusal);
        Assert.Null(editor.InsertToc(new TextPosition(new StoryRef(StoryKind.Note, "1"), Para(document, 2), 0), s_default, host));
        Assert.Equal(TocRefusal.NotInBody, editor.LastTocRefusal);
        Assert.Equal(before, BodyBytes(document));
        Assert.False(editor.History.CanUndo);

        // Inside a table of contents: refused with its reason; nothing to remove where there is none.
        Assert.NotNull(editor.InsertToc(At(document, 3), s_default, host));
        var inside = TocParagraphs(document)[0];
        Assert.Null(editor.InsertToc(new TextPosition(StoryRef.Body, inside, 0), s_default, host));
        Assert.Equal(TocRefusal.InsideToc, editor.LastTocRefusal);
        Assert.Equal(FieldRefusalKind.InsideToc, editor.LastFieldRefusal?.Kind);
        Assert.Null(editor.RemoveToc(At(document, 0)));
        Assert.Equal(TocRefusal.NothingToRemove, editor.LastTocRefusal);
    }

    [Fact]
    public void ARefreshAfterTheInsertFindsNothingToWrite()
    {
        var document = Doc(Heading(1, "A") + Heading(2, "B") + Text(string.Empty));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        Insert(editor, document, 2, host: host);

        var result = editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Empty(result.Content);
        Assert.Null(result.Structural);
        Assert.Equal(0, result.Written);
        Assert.False(result.StructuralDeferred);
    }

    [Fact]
    public void TheCaptionLabelsAGivenListCanBeMadeOfAreNeverEmpty()
    {
        // The card builds a list from these: the three built-in labels are always there, also in a document with no caption and no field at all.
        var document = Doc(Text("plain"));
        Assert.Equal(["Figure", "Table", "Equation"], CaptionLabels.Known(document).ToArray());
    }

    private static string Instruction(WordDocument document) => TocRefresh.Find(document.Body.Blocks)[0].Instruction;
}
