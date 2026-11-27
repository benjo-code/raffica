using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>A refresh after an undo or a redo brings the results back in line, and the independent oracle agrees with the refresh on every corpus file that has fields (milestone 4.5b).</summary>
public sealed class FieldRefreshHistoryTests
{
    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run(result)
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static string P(string inner, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}</w:pPr>{inner}</w:p>";

    // H1, caption, a plain paragraph, caption: turning the plain paragraph into a heading restarts the second caption.
    private static WordDocument Document()
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.Document(
            P(Run("Chapter"), "Heading1") + P(Run("Figure ") + Field("SEQ Figure \\s 1", "1")) + P(Run("text")) + P(Run("Figure ") + Field("SEQ Figure \\s 1", "2"))
                + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>",
            LayoutModel.StylesPart(styles));
    }

    private static string[] Numbers(WordDocument document) =>
        [ReferenceFields.Stored((Paragraph)document.Body.Blocks[1], 0)!, ReferenceFields.Stored((Paragraph)document.Body.Blocks[3], 0)!];

    private static TextRange Whole(WordDocument document, int block)
    {
        var paragraph = (Paragraph)document.Body.Blocks[block];
        return new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, TestEdit.TextOf(paragraph).Length));
    }

    [Fact]
    public void UndoOfATypedHeadingRestoresTheNumbersAfterTheNextRefresh()
    {
        var document = Document();
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var host = new FakeFieldHost(document);
        editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.Equal(["1", "2"], Numbers(document));

        Assert.NotNull(editor.ApplyParagraphStyle(Whole(document, 2), "Heading1"));
        editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.Equal(["1", "1"], Numbers(document));

        Assert.NotNull(editor.History.Undo());
        editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(["1", "2"], Numbers(document));
        Assert.False(document.Edits.IsDirty);
    }

    [Fact]
    public void RedoLikewise()
    {
        var document = Document();
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var host = new FakeFieldHost(document);
        Assert.NotNull(editor.ApplyParagraphStyle(Whole(document, 2), "Heading1"));
        editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.NotNull(editor.History.Undo());
        editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.Equal(["1", "2"], Numbers(document));

        Assert.NotNull(editor.History.Redo());
        editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(["1", "1"], Numbers(document));
        Assert.True(editor.History.CanUndo);
    }

    [Fact]
    public void TheReferenceOracleEqualsTheRefreshOnTheCorpusFilesWithFields()
    {
        var files = 0;
        var compared = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files.Where(f => f.Features.Contains("fields", StringComparer.Ordinal)))
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var host = new FakeFieldHost(file.Model);
            var editor = new TextEditor(file.Model, (_, _) => ProjectionHost.Plain);

            editor.RefreshFields(host, RefreshScope.OrderBased);

            compared += ReferenceFields.AssertStoredEqualsExpected(file.Model, host, entry.Id);
            files++;

            // A second refresh finds nothing to do and the document stays clean.
            Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
            Assert.False(file.Model.Edits.IsDirty);
        }

        TestContext.Current.SendDiagnosticMessage($"{files} corpus files with fields, {compared} results compared");
        Assert.True(files > 0, "no corpus file has the feature tag fields");
    }

    [Fact]
    public void TheReferenceOracleEqualsTheRefreshOnADocumentWithEveryCoveredField()
    {
        var document = LayoutModel.Document(FieldOracleTests.FieldsBody());
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var host = new FakeFieldHost(document);
        var expected = ReferenceFields.Expected(document, host);

        var result = editor.RefreshFields(host, RefreshScope.OrderBased);

        // Fifteen fields (a table cell, content controls, roman, \c, a missing bookmark included); the chapter reference has no label in a document with unnumbered headings and the reference to a missing bookmark has no target: both keep their stored result.
        Assert.Equal(15, expected.Count);
        Assert.Equal(2, expected.Count(f => f.Text is null));
        Assert.Equal(13, ReferenceFields.AssertStoredEqualsExpected(document, host, "every covered field"));
        Assert.True(result.Written > 0);
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    private sealed class ThrowingHost(IFieldHost inner) : IFieldHost
    {
        public bool IsComplete => inner.IsComplete;

        public string? NumberLabel(Paragraph paragraph) => throw new InvalidOperationException("test");

        public string? ChapterNumber(Paragraph paragraph) => throw new InvalidOperationException("test");

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => inner.PageOf(position);

        public string? NoteMarkText(int noteId, NoteType type) => inner.NoteMarkText(noteId, type);
    }

    [Fact]
    public async Task AFailureHalfwayThroughTheWalkReportsWhatWasWrittenAndTheLayoutAndTheBytesFollow()
    {
        using var folder = new TempFolder();
        var path = folder.File("half.docx");
        DocxBuilder.WithBody(
            path,
            P(Run("Chapter"), "Heading1")
                + P(Run("Figure ") + Field("SEQ Figure", "5"))
                + P(Run("In ") + Field("STYLEREF 1 \\s", "7"))
                + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>");
        using var file = DocxFile.Open(path);
        var model = file.Model;
        var layout = await SectionOracleTests.LaidAsync(model);
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        var host = new FakeFieldHost(model);
        file.SerialiseMainForTest(cached: true);

        void Check(string label)
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label);
        }

        var failure = Assert.Throws<FieldRefreshException>(() => editor.RefreshFields(new ThrowingHost(host), RefreshScope.OrderBased));

        // The caption was written before the walk failed: the exception carries that change, the derived version rose and the block was attributed.
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        var change = Assert.Single(failure.Content);
        Assert.True(model.Edits.DerivedVersion > 0);
        Assert.False(model.Edits.IsDirty);
        foreach (var c in failure.Content)
        {
            layout.ApplyEdit(c, 2);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "after the failed walk");
        Check("after the failed walk");

        // The retry has nothing left to report for the caption.
        var retried = editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.DoesNotContain(retried.Content, c => c.First == change.First);
        foreach (var c in retried.Content)
        {
            layout.ApplyEdit(c, 2);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "after the retry");
        Check("after the retry");
    }
}
