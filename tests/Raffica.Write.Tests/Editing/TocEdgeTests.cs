using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// There is always a place to type around a table of contents (milestone 4.5b): an insert never leaves the field last in its body, cell or content control, Enter at the end edge of a field gives a paragraph after it (a file
/// may already end in one), and Enter at the start edge of a field that is first in its list of blocks gives a paragraph above it. The field itself stays protected.
/// </summary>
public sealed class TocEdgeTests
{
    private static readonly TocOptions s_options = new(1, 3, true, true, TocLeader.Dots, null);

    // ---- helpers ------------------------------------------------------------------------------------------------------------------------

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string P(string inner, string? style = null) => $"<w:p>{(style is null ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")}{inner}</w:p>";

    private static string MarkXml(string kind) => $"<w:r><w:fldChar w:fldCharType=\"{kind}\"/></w:r>";

    private static string TocBegin() => MarkXml("begin") + "<w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-1\" \\h \\z \\u </w:instrText></w:r>" + MarkXml("separate");

    // A hand-written list of two entry paragraphs; the end mark is in the last entry, or (Word's shape) alone in a paragraph of its own.
    private static string TocXml(bool endOwn = true, string endPPr = "", string afterEnd = "") =>
        P(TocBegin() + Run("Stale one"))
        + (endOwn
            ? P(Run("Stale two")) + $"<w:p>{endPPr}{MarkXml("end")}{afterEnd}</w:p>"
            : $"<w:p>{endPPr}{Run("Stale two")}{MarkXml("end")}{afterEnd}</w:p>");

    private static string Cell(string content) =>
        "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"6000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/>" + content + "</w:tc></w:tr></w:tbl>";

    private static string Control(string content, string properties = "") => $"<w:sdt><w:sdtPr>{properties}</w:sdtPr><w:sdtContent>{content}</w:sdtContent></w:sdt>";

    // A blank document of Write whose body is replaced by the XML given (its styles and section properties stay).
    private static DocxFile File(string body)
    {
        using var buffer = new MemoryStream();
        buffer.Write(BlankDocument.Create(PaperSize.A4));
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("word/document.xml")!;
            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                xml = reader.ReadToEnd();
            }

            var from = xml.IndexOf("<w:body>", StringComparison.Ordinal) + "<w:body>".Length;
            var to = xml.IndexOf("<w:sectPr", StringComparison.Ordinal);
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            writer.Write(string.Concat(xml.AsSpan(0, from), body, xml.AsSpan(to)));
        }

        return DocxFile.Open(buffer.ToArray());
    }

    private static TextEditor EditorOf(DocxFile file) => new(file.Model, (_, _) => ProjectionHost.Plain);

    private static IEnumerable<Paragraph> Paragraphs(WordDocument document) => ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>();

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextPosition End(Paragraph paragraph) => At(paragraph, TextOf(paragraph).Length);

    private static TextRange Caret(TextPosition at) => new(at, at);

    private static string Bytes(WordDocument document) => string.Concat(ModelSnapshot.Of(document).Elements().Where(p => (string?)p.Attribute("name") == "document").Select(p => p.ToString(SaveOptions.DisableFormatting)));

    // The list of blocks that holds the one table of contents of the document, and its range there.
    private static (IList<Block> Container, int First, int Last) Site(WordDocument document)
    {
        var site = Assert.Single(TocRefresh.Find(document.Body.Blocks));
        return (site.Container, site.First, site.Last);
    }

    private static Paragraph Para(IList<Block> container, int index) => Assert.IsType<Paragraph>(container[index]);

    // The paragraph is in no table of contents of its list of blocks.
    private static void AssertOutsideEveryToc(TextEditor editor, IList<Block> container, Paragraph paragraph)
    {
        var index = container.IndexOf(paragraph);
        Assert.True(index >= 0, "the paragraph is not in the list of blocks");
        Assert.DoesNotContain(editor.TocRangesOf(container) ?? [], r => index >= r.First && index <= r.Last);
    }

    // Types into the paragraph, then undoes everything back to the start and redoes it: both ways the bytes are those of the moment.
    private static void AssertUndoAndRedoGiveTheSameBytes(TextEditor editor, WordDocument document, string before, int steps)
    {
        var after = Bytes(document);
        for (var i = 0; i < steps; i++)
        {
            Assert.NotNull(editor.History.Undo());
        }

        Assert.Equal(before, Bytes(document));
        for (var i = 0; i < steps; i++)
        {
            Assert.NotNull(editor.History.Redo());
        }

        Assert.Equal(after, Bytes(document));
    }

    // A refresh (a heading changes the entries) keeps the text typed next to the list where it is, and the saved file read again has it there too.
    private static async Task AssertARefreshAndASaveKeepAsync(DocxFile file, TextEditor editor, Paragraph typed, string text)
    {
        var headings = file.Model.Body.Blocks.OfType<Paragraph>().Where(p => p.Properties.StyleId == "Heading1").ToList();
        if (headings.Count > 0)
        {
            Assert.NotEmpty(editor.InsertText(Caret(End(headings[0])), " more").Changes);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var result = editor.RefreshFields(new FakeFieldHost(file.Model), RefreshScope.WithPages);
            if (result.Structural is { } record)
            {
                editor.History.AttachDerived(record);
            }
        }

        Assert.Equal(text, TextOf(typed));
        var (container, first, last) = Site(file.Model);
        var index = Paragraphs(file.Model).ToList().IndexOf(typed);
        Assert.True(index >= 0, "the typed paragraph is gone after the refresh");
        AssertOutsideEveryToc(editor, TextEditor.FindIn(file.Model.Body.Blocks, typed)!.Value.Container, typed);
        if (headings.Count > 0)
        {
            Assert.Contains(Enumerable.Range(first, last - first + 1).Select(i => TextOf(Para(container, i))), line => line.Contains(" more", StringComparison.Ordinal));
        }

        var folder = Directory.CreateTempSubdirectory("raffica-write-tocedge-").FullName;
        try
        {
            var path = Path.Combine(folder, "edge.docx");
            await file.SaveAsync(path, TestContext.Current.CancellationToken);
            using var again = DocxFile.Open(path);
            Assert.Equal(Paragraphs(file.Model).Select(TextOf), Paragraphs(again.Model).Select(TextOf));
            var saved = Assert.Single(TocRefresh.Find(again.Model.Body.Blocks));
            Assert.DoesNotContain(text, Enumerable.Range(saved.First, saved.Last - saved.First + 1).Select(i => TextOf(Para(saved.Container, i))));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- inserting ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AListInsertedIntoTheLastEmptyParagraphOfANewDocumentLeavesAPlaceToType()
    {
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        Assert.NotEmpty(editor.InsertText(Caret(At(Para(body, 0), 0)), "Title\n").Changes);
        var before = Bytes(file.Model);

        var inserted = editor.InsertToc(At(Para(body, 1), 0), s_options, new FakeFieldHost(file.Model));

        Assert.NotNull(inserted);
        var (container, _, last) = Site(file.Model);
        Assert.Equal(body.Count - 2, last);
        var after = Para(container, last + 1);
        Assert.Empty(after.Inlines);
        Assert.Equal(End(Para(container, last)), inserted.Caret);
        AssertOutsideEveryToc(editor, container, after);

        // The end edge stays protected; the paragraph after it takes text, Enter and a table.
        Assert.Empty(editor.InsertText(Caret(inserted.Caret), "x").Changes);
        Assert.NotEmpty(editor.InsertText(Caret(At(after, 0)), "after").Changes);
        AssertUndoAndRedoGiveTheSameBytes(editor, file.Model, before, 2);
        var typed = Para(body, body.Count - 1);
        Assert.Equal("after", TextOf(typed));
        await AssertARefreshAndASaveKeepAsync(file, editor, typed, "after");
    }

    [Fact]
    public async Task AListInsertedBelowTheLastParagraphLeavesAPlaceToType()
    {
        using var file = File(P(Run("Chapter"), "Heading1") + P(Run("The end of the text.")));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var before = Bytes(file.Model);

        var inserted = editor.InsertToc(End(Para(body, 1)), s_options, new FakeFieldHost(file.Model));

        Assert.NotNull(inserted);
        var (container, first, last) = Site(file.Model);
        Assert.Equal(2, first);
        Assert.Equal(body.Count - 2, last);
        var after = Para(container, last + 1);
        Assert.Empty(after.Inlines);
        Assert.NotEmpty(editor.InsertText(Caret(At(after, 0)), "after").Changes);
        AssertUndoAndRedoGiveTheSameBytes(editor, file.Model, before, 2);
        await AssertARefreshAndASaveKeepAsync(file, editor, Para(body, body.Count - 1), "after");
    }

    [Theory]
    [InlineData("cell")]
    [InlineData("control")]
    public async Task AListInsertedAtTheEndOfACellOrAContentControlLeavesAPlaceToTypeInIt(string kind)
    {
        var inner = P(Run("Inside."));
        using var file = File(P(Run("Chapter"), "Heading1") + (kind == "cell" ? Cell(inner) : Control(inner)) + P(Run("The end.")));
        var editor = EditorOf(file);
        var host = Paragraphs(file.Model).Single(p => TextOf(p) == "Inside.");
        var container = TextEditor.FindIn(file.Model.Body.Blocks, host)!.Value.Container;
        var before = Bytes(file.Model);

        Assert.NotNull(editor.InsertToc(End(host), s_options, new FakeFieldHost(file.Model)));

        var site = Assert.Single(TocRefresh.Find(file.Model.Body.Blocks));
        Assert.Same(container, site.Container);
        Assert.Equal(container.Count - 2, site.Last);
        var after = Para(container, container.Count - 1);
        Assert.Empty(after.Inlines);
        Assert.NotEmpty(editor.InsertText(Caret(At(after, 0)), "after").Changes);
        AssertUndoAndRedoGiveTheSameBytes(editor, file.Model, before, 2);
        var again = TextEditor.FindIn(file.Model.Body.Blocks, Paragraphs(file.Model).Single(p => TextOf(p) == "after"))!.Value;
        await AssertARefreshAndASaveKeepAsync(file, editor, Para(again.Container, again.Index), "after");
    }

    [Fact]
    public void AListWithOnlyMarkersAfterItGetsTheParagraphBeforeThem()
    {
        using var file = File(P("<w:bookmarkStart w:id=\"1\" w:name=\"Across\"/>" + Run("Text.")) + "<w:bookmarkEnd w:id=\"1\"/>");
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        Assert.IsType<UnknownBlock>(body[^1]);

        Assert.NotNull(editor.InsertToc(End(Para(body, 0)), s_options, new FakeFieldHost(file.Model)));

        var (_, _, last) = Site(file.Model);
        Assert.Empty(Para(body, last + 1).Inlines);
        Assert.IsType<UnknownBlock>(body[last + 2]);
        Assert.Equal(last + 3, body.Count);
    }

    [Fact]
    public void AListThatIsNotLastGetsNoExtraParagraph()
    {
        using var file = File(P(string.Empty) + Cell(P(Run("cell"))) + P(Run("The end.")));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;

        Assert.NotNull(editor.InsertToc(At(Para(body, 0), 0), s_options, new FakeFieldHost(file.Model)));

        var (_, first, last) = Site(file.Model);
        Assert.Equal(0, first);
        Assert.IsType<Table>(body[last + 1]);
        Assert.Equal(last + 3, body.Count);
    }

    // ---- Enter at the end edge ----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "")]
    [InlineData(false, "<w:r><w:t>tail</w:t></w:r>")]
    [InlineData(true, "<w:r><w:t>tail</w:t></w:r>")]
    public async Task EnterAtTheEndEdgeOfAListThatEndsTheBodyGivesAParagraphAfterIt(bool endOwn, string afterEnd)
    {
        using var file = File(P(Run("Chapter"), "Heading1") + TocXml(endOwn, afterEnd: afterEnd));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var (_, first, last) = Site(file.Model);
        Assert.Equal(body.Count - 1, last);
        var end = Para(body, last);
        var before = Bytes(file.Model);

        // The edge refuses text; a click below the document and Ctrl+End snap to it.
        Assert.Empty(editor.InsertText(Caret(End(end)), "x").Changes);
        Assert.Equal(End(end), editor.SnapCaret(End(end)));

        var split = editor.SplitParagraph(Caret(End(end)));

        Assert.Single(split.Changes);
        Assert.Null(editor.LastFieldRefusal);
        Assert.Equal(last + 2, body.Count);
        var made = Para(body, last + 1);
        Assert.Equal(At(made, 0), split.Caret);
        Assert.Empty(made.Inlines);
        Assert.Null(made.Properties.StyleId);
        Assert.Same(end, body[last]);
        Assert.Equal(first, Site(file.Model).First);
        Assert.Equal(last, Site(file.Model).Last);
        AssertOutsideEveryToc(editor, body, made);

        Assert.NotEmpty(editor.InsertText(Caret(split.Caret), "after").Changes);
        Assert.Equal("after", TextOf(made));
        AssertUndoAndRedoGiveTheSameBytes(editor, file.Model, before, 2);
        var typed = Para(body, body.Count - 1);
        await AssertARefreshAndASaveKeepAsync(file, editor, typed, "after");

        // What stood after the end mark in its paragraph is still behind the field.
        if (afterEnd.Length > 0)
        {
            var (_, _, now) = Site(file.Model);
            Assert.EndsWith("tail", TextOf(Para(body, now)), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("cell")]
    [InlineData("control")]
    public void EnterAtTheEndEdgeOfAListThatEndsACellOrAControlStaysInIt(string kind)
    {
        var inner = P(Run("Above.")) + TocXml();
        using var file = File((kind == "cell" ? Cell(inner) : Control(inner, "<w:docPartObj><w:docPartGallery w:val=\"Table of Contents\"/></w:docPartObj>")) + P(Run("The end.")));
        var editor = EditorOf(file);
        var (container, _, last) = Site(file.Model);
        var before = Bytes(file.Model);

        var split = editor.SplitParagraph(Caret(End(Para(container, last))));

        Assert.Single(split.Changes);
        Assert.Equal(last + 2, container.Count);
        Assert.Same(container[last + 1], split.Caret.Paragraph);
        AssertOutsideEveryToc(editor, container, split.Caret.Paragraph);
        Assert.NotEmpty(editor.InsertText(Caret(split.Caret), "after").Changes);
        AssertUndoAndRedoGiveTheSameBytes(editor, file.Model, before, 2);
    }

    [Fact]
    public void EnterAtTheEndEdgeOfAListWhoseLastParagraphEndsASectionTakesTheBreakAlong()
    {
        const string section = "<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:pPr>";
        using var file = File(P(Run("Chapter"), "Heading1") + TocXml(endOwn: true, endPPr: section) + P(Run("Next section.")));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var (_, _, last) = Site(file.Model);
        var properties = Para(body, last).SectionBreak;
        Assert.NotNull(properties);
        var before = Bytes(file.Model);

        var split = editor.SplitParagraph(Caret(End(Para(body, last))));

        var change = Assert.Single(split.Changes);
        Assert.Equal((last, 1, 2), (change.First, change.Removed, change.Added));
        Assert.Null(Para(body, last).SectionBreak);
        Assert.Same(properties, split.Caret.Paragraph.SectionBreak);
        Assert.Same(body[last + 1], split.Caret.Paragraph);
        Assert.Equal(2, SectionList.Of(file.Model).Count);
        Assert.NotEmpty(editor.InsertText(Caret(split.Caret), "after").Changes);
        AssertUndoAndRedoGiveTheSameBytes(editor, file.Model, before, 2);
        Assert.Equal(2, SectionList.Of(file.Model).Count);
    }

    [Fact]
    public void EnterAtTheEndEdgeOfAListBetweenTwoListsGivesAParagraphBetweenThem()
    {
        using var file = File(TocXml() + TocXml() + P(Run("The end.")));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var sites = TocRefresh.Find(body);
        Assert.Equal(2, sites.Count);
        var second = Para(body, sites[1].First);

        // The start of the second list is no dead place once the paragraph between them exists; before that its start edge is refused (it is not first).
        Assert.Empty(editor.SplitParagraph(Caret(At(second, 0))).Changes);

        var split = editor.SplitParagraph(Caret(End(Para(body, sites[0].Last))));

        Assert.Single(split.Changes);
        Assert.Same(body[sites[0].Last + 1], split.Caret.Paragraph);
        Assert.Same(second, body[sites[0].Last + 2]);
        Assert.NotEmpty(editor.InsertText(Caret(split.Caret), "between").Changes);
    }

    [Fact]
    public void EnterAtTheEndOfAParagraphWhereTheNextListBeginsIsRefused()
    {
        // The second list begins in the paragraph that ends the first: after that paragraph is inside the second.
        var body = P(TocBegin() + Run("one")) + P(Run("two") + MarkXml("end") + TocBegin() + Run("three")) + P(Run("four") + MarkXml("end")) + P(Run("The end."));
        using var file = File(body);
        var editor = EditorOf(file);
        var blocks = file.Model.Body.Blocks;
        var before = Bytes(file.Model);

        Assert.Empty(editor.SplitParagraph(Caret(End(Para(blocks, 1)))).Changes);

        Assert.Equal(FieldRefusalKind.InsideToc, editor.LastFieldRefusal?.Kind);
        Assert.Equal(before, Bytes(file.Model));
    }

    // ---- Enter at the start edge --------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("control")]
    [InlineData("marker")]
    public async Task EnterAtTheStartEdgeOfAListThatIsFirstGivesAParagraphAboveIt(string kind)
    {
        var list = P(TocBegin() + Run("Stale one")) + P(MarkXml("end"));
        var xml = kind switch
        {
            "cell" => Cell(list + P(Run("Below."))) + P(Run("Chapter"), "Heading1"),
            "control" => Control(list) + P(Run("Chapter"), "Heading1"),
            "marker" => "<w:bookmarkStart w:id=\"1\" w:name=\"Across\"/>" + list + P("<w:bookmarkEnd w:id=\"1\"/>" + Run("Chapter"), "Heading1"),
            _ => list + P(Run("Chapter"), "Heading1"),
        };
        using var file = File(xml);
        var editor = EditorOf(file);
        var (container, first, last) = Site(file.Model);
        var start = Para(container, first);
        var before = Bytes(file.Model);
        Assert.Empty(editor.InsertText(Caret(At(start, 0)), "x").Changes);

        var split = editor.SplitParagraph(Caret(At(start, 0)));

        var change = Assert.Single(split.Changes);
        Assert.Equal((first, 0, 1), (change.First, change.Removed, change.Added));
        var made = Para(container, first);
        Assert.Equal(At(made, 0), split.Caret);
        Assert.Empty(made.Inlines);
        Assert.Same(start, container[first + 1]);
        Assert.Equal((first + 1, last + 1), (Site(file.Model).First, Site(file.Model).Last));
        AssertOutsideEveryToc(editor, container, made);

        // The caret is in the new paragraph: a title is typed there.
        Assert.NotEmpty(editor.InsertText(Caret(split.Caret), "Contents").Changes);
        AssertUndoAndRedoGiveTheSameBytes(editor, file.Model, before, 2);
        var typed = Paragraphs(file.Model).Single(p => TextOf(p) == "Contents");
        await AssertARefreshAndASaveKeepAsync(file, editor, typed, "Contents");
    }

    [Fact]
    public void EnterAtEveryOtherPlaceOfAListIsRefused()
    {
        using var file = File(P(Run("Above.")) + TocXml() + P(Run("The end.")));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var (_, first, last) = Site(file.Model);
        var before = Bytes(file.Model);
        var revision = file.Model.Edits.Revision;

        // The start edge of a list that is not first, its first paragraph further on, a middle paragraph at both ends.
        foreach (var at in new[] { At(Para(body, first), 0), At(Para(body, first), 2), End(Para(body, first)), At(Para(body, first + 1), 0), End(Para(body, first + 1)) })
        {
            Assert.Empty(editor.SplitParagraph(Caret(at)).Changes);
            Assert.Equal(FieldRefusalKind.InsideToc, editor.LastFieldRefusal?.Kind);
        }

        // A selection is no edge: it takes a part of the field.
        Assert.Empty(editor.SplitParagraph(new TextRange(At(Para(body, last - 1), 0), End(Para(body, last)))).Changes);
        Assert.Equal(before, Bytes(file.Model));
        Assert.Equal(revision, file.Model.Edits.Revision);
        Assert.False(editor.History.CanUndo);

        // Typing and pasting at the edges stay refused: only Enter makes room.
        Assert.Empty(editor.Paste(Caret(End(Para(body, last))), new ClipboardContent("pasted", null), _ => true).Changes);
        Assert.Empty(editor.InsertText(Caret(End(Para(body, last))), "a\nb").Changes);
        Assert.Equal(before, Bytes(file.Model));
    }

    [Fact]
    public void EnterAtTheEdgeOfAListInAControlThatHoldsOneLineIsRefused()
    {
        using var file = File(Control(TocXml(), "<w:text/>") + P(Run("The end.")));
        var editor = EditorOf(file);
        var (container, first, last) = Site(file.Model);
        var before = Bytes(file.Model);

        Assert.Empty(editor.SplitParagraph(Caret(End(Para(container, last)))).Changes);
        Assert.Empty(editor.SplitParagraph(Caret(At(Para(container, first), 0))).Changes);
        Assert.Equal(before, Bytes(file.Model));
    }

    [Fact]
    public void EnterAtTheEndEdgeOfAnEmptyNumberedEndParagraphDoesNotEndItsList()
    {
        const string numbered = "<w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>";
        using var file = File(TocXml(endOwn: true, endPPr: numbered));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var (_, _, last) = Site(file.Model);
        var end = Para(body, last);

        var split = editor.SplitParagraph(Caret(At(end, 0)));

        Assert.Same(body[last + 1], split.Caret.Paragraph);
        Assert.NotNull(end.Properties.NumberingId);
        Assert.Null(split.Caret.Paragraph.Properties.NumberingId);
    }
}
