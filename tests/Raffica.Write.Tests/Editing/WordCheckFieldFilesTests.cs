using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Makes the files of the Word check for milestone 4.5b (<c>Docs/write-word-check.md</c>, section "Fields, captions and tables of contents (milestone 4.5b)"): every command goes through the editor as the app calls it,
/// the field results are brought current as the app's refresh does it (over the layout's labels, note marks and pages) and the file is saved through <see cref="DocxFile.SaveAsync(string, CancellationToken)"/>.
/// <c>pwsh tools/word-open-check.ps1 &lt;files&gt;</c> asks Word whether it opens them without repair and <c>pwsh tools/word-field-results.ps1 &lt;files&gt;</c> whether Word's own field update gives the same results.
/// A file whose <c>&lt;name&gt;.skip.json</c> lists field instructions holds fields where a difference is expected (Write does not update them, or the difference is a recorded one): the script reports them and does not
/// count them. The files named <c>probe-*</c> are written by hand to ask Word how it computes a case. The tests write nothing and skip unless the folder is named by <c>RAFFICA_WORD_CHECK_DIR</c>.
/// </summary>
public sealed class WordCheckFieldFilesTests
{
    private const string FolderVariable = "RAFFICA_WORD_CHECK_DIR";

    private const string Sentence = "The quick brown fox jumps over the lazy dog and keeps running over the hill until the evening falls on the valley below.";

    private static readonly TocOptions s_contents = new(1, 3, true, true, TocLeader.Dots, null);

