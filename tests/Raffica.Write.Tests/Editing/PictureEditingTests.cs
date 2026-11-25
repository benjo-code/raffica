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
        var document = Doc(Line("alpha"), Line("beta"));
        var editor = Editor(document);
        var marker = "BYTES-MARKER-0815"u8.ToArray();
        var withMarker = TestPictures.Png(4, 4).Concat(marker).ToArray();
        var messages = new List<string>();

        void Collect(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or StalePositionException)
            {
                messages.Add(ex.Message);
            }
        }

        Assert.NotNull(editor.InsertPicture(At(document, 0, 0), withMarker, Secret));
        var id = IdOf(document, 0);
        Collect(() => editor.SetAltText(id, null!));
        Collect(() => editor.MovePicture(id, new TextPosition(StoryRef.Body, new Paragraph(), 0), 0, 0));
        Collect(() => editor.InsertPicture(new TextPosition(StoryRef.Body, new Paragraph(), 0), withMarker, Secret));
        Collect(() => editor.ResizePicture(null!, 1, 1));
        Collect(() => document.Media.Add(withMarker, "application/x-bogus"));
        Collect(() => document.Media.Add(Array.Empty<byte>(), "image/png"));
        foreach (var refusal in Enum.GetValues<PictureRefusal>())
        {
            messages.Add(refusal.ToString());
        }

        Assert.NotEmpty(messages);
        foreach (var message in messages)
        {
            Assert.DoesNotContain("BYTES-MARKER", message, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, message, StringComparison.Ordinal);
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
}
