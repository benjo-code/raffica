using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Makes the files of the owner's Word check for milestone 4.4a (<c>Docs/write-word-check.md</c>, section "Headings, lists and the outline (milestone 4.4a)"): every operation goes through the editor as the
/// app calls it and the file is saved through <see cref="DocxFile.SaveAsync(string, CancellationToken)"/>. The tests write nothing and skip unless the folder is named by <c>RAFFICA_WORD_CHECK_DIR</c>;
/// <c>pwsh tools/word-open-check.ps1 &lt;folder&gt;\*.docx</c> then asks Word whether it opens each file without repair.
/// </summary>
public sealed class WordCheckFilesTests
{
    private const string FolderVariable = "RAFFICA_WORD_CHECK_DIR";

    private static string Folder()
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(folder), "Set RAFFICA_WORD_CHECK_DIR to a folder to make the Word-check files.");

        Directory.CreateDirectory(folder!);
        return folder!;
    }

    private static TextPosition At(Paragraph paragraph, int offset = 0) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(Paragraph paragraph, int offset = 0) => new(At(paragraph, offset), At(paragraph, offset));

    private static TextRange Over(Paragraph first, Paragraph last) => new(At(first), At(last, 0));

    private static List<Paragraph> Paragraphs(WordDocument document) => [.. document.Body.Blocks.OfType<Paragraph>()];

    private static Paragraph Find(WordDocument document, string text) =>
        Paragraphs(document).Single(p => TestEditText(p) == text);

    private static string TestEditText(Paragraph paragraph) =>
        ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static int IndexOf(WordDocument document, string text) => document.Body.Blocks.ToList().IndexOf(Find(document, text));

    private static TextEditor EditorOf(DocxFile file) =>
        new(file.Model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(file.Model));

    /// <summary>Types the lines into the empty first paragraph (a newline is Enter) and gives the listed lines their paragraph style.</summary>
    private static void Type(DocxFile file, TextEditor editor, string[] lines, Dictionary<string, string> styles)
    {
        var first = Paragraphs(file.Model)[0];
        Assert.Equal(string.Empty, TestEditText(first));
        editor.InsertText(Caret(first), string.Join("\n", lines));
        foreach (var (text, style) in styles)
        {
            editor.ApplyParagraphStyle(Caret(Find(file.Model, text)), style);
        }
    }

    [Fact]
    public async Task NumberedHeadingsOfABlankDocumentAreSavedForWord()
    {
        var folder = Folder();

        var token = TestContext.Current.CancellationToken;
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        Type(
            file,
            editor,
            ["Introduction", "First body paragraph.", "Methods", "Second body paragraph.", "Sample design", "Third body paragraph.", "Results", "Fourth body paragraph.", "Discussion", "Fifth body paragraph."],
            new() { ["Introduction"] = "Heading1", ["Methods"] = "Heading1", ["Sample design"] = "Heading2", ["Results"] = "Heading1", ["Discussion"] = "Heading1" });

        Assert.NotNull(editor.SetHeadingNumbering(true, At(Find(file.Model, "Introduction"))));

        // Move the chapter "Methods" (with its sub-heading and both body paragraphs) in front of "Discussion".
        var methods = IndexOf(file.Model, "Methods");
        var discussion = IndexOf(file.Model, "Discussion");
        Assert.NotNull(editor.MoveBlocks(methods, 4, discussion));

        // Promote "Sample design" to a chapter of its own.
        var sample = IndexOf(file.Model, "Sample design");
        Assert.NotNull(editor.ShiftOutlineLevels(sample, sample + 1, -1));
        Assert.Equal("Heading1", Find(file.Model, "Sample design").Properties.StyleId);

        await file.SaveAsync(Path.Combine(folder, "numbered-headings.docx"), token);
    }

    [Fact]
    public async Task ListsOfABlankDocumentAreSavedForWord()
    {
        var folder = Folder();

        var token = TestContext.Current.CancellationToken;
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        Type(
            file,
            editor,
            [
                "Shopping", "Apples", "Pears", "Plums",
                "Steps", "First step", "Second step", "Third step", "Fourth step", "Fifth step",
                "Outline", "Level one", "Level two", "Level three", "Back to level one",
            ],
            []);

        // A bullet list.
        editor.ToggleList(Over(Find(file.Model, "Apples"), Find(file.Model, "Plums")), ListKind.Bullet);

        // A numbered list of five, restarted at the fourth step.
        editor.ToggleList(Over(Find(file.Model, "First step"), Find(file.Model, "Fifth step")), ListKind.Numbered);
        Assert.NotNull(editor.RestartNumbering(At(Find(file.Model, "Fourth step"))));

        // A multilevel list by changing levels (what Tab does in a list item).
        editor.ToggleList(Over(Find(file.Model, "Level one"), Find(file.Model, "Back to level one")), ListKind.Numbered);
        Assert.NotNull(editor.ChangeListLevel(Caret(Find(file.Model, "Level two")), 1));
        Assert.NotNull(editor.ChangeListLevel(Caret(Find(file.Model, "Level three")), 1));
        Assert.NotNull(editor.ChangeListLevel(Caret(Find(file.Model, "Level three")), 1));

        await file.SaveAsync(Path.Combine(folder, "lists.docx"), token);
    }

    [Fact]
    public async Task NumberedHeadingsOfTheThesisAreSavedForWord()
    {
        var folder = Folder();

        var token = TestContext.Current.CancellationToken;
        var path = Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");
        using var file = DocxFile.Open(path);
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        static int LevelOf(Block block) => block is Paragraph { Properties.StyleId: { } id } && id.StartsWith("Heading", StringComparison.Ordinal) && int.TryParse(id.AsSpan(7), out var n) ? n : 0;

        // The chapters are the top-level Heading1 paragraphs. Move the second one in front of the last.
        var chapters = Enumerable.Range(0, body.Count).Where(i => LevelOf(body[i]) == 1).ToList();
        Assert.True(chapters.Count >= 4);
        Assert.NotNull(editor.MoveBlocks(chapters[1], chapters[2] - chapters[1], chapters[^1]));

        // Promote the first Heading2 with the sub-headings under it.
        body = file.Model.Body.Blocks;
        var two = Enumerable.Range(0, body.Count).First(i => LevelOf(body[i]) == 2);
        var end = two + 1;
        while (end < body.Count && (LevelOf(body[end]) is 0 or > 2))
        {
            end++;
        }

        Assert.NotNull(editor.ShiftOutlineLevels(two, end - 1, -1));

        // The numbering switch off and on again.
        var caret = At(Paragraphs(file.Model)[0]);
        Assert.NotNull(editor.SetHeadingNumbering(false, caret));
        Assert.NotNull(editor.SetHeadingNumbering(true, At(Paragraphs(file.Model)[0])));

        await file.SaveAsync(Path.Combine(folder, "numbered-thesis-chapter-move.docx"), token);
    }
}