    private static string Folder()
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(folder), "Set RAFFICA_WORD_CHECK_DIR to a folder to make the Word-check files.");

        Directory.CreateDirectory(folder!);
        return folder!;
    }

    private static TextEditor EditorOf(DocxFile file) =>
        new(file.Model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(file.Model));

    private static TextRange Caret(TextPosition at) => new(at, at);

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static Paragraph Para(DocxFile file, string text) => file.Model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == text);

    private static TextPosition Start(DocxFile file, string text) => new(StoryRef.Body, Para(file, text), 0);

    private static TextPosition End(Paragraph paragraph) => new(StoryRef.Body, paragraph, TextOf(paragraph).Length);

    private static EditResult Did(EditResult? result, string what)
    {
        Assert.True(result is not null, "the editor refused: " + what);
        return result!;
    }

    // A blank A4 document with one paragraph per line, a line of a style given that style, and the headings numbered.
    private static (DocxFile File, TextEditor Editor) Build(params (string? Style, string Text)[] lines)
    {
        var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var first = file.Model.Body.Blocks.OfType<Paragraph>().First();
        Did(editor.InsertText(Caret(new TextPosition(StoryRef.Body, first, 0)), string.Join("\n", lines.Select(l => l.Text))), "typing the lines");
        var paragraphs = file.Model.Body.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(lines.Length, paragraphs.Count);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Style is { } style)
            {
                var paragraph = (Paragraph)file.Model.Body.Blocks[i];
                editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, paragraph, 0)), style);
            }
        }

        return (file, editor);
    }

    // Numbers the headings (the 4.4a switch) with the caret in the first heading.
    private static void Number(DocxFile file, TextEditor editor)
    {
        var heading = file.Model.Body.Blocks.OfType<Paragraph>().First(p => p.Properties.StyleId == "Heading1");
        Did(editor.SetHeadingNumbering(true, new TextPosition(StoryRef.Body, heading, 0)), "numbering the headings");
    }

    private static IEnumerable<(string? Style, string Text)> Filler(string name, int count) =>
        Enumerable.Range(1, count).Select(n => ((string?)null, string.Create(CultureInfo.InvariantCulture, $"{name} {n}. {Sentence}")));

    // What the app's host answers: the labels and note marks of the layout's own counter, the pages of a complete pages layout.
    private sealed class Host(WordDocument document, DocumentLayout layout) : IFieldHost
    {
        private readonly FieldLabels _labels = new(document);
        private readonly FieldPages _pages = new(document, layout);

        public bool IsComplete => layout.IsComplete;

        public string? NumberLabel(Paragraph paragraph) => _labels.NumberLabel(paragraph);

        public string? ChapterNumber(Paragraph paragraph) => _labels.ChapterNumber(paragraph);

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => position.Story.Kind == StoryKind.Body ? _pages.PageOf(position.Paragraph) : null;

        public long? PageSignature => _pages.Signature();

        public string? NoteMarkText(int noteId, NoteType type) => _labels.NoteMarkText(noteId, type);
    }

    private static Host HostOf(DocxFile file) => new(file.Model, Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions())));

    // The refresh as the app runs it before a save: passes over a fresh complete layout until the results are settled, a structural change attached to the history.
    private static void Settle(DocxFile file, TextEditor editor)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var result = editor.RefreshFields(HostOf(file), RefreshScope.WithPages);
            if (result.Structural is { } record)
            {
                editor.History.AttachDerived(record);
            }

            if (result.Settled)
            {
                return;
            }
        }

        Assert.Fail("the field results did not settle in three passes");
    }

    private static async Task SaveAsync(DocxFile file, string folder, string name, params string[] expectedDifferences)
    {
        var path = Path.Combine(folder, name);
        await file.SaveAsync(path, TestContext.Current.CancellationToken);
        var skip = Path.ChangeExtension(path, ".skip.json");
        if (expectedDifferences.Length > 0)
        {
            await File.WriteAllTextAsync(skip, JsonSerializer.Serialize(expectedDifferences, WordCheckJson.Default.StringArray), new UTF8Encoding(false), TestContext.Current.CancellationToken);
        }
        else if (File.Exists(skip))
        {
            File.Delete(skip);
        }
    }

    // ---- the commands -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CaptionsWithAndWithoutChapterNumbersAreSavedForWord()
    {
        var folder = Folder();
        var (file, editor) = Build(
            [("Heading1", "Introduction"), (null, "A first picture."), (null, "Some text between."), ("Heading1", "Method"), (null, "A second picture."), (null, "A formula stands here."), (null, "Values follow."), (null, "The end.")]);
        using var _ = file;
        Number(file, editor);
        var host = HostOf(file);

        Did(editor.InsertCaption(End(Para(file, "A first picture.")), "Figure", ": The first", CaptionPlace.Below, true, "Figure", host), "a figure caption with a chapter number");
        Did(editor.InsertCaption(End(Para(file, "A second picture.")), "Figure", ": The second", CaptionPlace.Below, true, "Figure", HostOf(file)), "a second figure caption with a chapter number");
        Did(editor.InsertCaption(End(Para(file, "A formula stands here.")), "Equation", string.Empty, CaptionPlace.Below, false, "Equation", HostOf(file)), "an equation caption");
        Did(editor.InsertTable(Start(file, "Values follow."), 2, 3), "a table");
        var tableAt = Enumerable.Range(0, file.Model.Body.Blocks.Count).First(i => file.Model.Body.Blocks[i] is Table);
        Did(editor.InsertCaption(new TableRef(StoryRef.Body, tableAt), "Table", ": Values", CaptionPlace.Above, false, "Table", HostOf(file)), "a table caption above");
        Settle(file, editor);

        // A caption inserted before the others renumbers them.
        Did(editor.InsertCaption(End(Para(file, "Some text between.")), "Figure", ": In between", CaptionPlace.Below, true, "Figure", HostOf(file)), "a caption in between");
        Did(editor.InsertCaption(Start(file, "Introduction") with { Paragraph = Para(file, "A first picture.") }, "Equation", ": Earlier", CaptionPlace.Above, false, "Equation", HostOf(file)), "an equation caption before the first");
        Settle(file, editor);

        await SaveAsync(file, folder, "fields-captions.docx");
    }

    [Fact]
    public async Task CrossReferencesToEveryTargetAreSavedForWord()
    {
        var folder = Folder();
        var kinds = new[] { CrossRefTargetKind.Heading, CrossRefTargetKind.Caption, CrossRefTargetKind.Footnote, CrossRefTargetKind.Endnote, CrossRefTargetKind.Bookmark };
        var lines = new List<(string? Style, string Text)> { ("Heading1", "Introduction"), (null, "A picture."), (null, "A sentence with a footnote."), (null, "A sentence with an endnote."), (null, "A marked phrase stands here.") };
        lines.AddRange(Filler("Introduction", 30));
        lines.Add(("Heading1", "Method"));
        lines.Add(("Heading2", "Sampling"));
        lines.AddRange(Filler("Method", 30));
        var references = kinds.SelectMany(k => CrossReferences.Offered(k).Select(kind => (Target: k, Kind: kind))).ToList();
        lines.AddRange(references.Select((r, i) => ((string?)null, string.Create(CultureInfo.InvariantCulture, $"Reference {i + 1} ({r.Target}, {r.Kind}): "))));
        var (file, editor) = Build([.. lines]);
        using var _ = file;
        Number(file, editor);
        Did(editor.InsertCaption(End(Para(file, "A picture.")), "Figure", ": What it shows", CaptionPlace.Below, false, "Figure", HostOf(file)), "a caption");
        Did(editor.InsertNote(End(Para(file, "A sentence with a footnote.")), NoteType.Footnote), "a footnote");
        Did(editor.InsertNote(End(Para(file, "A sentence with an endnote.")), NoteType.Endnote), "an endnote");
        var phrase = Para(file, "A marked phrase stands here.");
        Did(editor.AddBookmark(new TextRange(new TextPosition(StoryRef.Body, phrase, 2), new TextPosition(StoryRef.Body, phrase, 15)), "Phrase"), "a bookmark");
        Settle(file, editor);

        for (var i = 0; i < references.Count; i++)
        {
            var (targetKind, kind) = references[i];
            var host = HostOf(file);
            var targets = CrossReferences.Targets(file.Model, host, targetKind);
            Assert.NotEmpty(targets);

            // The heading references point at the last heading (a level 2 one, so the number has two parts).
            var target = targetKind == CrossRefTargetKind.Heading ? targets[^1] : targets[0];
            var at = End(Para(file, string.Create(CultureInfo.InvariantCulture, $"Reference {i + 1} ({targetKind}, {kind}): ")));
            Did(editor.InsertCrossReference(at, target, kind, host), $"a reference to a {targetKind} showing {kind}");
        }

        Settle(file, editor);
        await SaveAsync(file, folder, "fields-cross-references.docx");
    }

    [Fact]
    public async Task TablesOfContentsAndListsAreSavedForWord()
    {
        var folder = Folder();
        var lines = new List<(string? Style, string Text)> { (null, "Contents stand here."), (null, "Figures stand here."), (null, "Tables stand here.") };
        for (var chapter = 1; chapter <= 3; chapter++)
        {
            lines.Add(("Heading1", $"Chapter heading {chapter}"));
            lines.AddRange(Filler($"Opening {chapter}", 18));
            lines.Add(("Heading2", $"Section heading {chapter}"));
            lines.Add((null, $"A picture of chapter {chapter}."));
            lines.AddRange(Filler($"Middle {chapter}", 14));
            lines.Add(("Heading3", $"Subsection heading {chapter}"));
            lines.Add((null, $"Values of chapter {chapter}."));
            lines.AddRange(Filler($"Closing {chapter}", 10));
        }

        var (file, editor) = Build([.. lines]);
        using var _ = file;
        Number(file, editor);
        for (var chapter = 1; chapter <= 3; chapter++)
        {
            Did(editor.InsertCaption(End(Para(file, $"A picture of chapter {chapter}.")), "Figure", $": Picture {chapter}", CaptionPlace.Below, false, "Figure", HostOf(file)), "a figure caption");
            Did(editor.InsertCaption(End(Para(file, $"Values of chapter {chapter}.")), "Table", $": Values {chapter}", CaptionPlace.Above, false, "Table", HostOf(file)), "a table caption");
        }

        Did(editor.InsertToc(End(Para(file, "Contents stand here.")), s_contents, HostOf(file)), "a table of contents");
        Did(editor.InsertToc(End(Para(file, "Figures stand here.")), s_contents with { Label = "Figure" }, HostOf(file)), "a list of figures");
        Did(editor.InsertToc(End(Para(file, "Tables stand here.")), s_contents with { Label = "Table", Leader = TocLeader.Hyphens }, HostOf(file)), "a list of tables");
        Settle(file, editor);

        await SaveAsync(file, folder, "fields-contents.docx");
    }

    [Fact]
    public async Task TableOfContentsOptionsAreSavedForWord()
    {
        var folder = Folder();
        var lines = new List<(string? Style, string Text)> { (null, "Without page numbers."), (null, "With numbers after a tab."), (null, "With an underscore leader.") };
        for (var chapter = 1; chapter <= 3; chapter++)
        {
            lines.Add(("Heading1", $"Part {chapter}"));
            lines.AddRange(Filler($"Part {chapter}", 25));
            lines.Add(("Heading2", $"Topic {chapter}"));
            lines.AddRange(Filler($"Topic {chapter}", 12));
        }

        var (file, editor) = Build([.. lines]);
        using var _ = file;
        Did(editor.InsertToc(End(Para(file, "Without page numbers.")), new TocOptions(1, 2, false, false, TocLeader.None, null), HostOf(file)), "contents without page numbers");
        Did(editor.InsertToc(End(Para(file, "With numbers after a tab.")), new TocOptions(1, 1, true, false, TocLeader.None, null), HostOf(file)), "contents with plain tabs");
        Did(editor.InsertToc(End(Para(file, "With an underscore leader.")), new TocOptions(2, 2, true, true, TocLeader.Underscore, null), HostOf(file)), "contents of level 2");
        Settle(file, editor);

        await SaveAsync(file, folder, "fields-contents-options.docx");
    }

    [Fact]
    public async Task AStaleTableOfContentsIsBroughtCurrentAndSavedForWord()
    {
        var folder = Folder();
        var lines = new List<(string? Style, string Text)> { (null, "Contents stand here."), ("Heading1", "First"), (null, "Text of the first."), ("Heading1", "Second"), (null, "A paragraph that becomes a heading."), ("Heading1", "Third") };
        lines.AddRange(Filler("Third", 45));
        lines.Add(("Heading1", "Fourth"));
        lines.AddRange(Filler("Fourth", 3));
        var (file, editor) = Build([.. lines]);
        using var _ = file;
        Number(file, editor);
        Did(editor.InsertToc(End(Para(file, "Contents stand here.")), s_contents, HostOf(file)), "a table of contents");
        Settle(file, editor);

        // A heading appears, one is renamed, one goes and the pages move.
        Did(editor.ApplyParagraphStyle(Caret(Start(file, "A paragraph that becomes a heading.")), "Heading2"), "a new heading");
        var second = file.Model.Body.Blocks.OfType<Paragraph>().First(p => p.Properties.StyleId == "Heading1" && TextOf(p) == "Second");
        Did(editor.InsertText(Caret(End(second)), " renamed"), "renaming a heading");
        var third = file.Model.Body.Blocks.OfType<Paragraph>().First(p => p.Properties.StyleId == "Heading1" && TextOf(p) == "Third");
        editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, third, 0)), "Normal");
        Did(editor.InsertText(Caret(Start(file, "Text of the first.")), string.Concat(Enumerable.Repeat("An added line.\n", 40))), "more text before the second heading");
        Settle(file, editor);

        await SaveAsync(file, folder, "fields-stale-contents.docx");
    }

    [Fact]
    public async Task BookmarkReferencesAndAChapterRestartAreSavedForWord()
    {
        var folder = Folder();
        var lines = new List<(string? Style, string Text)> { ("Heading1", "One"), (null, "A picture here."), (null, "An important statement of the first chapter.") };
        lines.AddRange(Filler("One", 45));
        lines.Add(("Heading1", "Two"));
        lines.Add((null, "Another picture here."));
        lines.Add((null, "Refer to it: "));
        lines.Add((null, "On page: "));
        var (file, editor) = Build([.. lines]);
        using var _ = file;
        Number(file, editor);
        Did(editor.InsertCaption(End(Para(file, "A picture here.")), "Figure", string.Empty, CaptionPlace.Below, true, "Figure", HostOf(file)), "a caption of chapter one");
        Did(editor.InsertCaption(End(Para(file, "Another picture here.")), "Figure", string.Empty, CaptionPlace.Below, true, "Figure", HostOf(file)), "a caption of chapter two");
        var statement = Para(file, "An important statement of the first chapter.");
        Did(editor.AddBookmark(new TextRange(new TextPosition(StoryRef.Body, statement, 0), End(statement)), "Statement"), "a bookmark");
        var target = Assert.Single(CrossReferences.Targets(file.Model, HostOf(file), CrossRefTargetKind.Bookmark));
        Did(editor.InsertCrossReference(End(Para(file, "Refer to it: ")), target, CrossRefKind.Text, HostOf(file)), "the text of the bookmark");
        target = Assert.Single(CrossReferences.Targets(file.Model, HostOf(file), CrossRefTargetKind.Bookmark));
        Did(editor.InsertCrossReference(End(Para(file, "On page: ")), target, CrossRefKind.Page, HostOf(file)), "the page of the bookmark");
        Settle(file, editor);

        // The bookmarked text changes and the reference follows.
        var changed = Para(file, "An important statement of the first chapter.");
        Did(editor.InsertText(Caret(new TextPosition(StoryRef.Body, changed, 13)), "and new "), "typing inside the bookmark");
        Settle(file, editor);

        await SaveAsync(file, folder, "fields-bookmarks.docx");
    }

    // With pictureOnly the new heading is a paragraph that holds a picture and no text: Word lists it (with a copy of the picture), Write leaves it out (a recorded difference).
    [Theory]
    [InlineData("fields-thesis.docx", false)]
    [InlineData("probe-picture-heading.docx", true)]
    public async Task TheThesisWithAHeadingMoreIsSavedForWord(string name, bool pictureOnly)
    {
        var folder = Folder();
        using var file = DocxFile.Open(Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx"));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        bool HasText(Paragraph p) => p.Inlines.OfType<Run>().Any(r => r.Content.OfType<TextContent>().Any(t => t.Text.Trim().Length > 0));
        var plain = Enumerable.Range(body.Count / 2, body.Count / 2).First(i => body[i] is Paragraph { SectionBreak: null, Properties.StyleId: null } p && TextOf(p).Length is > 0 and < 2000 && HasText(p) != pictureOnly && !FieldView.IsInTableOfContents(body, i));
        Did(editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, (Paragraph)body[plain], 0)), "Heading1"), "a heading in the thesis");
        Settle(file, editor);

        await SaveAsync(file, folder, name, pictureOnly ? ["TOC \\o \"1-1\" \\n \\h \\z \\u"] : []);
    }

    // ---- removing ------------------------------------------------------------------------------------------------------------------

    // Removes every table of contents of the body, one command each, as the app does with the caret in it; returns how many there were.
    private static int RemoveEveryToc(TextEditor editor, DocxFile file)
    {
        var removed = 0;
        while (BookmarkOperations.ParagraphsOf(file.Model.Body.Blocks).Select(p => new TextPosition(StoryRef.Body, p, 0)).Cast<TextPosition?>().FirstOrDefault(at => editor.RefusalForRemoveToc(at!.Value) is null) is { } inside)
        {
            Did(editor.RemoveToc(inside), "removing a table of contents");
            removed++;
            Assert.True(removed < 20, "the tables of contents do not go away");
        }

        return removed;
    }

    [Fact]
    public async Task ARemovedTableOfContentsIsSavedForWord()
    {
        var folder = Folder();
        var lines = new List<(string? Style, string Text)> { (null, "Contents stand here."), (null, "Figures stand here.") };
        for (var chapter = 1; chapter <= 2; chapter++)
        {
            lines.Add(("Heading1", $"Chapter heading {chapter}"));
            lines.AddRange(Filler($"Opening {chapter}", 20));
            lines.Add(("Heading2", $"Section heading {chapter}"));
            lines.Add((null, $"A picture of chapter {chapter}."));
            lines.AddRange(Filler($"Closing {chapter}", 10));
        }

        lines.Add((null, "See the section: "));
        var (file, editor) = Build([.. lines]);
        using var _ = file;
        Number(file, editor);
        for (var chapter = 1; chapter <= 2; chapter++)
        {
            Did(editor.InsertCaption(End(Para(file, $"A picture of chapter {chapter}.")), "Figure", $": Picture {chapter}", CaptionPlace.Below, false, "Figure", HostOf(file)), "a caption");
        }

        Did(editor.InsertToc(End(Para(file, "Contents stand here.")), s_contents, HostOf(file)), "a table of contents");
        Did(editor.InsertToc(End(Para(file, "Figures stand here.")), s_contents with { Label = "Figure" }, HostOf(file)), "a list of figures");
        Settle(file, editor);
        var target = CrossReferences.Targets(file.Model, HostOf(file), CrossRefTargetKind.Heading)[^1];
        Did(editor.InsertCrossReference(End(Para(file, "See the section: ")), target, CrossRefKind.Text, HostOf(file)), "a reference to a heading");
        Settle(file, editor);
        Assert.Equal(2, TocRefresh.Find(file.Model.Body.Blocks).Count);

        // The contents go, the list of figures stays: the headings lose the bookmarks of the contents, the captions keep theirs.
        var first = BookmarkOperations.ParagraphsOf(file.Model.Body.Blocks).First(p => editor.RefusalForRemoveToc(new TextPosition(StoryRef.Body, p, 0)) is null);
        var before = BookmarkOperations.AllNames(file.Model).Count(n => n.StartsWith("_Toc", StringComparison.Ordinal));
        Did(editor.RemoveToc(new TextPosition(StoryRef.Body, first, 0)), "removing the table of contents");
        Assert.Single(TocRefresh.Find(file.Model.Body.Blocks));
        Assert.Equal(before - 4, BookmarkOperations.AllNames(file.Model).Count(n => n.StartsWith("_Toc", StringComparison.Ordinal)));
        Settle(file, editor);
        await SaveAsync(file, folder, "fields-removed-contents.docx");

        // And the list of figures too: no list and no bookmark of a list is left.
        Assert.Equal(1, RemoveEveryToc(editor, file));
        Assert.DoesNotContain(BookmarkOperations.AllNames(file.Model), n => n.StartsWith("_Toc", StringComparison.Ordinal));
        Settle(file, editor);
        await SaveAsync(file, folder, "fields-removed-every-list.docx");
    }

    // A table of contents written by hand: the sentence of the field over two entry paragraphs, with the markers given inside the first entry.
    private static string HandToc(string markers = "") =>
        P(Mark("begin") + "<w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-1\" \\h \\z \\u </w:instrText></w:r>" + Mark("separate") + markers + Run("First entry"))
        + P(Run("Second entry") + Mark("end"));

    private static string TableOf(string cellContent) =>
        "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"6000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/>" + cellContent + "</w:tc></w:tr></w:tbl>";

    private static string Control(string content) => "<w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val=\"Table of Contents\"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtContent>" + content + "</w:sdtContent></w:sdt>";

    // Where a removal can leave a document Word does not take: a list that other fields point into, markers of other bookmarks in its entries, a control or a cell that holds nothing else, and a table before or around it.
    [Theory]
    [InlineData("probe-removed-contents.docx")]
    [InlineData("probe-removed-contents-table-then-end.docx")]
    [InlineData("probe-removed-contents-cell-ends-in-table.docx")]
    [InlineData("probe-removed-contents-between-tables.docx")]
    [InlineData("probe-removed-contents-control-ends-in-table.docx")]
    public async Task HandWrittenTablesOfContentsAreRemovedAndSavedForWord(string name)
    {
        var folder = Folder();
        var heading = P(Bookmarked(1, "_Toc100001", Run("A heading")), "Heading1");
        var (body, expected) = name switch
        {
            "probe-removed-contents.docx" => (
                heading + P(Run("Its page: ") + Field("PAGEREF _Toc100001 \\h", "1")) + HandToc("<w:bookmarkStart w:id=\"2\" w:name=\"Inside\"/>" + Run("marked ") + "<w:bookmarkEnd w:id=\"2\"/>")
                    + P(Run("Its text: ") + Field("REF Inside \\h", "marked ")) + Control(HandToc()) + P(Run("Between.")) + TableOf(HandToc()) + P(Run("The end.")),
                new[] { "REF Inside \\h" }),
            "probe-removed-contents-table-then-end.docx" => (heading + TableOf(P(Run("cell"))) + HandToc(), []),
            "probe-removed-contents-cell-ends-in-table.docx" => (heading + TableOf(P(Run("cell")) + TableOf(P(Run("inner"))) + HandToc()) + P(Run("The end.")), []),
            "probe-removed-contents-between-tables.docx" => (heading + TableOf(P(Run("one"))) + HandToc() + TableOf(P(Run("two"))) + P(Run("The end.")), []),
            _ => (heading + Control(TableOf(P(Run("cell"))) + HandToc()), (string[])[]),
        };
        using var file = Probe(body);
        var editor = EditorOf(file);

        Assert.Equal(name == "probe-removed-contents.docx" ? 3 : 1, RemoveEveryToc(editor, file));
        Settle(file, editor);

        await SaveAsync(file, folder, name, expected);
    }

    [Fact]
    public async Task DeletedBookmarksAreSavedForWord()
    {
        var folder = Folder();
        var (file, editor) = Build(
            [(null, "An important statement stands here."), (null, "A range starts in this paragraph."), (null, "And it ends in this one."), (null, "A point stands here."), (null, "A phrase that stays."), (null, "Values follow."), (null, "Text of the deleted: "), (null, "Page of the deleted: "), (null, "Text of the kept: "), (null, "The end.")]);
        using var _ = file;
        var statement = Para(file, "An important statement stands here.");
        Did(editor.AddBookmark(new TextRange(new TextPosition(StoryRef.Body, statement, 3), new TextPosition(StoryRef.Body, statement, 22)), "Gone"), "a bookmark");
        Did(editor.AddBookmark(new TextRange(new TextPosition(StoryRef.Body, Para(file, "A range starts in this paragraph."), 2), new TextPosition(StoryRef.Body, Para(file, "And it ends in this one."), 6)), "Range"), "a bookmark over two paragraphs");
        Did(editor.AddBookmark(Caret(Start(file, "A point stands here.")), "Point"), "a point bookmark");
        var stays = Para(file, "A phrase that stays.");
        Did(editor.AddBookmark(new TextRange(new TextPosition(StoryRef.Body, stays, 2), new TextPosition(StoryRef.Body, stays, 8)), "Kept"), "a bookmark that stays");
        Did(editor.InsertTable(Start(file, "Values follow."), 2, 2), "a table");
        var cell = BookmarkOperations.ParagraphsOf([file.Model.Body.Blocks.OfType<Table>().Single()]).First();
        Did(editor.InsertText(Caret(new TextPosition(StoryRef.Body, cell, 0)), "In a cell"), "text in a cell");
        Did(editor.AddBookmark(new TextRange(new TextPosition(StoryRef.Body, cell, 0), End(cell)), "Cell"), "a bookmark in a cell");

        void Refer(string line, string name, CrossRefKind kind)
        {
            var target = CrossReferences.Targets(file.Model, HostOf(file), CrossRefTargetKind.Bookmark).Single(t => string.Equals(t.Display, name, StringComparison.Ordinal));
            Did(editor.InsertCrossReference(End(Para(file, line)), target, kind, HostOf(file)), "a reference to " + name);
        }

        Refer("Text of the deleted: ", "Gone", CrossRefKind.Text);
        Refer("Page of the deleted: ", "Gone", CrossRefKind.Page);
        Refer("Text of the kept: ", "Kept", CrossRefKind.Text);
        Settle(file, editor);

        foreach (var name in (string[])["Gone", "Range", "Point", "Cell"])
        {
            Did(editor.DeleteBookmark(name), "deleting the bookmark " + name);
        }

        Assert.Equal(["Kept"], editor.VisibleBookmarks().Select(b => b.Name));
        Settle(file, editor);

        // A reference points at Write's own hidden bookmark over the target, so the references to a deleted bookmark keep working, in Word too.
        await SaveAsync(file, folder, "fields-deleted-bookmarks.docx");
    }

    // Bookmarks as files hold them: an end marker between two paragraphs (deleting it leaves that marker), markers inside a link and a content control.
    [Fact]
    public async Task DeletedBookmarksOfAFileAreSavedForWord()
    {
        var folder = Folder();
        var body = P("<w:bookmarkStart w:id=\"5\" w:name=\"Across\"/>" + Run("Starts in the paragraph.")) + "<w:bookmarkEnd w:id=\"5\"/>"
            + P("<w:hyperlink w:anchor=\"Across\">" + Bookmarked(6, "InLink", Run("a link")) + "</w:hyperlink>")
            + P("<w:sdt><w:sdtPr/><w:sdtContent>" + Bookmarked(7, "InControl", Run("a control")) + "</w:sdtContent></w:sdt>")
            + P(Run("The end."));
        using var file = Probe(body);
        var editor = EditorOf(file);

        foreach (var name in (string[])["Across", "InLink", "InControl"])
        {
            Did(editor.DeleteBookmark(name), "deleting the bookmark " + name);
        }

        Assert.Empty(BookmarkOperations.AllNames(file.Model));
        Settle(file, editor);

        await SaveAsync(file, folder, "probe-deleted-bookmarks.docx");
    }

    // Bookmarks Write holds only as kept content (between blocks, in a table row) and bookmarks of a footnote and a footer, with the highest ids and generated names, then Write's own inserts: no id and no name is used twice.
    [Fact]
    public async Task BookmarksBesideForeignOnesAreSavedForWord()
    {
        var folder = Folder();
        var body = "<w:bookmarkStart w:id=\"500\" w:name=\"_Toc200000001\"/>" + P(Run("A paragraph in a range that starts between blocks.")) + "<w:bookmarkEnd w:id=\"500\"/>"
            + "<w:bookmarkStart w:id=\"501\" w:name=\"BodyLevel\"/><w:bookmarkEnd w:id=\"501\"/>"
            + "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>"
            + "<w:tr><w:bookmarkStart w:id=\"502\" w:name=\"RowLevel\"/><w:tc><w:tcPr/>" + P(Run("one")) + "</w:tc><w:tc><w:tcPr/>" + P(Run("two")) + "</w:tc><w:bookmarkEnd w:id=\"502\"/></w:tr></w:tbl>"
            + P(Run("Contents stand here."))
            + P(Run("Alpha"), "Heading1")
            + P(Run("A sentence with a footnote."))
            + P(Run("See: "))
            + P(Run("The end."));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Number(file, editor);
        Did(editor.InsertNote(End(Para(file, "A sentence with a footnote.")), NoteType.Footnote), "a footnote");
        Did(editor.InsertPageNumber(Start(file, "The end."), PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Center, new PageNumberWords("Page", "of"), "1", "1"), "a page number in the footer");
        AddForeign(file.Model.Footnotes!.Notes.Last(n => n.Kind == NoteKind.Normal).Content, 503, "_Ref200000005");
        AddForeign(file.Model.HeadersFooters.Values.Single(p => p.Kind == HeaderFooterKind.Footer).Content, 504, "InFooter");

        Assert.Null(editor.AddBookmark(Caret(Start(file, "The end.")), "RowLevel"));
        Assert.Null(editor.AddBookmark(Caret(Start(file, "The end.")), "InFooter"));
        Did(editor.AddBookmark(Caret(Start(file, "The end.")), "Mine"), "a bookmark of Write");
        Did(editor.InsertToc(End(Para(file, "Contents stand here.")), s_contents, HostOf(file)), "a table of contents");
        Settle(file, editor);
        var heading = CrossReferences.Targets(file.Model, HostOf(file), CrossRefTargetKind.Heading)[0];
        Did(editor.InsertCrossReference(End(Para(file, "See: ")), heading, CrossRefKind.Text, HostOf(file)), "a reference to the heading");
        Settle(file, editor);

        await SaveAsync(file, folder, "probe-foreign-bookmarks.docx");

        // In the saved bytes every bookmark of every part has an id and a name of its own.
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var starts = new List<XElement>();
        using (var archive = ZipFile.OpenRead(Path.Combine(folder, "probe-foreign-bookmarks.docx")))
        {
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("word/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            {
                using var stream = entry.Open();
                starts.AddRange(XDocument.Load(stream).Descendants(w + "bookmarkStart"));
            }
        }

        Assert.Equal(8, starts.Count);
        Assert.Equal(starts.Count, starts.Select(s => (string)s.Attribute(w + "id")!).Distinct().Count());
        Assert.Equal(starts.Count, starts.Select(s => (string)s.Attribute(w + "name")!).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(starts, s => (string?)s.Attribute(w + "name") == "_Toc200000002");
        Assert.Contains(starts, s => (string?)s.Attribute(w + "name") == "_Ref200000006");
    }

    // A bookmark around the first paragraph of a story, as a file made by another program would hold it.
    private static void AddForeign(Story story, int id, string name)
    {
        var paragraph = story.Blocks.OfType<Paragraph>().First();
        paragraph.Inlines.Insert(0, new BookmarkStart(id, name));
        paragraph.Inlines.Add(new BookmarkEnd(id));
    }

    // ---- fields Write leaves alone ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AMixOfSupportedAndUnsupportedFieldsIsSavedForWord()
    {
        var folder = Folder();
        var body = P(Bookmarked(1, "_Ref100", Run("A heading to refer to")), "Heading1")
            + P(Run("Date: ") + Field("DATE \\@ \"yyyy\"", "1999"))
            + P(Run("Hidden number: ") + Field("SEQ Hidden \\h", "stored"))
            + P(Run("Relative page: ") + Field("PAGEREF _Ref100 \\p", "stored above"))
            + P(Run("Full number: ") + Field("REF _Ref100 \\w", "stored"))
            + P(Run("Supported number: ") + Field("SEQ Mixed \\* ARABIC", "9"))
            + P(Run("Supported text: ") + Field("REF _Ref100 \\h", "stored"))
            + P(Run("Supported page: ") + Field("PAGEREF _Ref100 \\h", "9"));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Settle(file, editor);

        await SaveAsync(file, folder, "fields-mixed.docx", "DATE \\@ \"yyyy\"", "SEQ Hidden \\h", "PAGEREF _Ref100 \\p", "REF _Ref100 \\w");
    }

    // ---- probes: how Word computes a case ----------------------------------------------------------------------------------------

    [Fact]
    public async Task SequenceFieldsInTrackedChangesAreSavedForWord()
    {
        var folder = Folder();
        const string Change = " w:author=\"Probe\" w:date=\"2026-01-01T00:00:00Z\"";
        var body = P(Run("Plain: ") + Field("SEQ Probe \\* ARABIC", "?"))
            + P($"<w:ins w:id=\"11\"{Change}>" + Run("Inserted: ") + Field("SEQ Probe", "2") + "</w:ins>")
            + P(Run("After the insertion: ") + Field("SEQ Probe \\* ARABIC", "?"))
            + P($"<w:del w:id=\"12\"{Change}>" + Deleted("Deleted: ") + DeletedField("SEQ Probe", "4") + "</w:del>")
            + P(Run("After the deletion: ") + Field("SEQ Probe \\* ARABIC", "?"));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Settle(file, editor);

        // Word updates the fields inside a change; Write keeps their stored results (a recorded limit). Everything after them must agree.
        await SaveAsync(file, folder, "probe-seq-tracked.docx", "SEQ Probe");
    }

    [Theory]
    [InlineData("probe-chapter-numbers-period.docx", "%1.", "%1.%2.")]
    [InlineData("probe-chapter-numbers-word.docx", "Chapter %1:", "%1.%2")]
    [InlineData("probe-chapter-numbers-brackets.docx", "(%1)", "%1.%2)")]
    public async Task ChapterNumbersAndRestartsAreSavedForWord(string name, string first, string second)
    {
        var folder = Folder();
        var body = P(Bookmarked(1, "_Ref101", Run("Alpha")), "Heading1")
            + P(Run("A: ") + Field("SEQ A \\s 1", "?") + Run(" chapter: ") + Field("STYLEREF 1 \\s", "?"))
            + P(Bookmarked(2, "_Ref102", Run("Alpha two")), "Heading2")
            + P(Run("B: ") + Field("SEQ B \\s 2", "?") + Run(" C: ") + Field("SEQ C", "?"))
            + P(Run("B: ") + Field("SEQ B \\s 2", "?"))
            + P(Run("Alpha three"), "Heading2")
            + P(Run("B: ") + Field("SEQ B \\s 2", "?") + Run(" chapter under level two: ") + Field("STYLEREF 1 \\s", "?"))
            + P(Run("Beta"), "Heading1")
            + P(Run("B: ") + Field("SEQ B \\s 2", "?") + Run(" C: ") + Field("SEQ C", "?"))
            + P(Run("C restarted at 5: ") + Field("SEQ C \\r 5", "?") + Run(" next: ") + Field("SEQ C", "?") + Run(" current: ") + Field("SEQ C \\c", "?") + Run(" next: ") + Field("SEQ C \\n", "?"))
            + P(Run("Gamma"), "Heading1")
            + P(Run("C restarted at 3 with level 1: ") + Field("SEQ C \\r 3 \\s 1", "?"))
            + P(Run("C with level 1: ") + Field("SEQ C \\s 1", "?"))
            + P(Run("Gamma two"), "Heading2")
            + P(Run("C with level 1 under level two: ") + Field("SEQ C \\s 1", "?"))
            + P(Run("Delta"), "Heading1")
            + P(Run("C current after a heading: ") + Field("SEQ C \\c \\s 1", "?") + Run(" then: ") + Field("SEQ C \\s 1", "?"))
            + P(Run("D roman: ") + Field("SEQ D \\* roman", "?") + Run(" D letter: ") + Field("SEQ D \\* ALPHABETIC", "?"))
            + P(Run("Number of level two: ") + Field("REF _Ref102 \\n \\h", "?"))
            + P(Run("Relative number of level two: ") + Field("REF _Ref102 \\r \\h", "?"))
            + P(Run("Number of level one: ") + Field("REF _Ref101 \\n \\h", "?"))
            + P(Run("Relative number of level one: ") + Field("REF _Ref101 \\r \\h", "?"))
            + P(Run("Text of level one: ") + Field("REF _Ref101 \\h", "?"));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Number(file, editor);
        var abstracts = file.Model.Numbering!.Abstracts.Where(a => a.Levels.Count > 1 && a.Levels.Any(l => l.Level == 0 && l.Text?.Contains("%1", StringComparison.Ordinal) == true)).ToList();
        foreach (var level in abstracts.SelectMany(a => a.Levels))
        {
            level.Text = level.Level switch { 0 => first, 1 => second, _ => level.Text };
        }

        Assert.NotEmpty(abstracts);
        Settle(file, editor);

        await SaveAsync(file, folder, name);
    }

    [Fact]
    public async Task AReferenceToItselfIsSavedForWord()
    {
        var folder = Folder();
        var body = P(Bookmarked(1, "Self", Run("Own text ") + Field("REF Self \\h", "stored")))
            + P(Run("From outside: ") + Field("REF Self \\h", "?"));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Settle(file, editor);

        // Word reports an invalid self-reference; Write keeps the stored result (a recorded difference).
        await SaveAsync(file, folder, "probe-self-reference.docx", "REF Self \\h");
    }

    [Theory]
    [InlineData("probe-label-roman.docx", "Chapter %1:", "upperRoman")]
    [InlineData("probe-label-letter.docx", "Appendix %1", "lowerLetter")]
    [InlineData("probe-label-literal-digits.docx", "Part 2.%1:", "decimal")]
    [InlineData("probe-label-dashes.docx", "A-%1-", "decimal")]
    [InlineData("probe-label-spaces.docx", "[ %1 ] .", "decimal")]
    [InlineData("probe-label-suffix.docx", "%1.0", "decimalZero")]
    public async Task ChapterNumberFormatsAreSavedForWord(string name, string text, string format)
    {
        var folder = Folder();
        var body = P(Bookmarked(1, "_Ref101", Run("Alpha")), "Heading1")
            + P(Run("chapter: ") + Field("STYLEREF 1 \\s", "?"))
            + P(Run("number: ") + Field("REF _Ref101 \\n \\h", "?") + Run(" relative: ") + Field("REF _Ref101 \\r \\h", "?"));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Number(file, editor);
        var levels = file.Model.Numbering!.Abstracts.SelectMany(a => a.Levels).Where(l => l.Level == 0 && l.Text?.Contains("%1", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(levels);
        foreach (var level in levels)
        {
            level.Text = text;
            level.Format = format;
        }
        Settle(file, editor);

        await SaveAsync(file, folder, name);
    }

    [Theory]
    [InlineData("probe-label-deep.docx", "Sec %1 - %2)")]
    [InlineData("probe-label-deep-last.docx", "(%2)")]
    [InlineData("probe-label-deep-words.docx", "%1 part %2 of it")]
    public async Task AChapterHeadingOnASecondListLevelIsSavedForWord(string name, string text)
    {
        var folder = Folder();
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = P(Run("First"), "Heading1") + P(Bookmarked(1, "_Ref101", Run("Second level chapter")), "Heading1") + P(Run("chapter: ") + Field("STYLEREF 1 \\s", "?"))
            + P(Run("number: ") + Field("REF _Ref101 \\n \\h", "?"));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Number(file, editor);
        var style = file.Model.Styles!.Find("Heading1")!.ParagraphProperties.Get(w + "numPr")!;
        var numId = (string)style.Element(w + "numId")!.Attribute(w + "val")!;
        var second = file.Model.Body.Blocks.OfType<Paragraph>().ElementAt(1);
        second.Properties.Set(w + "numPr", new XElement(w + "numPr", new XElement(w + "ilvl", new XAttribute(w + "val", "1")), new XElement(w + "numId", new XAttribute(w + "val", numId))));
        foreach (var level in file.Model.Numbering!.Abstracts.SelectMany(a => a.Levels).Where(l => l.Level == 1))
        {
            level.Text = text;
        }

        Settle(file, editor);

        await SaveAsync(file, folder, name);
    }

    [Fact]
    public async Task SequenceRestartsAreSavedForWord()
    {
        var folder = Folder();
        string One(string label, string instruction) => P(Run(label + ": ") + Field(instruction, "?"));
        string Heading(string text, int level = 1) => P(Run(text), "Heading" + level.ToString(CultureInfo.InvariantCulture));
        var body = Heading("Start")
            + One("E", "SEQ E") + Heading("E") + One("E", "SEQ E") + One("E", "SEQ E \\s 1")
            + One("F", "SEQ F") + Heading("F") + One("F", "SEQ F \\r 3") + One("F", "SEQ F \\s 1")
            + Heading("G") + One("G", "SEQ G \\s 1") + One("G", "SEQ G \\s 1") + Heading("G two") + One("G", "SEQ G \\c") + One("G", "SEQ G \\s 1")
            + One("H", "SEQ H") + Heading("H") + One("H", "SEQ H \\n \\s 1")
            + Heading("I") + One("I", "SEQ I \\c") + One("I", "SEQ I")
            + One("J", "SEQ J \\s 1") + "<w:p><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr>" + Run("An outline level paragraph") + "</w:p>" + One("J", "SEQ J \\s 1")
            + One("K", "SEQ K \\s 1") + "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr></w:p>" + One("K", "SEQ K \\s 1")
            + One("L", "SEQ L \\s 2") + Heading("L three", 3) + One("L", "SEQ L \\s 2")
            + One("M", "SEQ M \\s 1") + P(Run("M heading ") + Field("SEQ M \\s 1", "?"), "Heading1") + One("M", "SEQ M \\s 1")
            + One("N", "SEQ N") + One("N hidden", "SEQ N \\h") + One("N", "SEQ N")
            + One("N hidden shown", "SEQ N \\h \\* ARABIC") + One("N", "SEQ N")
            + One("Q", "SEQ Q") + One("Q text format", "SEQ Q \\* CardText") + One("Q", "SEQ Q") + One("Q restart text", "SEQ Q \\r 7 \\* Ordinal") + One("Q", "SEQ Q")
            + One("T", "SEQ T") + One("T bookmark", "SEQ T NoSuchBookmark") + One("T", "SEQ T")
            + One("U", "SEQ U \\r 0") + One("U", "SEQ U")
            + One("V", "SEQ V") + P(Run("V nested: ") + Mark("begin") + "<w:r><w:instrText xml:space=\"preserve\"> QUOTE \"</w:instrText></w:r>" + Field("SEQ V \\* MERGEFORMAT", "?") + "<w:r><w:instrText xml:space=\"preserve\">\" </w:instrText></w:r>" + Mark("separate") + Run("?") + Mark("end")) + One("V", "SEQ V")
            + One("W", "SEQ W \\s 1") + One("W", "SEQ w \\s 1");
        using var file = Probe(body);
        var editor = EditorOf(file);
        Settle(file, editor);

        // Fields Write does not update (hidden, other number formats, a bookmark argument, nested ones): Word writes them, Write keeps them; the numbers after them must agree.
        await SaveAsync(file, folder, "probe-seq-restarts.docx", "SEQ N \\h", "SEQ N \\h \\* ARABIC", "SEQ Q \\* CardText", "SEQ Q \\r 7 \\* Ordinal", "SEQ T NoSuchBookmark", "QUOTE \" SEQ V \\* MERGEFORMAT \"", "SEQ V \\* MERGEFORMAT");
    }

    [Fact]
    public async Task SequenceFieldsInMovedTextAndTheirListAreSavedForWord()
    {
        var folder = Folder();
        const string Change = " w:author=\"Probe\" w:date=\"2026-01-01T00:00:00Z\"";
        string Caption(string text, string label) => P(Run(text + " ") + Field("SEQ " + label, "?"), "Caption");
        var body = P(Mark("begin") + "<w:r><w:instrText xml:space=\"preserve\"> TOC \\h \\z \\c \"Moved\" </w:instrText></w:r>" + Mark("separate") + Run("?"), "TableofFigures")
            + P(Mark("end"))
            + Caption("Plain", "Moved")
            + $"<w:p><w:pPr><w:pStyle w:val=\"Caption\"/></w:pPr><w:moveFromRangeStart w:id=\"20\" w:name=\"move1\"{Change}/><w:moveFrom w:id=\"21\"{Change}>" + Run("Moved ") + Field("SEQ Moved \\* MERGEFORMAT", "5") + "</w:moveFrom><w:moveFromRangeEnd w:id=\"20\"/></w:p>"
            + Caption("Between", "Moved")
            + $"<w:p><w:pPr><w:pStyle w:val=\"Caption\"/></w:pPr><w:moveToRangeStart w:id=\"22\" w:name=\"move1\"{Change}/><w:moveTo w:id=\"23\"{Change}>" + Run("Moved ") + Field("SEQ Moved \\* MERGEFORMAT", "5") + "</w:moveTo><w:moveToRangeEnd w:id=\"22\"/></w:p>"
            + $"<w:p><w:pPr><w:pStyle w:val=\"Caption\"/></w:pPr><w:ins w:id=\"24\"{Change}>" + Run("Inserted ") + Field("SEQ Moved \\* MERGEFORMAT", "6") + "</w:ins></w:p>"
            + $"<w:p><w:pPr><w:pStyle w:val=\"Caption\"/></w:pPr><w:del w:id=\"25\"{Change}>" + Deleted("Deleted ") + DeletedField("SEQ Moved \\* MERGEFORMAT", "7") + "</w:del></w:p>"
            + Caption("Hidden", "Moved \\h")
            + Caption("Last", "Moved");
        using var file = Probe(body);
        var editor = EditorOf(file);
        Settle(file, editor);

        await SaveAsync(file, folder, "probe-seq-moved.docx", "SEQ Moved \\* MERGEFORMAT", "SEQ Moved \\h");
    }

    [Fact]
    public async Task OddSequenceFieldsAreSavedForWord()
    {
        var folder = Folder();
        string One(string label, string instruction) => P(Run(label + ": ") + Field(instruction, "?"));
        var body = P(Run("Start"), "Heading1")
            + One("X", "SEQ X") + One("X bad r", "SEQ X \\r abc") + One("X", "SEQ X") + One("X zero s", "SEQ X \\s 0") + One("X", "SEQ X") + One("X bad s", "SEQ X \\s abc") + One("X", "SEQ X")
            + One("X bad switch", "SEQ X \\q") + One("X", "SEQ X") + One("X next and current", "SEQ X \\n \\c") + One("X", "SEQ X") + One("X mergeformat", "SEQ X \\* MERGEFORMAT") + One("X picture", "SEQ X \\# \"00\"") + One("X", "SEQ X")
            + One("Y", "SEQ Y") + P(Run("Y link: ") + Mark("begin") + "<w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \\l \"nowhere\" </w:instrText></w:r>" + Mark("separate") + Run("inner ") + Field("SEQ Y \\* MERGEFORMAT", "?") + Mark("end")) + One("Y", "SEQ Y")
            + One("Z", "SEQ Z \\s 1") + "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc>" + P(Run("Heading in a cell"), "Heading1") + "</w:tc></w:tr></w:tbl>" + One("Z", "SEQ Z \\s 1")
            + One("R", "SEQ R \\s 1") + "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:rPr><w:del w:id=\"30\" w:author=\"Probe\" w:date=\"2026-01-01T00:00:00Z\"/></w:rPr></w:pPr><w:del w:id=\"31\" w:author=\"Probe\" w:date=\"2026-01-01T00:00:00Z\">" + Deleted("A deleted heading") + "</w:del></w:p>" + One("R", "SEQ R \\s 1")
            + One("S", "SEQ S \\s 1") + "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:rPr><w:ins w:id=\"32\" w:author=\"Probe\" w:date=\"2026-01-01T00:00:00Z\"/></w:rPr></w:pPr><w:ins w:id=\"33\" w:author=\"Probe\" w:date=\"2026-01-01T00:00:00Z\">" + Run("An inserted heading") + "</w:ins></w:p>" + One("S", "SEQ S \\s 1")
            + P(Run("Hidden run: ") + "<w:r><w:rPr><w:vanish/></w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:rPr><w:vanish/></w:rPr><w:instrText xml:space=\"preserve\"> SEQ H </w:instrText></w:r><w:r><w:rPr><w:vanish/></w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:rPr><w:vanish/></w:rPr><w:t>?</w:t></w:r><w:r><w:rPr><w:vanish/></w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r>") + One("H", "SEQ H")
            + P("<w:fldSimple w:instr=\" SEQ P \"><w:r><w:t>?</w:t></w:r></w:fldSimple>") + One("P", "SEQ P")
            + One("End", "SEQ End");
        using var file = Probe(body);
        var editor = EditorOf(file);
        Settle(file, editor);

        await SaveAsync(file, folder, "probe-seq-odd.docx", "SEQ X \\q", "SEQ X \\# \"00\"", "HYPERLINK \\l \"nowhere\"", "SEQ Y \\* MERGEFORMAT");
    }

    // The negative control of tools/word-field-results.ps1: every supported field with a wrong stored result, which the script must report as DIFFERENT (or as a page difference for a page result); no refresh runs.
    [Fact]
    public async Task WrongStoredResultsAreSavedForWord()
    {
        var folder = Folder();
        var body = P(Mark("begin") + "<w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-1\" \\h \\z \\u </w:instrText></w:r>" + Mark("separate") + Run("A wrong entry"), "TOC1")
            + P(Mark("end"))
            + P(Bookmarked(1, "_Ref101", Run("Alpha")), "Heading1")
            + P(Run("A: ") + Field("SEQ Control", "91"))
            + P(Run("B: ") + Field("STYLEREF 1 \\s", "92"))
            + P(Run("C: ") + Field("REF _Ref101 \\h", "93"))
            + P(Run("D: ") + Field("PAGEREF _Ref101 \\h", "94"))
            + P(Run("E: ") + Field("NOTEREF _Ref101 \\h", "95"));
        using var file = Probe(body);
        var editor = EditorOf(file);
        Number(file, editor);

        await SaveAsync(file, folder, "probe-control.docx");
    }

    // ---- probes written by hand ---------------------------------------------------------------------------------------------------

    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r>";

    private static string Deleted(string text) => $"<w:r><w:delText xml:space=\"preserve\">{Escape(text)}</w:delText></w:r>";

    private static string Mark(string kind) => $"<w:r><w:fldChar w:fldCharType=\"{kind}\"/></w:r>";

    private static string Field(string instruction, string result) =>
        Mark("begin") + $"<w:r><w:instrText xml:space=\"preserve\"> {Escape(instruction)} </w:instrText></w:r>" + Mark("separate") + Run(result) + Mark("end");

    private static string DeletedField(string instruction, string result) =>
        Mark("begin") + $"<w:r><w:delInstrText xml:space=\"preserve\"> {Escape(instruction)} </w:delInstrText></w:r>" + Mark("separate") + Deleted(result) + Mark("end");

    private static string Bookmarked(int id, string name, string inner) => $"<w:bookmarkStart w:id=\"{id}\" w:name=\"{name}\"/>{inner}<w:bookmarkEnd w:id=\"{id}\"/>";

    private static string P(string inner, string? style = null) => $"<w:p>{(style is null ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")}{inner}</w:p>";

    // A blank document of Write whose body is replaced by the paragraphs given (its section properties stay).
    private static DocxFile Probe(string body)
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
            Assert.True(from > "<w:body>".Length && to >= from, "the blank document has no body to replace");
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            writer.Write(string.Concat(xml.AsSpan(0, from), body, xml.AsSpan(to)));
        }

        return DocxFile.Open(buffer.ToArray());
    }
}

/// <summary>The source-generated JSON of the lists the Word check writes (Native AOT rules apply to the whole solution).</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
internal sealed partial class WordCheckJson : System.Text.Json.Serialization.JsonSerializerContext;
