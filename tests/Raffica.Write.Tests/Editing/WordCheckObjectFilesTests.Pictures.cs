using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

// The picture files of the Word check of milestone 4.5a: every picture command through the editor, saved through DocxFile.SaveAsync (see the class comment). Each file also gets a ".facts.txt" with the pictures the model holds.
public sealed partial class WordCheckObjectFilesTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    private const string Lorem = "The quick brown fox jumps over the lazy dog while the text runs on for several lines, so that a picture floating beside it, above it or below it can show how the wrap of the text goes around it. "
        + "Pack my box with five dozen liquor jugs, and then some more words follow to make the paragraph long enough for the check.";

    // ---- helpers -------------------------------------------------------------------------------------------------------------------

    // The n-th top-level paragraph of the body, read again every time (an undo or a split replaces the objects).
    private static Paragraph P(DocxFile file, int n) => file.Model.Body.Blocks.OfType<Paragraph>().ElementAt(n);

    private static TextPosition At(DocxFile file, int paragraph, int? offset = null)
    {
        var p = P(file, paragraph);
        return new TextPosition(StoryRef.Body, p, offset ?? ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length);
    }

    // Types the lines as paragraphs, one after the other (Enter between them), as a person does.
    private static void Lines(TextEditor editor, DocxFile file, params string[] lines)
    {
        var at = Type(editor, FirstParagraph(file), lines[0]);
        for (var i = 1; i < lines.Length; i++)
        {
            at = Did(editor.SplitParagraph(Caret(at)), "split a paragraph").Caret;
            at = Did(editor.InsertText(Caret(at), lines[i]), "type a line").Caret;
        }
    }

    private static ObjectId Insert(TextEditor editor, TextPosition at, int width, int height, string? alt = null)
    {
        var result = Did(editor.InsertPicture(at, TestPictures.Png(width, height), alt), $"InsertPicture {width}x{height}");
        var id = editor.PictureAt(result.Caret with { Offset = result.Caret.Offset - 1 });
        Assert.NotNull(id);
        return id;
    }

    // The id of the picture the editor shows as the caret's neighbour after a move (the result's caret is the start of the paragraph that took it).
    private static ObjectId Moved(TextEditor editor, EditResult result)
    {
        var id = editor.PictureAt(result.Caret);
        Assert.NotNull(id);
        return id;
    }

    private static PictureInfo Info(DocxFile file, ObjectId id) => PictureView.Of(ObjectIds.Find(file.Model.Body.Blocks, id)!.Value.Picture)!;

    private static XElement Raw(DocxFile file, ObjectId id) => ObjectIds.Find(file.Model.Body.Blocks, id)!.Value.Picture.Raw;

    private static EmuSize Half(EmuSize size) => new(size.Width / 2, size.Height / 2);

    private static IEnumerable<Block> AllBlocks(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            switch (block)
            {
                case Table table:
                    foreach (var inner in table.Rows.OfType<TableRow>().SelectMany(r => r.Items).OfType<TableCell>().SelectMany(c => AllBlocks(c.Blocks)))
                    {
                        yield return inner;
                    }

                    break;
                case ContentControlBlock control:
                    foreach (var inner in AllBlocks(control.Content))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    private static List<Picture> PicturesOf(DocxFile file) =>
        [.. AllBlocks(file.Model.Body.Blocks).OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<Picture>()];

    private static string KindOf(PictureWrap wrap) => wrap switch
    {
        PictureWrap.Inline => "inline",
        PictureWrap.Square => "square",
        PictureWrap.TopAndBottom => "topbottom",
        PictureWrap.InFront => "front",
        _ => "behind",
    };

    private static string Points(long emu) => (emu / 12700.0).ToString("0.0", CultureInfo.InvariantCulture);

    private static int NoteCount(NoteCollection? collection) => collection?.Notes.Count(n => n.Kind == NoteKind.Normal) ?? 0;

    // What the model holds: the counts of the objects and one line per picture (kind, size in points, description), for the comparison with what Word reports (tools/word-object-counts.ps1).
    private static List<string> ObjectFacts(DocxFile file)
    {
        var pictures = PicturesOf(file);
        var infos = pictures.Select(p => PictureView.Of(p) ?? throw new InvalidOperationException("a picture the editor made is not a plain picture")).ToList();
        var tables = AllBlocks(file.Model.Body.Blocks).OfType<Table>().Count();
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"summary tables={tables} footnotes={NoteCount(file.Model.Footnotes)} endnotes={NoteCount(file.Model.Endnotes)} inline={infos.Count(i => i.Wrap == PictureWrap.Inline)} floating={infos.Count(i => i.Wrap != PictureWrap.Inline)}"),
        };
        lines.AddRange(infos.Select(i => $"picture kind={KindOf(i.Wrap)} width={Points(i.ExtentEmu.Width)} height={Points(i.ExtentEmu.Height)} alt={i.AltText}"));
        return lines;
    }

    // Saves and checks the package: a part for every picture the document still shows (and none for a picture that was deleted or undone), each under its own relationship.
    private static async Task SavePictureFileAsync(DocxFile file, string folder, string name)
    {
        var distinct = PicturesOf(file).Select(p => PictureView.Of(p)!.Media).Distinct().ToList();
        Assert.DoesNotContain(null, distinct);

        await SaveWithFactsAsync(file, folder, name);

        using var zip = ZipFile.OpenRead(Path.Combine(folder, name));
        var parts = zip.Entries.Count(e => e.FullName.Contains("media/", StringComparison.Ordinal));
        Assert.True(distinct.Count == parts, $"{name}: {distinct.Count} pictures show but the package holds {parts} media parts");
    }

    private static DocxFile Blank(out TextEditor editor)
    {
        var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        editor = EditorOf(file);
        return file;
    }

    // ---- insert --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task InlinePicturesInATextAreSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, "A sentence with a picture inside it and one more at its end.", "A second paragraph that starts with a picture.", "A third paragraph with a tall picture.");

        Insert(editor, At(file, 0), 200, 100, "A wide picture at the end of the sentence");
        Insert(editor, At(file, 0, 22), 80, 80, "A square picture inside the sentence");
        Insert(editor, At(file, 1, 0), 120, 160);
        Insert(editor, At(file, 2), 50, 300, "A tall, narrow picture");

        await SavePictureFileAsync(file, folder, "picture-inline.docx");
    }

    [Fact]
    public async Task PicturesInTableCellsAreSavedForWord()
    {
        var folder = Folder();
        using var file = BlankWithTable(out var editor, out var table, 3, 3);
        FillAll(editor, file, table);

        // A picture too wide for its cell is fitted to the cell; a small one keeps its size; one sits before the text of a cell and one behind it.
        Insert(editor, In(file, table, 1, 1), 640, 480, "A large picture in a cell");
        var smallCell = In(file, table, 2, 2);
        Insert(editor, smallCell with { Offset = ParagraphProjection.Build(smallCell.Paragraph, ProjectionHost.Plain).Text.Length }, 40, 30, "A small picture behind the text of a cell");
        Insert(editor, In(file, table, 0, 0), 60, 60);

        await SavePictureFileAsync(file, folder, "picture-in-cell.docx");
    }

    // ---- size and properties -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ResizedCroppedAndDescribedPicturesAreSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, "Resize keeps the ratio.", "A cropped picture is resized and reset.", "Several parts changed in one step.");

        // Half the size, and 1.5 times the size, each in the ratio of the picture.
        var half = Insert(editor, At(file, 0), 200, 100, "Resized to half");
        Did(editor.ResizePicture(half, Info(file, half).ExtentEmu.Width / 2, Info(file, half).ExtentEmu.Height / 2), "resize to half");
        var big = Insert(editor, At(file, 0, 0), 100, 100, "Resized to one and a half");
        Did(editor.ResizePicture(big, Info(file, big).ExtentEmu.Width * 3 / 2, Info(file, big).ExtentEmu.Height * 3 / 2), "resize to one and a half");

        // A crop of 10 percent each side of the width, written into the drawing as a file from Word has it; the resize keeps it and the reset shows it at the pixel size.
        var cropped = Insert(editor, At(file, 1), 160, 120, "A cropped picture");
        Raw(file, cropped).Descendants(Pic + "blipFill").Single().Element(A + "blip")!.AddAfterSelf(new XElement(A + "srcRect", new XAttribute("l", "10000"), new XAttribute("r", "10000")));
        Did(editor.ResizePicture(cropped, 2_000_000, 1_500_000), "resize a cropped picture");
        Assert.Equal(new PictureCrop(10000, 0, 10000, 0), Info(file, cropped).Crop);
        Did(editor.ResetPictureSize(cropped), "reset the size of a cropped picture");

        // The card's one OK: wrap, size and description together.
        var props = Insert(editor, At(file, 2), 60, 40, "Before");
        Did(editor.SetPictureProperties(props, new PictureProperties(WidthEmu: 2_400_000, HeightEmu: 1_600_000, AltText: "After <&> \"quoted\" and Ünïcode", Wrap: PictureWrap.Square)), "SetPictureProperties");

        await SavePictureFileAsync(file, folder, "picture-resize.docx");
    }

    [Fact]
    public async Task AlternativeTextsAreSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, "Alternative texts of pictures.", "Second line.", "Third line.", "Fourth line.");

        var set = Insert(editor, At(file, 0), 60, 40);
        Did(editor.SetAltText(set, "A description set afterwards"), "set an alt text");
        var changed = Insert(editor, At(file, 1), 60, 40, "The first text");
        Did(editor.SetAltText(changed, "A changed text with <angle brackets> & an ampersand, \"quotes\" and Ünïcode ✓"), "change an alt text");
        var removed = Insert(editor, At(file, 2), 60, 40, "To be removed");
        Did(editor.SetAltText(removed, string.Empty), "remove an alt text");
        Insert(editor, At(file, 3), 60, 40, new string('x', 500));

        await SavePictureFileAsync(file, folder, "picture-alt-text.docx");
    }

    // ---- wrap ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryWrapOfAPictureIsSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, Lorem, Lorem, Lorem, Lorem, Lorem, Lorem);

        var wraps = new[] { PictureWrap.Square, PictureWrap.TopAndBottom, PictureWrap.Behind, PictureWrap.InFront };
        for (var i = 0; i < wraps.Length; i++)
        {
            var id = Insert(editor, At(file, i, 0), 120, 90, $"Wrap {KindOf(wraps[i])}");
            Did(editor.SetPictureWrap(id, wraps[i]), $"wrap {wraps[i]}");
        }

        // Inline to square and back to inline (the inline form is written again), and floating to floating (only the wrap element changes).
        var back = Insert(editor, At(file, 4, 0), 120, 90, "Inline again");
        Did(editor.SetPictureWrap(back, PictureWrap.Square), "inline to square");
        Did(editor.SetPictureWrap(back, PictureWrap.Inline), "square back to inline");
        var between = Insert(editor, At(file, 5, 0), 120, 90, "Square, then top and bottom, then behind");
        Did(editor.SetPictureWrap(between, PictureWrap.Square), "inline to square");
        Did(editor.SetPictureWrap(between, PictureWrap.TopAndBottom), "square to top and bottom");
        Did(editor.SetPictureWrap(between, PictureWrap.Behind), "top and bottom to behind");

        await SavePictureFileAsync(file, folder, "picture-wrap.docx");
    }

    // ---- move ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MovedAndNudgedFloatingPicturesAreSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, Lorem, Lorem, Lorem, Lorem);

        // Down to another paragraph with offsets, then nudged; up to an earlier paragraph; and a move inside its own paragraph.
        var down = Insert(editor, At(file, 0, 0), 120, 90, "Moved down");
        Did(editor.SetPictureWrap(down, PictureWrap.Square), "wrap square");
        down = Moved(editor, Did(editor.MovePicture(down, At(file, 2, 0), 72 * 12700L, 10 * 12700L), "move to a later paragraph"));
        Did(editor.NudgePicture(down, 20, 30), "nudge");

        var up = Insert(editor, At(file, 3, 0), 90, 120, "Moved up");
        Did(editor.SetPictureWrap(up, PictureWrap.TopAndBottom), "wrap top and bottom");
        up = Moved(editor, Did(editor.MovePicture(up, At(file, 1, 0), 0, 0), "move to an earlier paragraph"));
        Assert.NotNull(up);

        var front = Insert(editor, At(file, 3, 0), 100, 100, "Moved in its own paragraph");
        Did(editor.SetPictureWrap(front, PictureWrap.InFront), "wrap in front");
        Did(editor.MovePicture(front, At(file, 3, 0), 150 * 12700L, 40 * 12700L), "move inside its own paragraph");

        await SavePictureFileAsync(file, folder, "picture-move.docx");
    }

    // ---- paste ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PastedPicturesAreSavedForWord()
    {
        var folder = Folder();
        using var file = BlankWithTable(out var editor, out var table, 2, 2);
        FillAll(editor, file, table);

        // Pasted at a caret, over a selected word of a paragraph, and over a selected text in a table cell.
        var lead = P(file, 0);
        var leadEnd = ParagraphProjection.Build(lead, ProjectionHost.Plain).Text.Length;
        Did(editor.PastePicture(Caret(new TextPosition(StoryRef.Body, lead, leadEnd)), TestPictures.Png(100, 60)), "paste at a caret");
        lead = P(file, 0);
        Did(editor.PastePicture(new TextRange(new TextPosition(StoryRef.Body, lead, 0), new TextPosition(StoryRef.Body, lead, 4)), TestPictures.Png(60, 100)), "paste over a word");
        var cell = In(file, table, 1, 1);
        Did(editor.PastePicture(new TextRange(cell, cell with { Offset = 4 }), TestPictures.Png(80, 80)), "paste over text in a cell");

        await SavePictureFileAsync(file, folder, "picture-paste.docx");
    }

    // ---- delete, undo and redo -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DeletedPicturesLeaveNoPartInTheSavedFile()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, "Two pictures in a line, one of them deleted.", "A second line with a floating picture, deleted, and one that stays.");

        Insert(editor, At(file, 0), 100, 60, "Stays");
        var gone = Insert(editor, At(file, 0, 0), 100, 60, "Deleted");
        Did(editor.DeletePicture(gone), "delete an inline picture");

        var floating = Insert(editor, At(file, 1, 0), 100, 60, "Floating, deleted");
        Did(editor.SetPictureWrap(floating, PictureWrap.Square), "wrap square");
        Did(editor.DeletePicture(floating), "delete a floating picture");
        var kept = Insert(editor, At(file, 1, 0), 100, 60, "Floating, stays");
        Did(editor.SetPictureWrap(kept, PictureWrap.Square), "wrap square");

        await SavePictureFileAsync(file, folder, "picture-deleted.docx");
    }

    [Fact]
    public async Task UndoAndRedoOfPictureStepsAreSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, "Undo and redo of an insert.", "Undo and redo of a resize.", "An insert that was undone and replaced.");

        // An insert undone and redone.
        Insert(editor, At(file, 0), 100, 60, "A: inserted, undone, redone");
        Did(editor.History.Undo(), "undo the insert");
        Assert.Empty(PicturesOf(file));
        Did(editor.History.Redo(), "redo the insert");

        // A resize and the insert before it undone, then both redone.
        var b = Insert(editor, At(file, 1), 100, 60, "B: resized, everything undone and redone");
        Did(editor.ResizePicture(b, Info(file, b).ExtentEmu.Width / 2, Info(file, b).ExtentEmu.Height / 2), "resize");
        Did(editor.History.Undo(), "undo the resize");
        Did(editor.History.Undo(), "undo the insert of B");
        Did(editor.History.Redo(), "redo the insert of B");
        Did(editor.History.Redo(), "redo the resize");

        // An insert undone and a different picture inserted in its place (the first one's bytes are no longer needed).
        Insert(editor, At(file, 2), 70, 70, "C: undone");
        Did(editor.History.Undo(), "undo the insert of C");
        Insert(editor, At(file, 2), 50, 90, "D: inserted after the undo");

        await SavePictureFileAsync(file, folder, "picture-undo-redo.docx");
        Assert.Equal(3, PicturesOf(file).Count);
    }

    [Fact]
    public async Task APictureBroughtBackByAnUndoOfItsDeletionIsSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, "A picture deleted and brought back.", "A picture deleted, brought back and deleted again.");

        var a = Insert(editor, At(file, 0), 100, 60, "A: back after the undo");
        Did(editor.DeletePicture(a), "delete A");
        Did(editor.History.Undo(), "undo the deletion of A");

        var b = Insert(editor, At(file, 1), 100, 60, "B: gone after the redo");
        Did(editor.DeletePicture(b), "delete B");
        Did(editor.History.Undo(), "undo the deletion of B");
        Did(editor.History.Redo(), "redo the deletion of B");

        await SavePictureFileAsync(file, folder, "picture-delete-undo.docx");
        Assert.Single(PicturesOf(file));
    }

    // ---- together ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PicturesTablesAndNotesInOneDocumentAreSavedForWord()
    {
        var folder = Folder();
        using var file = Blank(out var editor);
        Lines(editor, file, "A sentence with a picture and a footnote.", "The closing paragraph with a floating picture.");

        var lead = At(file, 0);
        Insert(editor, lead, 120, 80, "Inline in the first paragraph");
        var typed = Did(editor.InsertText(Caret(At(file, 0)), " The note follows."), "typing");
        NoteWithText(editor, typed.Caret with { Offset = typed.Caret.Offset - 1 }, NoteType.Footnote, "The footnote of the first paragraph.");

        // A table behind the first paragraph, a picture in one cell and a note in another.
        var table = Inserted(editor, Did(editor.InsertTable(At(file, 0), 2, 3), "InsertTable"));
        FillAll(editor, file, table);
        Insert(editor, In(file, table, 1, 0), 100, 70, "A picture in a cell");
        var cell = Did(editor.InsertText(Caret(In(file, table, 2, 1)), " and an endnote"), "typing in a cell");
        NoteWithText(editor, cell.Caret with { Offset = cell.Caret.Offset - 1 }, NoteType.Endnote, "The endnote of a cell.");

        // A floating picture in the paragraph behind the table, the text wrapping around it.
        var last = file.Model.Body.Blocks.OfType<Paragraph>().Count() - 1;
        var floating = Insert(editor, At(file, last, 0), 90, 90, "Floating behind the table");
        Did(editor.SetPictureWrap(floating, PictureWrap.Square), "wrap square");

        await SavePictureFileAsync(file, folder, "picture-table-notes.docx");
    }
}
