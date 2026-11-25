using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>What the editor writes for a floating picture is what the layout reads back: the wrap, the side, the distances, the position and the size.</summary>
public sealed class FloatRoundTripTests
{
    private static readonly byte[] s_png = TestPictures.Png(40, 30);

    private static (WordDocument Document, TextEditor Editor, ObjectId Id) Inserted()
    {
        var document = Document(Line("alpha") + Line("beta") + Line("gamma") + Sect());
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        Assert.NotNull(editor.InsertPicture(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 2), s_png, null));
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var run = paragraph.Inlines.OfType<Run>().First(r => r.Content.OfType<Picture>().Any());
        return (document, editor, ObjectIds.Of(StoryRef.Body, document.Body.Blocks, paragraph, run, run.Content.OfType<Picture>().Single())!);
    }

    private static Picture PictureOf(WordDocument document, ObjectId id) => ObjectIds.Find(document.Body.Blocks, id)!.Value.Picture;

    private static FloatSpec? Read(WordDocument document, ObjectId id) =>
        FloatReader.FromDrawing(PictureOf(document, id).Raw, new ImageProbe(_ => s_png), "document");

    [Fact]
    public void TheLayoutReadsBackExactlyWhatTheEditorWrote()
    {
        foreach (var (wrap, expected, behind) in new[]
        {
            (PictureWrap.Square, FloatWrap.Square, false),
            (PictureWrap.TopAndBottom, FloatWrap.TopBottom, false),
            (PictureWrap.InFront, FloatWrap.None, false),
            (PictureWrap.Behind, FloatWrap.None, true),
        })
        {
            var (document, editor, id) = Inserted();
            Assert.NotNull(editor.ResizePicture(id, 25 * 12700, 10 * 12700));
            Assert.Null(Read(document, id));
            Assert.NotNull(editor.SetPictureWrap(id, wrap));

            var spec = Read(document, id)!;

            Assert.Equal(FloatKind.Image, spec.Kind);
            Assert.Equal(25f, spec.Width);
            Assert.Equal(10f, spec.Height);
            Assert.Equal(expected, spec.Wrap);
            Assert.Equal(behind, spec.Behind);
            Assert.Equal(WrapSide.Both, spec.Side);
            Assert.Equal((0f, 0f, 9f, 9f), (spec.DistanceTop, spec.DistanceBottom, spec.DistanceLeft, spec.DistanceRight));
            Assert.Equal((HorizontalRelative.Column, 0f, FloatAlign.None), (spec.HorizontalFrom, spec.HorizontalOffset, spec.HorizontalAlign));
            Assert.Equal((VerticalRelative.Paragraph, 0f, FloatAlign.None), (spec.VerticalFrom, spec.VerticalOffset, spec.VerticalAlign));
            Assert.NotNull(spec.Image);
        }
    }

    [Fact]
    public void TheOffsetsOfAMoveAndANudgeAreReadBackInPoints()
    {
        var (document, editor, id) = Inserted();
        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Square));

        Assert.NotNull(editor.NudgePicture(id, 12, -3));
        var nudged = Read(document, id)!;
        Assert.Equal((12f, -3f), (nudged.HorizontalOffset, nudged.VerticalOffset));

        var moved = editor.MovePicture(id, new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[2], 0), 50 * 12700, 7 * 12700);
        Assert.NotNull(moved);
        var movedId = editor.PictureAt(moved.Caret)!;
        var spec = Read(document, movedId)!;
        Assert.Equal((50f, 7f), (spec.HorizontalOffset, spec.VerticalOffset));
        Assert.Equal(FloatWrap.Square, spec.Wrap);
    }

    [Fact]
    public async Task ALaidOutDocumentPlacesTheFloatAtItsParagraphAndNamesItWithTheEditorsId()
    {
        var (document, editor, id) = Inserted();
        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Square));
        Assert.NotNull(editor.NudgePicture(id, 100, 20));

        var layout = Laid(document);
        await layout.LayOutRestAsync(null, CancellationToken.None);

        var box = Assert.Single(layout.Page(0).Objects.Pictures);
        Assert.Equal(id, box.Id);
        Assert.True(box.Floating);
        Assert.True(box.Resizable);
        Assert.Equal(30f, box.Width, 0.01f);
    }
}
