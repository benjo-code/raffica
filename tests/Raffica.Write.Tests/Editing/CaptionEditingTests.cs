using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Captions (milestone 4.5b): the shape of the paragraph, the numbers (at once and after the refresh), the chapter number, the label words, the place beside a table, the style as a companion, the refusals and the history.</summary>
public sealed class CaptionEditingTests
{
    private static readonly string[] s_threeLabels = ["Figure", "Table", "Equation"];

    private const string Section = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>";

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run(result)
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static string P(string inner, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}</w:pPr>{inner}</w:p>";

    private static string Text(string text, string? style = null) => P(Run(text), style);

    // The styles of a plain document; with numbered headings Heading1 carries the list of the numbering part, as the heading switch of the Home tab leaves it.
    private static WordDocument Doc(string body, bool captionStyle = true, bool numberedHeadings = false, string? language = null)
    {
        var lang = language is null ? string.Empty : $"<w:rPr><w:lang w:val=\"{language}\"/></w:rPr>";
        var styles = $"<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/>{lang}</w:style>";
        if (captionStyle)
        {
            styles += "<w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/></w:style>";
        }

        for (var i = 1; i <= 3; i++)
        {
            var number = numberedHeadings ? "<w:numPr><w:numId w:val=\"1\"/></w:numPr>" : string.Empty;
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr>{number}<w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(body + Section, LayoutModel.StylesPart(styles), LayoutModel.NumberingPart(numbering));
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static Paragraph Para(WordDocument document, int block) => (Paragraph)document.Body.Blocks[block];

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, Para(document, block), offset);

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static string Bytes(WordDocument document) => ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);

    private static IReadOnlyList<FieldSpan> Fields(Paragraph paragraph) => [.. FieldView.InParagraph(paragraph).Where(f => f.Parent is null)];

    private static void Refresh(TextEditor editor, WordDocument document, FakeFieldHost? host = null)
    {
        host ??= new FakeFieldHost(document);
        editor.RefreshFields(host, RefreshScope.OrderBased);
        ReferenceFields.AssertStoredEqualsExpected(document, host, "after the refresh");
    }

