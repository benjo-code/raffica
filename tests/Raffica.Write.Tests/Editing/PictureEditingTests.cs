using System.IO.Compression;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Insert, resize, describe, wrap, move, nudge, reset and delete a picture: the XML each one writes, the refusals, the history (one step each) and what a save writes.</summary>
public sealed class PictureEditingTests
{
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    private const string Namespaces = "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";

    private const string Secret = "SECRET-ALT-4711";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private sealed class FakeTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ticks;

        public void Advance(int milliseconds) => _ticks += milliseconds;
    }

    private static TextEditor Editor(WordDocument document, TimeProvider? time = null) => new(document, (_, _) => ProjectionHost.Plain, time);

    private static WordDocument Doc(params string[] blocks) => Document(string.Concat(blocks) + Sect());

    private static TextPosition At(WordDocument document, int block, int offset) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    // A drawing as a file would hold it: an inline picture with a crop, an effect extent and a child nobody knows.
    private static string InlinePicture(int id = 7, string extra = "", string xfrm = "<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1000\" cy=\"2000\"/></a:xfrm>", string crop = "<a:srcRect l=\"10000\" t=\"0\" r=\"10000\" b=\"0\"/>") =>
        $"<w:r><w:drawing {Namespaces}><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"1000\" cy=\"2000\"/><wp:effectExtent l=\"1\" t=\"2\" r=\"3\" b=\"4\"/><wp:docPr id=\"{id}\" name=\"Picture {id}\"/><wp:cNvGraphicFramePr/>"
        + $"<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"x\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rId9\"/>{crop}<a:stretch/></pic:blipFill><pic:spPr>{xfrm}<a:prstGeom prst=\"rect\"/></pic:spPr></pic:pic></a:graphicData></a:graphic>{extra}</wp:inline></w:drawing></w:r>";

    private static string AnchoredPicture(int id, long relativeHeight, string position = "<wp:positionH relativeFrom=\"page\"><wp:posOffset>100</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>200</wp:posOffset></wp:positionV>") =>
        $"<w:r><w:drawing {Namespaces}><wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"{relativeHeight}\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\"><wp:simplePos x=\"0\" y=\"0\"/>{position}<wp:extent cx=\"1000\" cy=\"2000\"/><wp:wrapSquare wrapText=\"bothSides\"/><wp:docPr id=\"{id}\" name=\"Picture {id}\"/>"
        + "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"x\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rId9\"/></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1000\" cy=\"2000\"/></a:xfrm></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>";

    private static List<Picture> PicturesOf(Paragraph paragraph) => [.. paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>()];

    private static ObjectId IdOf(WordDocument document, int blockIndex, int ordinal = 0)
    {
        var paragraph = (Paragraph)document.Body.Blocks[blockIndex];
        var picture = PicturesOf(paragraph)[ordinal];
        var run = paragraph.Inlines.OfType<Run>().First(r => r.Content.Contains(picture));
        return ObjectIds.Of(StoryRef.Body, document.Body.Blocks, paragraph, run, picture)!;
    }

    private static PictureInfo Info(WordDocument document, ObjectId id) => PictureView.Of(ObjectIds.Find(document.Body.Blocks, id)!.Value.Picture)!;

    private static XElement Raw(WordDocument document, ObjectId id) => ObjectIds.Find(document.Body.Blocks, id)!.Value.Picture.Raw;

    private static string Snapshot(WordDocument document) => ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);

    // An inline picture inserted by the editor into paragraph 0 at offset 2.
    private static (WordDocument Document, TextEditor Editor, ObjectId Id) WithInserted(string extraBlocks = "", TimeProvider? time = null)
    {
        var document = Doc(Line("alpha"), Line("beta"), Line("gamma"), extraBlocks);
        var editor = Editor(document, time);
        Assert.NotNull(editor.InsertPicture(At(document, 0, 2), TestPictures.Png(40, 30), null));
        return (document, editor, IdOf(document, 0));
    }

    // ---- insert ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ThePositionAfterAPictureAndThePictureASelectionNamesFollowFromTheTextOffsets()
    {
        var (document, editor, id) = WithInserted();
        var paragraph = (Paragraph)document.Body.Blocks[0];

        // The picture is the one character at offset 2; the caret goes right behind it.
        var after = editor.PositionAfterPicture(id);
        Assert.Equal(new TextPosition(StoryRef.Body, paragraph, 3), after);
        Assert.Null(editor.PositionAfterPicture(new ObjectId(StoryRef.Body, 0, 99, 0)));

        // Exactly the one character of the picture covers it, in either direction; a caret, a longer range and a range of other text do not.
        Assert.Equal(id, editor.PictureOfSelection(new TextRange(At(document, 0, 2), At(document, 0, 3))));
        Assert.Equal(id, editor.PictureOfSelection(new TextRange(At(document, 0, 3), At(document, 0, 2))));
        Assert.Null(editor.PictureOfSelection(new TextRange(At(document, 0, 2), At(document, 0, 2))));
        Assert.Null(editor.PictureOfSelection(new TextRange(At(document, 0, 1), At(document, 0, 3))));
        Assert.Null(editor.PictureOfSelection(new TextRange(At(document, 0, 0), At(document, 0, 2))));
        Assert.Null(editor.PictureOfSelection(new TextRange(At(document, 0, 2), At(document, 1, 1))));
    }

    [Fact]
    public void AFloatingPictureHasNoCharacterSoTheOnlyOneOfTheParagraphOfTheSelectionIsTheTarget()
    {
        var document = Doc("<w:p><w:r><w:t>text</w:t></w:r>" + AnchoredPicture(3, 5) + "</w:p>", "<w:p><w:r><w:t>two</w:t></w:r>" + AnchoredPicture(4, 6) + AnchoredPicture(5, 7) + "</w:p>", Line("plain"));
        var editor = Editor(document);

        // Anywhere in a paragraph that anchors exactly one floating picture, a caret included, names it.
        var one = IdOf(document, 0);
        Assert.Equal(one, editor.PictureOfSelection(new TextRange(At(document, 0, 1), At(document, 0, 1))));
        Assert.Equal(one, editor.PictureOfSelection(new TextRange(At(document, 0, 0), At(document, 0, 2))));

        // Two of them are ambiguous, a paragraph with none names nothing.
        Assert.Null(editor.PictureOfSelection(new TextRange(At(document, 1, 1), At(document, 1, 1))));
        Assert.Null(editor.PictureOfSelection(new TextRange(At(document, 2, 1), At(document, 2, 1))));
    }

    [Fact]
    public void InsertWritesAnInlineDrawingWithAUniqueDocPrId()
    {
        var document = Doc(Line("alpha"), Para(InlinePicture(7)), Line("gamma"));
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        var result = editor.InsertPicture(At(document, 0, 2), TestPictures.Png(40, 30), "a cat");

        Assert.NotNull(result);
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var picture = Assert.Single(PicturesOf(paragraph));
        var info = PictureView.Of(picture)!;
        Assert.Equal(PictureWrap.Inline, info.Wrap);
        Assert.Equal(new EmuSize(40 * 9525, 30 * 9525), info.ExtentEmu);
        Assert.Equal("a cat", info.AltText);
        Assert.Equal("8", (string?)picture.Raw.Element(Wp + "inline")!.Element(Wp + "docPr")!.Attribute("id"));
        Assert.Equal("a cat", (string?)picture.Raw.Descendants(Pic + "cNvPr").Single().Attribute("descr"));
        Assert.DoesNotContain("alpha", picture.Raw.ToString());
        Assert.Equal("al￼pha", ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text);

        // The picture is in the store under the relationship the drawing names, and the result says so.
        var media = Assert.Single(result.Objects!.MediaAdded);
        Assert.Equal(media.RelationshipId, info.Media);
        Assert.Equal("image/png", media.ContentType);
        Assert.Equal(TestPictures.Png(40, 30), document.Media.TryGet(media.RelationshipId)!.Value.ToArray());
        Assert.Equal(new TextPosition(StoryRef.Body, paragraph, 3), result.Caret);
        Assert.True(document.Edits.Revision > revision);
        Assert.Equal(IdOf(document, 0), editor.PictureAt(At(document, 0, 2)));
        Assert.Null(editor.PictureAt(At(document, 0, 1)));

        // The next one is above every id of the document, in any paragraph.
        Assert.NotNull(editor.InsertPicture(At(document, 2, 0), TestPictures.Png(10, 10), null));
        Assert.Equal("9", (string?)PicturesOf((Paragraph)document.Body.Blocks[2]).Single().Raw.Element(Wp + "inline")!.Element(Wp + "docPr")!.Attribute("id"));
    }

    [Fact]
    public void InsertFitsTheTextWidthKeepingTheAspectRatioAndRoundTripsThroughTheXml()
    {
        var document = Doc(Line("alpha"));
        var editor = Editor(document);

        Assert.NotNull(editor.InsertPicture(At(document, 0, 0), TestPictures.PngHeaderOnly(2000, 1000), null));

        var info = Info(document, IdOf(document, 0));
        Assert.Equal(468 * 12700, info.ExtentEmu.Width);
        Assert.Equal(234 * 12700, info.ExtentEmu.Height);
        var xml = Snapshot(document);
        var reread = XElement.Parse(xml);
        Assert.Contains("<a:blip", reread.ToString());
        Assert.NotNull(XDocument.Parse(xml));
    }

    [Fact]
    public void InsertReplacesNothingAndAnUndoPutsTheParagraphBack()
    {
        var document = Doc(Line("alpha"));
        var editor = Editor(document);
        var before = Snapshot(document);
        var revision = document.Edits.Revision;

        var inserted = editor.InsertPicture(At(document, 0, 5), TestPictures.Png(4, 4), null);

        Assert.NotNull(inserted);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.NotEqual(before, Snapshot(document));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.NotNull(editor.History.Redo());
        Assert.Single(PicturesOf((Paragraph)document.Body.Blocks[0]));
    }

    [Fact]
    public void InsertInATableCellIsInlineAndAllowed()
    {
        var cell = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = Doc(Line("alpha"), cell, Line("after"));
        var table = (Table)document.Body.Blocks[1];
        var cellParagraph = ((TableCell)((TableRow)table.Rows[0]).Items[0]).Blocks.OfType<Paragraph>().Single();

        var result = Editor(document).InsertPicture(new TextPosition(StoryRef.Body, cellParagraph, 1), TestPictures.Png(8, 8), "x");

        Assert.NotNull(result);
        Assert.Single(PicturesOf(cellParagraph));
    }

    [Fact]
    public void ADropInACellNoteHeaderOrFooterIsRefused()
    {
        var nested = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>deep</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/></w:tc></w:tr></w:tbl>";
        var plain = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var control = "<w:sdt><w:sdtPr><w:text/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>one</w:t></w:r></w:p></w:sdtContent></w:sdt>";
        var document = Doc(Line("alpha"), nested, plain, control, Para(AnchoredPicture(5, 10)));
        var editor = Editor(document);
        var png = TestPictures.Png(4, 4);
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var before = Snapshot(document);

        Assert.Equal(PictureRefusal.HeaderOrFooter, editor.RefusalForInsertPicture(new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId1"), paragraph, 0), png));
        Assert.Equal(PictureRefusal.Note, editor.RefusalForInsertPicture(new TextPosition(new StoryRef(StoryKind.Note, "1"), paragraph, 0), png));
        Assert.Equal(PictureRefusal.Comment, editor.RefusalForInsertPicture(new TextPosition(new StoryRef(StoryKind.Comment, "1"), paragraph, 0), png));
        Assert.Equal(PictureRefusal.PrefixDocument, editor.RefusalForInsertPicture(At(document, 0, 0), png, null, prefixDocument: true));
        Assert.Equal(PictureRefusal.NoPosition, editor.RefusalForInsertPicture(new TextPosition(StoryRef.Body, null!, 0), png));

        var innerTable = (Table)((TableCell)((TableRow)((Table)document.Body.Blocks[1]).Rows[0]).Items[0]).Blocks[0];
        var deepParagraph = ((TableCell)((TableRow)innerTable.Rows[0]).Items[0]).Blocks.OfType<Paragraph>().Single();
        Assert.Equal(PictureRefusal.NestedTable, editor.RefusalForInsertPicture(new TextPosition(StoryRef.Body, deepParagraph, 0), png));

        var restricted = (Paragraph)((ContentControlBlock)document.Body.Blocks[3]).Content[0];
        Assert.Equal(PictureRefusal.RestrictedControl, editor.RefusalForInsertPicture(new TextPosition(StoryRef.Body, restricted, 0), png));
        Assert.Null(editor.InsertPicture(new TextPosition(StoryRef.Body, restricted, 0), png, null));

        // A floating picture is not made in a cell, not moved into one and not nudged there.
        var cellParagraph = ((TableCell)((TableRow)((Table)document.Body.Blocks[2]).Rows[0]).Items[0]).Blocks.OfType<Paragraph>().Single();
        var floating = IdOf(document, 4);
        Assert.Equal(PictureRefusal.FloatInCell, editor.RefusalForMovePicture(floating, new TextPosition(StoryRef.Body, cellParagraph, 0)));
        Assert.Equal(PictureRefusal.AnotherPlace, editor.RefusalForMovePicture(floating, new TextPosition(StoryRef.Body, restricted, 0)));

        var inCell = editor.InsertPicture(new TextPosition(StoryRef.Body, cellParagraph, 0), png, null);
        Assert.NotNull(inCell);
        var cellId = ObjectIds.Of(StoryRef.Body, document.Body.Blocks, cellParagraph, cellParagraph.Inlines.OfType<Run>().First(r => r.Content.OfType<Picture>().Any()), PicturesOf(cellParagraph)[0])!;
        Assert.Equal(PictureRefusal.FloatInCell, editor.RefusalForPictureWrap(cellId, PictureWrap.Square));
        Assert.Null(editor.SetPictureWrap(cellId, PictureWrap.Behind));
        Assert.Equal(PictureWrap.Inline, Info(document, cellId).Wrap);
        Assert.NotEqual(before, Snapshot(document));
    }

    // ---- bytes -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void OversizedOrLyingBytesAreRefusedWithoutAChange()
    {
        var document = Doc(Line("alpha"));
        var editor = Editor(document);
        var before = Snapshot(document);
        var revision = document.Edits.Revision;
        var big = new byte[60 * 1024 * 1024];
        TestPictures.Png(4, 4).CopyTo(big, 0);
        var wrongType = TestPictures.Png(4, 4).ToArray();
        wrongType[1] = 0;

        (byte[] Bytes, PictureRefusal Expected)[] cases =
        [
            (big, PictureRefusal.TooLarge),
            (TestPictures.PngHeaderOnly(10000, 10000), PictureRefusal.TooManyPixels),
            (TestPictures.PngHeaderOnly(8001, 4000), PictureRefusal.TooManyPixels),
            (TestPictures.Png(30, 30).AsSpan(0, 20).ToArray(), PictureRefusal.NotAnImage),
            ([], PictureRefusal.NotAnImage),
            (wrongType, PictureRefusal.NotAnImage),
            ("<html>not a picture</html>"u8.ToArray(), PictureRefusal.NotAnImage),
            (TestPictures.PngHeaderOnly(0, 5), PictureRefusal.NotAnImage),
        ];
        foreach (var (bytes, expected) in cases)
        {
            Assert.Equal(expected, editor.RefusalForInsertPicture(At(document, 0, 1), bytes));
            Assert.Null(editor.InsertPicture(At(document, 0, 1), bytes, null));
        }

        Assert.Equal(PictureRefusal.AltTextTooLong, editor.RefusalForInsertPicture(At(document, 0, 1), TestPictures.Png(4, 4), new string('x', 4097)));
        Assert.Null(editor.InsertPicture(At(document, 0, 1), TestPictures.Png(4, 4), new string('x', 4097)));
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.False(document.HasMedia && document.Media.Entries.Count > 0);
        Assert.Equal(0, editor.History.UndoCount);

        // One pixel and exactly 32,000,000 pixels pass.
        Assert.Null(editor.RefusalForInsertPicture(At(document, 0, 1), TestPictures.Png(1, 1)));
        Assert.Null(editor.RefusalForInsertPicture(At(document, 0, 1), TestPictures.PngHeaderOnly(8000, 4000)));
        Assert.NotNull(editor.InsertPicture(At(document, 0, 1), TestPictures.Png(1, 1), null));
    }

    [Fact]
    public void TheStoresTotalCapRefusesAnInsertWithoutAChange()
    {
        var document = Doc(Line("alpha"));
        var editor = Editor(document);
        var payload = new byte[MediaStore.MaxEntryBytes];
        TestPictures.Png(4, 4).CopyTo(payload, 0);
        var before = Snapshot(document);
        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(editor.InsertPicture(At(document, 0, 0), payload, null));
        }

        // 5 x 50 MiB is 250 MiB; one more would pass 256 MiB.
        var snapshot = Snapshot(document);
        Assert.Equal(PictureRefusal.TooMuchMedia, editor.RefusalForInsertPicture(At(document, 0, 0), payload));
        Assert.Null(editor.InsertPicture(At(document, 0, 0), payload, null));
        Assert.Equal(snapshot, Snapshot(document));
        Assert.NotEqual(before, snapshot);
    }

    // ---- size and alt text -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void AltTextIsStored()
    {
        var (document, editor, id) = WithInserted();

        Assert.NotNull(editor.SetAltText(id, "A <cat> & \"dog\""));

        Assert.Equal("A <cat> & \"dog\"", Info(document, id).AltText);
        Assert.Equal("A <cat> & \"dog\"", (string?)Raw(document, id).Descendants(Pic + "cNvPr").Single().Attribute("descr"));
        Assert.Contains("A &lt;cat&gt; &amp; &quot;dog&quot;", Raw(document, id).ToString());

        // A character XML cannot hold is dropped; an empty text removes the attribute exactly.
        Assert.NotNull(editor.SetAltText(id, "bad\u0001text"));
        Assert.Equal("badtext", Info(document, id).AltText);
        Assert.NotNull(editor.SetAltText(id, string.Empty));
        Assert.Null(Raw(document, id).Descendants(Wp + "docPr").Single().Attribute("descr"));
        Assert.Null(Raw(document, id).Descendants(Pic + "cNvPr").Single().Attribute("descr"));
        Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForAltText(id, string.Empty));
        Assert.Equal(PictureRefusal.AltTextTooLong, editor.RefusalForAltText(id, new string('a', 4097)));
        Assert.Null(editor.RefusalForAltText(id, new string('a', 4096)));
    }

    [Fact]
    public void ResizeKeepsTheCropAndEveryOtherChild()
    {
        var document = Doc(Para(InlinePicture(7, extra: "<wp:unknown foo=\"bar\"><wp:deep/></wp:unknown>")));
        var editor = Editor(document);
        var id = IdOf(document, 0);
        var before = Raw(document, id).ToString(SaveOptions.DisableFormatting);

        Assert.NotNull(editor.ResizePicture(id, 3000, 6000));

        var expected = before.Replace("cx=\"1000\" cy=\"2000\"", "cx=\"3000\" cy=\"6000\"", StringComparison.Ordinal);
        Assert.NotEqual(before, expected);
        Assert.Equal(expected, Raw(document, id).ToString(SaveOptions.DisableFormatting));
        var info = Info(document, id);
        Assert.Equal(new PictureCrop(10000, 0, 10000, 0), info.Crop);
        Assert.Equal(new EmuSize(3000, 6000), info.ExtentEmu);
        Assert.NotNull(Raw(document, id).Descendants(Wp + "unknown").SingleOrDefault());

        // The same size, zero, negative and above Word's largest side are refused.
        Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForResizePicture(id, 3000, 6000));
        Assert.Equal(PictureRefusal.BadSize, editor.RefusalForResizePicture(id, 0, 10));
        Assert.Equal(PictureRefusal.BadSize, editor.RefusalForResizePicture(id, 10, -1));
        Assert.Equal(PictureRefusal.BadSize, editor.RefusalForResizePicture(id, PictureRules.MaxExtentEmu + 1, 10));
        Assert.Null(editor.RefusalForResizePicture(id, PictureRules.MaxExtentEmu, 1));
        Assert.Equal(1, editor.History.UndoCount);
    }

    [Fact]
    public void PropertiesChangeSizeDescriptionAndWrapAsOneStepAndUndoTakesAllBack()
    {
        var document = Doc(Para(InlinePicture(7)), Line("after"));
        var editor = Editor(document);
        var id = IdOf(document, 0);
        var before = Snapshot(document);

        var result = editor.SetPictureProperties(id, new PictureProperties(WidthEmu: 3000, HeightEmu: 6000, AltText: "all at once", Wrap: PictureWrap.TopAndBottom));

        Assert.NotNull(result);
        Assert.True(result.KeepSelection);
        Assert.Equal(1, editor.History.UndoCount);
        var info = Info(document, id);
        Assert.Equal(new EmuSize(3000, 6000), info.ExtentEmu);
        Assert.Equal("all at once", info.AltText);
        Assert.Equal(PictureWrap.TopAndBottom, info.Wrap);
        Assert.Equal(new PictureCrop(10000, 0, 10000, 0), info.Crop);
        Assert.NotEqual(before, Snapshot(document));

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(new EmuSize(3000, 6000), Info(document, IdOf(document, 0)).ExtentEmu);
    }

    [Fact]
    public void PropertiesSkipWhatIsAlreadySoRefuseWhatTheRulesRefuseAndNeverLeakTheText()
    {
        var document = Doc(Para(InlinePicture(7)), Para(InlinePicture(8, xfrm: "<a:xfrm rot=\"5400000\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1000\" cy=\"2000\"/></a:xfrm>")));
        var editor = Editor(document);
        var id = IdOf(document, 0);
        var turned = IdOf(document, 1);

        // The same size and the same wrap are skipped; the description is the one change.
        Assert.NotNull(editor.SetPictureProperties(id, new PictureProperties(WidthEmu: 1000, HeightEmu: 2000, AltText: "only this", Wrap: PictureWrap.Inline)));
        Assert.Equal(new EmuSize(1000, 2000), Info(document, id).ExtentEmu);
        Assert.Equal(PictureWrap.Inline, Info(document, id).Wrap);
        Assert.Equal("only this", Info(document, id).AltText);

        // Nothing at all to change, an empty request and a part the picture has already: NothingToChange, no step.
        var steps = editor.History.UndoCount;
        Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForSetPictureProperties(id, new PictureProperties()));
        Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForSetPictureProperties(id, new PictureProperties(1000, 2000, AltText: "only this", Wrap: PictureWrap.Inline)));
        Assert.Null(editor.SetPictureProperties(id, new PictureProperties()));
        Assert.Equal(steps, editor.History.UndoCount);

        // One part the rules refuse refuses the whole call: nothing changes, not even the description.
        var before = Snapshot(document);
        Assert.Equal(PictureRefusal.Rotated, editor.RefusalForSetPictureProperties(turned, new PictureProperties(WidthEmu: 2000, AltText: Secret)));
        Assert.Null(editor.SetPictureProperties(turned, new PictureProperties(WidthEmu: 2000, AltText: Secret)));
        Assert.Equal(PictureRefusal.BadSize, editor.RefusalForSetPictureProperties(id, new PictureProperties(WidthEmu: 0, AltText: Secret)));
        Assert.Equal(PictureRefusal.AltTextTooLong, editor.RefusalForSetPictureProperties(id, new PictureProperties(AltText: new string('x', PictureRules.MaxAltTextLength + 1))));
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(steps, editor.History.UndoCount);

        // A turned picture takes a description alone (its size is the only thing the rule keeps).
        Assert.NotNull(editor.SetPictureProperties(turned, new PictureProperties(AltText: "turned")));
        Assert.Equal("turned", Info(document, turned).AltText);

        // A reset wins over a size and needs the bytes of the picture: without them it is refused as the plain reset is.
        Assert.Equal(PictureRefusal.OriginalSizeUnknown, editor.RefusalForSetPictureProperties(id, new PictureProperties(WidthEmu: 5, ResetSize: true)));
    }

    [Fact]
    public void ARotatedPictureRefusesResize()
    {
        var document = Doc(Para(InlinePicture(7, xfrm: "<a:xfrm rot=\"5400000\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1000\" cy=\"2000\"/></a:xfrm>")));
        var editor = Editor(document);
        var id = IdOf(document, 0);
        var before = Snapshot(document);
        var revision = document.Edits.Revision;

        Assert.Equal(PictureRefusal.Rotated, editor.RefusalForResizePicture(id, 3000, 6000));
        Assert.Null(editor.ResizePicture(id, 3000, 6000));
        Assert.Equal(PictureRefusal.Rotated, editor.RefusalForResetPictureSize(id));
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);

        // The other commands still apply to a rotated picture.
        Assert.NotNull(editor.SetAltText(id, "turned"));
    }

    [Fact]
    public void ResetPictureSizeGoesBackToThePixelsAtNinetySixDpiAndAcrossTheCrop()
    {
        var (document, editor, id) = WithInserted();
        Assert.NotNull(editor.ResizePicture(id, 5000, 7000));

        Assert.NotNull(editor.ResetPictureSize(id));

        Assert.Equal(new EmuSize(40 * 9525, 30 * 9525), Info(document, id).ExtentEmu);
        Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForResetPictureSize(id));

        // A crop of 10 percent each side of the width shows 80 percent of the original at that scale; the crop itself stays.
        var raw = Raw(document, id);
        raw.Descendants(Pic + "blipFill").Single().Element(A + "blip")!.AddAfterSelf(new XElement(A + "srcRect", new XAttribute("l", "10000"), new XAttribute("r", "10000")));
        Assert.NotNull(editor.ResizePicture(id, 5000, 7000));
        Assert.NotNull(editor.ResetPictureSize(id));
        Assert.Equal(new EmuSize((long)Math.Round(40 * 9525 * 0.8), 30 * 9525), Info(document, id).ExtentEmu);
        Assert.Equal(new PictureCrop(10000, 0, 10000, 0), Info(document, id).Crop);
    }

    [Fact]
    public void ResetPictureSizeReadsAPackagePictureThroughTheOwnersSource()
    {
        var document = Doc(Para(InlinePicture(7, crop: string.Empty)));
        var editor = Editor(document);
        var id = IdOf(document, 0);
        Assert.Equal(PictureRefusal.OriginalSizeUnknown, editor.RefusalForResetPictureSize(id));

        editor.PictureBytes = rid => rid == "rId9" ? TestPictures.Png(20, 10) : null;

        Assert.NotNull(editor.ResetPictureSize(id));
        Assert.Equal(new EmuSize(20 * 9525, 10 * 9525), Info(document, id).ExtentEmu);
    }

    // ---- wrap --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void InlineToFloatingWritesWordsDefaults()
    {
        var (document, editor, id) = WithInserted(Para(AnchoredPicture(50, 1000)));
        var inline = Raw(document, id).Element(Wp + "inline")!;
        var docPr = new XElement(inline.Element(Wp + "docPr")!);
        var graphic = new XElement(inline.Element(A + "graphic")!);
        var extent = new XElement(inline.Element(Wp + "extent")!);

        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Square));

        var anchor = Raw(document, id).Element(Wp + "anchor")!;
        Assert.Null(Raw(document, id).Element(Wp + "inline"));
        string?[] expectedAttributes = ["0", "0", "114300", "114300", "0", "1001", "0", "0", "1", "1"];
        string[] attributeNames = ["distT", "distB", "distL", "distR", "simplePos", "relativeHeight", "behindDoc", "locked", "layoutInCell", "allowOverlap"];
        Assert.Equal(expectedAttributes, attributeNames.Select(n => (string?)anchor.Attribute(n)).ToArray());
        Assert.Equal(["simplePos", "positionH", "positionV", "extent", "effectExtent", "wrapSquare", "docPr", "cNvGraphicFramePr", "graphic"], anchor.Elements().Select(e => e.Name.LocalName).ToArray());
        Assert.Equal("column", (string?)anchor.Element(Wp + "positionH")!.Attribute("relativeFrom"));
        Assert.Equal("0", anchor.Element(Wp + "positionH")!.Element(Wp + "posOffset")!.Value);
        Assert.Equal("paragraph", (string?)anchor.Element(Wp + "positionV")!.Attribute("relativeFrom"));
        Assert.Equal("bothSides", (string?)anchor.Element(Wp + "wrapSquare")!.Attribute("wrapText"));
        Assert.True(XNode.DeepEquals(docPr, anchor.Element(Wp + "docPr")));
        Assert.True(XNode.DeepEquals(graphic, anchor.Element(A + "graphic")));
        Assert.True(XNode.DeepEquals(extent, anchor.Element(Wp + "extent")));
        Assert.Equal(PictureWrap.Square, Info(document, id).Wrap);

        // Between the floating wraps only the wrap element and behindDoc change.
        var others = anchor.Elements().Where(e => !e.Name.LocalName.StartsWith("wrap", StringComparison.Ordinal)).Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray();
        foreach (var (wrap, name, behind) in new[] { (PictureWrap.TopAndBottom, "wrapTopAndBottom", "0"), (PictureWrap.InFront, "wrapNone", "0"), (PictureWrap.Behind, "wrapNone", "1"), (PictureWrap.Square, "wrapSquare", "0") })
        {
            Assert.NotNull(editor.SetPictureWrap(id, wrap));
            var now = Raw(document, id).Element(Wp + "anchor")!;
            Assert.Single(now.Elements(), e => e.Name.LocalName.StartsWith("wrap", StringComparison.Ordinal));
            Assert.NotNull(now.Element(Wp + name));
            Assert.Equal(behind, (string?)now.Attribute("behindDoc"));
            Assert.Equal(others, now.Elements().Where(e => !e.Name.LocalName.StartsWith("wrap", StringComparison.Ordinal)).Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray());
            Assert.Equal(wrap, Info(document, id).Wrap);
        }

        Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForPictureWrap(id, PictureWrap.Square));
    }

    [Fact]
    public void FloatingToInlineRestoresTheInlineForm()
    {
        var (document, editor, id) = WithInserted();
        var original = new XElement(Raw(document, id));

        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Behind));
        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Inline));

        Assert.True(XNode.DeepEquals(original, Raw(document, id)), "the inline drawing is not what it was\n" + original + "\n" + Raw(document, id));
        Assert.Equal(3, editor.History.UndoCount);
    }

    [Fact]
    public void AFloatIsAboveEveryOtherDrawingAndUnknownChildrenStay()
    {
        var document = Doc(Para(InlinePicture(7, extra: "<wp:keep a=\"1\"/>")), Para(AnchoredPicture(8, 4000)));
        var editor = Editor(document);
        var id = IdOf(document, 0);

        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.TopAndBottom));

        var anchor = Raw(document, id).Element(Wp + "anchor")!;
        Assert.Equal("4001", (string?)anchor.Attribute("relativeHeight"));
        Assert.Equal("a", anchor.Element(Wp + "keep")!.Attribute("a")!.Name.LocalName);
        Assert.Equal("graphic", anchor.Elements().ElementAt(anchor.Elements().Count() - 2).Name.LocalName);
        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Inline));
        Assert.NotNull(Raw(document, id).Element(Wp + "inline")!.Element(Wp + "keep"));
    }

    // ---- move and nudge --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void MoveRecomputesTheOffsetsAgainstTheNewAnchorParagraph()
    {
        var document = Doc(Para(AnchoredPicture(5, 10)), Line("beta"), Line("gamma"));
        var editor = Editor(document);
        var id = IdOf(document, 0);
        var before = Snapshot(document);
        var target = At(document, 2, 3);

        var result = editor.MovePicture(id, target, 123456, -654321);

        Assert.NotNull(result);
        Assert.Empty(PicturesOf((Paragraph)document.Body.Blocks[0]));
        var moved = Assert.Single(PicturesOf((Paragraph)document.Body.Blocks[2]));
        Assert.Same(moved, ((Paragraph)document.Body.Blocks[2]).Inlines.OfType<Run>().First().Content.OfType<Picture>().Single());
        var info = PictureView.Of(moved)!;
        Assert.Equal(new PictureAnchorInfo("page", 123456, "paragraph", -654321, 10, false), info.Anchor);
        Assert.Equal(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[2], 0), result.Caret);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.Equal("gamma", string.Concat(((Paragraph)document.Body.Blocks[2]).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().Select(t => t.Text)));

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Single(PicturesOf((Paragraph)document.Body.Blocks[2]));
        Assert.Empty(PicturesOf((Paragraph)document.Body.Blocks[0]));
    }

    [Fact]
    public void MoveReplacesAnAlignmentAndClampsTheOffsets()
    {
        var aligned = "<wp:positionH relativeFrom=\"margin\"><wp:align>center</wp:align></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>5</wp:posOffset></wp:positionV>";
        var document = Doc(Para(AnchoredPicture(5, 10, aligned)), Line("beta"));
        var editor = Editor(document);
        var id = IdOf(document, 0);

        Assert.NotNull(editor.MovePicture(id, At(document, 0, 0), 77, long.MaxValue));

        var anchor = Info(document, IdOf(document, 0)).Anchor!;
        Assert.Equal(("margin", 77L, "paragraph", PictureRules.MaxOffsetEmu), (anchor.HorizontalFrom, anchor.HorizontalOffsetEmu, anchor.VerticalFrom, anchor.VerticalOffsetEmu));
        Assert.Null(Raw(document, IdOf(document, 0)).Descendants(Wp + "align").FirstOrDefault());
    }

    [Fact]
    public void AnInlinePictureIsNotMovedOrNudged()
    {
        var (document, editor, id) = WithInserted();

        Assert.Equal(PictureRefusal.NotFloating, editor.RefusalForMovePicture(id, At(document, 1, 0)));
        Assert.Equal(PictureRefusal.NotFloating, editor.RefusalForNudgePicture(id, 1, 0));
        Assert.Null(editor.MovePicture(id, At(document, 1, 0), 0, 0));
        Assert.Null(editor.NudgePicture(id, 1, 0));
        Assert.Equal(1, editor.History.UndoCount);
    }

    [Fact]
    public void NudgeCoalescesPerSecond()
    {
        var time = new FakeTime();
        var document = Doc(Para(AnchoredPicture(5, 10)), Para(AnchoredPicture(6, 11)));
        var editor = Editor(document, time);
        var first = IdOf(document, 0);
        var second = IdOf(document, 1);

        Assert.NotNull(editor.NudgePicture(first, 1, 0));
        time.Advance(400);
        Assert.NotNull(editor.NudgePicture(first, 10, -2));
        time.Advance(400);
        Assert.NotNull(editor.NudgePicture(first, 0, 1));
        Assert.Equal(1, editor.History.UndoCount);
        var anchor = Info(document, first).Anchor!;
        Assert.Equal((100 + (11 * 12700), 200 + (-1 * 12700)), (anchor.HorizontalOffsetEmu, anchor.VerticalOffsetEmu));

        // A pause of a second starts a new step; so does another picture.
        time.Advance(1000);
        Assert.NotNull(editor.NudgePicture(first, 1, 0));
        Assert.Equal(2, editor.History.UndoCount);
        time.Advance(10);
        Assert.NotNull(editor.NudgePicture(second, 1, 0));
        Assert.Equal(3, editor.History.UndoCount);

        // Undo takes back the whole of the first second's nudges in one step.
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal((100 + (11 * 12700), 200 + (-1 * 12700)), (Info(document, first).Anchor!.HorizontalOffsetEmu, Info(document, first).Anchor!.VerticalOffsetEmu));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal((100L, 200L), (Info(document, first).Anchor!.HorizontalOffsetEmu, Info(document, first).Anchor!.VerticalOffsetEmu));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void ANudgeNeverJoinsTypingAndTypingNeverJoinsANudge()
    {
        var time = new FakeTime();
        var document = Doc(Para("<w:r><w:t>ab</w:t></w:r>" + AnchoredPicture(5, 10)));
        var editor = Editor(document, time);
        var id = IdOf(document, 0);
        var paragraph = (Paragraph)document.Body.Blocks[0];

        Assert.NotNull(editor.NudgePicture(id, 1, 0));
        time.Advance(100);
        editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, 0)), "x");
        time.Advance(100);
        Assert.NotNull(editor.NudgePicture(IdOf(document, 0), 1, 0));

        Assert.Equal(3, editor.History.UndoCount);
    }

    [Fact]
    public void ANudgeOfAnAlignedPictureAndOfNothingIsRefused()
    {
        var aligned = "<wp:positionH relativeFrom=\"margin\"><wp:align>left</wp:align></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>5</wp:posOffset></wp:positionV>";
        var document = Doc(Para(AnchoredPicture(5, 10, aligned)));
        var editor = Editor(document);
        var id = IdOf(document, 0);

        Assert.Equal(PictureRefusal.AlignedPosition, editor.RefusalForNudgePicture(id, 1, 0));
        Assert.Null(editor.RefusalForNudgePicture(id, 0, 3));
        Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForNudgePicture(id, 0, 0));
    }

    // ---- delete and stale ids ----------------------------------------------------------------------------------------------------

    [Fact]
    public void DeleteRemovesOnlyTheDrawingAndUndoBringsItBack()
    {
        var document = Doc(Para("<w:r><w:t>ab</w:t>" + InlinePicture().Replace("<w:r>", string.Empty, StringComparison.Ordinal).Replace("</w:r>", string.Empty, StringComparison.Ordinal) + "<w:t>cd</w:t></w:r>" + InlinePicture(9)));
        var editor = Editor(document);
        var paragraph = (Paragraph)document.Body.Blocks[0];
        Assert.Equal(2, PicturesOf(paragraph).Count);
        var before = Snapshot(document);
        var id = IdOf(document, 0, 0);

        var result = editor.DeletePicture(id);

        Assert.NotNull(result);
        Assert.Equal(new TextPosition(StoryRef.Body, paragraph, 2), result.Caret);
        Assert.Single(PicturesOf(paragraph));
        Assert.Equal("abcd￼", ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text);
        Assert.Equal(PictureRefusal.NoPicture, editor.RefusalForDeletePicture(new ObjectId(StoryRef.Body, 0, 99, 0)));

        // The run of the second picture held nothing else: it goes with it.
        Assert.NotNull(editor.DeletePicture(IdOf(document, 0, 0)));
        Assert.Empty(PicturesOf(paragraph));
        Assert.Equal(2, paragraph.Inlines.OfType<Run>().Count() + 1);
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
    }

    [Fact]
    public void AStaleIdIsRefusedAsNoPictureAndChangesNothing()
    {
        var (document, editor, id) = WithInserted();
        Assert.NotNull(editor.DeletePicture(id));
        var before = Snapshot(document);
        var revision = document.Edits.Revision;

        Assert.Equal(PictureRefusal.NoPicture, editor.RefusalForResizePicture(id, 10, 10));
        Assert.Null(editor.ResizePicture(id, 10, 10));
        Assert.Null(editor.SetAltText(id, "x"));
        Assert.Null(editor.SetPictureWrap(id, PictureWrap.Square));
        Assert.Null(editor.NudgePicture(id, 1, 1));
        Assert.Null(editor.ResetPictureSize(id));
        Assert.Null(editor.DeletePicture(id));
        Assert.Equal(PictureRefusal.NoPicture, editor.RefusalForPictureWrap(new ObjectId(new StoryRef(StoryKind.Body, null), 5000, 0, 0), PictureWrap.Behind));
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(PictureRefusal.HeaderOrFooter, editor.RefusalForDeletePicture(new ObjectId(new StoryRef(StoryKind.HeaderFooter, "rId1"), 0, 0, 0)));
    }

    [Fact]
    public void AnObjectThatIsNotAPlainPictureIsNoPicture()
    {
        var shape = "<w:r><w:drawing " + Namespaces + "><wp:inline><wp:extent cx=\"5\" cy=\"5\"/><wp:docPr id=\"3\" name=\"s\"/><a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"/></a:graphic></wp:inline></w:drawing></w:r>";
        var document = Doc(Para(shape));
        var editor = Editor(document);
        var id = new ObjectId(StoryRef.Body, 0, 0, 0);

        Assert.Equal(PictureRefusal.NoPicture, editor.RefusalForResizePicture(id, 10, 10));
        Assert.Null(editor.DeletePicture(id));
    }

    [Fact]
    public void NoMessageHoldsBytesNameOrAltText()
    {
        const string Alt = "SECRET-ALT-7f3a";
        const string Name = "SECRET-NAME-91";
        var marker = "SECRET-BYTES"u8.ToArray();
        var table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = Doc(Line("alpha"), Para(InlinePicture(7).Replace("Picture 7", Name, StringComparison.Ordinal)), Para(AnchoredPicture(8, 10).Replace("Picture 8", Name, StringComparison.Ordinal)), Line("gamma"), table, Line("after"));
        var editor = Editor(document);
        var seen = new List<string>();

        void Note(object? value)
        {
            if (value is not null and not EditResult)
            {
                seen.Add(value.ToString()!);
            }
        }

        T Observe<T>(Func<T> operation)
        {
            try
            {
                var result = operation();
                Note(result);
                return result;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                seen.Add(ex.ToString());
                seen.Add(ex.Message);
                return default!;
            }
        }

        void Throws<TException>(Action operation)
            where TException : Exception
        {
            var ex = Assert.ThrowsAny<TException>(operation);
            seen.Add(ex.ToString());
            seen.Add(ex.Message);
        }

        // Payloads: a picture with the marker in its data, a refused one of every kind that carries it, and the three pieces of text.
        var withMarker = TestPictures.Png(4, 4).Concat(marker).ToArray();
        var tooLarge = new byte[(51 * 1024 * 1024) + marker.Length];
        marker.CopyTo(tooLarge, tooLarge.Length - marker.Length);
        TestPictures.Png(4, 4).CopyTo(tooLarge, 0);
        byte[][] payloads =
        [
            withMarker,
            tooLarge,
            [.. TestPictures.PngHeaderOnly(10000, 10000), .. marker],
            [.. marker, .. marker],
            [.. TestPictures.Png(4, 4).AsSpan(0, 20), .. marker],
            [.. "GIF89a"u8, .. marker],
        ];
        var longAlt = Alt + new string('x', 4100);
        var body = document.Body.Blocks;
        var paragraph = (Paragraph)body[0];
        var cellParagraph = ((TableCell)((TableRow)((Table)body[4]).Rows[0]).Items[0]).Blocks.OfType<Paragraph>().Single();
        var stale = new Paragraph();
        var inline = IdOf(document, 1);
        var floating = IdOf(document, 2);
        var gone = new ObjectId(StoryRef.Body, 99, 0, 0);
        var header = new ObjectId(new StoryRef(StoryKind.HeaderFooter, "rId1"), 0, 0, 0);
        TextPosition[] places =
        [
            new TextPosition(StoryRef.Body, paragraph, 1),
            new TextPosition(StoryRef.Body, cellParagraph, 0),
            new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId1"), paragraph, 0),
            new TextPosition(new StoryRef(StoryKind.Note, "1"), paragraph, 0),
            new TextPosition(new StoryRef(StoryKind.Comment, "1"), paragraph, 0),
            new TextPosition(StoryRef.Body, null!, 0),
        ];

        // Every refusal path of the insert and its twin, with the secret alt text and with each payload.
        foreach (var alt in new string?[] { Alt, longAlt, null })
        {
            foreach (var payload in payloads)
            {
                foreach (var place in places)
                {
                    Observe(() => editor.RefusalForInsertPicture(place, payload, alt));
                    Observe(() => editor.RefusalForInsertPicture(place, payload, alt, prefixDocument: true));
                    if (!ReferenceEquals(payload, withMarker) || place.Paragraph is null || place.Story.Kind != StoryKind.Body)
                    {
                        Observe(() => editor.InsertPicture(place, payload, alt));
                    }
                }

                Observe(() => editor.InsertPicture(new TextPosition(StoryRef.Body, stale, 0), payload, alt));
                Assert.Equal(PictureRefusal.NoPosition, Observe(() => editor.RefusalForInsertPicture(new TextPosition(StoryRef.Body, stale, 0), payload, alt)));
            }
        }

        // A good insert (the marker is in the data) and every other operation, on a picture, a stale id, another story and a prefix document, with the alt text secret as input.
        Assert.NotNull(editor.InsertPicture(places[0], withMarker, Alt));
        var inserted = IdOf(document, 0);
        foreach (var id in new[] { inline, floating, inserted, gone, header })
        {
            foreach (var prefix in new[] { false, true })
            {
                Observe(() => editor.RefusalForResizePicture(id, 10, 10, prefix));
                Observe(() => editor.RefusalForResizePicture(id, -1, 10, prefix));
                Observe(() => editor.RefusalForAltText(id, longAlt, prefix));
                Observe(() => editor.RefusalForAltText(id, Alt, prefix));
                Observe(() => editor.RefusalForPictureWrap(id, PictureWrap.Behind, prefix));
                Observe(() => editor.RefusalForPictureWrap(id, (PictureWrap)99, prefix));
                Observe(() => editor.RefusalForNudgePicture(id, 1, 1, prefix));
                Observe(() => editor.RefusalForResetPictureSize(id, prefix));
                Observe(() => editor.RefusalForDeletePicture(id, prefix));
                Observe(() => editor.RefusalForMovePicture(id, places[1], prefix));
                Observe(() => editor.ResizePicture(id, -1, 10, prefix));
                Observe(() => editor.SetAltText(id, longAlt, prefix));
                Observe(() => editor.SetPictureWrap(id, (PictureWrap)99, prefix));
                Observe(() => editor.NudgePicture(id, 0, 0, prefix));
                Observe(() => editor.MovePicture(id, places[1], 1, 1, prefix));
                Observe(() => editor.ResetPictureSize(id, prefix));
            }

            Throws<ArgumentNullException>(() => editor.SetAltText(id, null!));
            Throws<ArgumentNullException>(() => editor.RefusalForAltText(id, null!));
        }

        Throws<StalePositionException>(() => editor.MovePicture(floating, new TextPosition(StoryRef.Body, stale, 0), 1, 1));
        Throws<ArgumentNullException>(() => editor.ResizePicture(null!, 1, 1));
        Observe(() => editor.SetAltText(inserted, Alt));
        Observe(() => editor.SetAltText(inserted, Alt));
        Observe(() => editor.RefusalForAltText(inserted, Alt));
        Observe(() => editor.DeletePicture(inline));
        Observe(() => editor.DeletePicture(inline));

        // The store refuses bad input with fixed messages.
        Throws<ArgumentException>(() => document.Media.Add(withMarker, "application/x-" + Name));
        Throws<ArgumentOutOfRangeException>(() => document.Media.Add(Array.Empty<byte>(), "image/png"));
        Throws<ArgumentOutOfRangeException>(() => document.Media.Add(tooLarge, "image/png"));
        foreach (var refusal in Enum.GetValues<PictureRefusal>())
        {
            seen.Add(refusal.ToString());
        }

        Assert.True(seen.Count > 300, $"only {seen.Count} results and messages were collected");
        string[] secrets =
        [
            Alt, Name, "SECRET-BYTES", "SECRET-ALT", "SECRET-NAME",
            Convert.ToBase64String(marker), Convert.ToHexString(marker), Convert.ToBase64String("SECRET-ALT-7f3a"u8),
        ];
        foreach (var message in seen)
        {
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---- what a save writes ----------------------------------------------------------------------------------------------------------

    private static List<string> MediaParts(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return [.. archive.Entries.Select(e => e.FullName).Where(n => n.StartsWith("word/media/", StringComparison.Ordinal))];
    }

    [Fact]
    public async Task UndoRemovesTheMediaFromTheNextSave()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = Editor(file.Model);
        var paragraph = file.Model.Body.Blocks.OfType<Paragraph>().First();

        Assert.NotNull(editor.InsertPicture(new TextPosition(StoryRef.Body, paragraph, 0), TestPictures.Png(6, 6), null));
        var first = folder.File("one.docx");
        await file.SaveAsync(first, Cancel);
        Assert.Single(MediaParts(first));

        Assert.NotNull(editor.History.Undo());
        var second = folder.File("two.docx");
        await file.SaveAsync(second, Cancel);
        Assert.Empty(MediaParts(second));

        // A redo finds its bytes in the store again.
        Assert.NotNull(editor.History.Redo());
        var third = folder.File("three.docx");
        await file.SaveAsync(third, Cancel);
        Assert.Single(MediaParts(third));

        // A deleted picture leaves no part either, and its undo brings the part back.
        var id = editor.PictureAt(new TextPosition(StoryRef.Body, file.Model.Body.Blocks.OfType<Paragraph>().First(), 0))!;
        Assert.NotNull(editor.DeletePicture(id));
        var fourth = folder.File("four.docx");
        await file.SaveAsync(fourth, Cancel);
        Assert.Empty(MediaParts(fourth));
        Assert.NotNull(editor.History.Undo());
        var fifth = folder.File("five.docx");
        await file.SaveAsync(fifth, Cancel);
        Assert.Single(MediaParts(fifth));
    }

    [Fact]
    public void EveryOperationIsOneHistoryStepThatUndoesToTheExactModel()
    {
        var (document, editor, id) = WithInserted();
        var steps = 1;
        var snapshots = new List<string> { Snapshot(document) };

        void Step(Func<EditResult?> operation)
        {
            Assert.NotNull(operation());
            steps++;
            Assert.Equal(steps, editor.History.UndoCount);
            snapshots.Add(Snapshot(document));
        }

        Step(() => editor.SetAltText(id, "alt"));
        Step(() => editor.ResizePicture(id, 4000, 3000));
        Step(() => editor.SetPictureWrap(id, PictureWrap.Square));
        Step(() => editor.SetPictureWrap(id, PictureWrap.Behind));
        Step(() => editor.ResetPictureSize(id));
        Step(() => editor.MovePicture(id, At(document, 2, 1), 500, 600));
        var movedId = editor.PictureAt(At(document, 2, 0))!;
        Step(() => editor.SetPictureWrap(movedId, PictureWrap.Inline));
        Step(() => editor.DeletePicture(movedId));

        for (var i = snapshots.Count - 2; i >= 0; i--)
        {
            Assert.NotNull(editor.History.Undo());
            Assert.Equal(snapshots[i], Snapshot(document));
        }

        for (var i = 1; i < snapshots.Count; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Assert.Equal(snapshots[i], Snapshot(document));
        }
    }

    // ---- the width of a cell ---------------------------------------------------------------------------------------------------------

    private static string CellTable(string tableProperties, string firstCellProperties = "", string secondCellProperties = "")
    {
        var cells = $"<w:tc>{(firstCellProperties.Length == 0 ? string.Empty : "<w:tcPr>" + firstCellProperties + "</w:tcPr>")}<w:p><w:r><w:t>narrow</w:t></w:r></w:p></w:tc>"
            + $"<w:tc>{(secondCellProperties.Length == 0 ? string.Empty : "<w:tcPr>" + secondCellProperties + "</w:tcPr>")}<w:p><w:r><w:t>wide</w:t></w:r></w:p></w:tc>";
        return $"<w:tbl><w:tblPr>{tableProperties}</w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"9000\"/></w:tblGrid><w:tr>{cells}</w:tr>"
            + "<w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p><w:r><w:t>both</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
    }

    private static Paragraph CellParagraph(WordDocument document, int row, int cell) =>
        ((TableCell)((TableRow)((Table)document.Body.Blocks[1]).Rows[row]).Items[cell]).Blocks.OfType<Paragraph>().Single();

    private static EmuSize InsertedSize(WordDocument document, Paragraph paragraph, byte[] png)
    {
        var editor = Editor(document);
        Assert.NotNull(editor.InsertPicture(new TextPosition(StoryRef.Body, paragraph, 0), png, null));
        var picture = Assert.Single(PicturesOf(paragraph));
        return PictureView.Of(picture)!.ExtentEmu;
    }

    // Twips to EMU: 635.
    [Fact]
    public void APictureInANarrowCellOfAFixedTableIsNoWiderThanTheCellContent()
    {
        // 2000 twips of grid, 108 each side by default: 1784 twips of content. The picture is 200 x 100 pixels (1,905,000 x 952,500 EMU).
        var document = Doc(Line("before"), CellTable("<w:tblLayout w:type=\"fixed\"/>"));

        var size = InsertedSize(document, CellParagraph(document, 0, 0), TestPictures.Png(200, 100));

        Assert.Equal(1784 * 635L, size.Width);
        Assert.Equal(1784 * 635L / 2, size.Height);
    }

    [Fact]
    public void ACellsOwnMarginsAndATablesDefaultMarginsAreTakenOffTheGridWidth()
    {
        var own = Doc(Line("before"), CellTable("<w:tblLayout w:type=\"fixed\"/>", "<w:tcMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"500\" w:type=\"dxa\"/></w:tcMar>"));
        Assert.Equal(1500 * 635L, InsertedSize(own, CellParagraph(own, 0, 0), TestPictures.Png(200, 100)).Width);

        var table = Doc(Line("before"), CellTable("<w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"200\" w:type=\"dxa\"/><w:right w:w=\"200\" w:type=\"dxa\"/></w:tblCellMar>"));
        Assert.Equal(1600 * 635L, InsertedSize(table, CellParagraph(table, 0, 0), TestPictures.Png(200, 100)).Width);
    }

    [Fact]
    public void APictureInAWideOrSpanningCellKeepsItsPixelSizeAndOneThatIsTooWideFitsTheSpan()
    {
        var wide = Doc(Line("before"), CellTable("<w:tblLayout w:type=\"fixed\"/>"));
        Assert.Equal(new EmuSize(200 * 9525, 100 * 9525), InsertedSize(wide, CellParagraph(wide, 0, 1), TestPictures.Png(200, 100)));

        // The spanning cell is 11,000 twips of grid, 10,784 of content, and the section's text area is 9360: the section is the limit.
        var span = Doc(Line("before"), CellTable("<w:tblLayout w:type=\"fixed\"/>"));
        Assert.Equal(9360 * 635L, InsertedSize(span, CellParagraph(span, 1, 0), TestPictures.Png(1000, 100)).Width);
    }

    [Fact]
    public void AnAutofitOrPercentageTableFallsBackToTheSectionTextWidth()
    {
        // The grid of such a table is advice (the columns follow the content), so a narrow grid column limits nothing.
        foreach (var properties in new[] { string.Empty, "<w:tblW w:w=\"5000\" w:type=\"pct\"/>", "<w:tblLayout w:type=\"autofit\"/>" })
        {
            var document = Doc(Line("before"), CellTable(properties));
            Assert.Equal(new EmuSize(200 * 9525, 100 * 9525), InsertedSize(document, CellParagraph(document, 0, 0), TestPictures.Png(200, 100)));
        }

        var big = Doc(Line("before"), CellTable(string.Empty));
        Assert.Equal(9360 * 635L, InsertedSize(big, CellParagraph(big, 0, 0), TestPictures.Png(2000, 100)).Width);
    }

    // ---- tight and through ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void ATightOrThroughWrapReadsAsSquareAndSquareCanBeWrittenOverIt()
    {
        foreach (var name in new[] { "wrapTight", "wrapThrough" })
        {
            var tight = AnchoredPicture(5, 10).Replace("<wp:wrapSquare wrapText=\"bothSides\"/>", $"<wp:{name} wrapText=\"left\" distL=\"100\" distR=\"200\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/><wp:lineTo x=\"1\" y=\"1\"/></wp:wrapPolygon></wp:{name}>", StringComparison.Ordinal);
            var document = Doc(Para(tight));
            var editor = Editor(document);
            var id = IdOf(document, 0);
            Assert.Equal(PictureWrap.Square, Info(document, id).Wrap);

            Assert.Null(editor.RefusalForPictureWrap(id, PictureWrap.Square));
            Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Square));

            var anchor = Raw(document, id).Element(Wp + "anchor")!;
            Assert.Null(anchor.Element(Wp + name));
            var square = Assert.Single(anchor.Elements(Wp + "wrapSquare"));
            Assert.Equal(["left", "100", "200"], [(string?)square.Attribute("wrapText"), (string?)square.Attribute("distL"), (string?)square.Attribute("distR")]);
            Assert.Empty(square.Elements());
            Assert.Equal(PictureRefusal.NothingToChange, editor.RefusalForPictureWrap(id, PictureWrap.Square));
            Assert.NotNull(editor.History.Undo());
            Assert.NotNull(Raw(document, id).Element(Wp + "anchor")!.Element(Wp + name));
        }
    }

    // ---- ids that hostile files hold ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AnIdOrHeightAtTheEndOfTheUnsignedRangeStillGivesAFreeIdAndAHeightInRange()
    {
        var document = Doc(Line("alpha"), Para(InlinePicture(7).Replace("id=\"7\"", "id=\"4294967295\"", StringComparison.Ordinal)), Para(AnchoredPicture(1, 1).Replace("relativeHeight=\"1\"", "relativeHeight=\"4294967295\"", StringComparison.Ordinal)),
            Para(AnchoredPicture(2, 5).Replace("id=\"2\"", "id=\"99999999999\"", StringComparison.Ordinal)));
        var editor = Editor(document);

        Assert.NotNull(editor.InsertPicture(At(document, 0, 0), TestPictures.Png(4, 4), null));

        var id = IdOf(document, 0);
        var docPr = (long)Raw(document, id).Descendants(Wp + "docPr").Single().Attribute("id")!;
        Assert.InRange(docPr, 1, uint.MaxValue - 1);
        var used = Enumerable.Range(1, 3).SelectMany(i => PicturesOf((Paragraph)document.Body.Blocks[i]).Select(p => (long)p.Raw.Descendants(Wp + "docPr").Single().Attribute("id")!)).ToList();
        Assert.DoesNotContain(docPr, used);
        Assert.Equal(2, docPr);

        Assert.NotNull(editor.SetPictureWrap(id, PictureWrap.Square));
        Assert.Equal(uint.MaxValue, (long)Raw(document, id).Element(Wp + "anchor")!.Attribute("relativeHeight")!);
    }

    [Fact]
    public void AnIdInAnUnknownRowItemIsCounted()
    {
        var wrapped = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:sdt><w:sdtContent><w:tr><w:tc><w:p>" + InlinePicture(41) + "</w:p></w:tc></w:tr></w:sdtContent></w:sdt><w:tr><w:tc><w:p><w:r><w:t>x</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = Doc(Line("alpha"), wrapped);
        Assert.Contains(((Table)document.Body.Blocks[1]).Rows, r => r is UnknownRowItem);
        var editor = Editor(document);

        Assert.NotNull(editor.InsertPicture(At(document, 0, 0), TestPictures.Png(4, 4), null));

        Assert.Equal("42", (string?)Raw(document, IdOf(document, 0)).Descendants(Wp + "docPr").Single().Attribute("id"));
    }

    // ---- a move across many blocks -----------------------------------------------------------------------------------------------------

    [Fact]
    public void AMoveOfTwentyThousandBlocksOrMoreIsTooFarAndOneLessIsAllowed()
    {
        var document = Doc(Line("start"), Para(AnchoredPicture(5, 10)));
        for (var i = 0; i < 20_005; i++)
        {
            document.Body.Blocks.Add(new Paragraph());
        }

        var editor = Editor(document);
        var id = IdOf(document, 1);
        var farthest = (Paragraph)document.Body.Blocks[1 + 20_000];
        var nearest = (Paragraph)document.Body.Blocks[1 + 19_999];

        Assert.Equal(PictureRefusal.TooFar, editor.RefusalForMovePicture(id, new TextPosition(StoryRef.Body, farthest, 0)));
        Assert.Null(editor.MovePicture(id, new TextPosition(StoryRef.Body, farthest, 0), 1, 1));
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Null(editor.RefusalForMovePicture(id, new TextPosition(StoryRef.Body, nearest, 0)));
        Assert.NotNull(editor.MovePicture(id, new TextPosition(StoryRef.Body, nearest, 0), 1, 1));
        Assert.Single(PicturesOf(nearest));
        Assert.NotNull(editor.History.Undo());
        Assert.Single(PicturesOf((Paragraph)document.Body.Blocks[1]));
    }
}
