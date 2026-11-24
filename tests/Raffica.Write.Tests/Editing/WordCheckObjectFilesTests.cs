using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Makes the files of the owner's Word check for milestone 4.5a (<c>Docs/write-word-check.md</c>, section "Tables, pictures and notes (milestone 4.5a)"): every operation goes through the editor as the app calls
/// it and the file is saved through <see cref="DocxFile.SaveAsync(string, CancellationToken)"/>; <c>pwsh tools/word-open-check.ps1 &lt;files&gt;</c> then asks Word whether it opens each without repair. This is the
/// part for the notes (Task 3: <c>footnote.docx</c>, <c>endnote.docx</c> and <c>notes-in-table.docx</c>); the table and picture files join it in the later tasks. The tests write nothing and skip unless the
/// folder is named by <c>RAFFICA_WORD_CHECK_DIR</c>.
/// </summary>
public sealed class WordCheckObjectFilesTests
{
    private const string FolderVariable = "RAFFICA_WORD_CHECK_DIR";

    private static string Folder()
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(folder), "Set RAFFICA_WORD_CHECK_DIR to a folder to make the Word-check files.");

        Directory.CreateDirectory(folder!);
        return folder!;
    }

    private static TextEditor EditorOf(DocxFile file) =>
        new(file.Model, (_, _) => ProjectionHost.Plain, format: new Raffica.Write.Layout.LayoutEffectiveFormat(file.Model));

    private static TextRange Caret(TextPosition at) => new(at, at);

    private static Paragraph FirstParagraph(DocxFile file) => file.Model.Body.Blocks.OfType<Paragraph>().First();

    // Types a text at the end of the paragraph and returns the caret behind it.
    private static TextPosition Type(TextEditor editor, Paragraph paragraph, string text)
    {
        var end = new TextPosition(StoryRef.Body, paragraph, ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length);
        var result = editor.InsertText(Caret(end), text);
        return result.Caret;
    }

    // A note at the caret with a line of text typed into it, as the app does (the caret lands in the note).
    private static void NoteWithText(TextEditor editor, TextPosition at, NoteType kind, string text)
    {
        var result = editor.InsertNote(at, kind);
        Assert.NotNull(result);
        var inNote = result.Caret;
        Assert.Equal(StoryKind.Note, inNote.Story.Kind);
        editor.InsertText(Caret(inNote), text);
    }

    [Fact]
    public async Task AFootnoteIsSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var paragraph = FirstParagraph(file);
        var at = Type(editor, paragraph, "A sentence that carries a footnote.");
        NoteWithText(editor, at with { Offset = at.Offset - 1 }, NoteType.Footnote, "The footnote says where the sentence comes from.");

        await file.SaveAsync(Path.Combine(folder, "footnote.docx"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnEndnoteIsSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var paragraph = FirstParagraph(file);
        var at = Type(editor, paragraph, "A sentence that carries an endnote.");
        NoteWithText(editor, at with { Offset = at.Offset - 1 }, NoteType.Endnote, "The endnote collects its sources at the end.");

        await file.SaveAsync(Path.Combine(folder, "endnote.docx"), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NotesInATableAndInTheBodyAreSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var closing = FirstParagraph(file);

        // A table of one row and two cells before the closing paragraph (a body must not end in a table), each cell with a paragraph.
        var table = new Table();
        table.GridColumns.Add(4500);
        table.GridColumns.Add(4500);
        var row = new TableRow();
        var cells = new[] { new TableCell(), new TableCell() };
        var paragraphs = new[] { new Paragraph(), new Paragraph() };
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i].Blocks.Add(paragraphs[i]);
            row.Items.Add(cells[i]);
        }

        table.Rows.Add(row);
        body.Insert(body.IndexOf(closing), table);

        var first = editor.InsertText(Caret(new TextPosition(StoryRef.Body, paragraphs[0], 0)), "Cell with a footnote.");
        var second = editor.InsertText(Caret(new TextPosition(StoryRef.Body, paragraphs[1], 0)), "Cell with an endnote.");
        NoteWithText(editor, first.Caret with { Offset = first.Caret.Offset - 1 }, NoteType.Footnote, "A footnote of a cell.");
        NoteWithText(editor, second.Caret with { Offset = second.Caret.Offset - 1 }, NoteType.Endnote, "An endnote of a cell.");

        var tail = Type(editor, closing, "Text behind the table with a footnote.");
        NoteWithText(editor, tail with { Offset = tail.Offset - 1 }, NoteType.Footnote, "A footnote of the body.");

        await file.SaveAsync(Path.Combine(folder, "notes-in-table.docx"), TestContext.Current.CancellationToken);
    }
}
