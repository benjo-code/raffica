using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Paste of a picture from the clipboard (milestone 4.5a): the order fragment, text, picture; a selection replaced in one history step; the size rule; what is refused and says why.</summary>
public sealed class ImagePasteTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain) { FragmentCodec = new DocxFragmentCodec() };

    private static WordDocument Doc(params string[] blocks) => Document(string.Concat(blocks) + Sect());

    private static Paragraph P(WordDocument document, int block) => (Paragraph)document.Body.Blocks[block];

    private static TextPosition At(WordDocument document, int block, int offset) => new(StoryRef.Body, P(document, block), offset);

    private static TextRange Caret(WordDocument document, int block, int offset) => new(At(document, block, offset), At(document, block, offset));

    private static TextRange Sel(WordDocument document, int block, int start, int end) => new(At(document, block, start), At(document, block, end));

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static List<Picture> PicturesOf(Paragraph paragraph) => [.. paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>()];

    private static string Snapshot(WordDocument document) => ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);

    private static ClipboardContent ImageOnly(byte[] png) => new(string.Empty, null, Image: png);

    [Fact]
    public void AnImageOnlyClipboardInsertsAPictureAtTheCaret()
    {
        var document = Doc(Line("alpha"), Line("beta"));
        var editor = Editor(document);

        var result = editor.Paste(Caret(document, 0, 2), ImageOnly(TestPictures.Png(40, 30)), _ => true);

        var picture = Assert.Single(PicturesOf(P(document, 0)));
        var info = PictureView.Of(picture)!;
        Assert.Equal(PictureWrap.Inline, info.Wrap);
        Assert.True(string.IsNullOrEmpty(info.AltText));
        Assert.Equal("al￼pha", Text(P(document, 0)));
        Assert.Equal(new TextPosition(StoryRef.Body, P(document, 0), 3), result.Caret);
        var media = Assert.Single(result.Objects!.MediaAdded);
        Assert.Equal(media.RelationshipId, info.Media);
        Assert.Equal(TestPictures.Png(40, 30), document.Media.TryGet(media.RelationshipId)!.Value.ToArray());
        Assert.Empty(PicturesOf(P(document, 1)));
    }

    [Fact]
    public void ASelectionIsReplacedByThePictureAsByText()
    {
        var document = Doc(Line("alpha"), Line("beta"));
        var editor = Editor(document);

        var result = editor.Paste(Sel(document, 0, 1, 3), ImageOnly(TestPictures.Png(8, 8)), _ => true);

        Assert.Equal("a￼ha", Text(P(document, 0)));
        Assert.Single(PicturesOf(P(document, 0)));
        Assert.Equal(new TextPosition(StoryRef.Body, P(document, 0), 2), result.Caret);

        // Over two paragraphs the survivor holds the picture between what is left of both.
        var second = Doc(Line("alpha"), Line("beta"), Line("gamma"));
        var editor2 = Editor(second);
        var range = new TextRange(At(second, 0, 2), At(second, 1, 2));
        var result2 = editor2.Paste(range, ImageOnly(TestPictures.Png(8, 8)), _ => true);
        Assert.Equal(2, second.Body.Blocks.OfType<Paragraph>().Count(p => Text(p).Length > 0));
        Assert.Equal("al￼ta", Text(P(second, 0)));
        Assert.Equal(result2.Caret.Paragraph, P(second, 0));
    }

    [Fact]
    public void OnePasteIsOneUndoStep()
    {
        var document = Doc(Line("alpha"), Line("beta"), Line("gamma"));
        var editor = Editor(document);
        var before = Snapshot(document);
        var revision = document.Edits.Revision;

        editor.Paste(new TextRange(At(document, 0, 2), At(document, 1, 2)), ImageOnly(TestPictures.Png(8, 8)), _ => true);

        Assert.Equal(1, editor.History.UndoCount);
        var after = Snapshot(document);
        Assert.NotEqual(before, after);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, Snapshot(document));

        // A caret paste is one step as well.
        var other = Doc(Line("alpha"));
        var otherEditor = Editor(other);
        otherEditor.Paste(Caret(other, 0, 1), ImageOnly(TestPictures.Png(8, 8)), _ => true);
        Assert.Equal(1, otherEditor.History.UndoCount);
    }

    [Fact]
    public void AnImageWithTextPastesTheText()
    {
        var document = Doc(Line("alpha"));
        var editor = Editor(document);

        editor.Paste(Caret(document, 0, 2), new ClipboardContent("XY", null, Image: TestPictures.Png(8, 8)), _ => true);

        Assert.Equal("alXYpha", Text(P(document, 0)));
        Assert.Empty(PicturesOf(P(document, 0)));
        Assert.Empty(document.Media.Entries);
    }

    [Fact]
    public void OwnFragmentWinsOverAnImage()
    {
        var document = Doc(Para("<w:r><w:rPr><w:b/></w:rPr><w:t>Bold</w:t></w:r><w:r><w:t> tail</w:t></w:r>"), Line("end"));
        var editor = Editor(document);
        var copied = editor.Copy(Sel(document, 0, 0, 4));
        Assert.NotNull(copied.Fragment);

        editor.Paste(Caret(document, 1, 1), copied with { Image = TestPictures.Png(8, 8) }, _ => true);

        Assert.Equal("eBoldnd", Text(P(document, 1)));
        Assert.Empty(PicturesOf(P(document, 1)));
        Assert.Contains(P(document, 1).Inlines.OfType<Run>(), r => r.Properties.GetFlag(W + "b"));
        Assert.Empty(document.Media.Entries);
    }

    [Fact]
    public void TheSizeIsThePixelSizeAt96DpiCappedToTheTextWidth()
    {
        var document = Doc(Line("alpha"), Line("beta"));
        var editor = Editor(document);

        editor.Paste(Caret(document, 0, 0), ImageOnly(TestPictures.Png(40, 30)), _ => true);
        Assert.Equal(new EmuSize(40 * 9525, 30 * 9525), PictureView.Of(PicturesOf(P(document, 0)).Single())!.ExtentEmu);

        // Wider than the text: kept in proportion to the text width.
        editor.Paste(Caret(document, 1, 0), ImageOnly(TestPictures.PngHeaderOnly(2000, 1000)), _ => true);
        var info = PictureView.Of(PicturesOf(P(document, 1)).Single())!;
        Assert.Equal(468 * 12700, info.ExtentEmu.Width);
        Assert.Equal(234 * 12700, info.ExtentEmu.Height);

        // A 1 x 1 picture is a picture.
        editor.Paste(Caret(document, 1, 0), ImageOnly(TestPictures.Png(1, 1)), _ => true);
        Assert.Equal(2, PicturesOf(P(document, 1)).Count);
    }

    [Fact]
    public void AZeroByteOrTruncatedImagePastesNothingAndSaysWhy()
    {
        var document = Doc(Line("alpha"));
        var editor = Editor(document);
        var before = Snapshot(document);
        var png = TestPictures.Png(8, 8);
        byte[][] bad = [[], png[..10], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]];

        foreach (var bytes in bad)
        {
            Assert.Equal(PictureRefusal.NotAnImage, editor.RefusalForPastePicture(Sel(document, 0, 1, 3), bytes));
            Assert.Null(editor.PastePicture(Sel(document, 0, 1, 3), bytes));
            var result = editor.Paste(Sel(document, 0, 1, 3), ImageOnly(bytes), _ => true);
            Assert.Empty(result.Changes);
        }

        Assert.Equal(before, Snapshot(document));
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Empty(document.Media.Entries);
    }

    [Fact]
    public void AnImageInAHeaderFallsThroughToTextOrNothing()
    {
        var document = Doc(Line("body"));
        var story = new Story(new OoxmlRoot(W + "hdr", []));
        var run = new Run();
        run.Content.Add(new TextContent("head"));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(run);
        story.Blocks.Add(paragraph);
        document.HeadersFooters["rIdH"] = new HeaderFooterPart(story);
        var head = new StoryRef(StoryKind.HeaderFooter, "rIdH");
        var editor = Editor(document);
        var caret = new TextRange(new TextPosition(head, paragraph, 2), new TextPosition(head, paragraph, 2));
        var png = TestPictures.Png(8, 8);

        Assert.Equal(PictureRefusal.HeaderOrFooter, editor.RefusalForPastePicture(caret, png));

        // With text beside the picture the text goes in; without it nothing changes.
        editor.Paste(caret, new ClipboardContent("ZZ", null, Image: png), _ => true);
        Assert.Equal("heZZad", Text(paragraph));
        var before = Snapshot(document);
        var undo = editor.History.UndoCount;
        var result = editor.Paste(caret, ImageOnly(png), _ => true);
        Assert.Empty(result.Changes);
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(undo, editor.History.UndoCount);
        Assert.Empty(document.Media.Entries);
        Assert.Equal(PictureRefusal.PrefixDocument, editor.RefusalForPastePicture(Caret(document, 0, 0), png, prefixDocument: true));
    }

    [Fact]
    public void ADeletedNoteReferenceTakesItsNoteAndTheUndoBringsItBack()
    {
        var document = Doc(Para("<w:r><w:t>one two</w:t></w:r>"), Line("end"));
        var editor = Editor(document);
        Assert.NotNull(editor.InsertNote(At(document, 0, 3), NoteType.Footnote));
        var before = Snapshot(document);
        var notes = document.Footnotes!.Notes.Count;

        var result = editor.Paste(Sel(document, 0, 0, 5), ImageOnly(TestPictures.Png(8, 8)), _ => true);

        Assert.Single(result.Objects!.MediaAdded);
        Assert.Single(result.Objects.NotesRemoved);
        Assert.Equal(notes - 1, document.Footnotes!.Notes.Count);
        Assert.Equal(2, editor.History.UndoCount);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(notes, document.Footnotes!.Notes.Count);
    }

    [Fact]
    public void NoMessageHoldsBytes()
    {
        var document = Doc(Line("alpha"));
        var editor = Editor(document);
        var secret = new byte[] { 0x53, 0x45, 0x43, 0x52, 0x45, 0x54 };

        var refusal = editor.RefusalForPastePicture(Caret(document, 0, 1), secret);
        Assert.Equal(PictureRefusal.NotAnImage, refusal);
        var ex = Record.Exception(() => editor.Paste(Caret(document, 0, 1), ImageOnly(secret), _ => true));
        Assert.Null(ex);
        // The result says nothing but a code; no exception text, no name, no byte reaches a message.
        Assert.DoesNotContain("SECRET", refusal.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", Snapshot(document), StringComparison.Ordinal);
    }
}