    [Fact]
    public void AFigureCaptionIsALabelSeqAndText()
    {
        var document = Doc(Text("the picture"));
        var editor = Editor(document);

        var result = editor.InsertCaption(At(document, 0), "Figure", ": Results", CaptionPlace.Below, chapterNumber: false, "Figure");

        Assert.NotNull(result);
        Assert.Equal(2, document.Body.Blocks.Count);
        var caption = Para(document, 1);
        Assert.Equal("Caption", caption.Properties.StyleId);
        Assert.Equal("Figure 1: Results", TextOf(caption));
        var field = Assert.Single(Fields(caption));
        Assert.Equal(FieldKind.Seq, field.Support.Kind);
        Assert.Equal("Figure", field.Instruction.Argument);
        Assert.Equal("ARABIC", field.Instruction.ValueOf("*"));
        Assert.False(field.Instruction.Has("s"));
        Assert.True(field.IsComplete);

        // Word's own shape: the label word and its space, the five runs of the field, the text; no w:dirty, one change, one step.
        var xml = ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);
        Assert.DoesNotContain("w:dirty", xml, StringComparison.Ordinal);
        Assert.Equal(TextOf(caption).Length, result.Caret.Offset);
        Assert.Same(caption, result.Caret.Paragraph);
        var change = Assert.Single(result.Changes);
        Assert.Equal((1, 0, 1), (change.First, change.Removed, change.Added));
        Assert.Null(result.Style);
        Assert.True(editor.History.CanUndo);
    }

    [Fact]
    public void TheFirstCaptionNumberIs1AndTheNextIs2AfterRefresh()
    {
        var document = Doc(Text("one") + Text("two"));
        var editor = Editor(document);

        Assert.NotNull(editor.InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.NotNull(editor.InsertCaption(At(document, 2), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.NotNull(editor.InsertCaption(At(document, 3), "Table", string.Empty, CaptionPlace.Above, false, "Table"));

        // The numbers are right the moment the caption exists, per label; a refresh finds nothing to write.
        Assert.Equal(["Figure 1", "Figure 2", "Table 1"], new[] { TextOf(Para(document, 1)), TextOf(Para(document, 4)), TextOf(Para(document, 3)) });
        var host = new FakeFieldHost(document);
        Assert.Equal(0, editor.RefreshFields(host, RefreshScope.OrderBased).Written);
        ReferenceFields.AssertStoredEqualsExpected(document, host, "after the inserts");
    }

    [Fact]
    public void InsertingBeforeRenumbersTheLater()
    {
        var document = Doc(Text("one") + Text("two"));
        var editor = Editor(document);
        Assert.NotNull(editor.InsertCaption(At(document, 1), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.NotNull(editor.InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));

        // The new caption is the first one and shows 1; the later one still shows its old 1 until the refresh (which the session runs 250 ms after the edit).
        Assert.Equal("Figure 1", TextOf(Para(document, 1)));
        Assert.Equal("Figure 1", TextOf(Para(document, 3)));
        Refresh(editor, document);
        Assert.Equal(["Figure 1", "Figure 2"], new[] { TextOf(Para(document, 1)), TextOf(Para(document, 3)) });
    }

    [Fact]
    public void DeletingOrMovingACaptionRenumbers()
    {
        var document = Doc(Text("a") + Text("b") + Text("c"));
        var editor = Editor(document);
        Assert.NotNull(editor.InsertCaption(At(document, 2), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.NotNull(editor.InsertCaption(At(document, 1), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.NotNull(editor.InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Refresh(editor, document);
        Assert.Equal(["Figure 1", "Figure 2", "Figure 3"], new[] { TextOf(Para(document, 1)), TextOf(Para(document, 3)), TextOf(Para(document, 5)) });

        // Moving the last caption (with its paragraph) to the top renumbers all three.
        Assert.NotNull(editor.MoveBlocks(5, 1, 0));
        Refresh(editor, document);
        Assert.Equal("Figure 1", TextOf(Para(document, 0)));
        Assert.Equal(["Figure 2", "Figure 3"], new[] { TextOf(Para(document, 2)), TextOf(Para(document, 4)) });

        // Deleting the first caption's paragraph renumbers the others.
        var first = Para(document, 0);
        var last = Para(document, 1);
        Assert.NotNull(editor.Delete(new TextRange(new TextPosition(StoryRef.Body, first, 0), new TextPosition(StoryRef.Body, last, TextOf(last).Length))));
        Refresh(editor, document);
        Assert.Equal(["Figure 1", "Figure 2"], document.Body.Blocks.OfType<Paragraph>().Select(TextOf).Where(t => t.StartsWith("Figure", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void ChapterNumberWritesStyleRefAndSeqS1()
    {
        var document = Doc(Text("Chapter one", "Heading1") + Text("pic") + Text("Chapter two", "Heading1") + Text("pic two"), numberedHeadings: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        Assert.NotNull(editor.InsertCaption(At(document, 1), "Figure", ": a", CaptionPlace.Below, chapterNumber: true, "Figure", host));
        Assert.NotNull(editor.InsertCaption(At(document, 4), "Figure", ": b", CaptionPlace.Below, chapterNumber: true, "Figure", host));

        var first = Para(document, 2);
        var fields = Fields(first);
        Assert.Equal(2, fields.Count);
        Assert.Equal(FieldKind.StyleRef, fields[0].Support.Kind);
        Assert.Equal("1", fields[0].Instruction.Argument);
        Assert.True(fields[0].Instruction.Has("s"));
        Assert.Equal(FieldKind.Seq, fields[1].Support.Kind);
        Assert.Equal("1", fields[1].Instruction.ValueOf("s"));
        Assert.Equal("Figure 1-1: a", TextOf(first));
        Assert.Equal("Figure 2-1: b", TextOf(Para(document, 5)));

        // A third in the first chapter restarts nothing there; the oracle and a refresh agree.
        Assert.NotNull(editor.InsertCaption(At(document, 2), "Figure", string.Empty, CaptionPlace.Below, true, "Figure", host));
        Assert.Equal("Figure 1-2", TextOf(Para(document, 3)));
        Refresh(editor, document, host);
        Assert.Equal(["Figure 1-1: a", "Figure 1-2", "Figure 2-1: b"], document.Body.Blocks.OfType<Paragraph>().Select(TextOf).Where(t => t.StartsWith("Figure", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void ChapterNumberWithoutNumberedHeadingsIsRefusedWithAReason()
    {
        // No numbered headings at all: the rule names it before the editor is asked.
        var plain = Doc(Text("Chapter one", "Heading1") + Text("pic"));
        Assert.Equal(CaptionRefusal.ChapterNeedsNumberedHeadings, CaptionRules.Refusal(plain, At(plain, 1), chapterNumber: true));
        Assert.Null(CaptionRules.Refusal(plain, At(plain, 1), chapterNumber: false));
        var editor = Editor(plain);
        var before = Bytes(plain);
        var revision = plain.Edits.Revision;
        Assert.Null(editor.InsertCaption(At(plain, 1), "Figure", string.Empty, CaptionPlace.Below, true, "Figure"));
        Assert.Equal(CaptionRefusal.ChapterNeedsNumberedHeadings, editor.LastCaptionRefusal);
        Assert.Equal(before, Bytes(plain));
        Assert.Equal(revision, plain.Edits.Revision);
        Assert.False(editor.History.CanUndo);

        // Numbered headings, but the caption would stand above the first Heading 1: there is no chapter to name.
        var numbered = Doc(Text("pic") + Text("Chapter one", "Heading1") + Text("pic two"), numberedHeadings: true);
        var second = Editor(numbered);
        var host = new FakeFieldHost(numbered);
        Assert.Null(second.InsertCaption(At(numbered, 0), "Figure", string.Empty, CaptionPlace.Below, true, "Figure", host));
        Assert.Equal(CaptionRefusal.ChapterNeedsNumberedHeadings, second.LastCaptionRefusal);
        Assert.Equal(3, numbered.Body.Blocks.Count);
        Assert.NotNull(second.InsertCaption(At(numbered, 2), "Figure", string.Empty, CaptionPlace.Below, true, "Figure", host));
        Assert.Null(second.LastCaptionRefusal);
    }

    [Fact]
    public void TheLabelWordFollowsTheDocumentLanguageThenTheUiLanguage()
    {
        var en = CultureInfo.GetCultureInfo("en");
        var de = CultureInfo.GetCultureInfo("de");
        var germanDocument = Doc(Text("x"), language: "de-DE");
        var englishDocument = Doc(Text("x"), language: "en-US");
        var silentDocument = Doc(Text("x"));
        var frenchDocument = Doc(Text("x"), language: "fr-FR");

        // The document language decides when it is English or German; the interface decides when the file says nothing or something else.
        Assert.Equal(["Abbildung", "Tabelle", "Gleichung"], s_threeLabels.Select(l => CaptionLabels.WordFor(germanDocument, l, en)));
        Assert.Equal(["Figure", "Table", "Equation"], s_threeLabels.Select(l => CaptionLabels.WordFor(englishDocument, l, de)));
        Assert.Equal("Abbildung", CaptionLabels.WordFor(silentDocument, "Figure", de));
        Assert.Equal("Figure", CaptionLabels.WordFor(silentDocument, "Figure", en));
        Assert.Equal("Tabelle", CaptionLabels.WordFor(frenchDocument, "Table", de));
        Assert.Equal("Table", CaptionLabels.WordFor(frenchDocument, "Table", en));
        Assert.Equal("de", CaptionLabels.DocumentLanguage(germanDocument));
        Assert.Null(CaptionLabels.DocumentLanguage(silentDocument));

        // A label of the file's own is shown as it is, and the word is plain, editable text in the document.
        Assert.Equal("Listing", CaptionLabels.WordFor(germanDocument, "Listing", de));
        Assert.Equal("Abbildung", CaptionLabels.WordFor("Figure", de));
        var editor = Editor(germanDocument);
        Assert.NotNull(editor.InsertCaption(At(germanDocument, 0), "Figure", string.Empty, CaptionPlace.Below, false, CaptionLabels.WordFor(germanDocument, "Figure", en)));
        Assert.Equal("Abbildung 1", TextOf(Para(germanDocument, 1)));
        var word = Para(germanDocument, 1).Inlines.OfType<Run>().First();
        Assert.Equal("Abbildung ", Assert.IsType<TextContent>(Assert.Single(word.Content)).Text);
    }

    [Fact]
    public void ACaptionOnASelectedPictureOrTableIsPlacedAboveOrBelow()
    {
        const string TableXml = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = Doc(Text("before") + TableXml + Text("after"));
        var editor = Editor(document);
        var reference = new TableRef(StoryRef.Body, 1);

        // Below a table: a sibling after it, in the body.
        Assert.NotNull(editor.InsertCaption(reference, "Table", ": below", CaptionPlace.Below, false, "Table"));
        Assert.IsType<Table>(document.Body.Blocks[1]);
        Assert.Equal("Table 1: below", TextOf(Para(document, 2)));
        Assert.Equal("Caption", Para(document, 2).Properties.StyleId);

        // Above it: a sibling before it, and it numbers first.
        Assert.NotNull(editor.InsertCaption(reference, "Table", ": above", CaptionPlace.Above, false, "Table"));
        Assert.Equal("Table 1: above", TextOf(Para(document, 1)));
        Assert.IsType<Table>(document.Body.Blocks[2]);
        Refresh(editor, document);
        Assert.Equal("Table 2: below", TextOf(Para(document, 3)));

        // Undo twice: back to the table in place; the caret of an undo is in the table, not beside it.
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(["before", "after"], document.Body.Blocks.OfType<Paragraph>().Select(TextOf).ToArray());
        Assert.IsType<Table>(document.Body.Blocks[1]);

        // A picture's paragraph is a paragraph like any other: the caption goes above or below it, in the same container (here a cell).
        var cell = ((Table)document.Body.Blocks[1]).Rows.OfType<TableRow>().Single().Items.OfType<TableCell>().Single();
        var inCell = (Paragraph)cell.Blocks[0];
        Assert.NotNull(editor.InsertCaption(new TextPosition(StoryRef.Body, inCell, 0), "Figure", string.Empty, CaptionPlace.Above, false, "Figure"));
        Assert.Equal(2, cell.Blocks.Count);
        Assert.Equal("Figure 1", TextOf((Paragraph)cell.Blocks[0]));
        Assert.Equal("cell", TextOf((Paragraph)cell.Blocks[1]));
        Assert.Equal(3, document.Body.Blocks.Count);

        // A reference that names no table is refused.
        Assert.Null(editor.InsertCaption(new TableRef(StoryRef.Body, 0), "Table", string.Empty, CaptionPlace.Below, false, "Table"));
        Assert.Equal(CaptionRefusal.NoPosition, editor.LastCaptionRefusal);
    }

    [Fact]
    public void TheCaptionStyleIsACompanionOfTheStep()
    {
        var document = Doc(Text("one") + Text("two"), captionStyle: false);
        var editor = Editor(document);
        Assert.Null(document.Styles!.Find("Caption"));

        var first = editor.InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure");

        // The first caption makes the style and says so (the session then lays out afresh); a second finds it and says nothing.
        Assert.NotNull(first?.Style);
        var style = document.Styles.Find("Caption");
        Assert.NotNull(style);
        Assert.Equal("caption", style.Name);
        Assert.Equal("paragraph", style.Type);
        Assert.Equal("Caption", Para(document, 1).Properties.StyleId);
        var second = editor.InsertCaption(At(document, 2), "Figure", string.Empty, CaptionPlace.Below, false, "Figure");
        Assert.NotNull(second);
        Assert.Null(second.Style);
        Assert.Single(document.Styles.Styles, s => s.Id == "Caption");

        // A file whose caption style has a localized id gets its own style, not a second one.
        var german = Doc(Text("x"), captionStyle: false);
        german.Styles!.Styles.Add(new Style { Id = "Beschriftung", Type = "paragraph", Name = "caption" });
        var germanEditor = Editor(german);
        var result = germanEditor.InsertCaption(At(german, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Abbildung");
        Assert.Null(result?.Style);
        Assert.Equal("Beschriftung", Para(german, 1).Properties.StyleId);
        Assert.Null(german.Styles.Find("Caption"));
    }

    [Fact]
    public void UndoRemovesTheParagraphAndTheNumbersFollow()
    {
        var document = Doc(Text("one") + Text("two"));
        var editor = Editor(document);
        var start = Bytes(document);
        Assert.NotNull(editor.InsertCaption(At(document, 0), "Figure", ": a", CaptionPlace.Below, false, "Figure"));
        var afterFirst = Bytes(document);
        Assert.NotNull(editor.InsertCaption(At(document, 2), "Figure", ": b", CaptionPlace.Below, false, "Figure"));
        var afterSecond = Bytes(document);

        var undone = editor.History.Undo();

        Assert.NotNull(undone);
        Assert.Equal(afterFirst, Bytes(document));
        Assert.Equal(3, document.Body.Blocks.Count);
        Assert.Equal("two", TextOf(undone.Caret.Paragraph));
        Refresh(editor, document);
        Assert.Equal("Figure 1: a", TextOf(Para(document, 1)));

        Assert.NotNull(editor.History.Redo());
        Assert.Equal(afterSecond, Bytes(document));
        Refresh(editor, document);
        Assert.Equal(["Figure 1: a", "Figure 2: b"], document.Body.Blocks.OfType<Paragraph>().Select(TextOf).Where(t => t.StartsWith("Figure", StringComparison.Ordinal)).ToArray());

        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, Bytes(document));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void AFileLabelIsOffered()
    {
        var document = Doc(
            P(Run("Listing ") + Field("SEQ Listing \\* ARABIC", "1"))
            + P(Run("Figure ") + Field("SEQ figure", "1"))
            + P(Run("Scheme ") + Field("SEQ Scheme", "1"))
            + P(Run("Listing ") + Field("seq LISTING", "2"))
            + "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/>" + P(Run("Plan ") + Field("SEQ Plan", "1")) + "</w:tc></w:tr></w:tbl>");

        var labels = CaptionLabels.Known(document);

        // The three of Write first, then the file's own in the order it first uses them, a spelling in another case being the same label.
        Assert.Equal(["Figure", "Table", "Equation", "Listing", "Scheme", "Plan"], labels);
        Assert.Equal(["Figure", "Table", "Equation"], CaptionLabels.Known(Doc(Text("plain"))));

        // A label of the file takes its own counter; a new label is a sequence identifier (the rule of a bookmark name).
        var editor = Editor(document);
        Assert.NotNull(editor.InsertCaption(At(document, 3), "Listing", string.Empty, CaptionPlace.Below, false, "Listing"));
        Assert.Equal("Listing 3", TextOf(Para(document, 4)));
        Assert.Null(CaptionLabels.Problem("Scheme_2"));
        Assert.Equal(BookmarkNameProblem.BadCharacter, CaptionLabels.Problem("two words"));
        Assert.Equal(BookmarkNameProblem.BadStart, CaptionLabels.Problem("1st"));
        Assert.Equal(BookmarkNameProblem.Empty, CaptionLabels.Problem(string.Empty));
        Assert.Null(editor.InsertCaption(At(document, 0), "two words", string.Empty, CaptionPlace.Below, false, "x"));
    }

    [Fact]
    public void ARefusalChangesNothingAndSaysWhy()
    {
        // A table of contents: nothing is inserted at an entry, the edit leaves its own reason.
        var toc = TocDocument.Toc(false, "Entry one", "Entry two");
        var tocDocument = TocDocument.Document([TocDocument.Plain("before"), .. toc, TocDocument.Plain("after")]);
        var tocEditor = Editor(tocDocument);
        var before = Bytes(tocDocument);
        Assert.Null(tocEditor.InsertCaption(At(tocDocument, 2), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.Equal(CaptionRefusal.InsideToc, tocEditor.LastCaptionRefusal);
        Assert.Equal(FieldRefusalKind.InsideToc, tocEditor.LastFieldRefusal?.Kind);
        Assert.Equal(CaptionRefusal.InsideToc, tocEditor.RefusalForCaption(At(tocDocument, 1), false));
        Assert.Equal(before, Bytes(tocDocument));
        Assert.NotNull(tocEditor.InsertCaption(At(tocDocument, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));

        var document = Doc(Text("one") + Text("two"));
        var editor = Editor(document);
        var start = Bytes(document);
        Assert.Equal(CaptionRefusal.Prefix, editor.RefusalForCaption(At(document, 0), false, prefixDocument: true));
        Assert.Null(editor.InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure", null, prefixDocument: true));
        Assert.Equal(CaptionRefusal.Prefix, editor.LastCaptionRefusal);

        // Not the body.
        var header = new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId9"), Para(document, 0), 0);
        Assert.Equal(CaptionRefusal.NotInBody, CaptionRules.Refusal(document, header, false));
        Assert.Equal(CaptionRefusal.NoPosition, CaptionRules.Refusal(document, new TextPosition(StoryRef.Body, new Paragraph(), 0), false));

        // A text that is more than a line, a word with a line break and a label that is no identifier.
        Assert.Null(editor.InsertCaption(At(document, 0), "Figure", "a\nb", CaptionPlace.Below, false, "Figure"));
        Assert.Null(editor.InsertCaption(At(document, 0), "Figure", new string('x', 1001), CaptionPlace.Below, false, "Figure"));
        Assert.Null(editor.InsertCaption(At(document, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Fig\ture"));
        Assert.Equal(start, Bytes(document));
        Assert.False(editor.History.CanUndo);

        // A paragraph that ends its section: below it the caption would stand in the next section.
        var sectioned = Doc("<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:pPr><w:r><w:t>last of one</w:t></w:r></w:p>" + Text("two"));
        var sectionEditor = Editor(sectioned);
        Assert.Null(sectionEditor.InsertCaption(At(sectioned, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.Equal(CaptionRefusal.SectionEnd, sectionEditor.LastCaptionRefusal);
        Assert.NotNull(sectionEditor.InsertCaption(At(sectioned, 0), "Figure", string.Empty, CaptionPlace.Above, false, "Figure"));

        // A position that is not in the document is refused, not guessed.
        Assert.Null(editor.InsertCaption(new TextPosition(StoryRef.Body, new Paragraph(), 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.Equal(CaptionRefusal.NoPosition, editor.LastCaptionRefusal);
    }

    [Fact]
    public void ARestrictedControlTakesNoCaption()
    {
        var control = "<w:sdt><w:sdtPr><w:text/></w:sdtPr><w:sdtContent>" + Text("only line") + "</w:sdtContent></w:sdt>";
        var document = Doc(control + Text("after"));
        var editor = Editor(document);
        var inside = ((ContentControlBlock)document.Body.Blocks[0]).Content[0] as Paragraph;
        Assert.NotNull(inside);

        Assert.Equal(CaptionRefusal.RestrictedControl, editor.RefusalForCaption(new TextPosition(StoryRef.Body, inside, 0), false));
        Assert.Null(editor.InsertCaption(new TextPosition(StoryRef.Body, inside, 0), "Figure", string.Empty, CaptionPlace.Below, false, "Figure"));
        Assert.Single(((ContentControlBlock)document.Body.Blocks[0]).Content);
    }

    [Fact]
    public void ACaptionOnARealPictureStandsAboveOrBelowItsParagraphAndUndoRestoresTheBytes()
    {
        var document = Doc(Text("alpha") + Text("beta"));
        var editor = Editor(document);
        Assert.NotNull(editor.InsertPicture(At(document, 0, 2), TestPictures.Png(40, 30), null));
        var id = editor.PictureOfSelection(new TextRange(At(document, 0, 2), At(document, 0, 3)));
        Assert.NotNull(id);
        var behind = editor.PositionAfterPicture(id);
        Assert.NotNull(behind);
        var start = Bytes(document);

        Assert.NotNull(editor.InsertCaption(behind.Value, "Figure", ": cat", CaptionPlace.Below, false, "Figure"));
        Assert.Equal("Figure 1: cat", TextOf(Para(document, 1)));
        Assert.Equal("beta", TextOf(Para(document, 2)));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, Bytes(document));

        Assert.NotNull(editor.InsertCaption(behind.Value, "Figure", ": cat", CaptionPlace.Above, false, "Figure"));
        Assert.Equal("Figure 1: cat", TextOf(Para(document, 0)));
        Assert.Equal(3, document.Body.Blocks.Count);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, Bytes(document));
    }

    [Fact]
    public void ACaptionOnATableUndoRestoresTheBytes()
    {
        const string TableXml = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = Doc(Text("before") + TableXml + Text("after"));
        var editor = Editor(document);
        var start = Bytes(document);
        var table = new TableRef(StoryRef.Body, 1);

        foreach (var place in new[] { CaptionPlace.Above, CaptionPlace.Below })
        {
            var result = editor.InsertCaption(table, "Table", ": t", place, false, "Table");
            Assert.NotNull(result);
            var at = place == CaptionPlace.Above ? 1 : 2;
            Assert.Equal("Table 1: t", TextOf(Para(document, at)));
            Assert.IsType<Table>(document.Body.Blocks[place == CaptionPlace.Above ? 2 : 1]);
            Assert.NotNull(editor.History.Undo());
            Assert.Equal(start, Bytes(document));
        }
    }
}
