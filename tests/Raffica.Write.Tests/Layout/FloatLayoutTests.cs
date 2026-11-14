using System.Globalization;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using SkiaSharp;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class FloatLayoutTests
{
    private const float Tolerance = 0.06f;
    private const float Emu = 12700f;

    private const string Namespaces =
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";

    private static string Emus(float points) => ((long)(points * Emu)).ToString(CultureInfo.InvariantCulture);

    private static string Blip(string id) =>
        $"<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip r:embed=\"{id}\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic>";

    /// <summary>A run with an inline picture of the stated size in points.</summary>
    internal static string InlinePicture(float width, float height, string id = "rId1") =>
        $"<w:r><w:drawing {Namespaces}><wp:inline><wp:extent cx=\"{Emus(width)}\" cy=\"{Emus(height)}\"/><wp:docPr id=\"1\" name=\"p\"/>{Blip(id)}</wp:inline></w:drawing></w:r>";

    /// <summary>A run with an anchored picture. Offsets are points; <paramref name="wrap"/> is the wrap element (for example <c>wrapSquare</c>).</summary>
    internal static string Anchored(
        float width, float height, string wrap = "<wp:wrapSquare wrapText=\"bothSides\"/>", string hFrom = "column", float hOffset = 0f, string vFrom = "paragraph",
        float vOffset = 0f, string id = "rId1", bool behind = false, string? hAlign = null, string? vAlign = null, float distance = 0f)
    {
        var h = hAlign is null ? $"<wp:posOffset>{Emus(hOffset)}</wp:posOffset>" : $"<wp:align>{hAlign}</wp:align>";
        var v = vAlign is null ? $"<wp:posOffset>{Emus(vOffset)}</wp:posOffset>" : $"<wp:align>{vAlign}</wp:align>";
        var d = Emus(distance);
        return $"<w:r><w:drawing {Namespaces}><wp:anchor distT=\"{d}\" distB=\"{d}\" distL=\"{d}\" distR=\"{d}\" behindDoc=\"{(behind ? 1 : 0)}\">" +
            $"<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"{hFrom}\">{h}</wp:positionH><wp:positionV relativeFrom=\"{vFrom}\">{v}</wp:positionV>" +
            $"<wp:extent cx=\"{Emus(width)}\" cy=\"{Emus(height)}\"/>{wrap}<wp:docPr id=\"2\" name=\"f\"/>{Blip(id)}</wp:anchor></w:drawing></w:r>";
    }

    private static string Text(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string Para(params string[] runs) => $"<w:p>{string.Concat(runs)}</w:p>";

    private static string Words(int count) => string.Join(' ', Enumerable.Range(1, count).Select(i => "word" + i.ToString(CultureInfo.InvariantCulture)));

    private static DocumentLayout Lay(string body, LayoutOptions? options = null) =>
        Finish(DocumentLayout.Create(LayoutModel.Document(body + Sect()), Fonts, options ?? new LayoutOptions()));

    // The relationship id without the story: ids are handed out as "story#rId".
    private static string Local(string id) => id[(id.IndexOf('#', StringComparison.Ordinal) + 1)..];

    private static List<ImageItem> Images(LaidOutPage page) => page.Items.OfType<ImageItem>().ToList();

    private static List<PlaceholderItem> Placeholders(LaidOutPage page) => page.Items.OfType<PlaceholderItem>().ToList();

    // The leftmost glyph run start of each text line, by baseline.
    private static List<(float Baseline, float Left)> LineStarts(LaidOutPage page) =>
        Runs(page).GroupBy(r => r.Baseline).OrderBy(g => g.Key).Select(g => (g.Key, g.Min(r => r.X))).ToList();

    private static byte[] Png(int width = 4, int height = 4)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] Emf()
    {
        var bytes = new byte[100];
        bytes[0] = 1;
        bytes[40] = 0x20;
        bytes[41] = 0x45;
        bytes[42] = 0x4D;
        bytes[43] = 0x46;
        return bytes;
    }

    private static byte[] Wmf() => [0xD7, 0xCD, 0xC6, 0x9A, 0, 0, 0, 0, 0, 0, 0x10, 0x10, 0, 0, 0, 0, 0, 0];

    // A run with a floating text box (mc:AlternateContent with a wps text box and an empty VML fallback): 183 x 100 pt, yellow, two paragraphs.
    private static string TextBoxRun()
    {
        return $"<w:r><mc:AlternateContent {Namespaces}><mc:Choice Requires=\"wps\"><w:drawing><wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" behindDoc=\"0\">" +
            "<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"column\"><wp:posOffset>228600</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
            "<wp:extent cx=\"2324100\" cy=\"1270000\"/><wp:wrapNone/><wp:docPr id=\"2\" name=\"TextBox 1\"/><a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:wsp><wps:spPr><a:solidFill><a:srgbClr val=\"FFFF00\"/></a:solidFill></wps:spPr><wps:txbx><w:txbxContent><w:p><w:r><w:t>Floating text box</w:t></w:r></w:p><w:p><w:r><w:t>second line</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp>" +
            "</a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice><mc:Fallback><w:pict/></mc:Fallback></mc:AlternateContent></w:r>";
    }

    [Fact]
    public void InlinePictureUsesExtentAndScalesToTheTextWidth()
    {
        var layout = Lay(Para(InlinePicture(100f, 50f, "a")) + Para(InlinePicture(936f, 468f, "b")));
        var images = Images(layout.Page(0));
        Assert.Equal(2, images.Count);
        Assert.Equal("document#a", images[0].Image.Id);
        Assert.Equal(100f, images[0].Width, Tolerance);
        Assert.Equal(50f, images[0].Height, Tolerance);
        Assert.Equal(72f, images[0].X, Tolerance);

        // Wider than the 468 pt text area: shrunk to it, aspect ratio kept.
        Assert.Equal("document#b", images[1].Image.Id);
        Assert.Equal(468f, images[1].Width, Tolerance);
        Assert.Equal(234f, images[1].Height, Tolerance);
        Assert.Equal(72f, images[1].X, Tolerance);
    }

    [Fact]
    public void FloatingPictureSquareWrapNarrowsTheLines()
    {
        var layout = Lay(Para(Anchored(200f, 100f), Text(Words(120))));
        var page = layout.Page(0);
        var picture = Assert.Single(Images(page));
        Assert.Equal(72f, picture.X, Tolerance);
        Assert.Equal(72f, picture.Y, Tolerance);
        Assert.Equal(200f, picture.Width, Tolerance);
        var starts = LineStarts(page);
        var beside = starts.Where(s => s.Baseline < picture.Y + picture.Height).ToList();
        var below = starts.Where(s => s.Baseline > picture.Y + picture.Height + LineHeight()).ToList();
        Assert.True(beside.Count >= 3);
        Assert.NotEmpty(below);
        Assert.All(beside, s => Assert.True(s.Left >= 272f - Tolerance, $"line at {s.Baseline} starts at {s.Left}"));
        Assert.All(below, s => Assert.Equal(72f, s.Left, Tolerance));
        Assert.Contains("word120", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapDistancesWidenTheClearance()
    {
        var layout = Lay(Para(Anchored(200f, 100f, distance: 9f), Text(Words(120))));
        var page = layout.Page(0);
        var picture = Assert.Single(Images(page));
        var beside = LineStarts(page).Where(s => s.Baseline < picture.Y + picture.Height).ToList();
        Assert.All(beside, s => Assert.True(s.Left >= 281f - Tolerance));
    }

    [Fact]
    public void TopAndBottomWrapMovesTextBelow()
    {
        var layout = Lay(Para(Anchored(100f, 80f, "<wp:wrapTopAndBottom/>"), Text("Hello world")) + Para(Text("next")));
        var page = layout.Page(0);
        var picture = Assert.Single(Images(page));
        var baselines = Baselines(page);
        Assert.True(baselines[0] > picture.Y + picture.Height, $"first baseline {baselines[0]} is not below the picture bottom {picture.Y + picture.Height}");
        Assert.Equal(72f, LineStarts(page)[0].Left, Tolerance);
        Assert.Equal(["Hello world", "next"], Lines(page));
    }

    [Theory]
    [InlineData("<wp:wrapTight wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapTight>")]
    [InlineData("<wp:wrapThrough wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapThrough>")]
    public void TightAndThroughAreDrawnAsSquare(string wrap)
    {
        var square = LineStarts(Lay(Para(Anchored(200f, 100f), Text(Words(120)))).Page(0));
        var other = LineStarts(Lay(Para(Anchored(200f, 100f, wrap), Text(Words(120)))).Page(0));
        Assert.Equal(square.Count, other.Count);
        for (var i = 0; i < square.Count; i++)
        {
            Assert.Equal(square[i].Left, other[i].Left, Tolerance);
            Assert.Equal(square[i].Baseline, other[i].Baseline, Tolerance);
        }
    }

    [Fact]
    public void WrapNoneLeavesTheTextAloneAndBehindTextIsDrawnFirst()
    {
        var none = "<wp:wrapNone/>";
        var layout = Lay(Para(Anchored(200f, 100f, none, behind: true), Text(Words(40))));
        var page = layout.Page(0);
        Assert.All(LineStarts(page), s => Assert.Equal(72f, s.Left, Tolerance));
        var items = page.Items.ToList();
        var picture = items.OfType<ImageItem>().Single();
        Assert.True(items.IndexOf(picture) < items.FindIndex(i => i is GlyphRunItem), "a picture behind the text is drawn before it");

        var front = Lay(Para(Anchored(200f, 100f, none), Text(Words(40)))).Page(0);
        var frontItems = front.Items.ToList();
        Assert.True(frontItems.IndexOf(frontItems.OfType<ImageItem>().Single()) > frontItems.FindLastIndex(i => i is GlyphRunItem), "a picture in front of the text is drawn after it");
    }

    [Fact]
    public void AnchorOffsetsRelativeToPageMarginAndParagraph()
    {
        var none = "<wp:wrapNone/>";
        var body = Para(Text("first")) +
            Para(
                Text("second"),
                Anchored(20f, 10f, none, "page", 10f, "page", 20f, "page"),
                Anchored(20f, 10f, none, "margin", 5f, "margin", 7f, "margin"),
                Anchored(20f, 10f, none, "column", 3f, "paragraph", 11f, "paragraph"),
                Anchored(20f, 10f, none, "margin", 0f, "margin", 0f, "right", hAlign: "right", vAlign: "bottom"),
                Anchored(20f, 10f, none, "margin", 0f, "margin", 0f, "center", hAlign: "center", vAlign: "center"));
        var page = Lay(body).Page(0);
        var byId = Images(page).ToDictionary(i => Local(i.Image.Id));
        var top = 72f + LineHeight();

        Assert.Equal(10f, byId["page"].X, Tolerance);
        Assert.Equal(20f, byId["page"].Y, Tolerance);
        Assert.Equal(77f, byId["margin"].X, Tolerance);
        Assert.Equal(79f, byId["margin"].Y, Tolerance);
        Assert.Equal(75f, byId["paragraph"].X, Tolerance);
        Assert.Equal(top + 11f, byId["paragraph"].Y, Tolerance);
        Assert.Equal(540f - 20f, byId["right"].X, Tolerance);
        Assert.Equal(720f - 10f, byId["right"].Y, Tolerance);
        Assert.Equal(306f - 10f, byId["center"].X, Tolerance);
        Assert.Equal(396f - 5f, byId["center"].Y, Tolerance);
    }

    [Fact]
    public void FloatNeverLeavesThePage()
    {
        var body = Para(
            Text("x"),
            Anchored(50f, 40f, "<wp:wrapNone/>", "page", 10_000f, "page", 10_000f, "far"),
            Anchored(50f, 40f, "<wp:wrapNone/>", "page", -500f, "page", -900f, "neg"),
            Anchored(5000f, 8000f, "<wp:wrapSquare wrapText=\"bothSides\"/>", "page", 0f, "page", 0f, "huge"));
        var page = Lay(body).Page(0);
        Assert.Equal(3, Images(page).Count);
        foreach (var image in Images(page))
        {
            Assert.True(image.X >= -Tolerance && image.Y >= -Tolerance, $"{image.Image.Id} at {image.X},{image.Y}");
            Assert.True(image.X + image.Width <= page.Width + Tolerance, $"{image.Image.Id} right edge {image.X + image.Width}");
            Assert.True(image.Y + image.Height <= page.Height + Tolerance, $"{image.Image.Id} bottom edge {image.Y + image.Height}");
        }

        var far = Images(page).Single(i => Local(i.Image.Id) == "far");
        Assert.Equal(612f - 50f, far.X, Tolerance);
        Assert.Equal(792f - 40f, far.Y, Tolerance);
    }

    [Fact]
    public void APictureBiggerThanThePageNeitherLoopsNorDropsContent()
    {
        var layout = Lay(Para(Text("before")) + Para(InlinePicture(5000f, 9000f, "big")) + Para(Text("after")));
        Assert.True(layout.LaidOutPages is >= 1 and <= 4);
        var all = string.Join("\n", Pages(layout).Select(p => p.Text));
        Assert.Contains("before", all, StringComparison.Ordinal);
        Assert.Contains("after", all, StringComparison.Ordinal);
        var image = Pages(layout).SelectMany(Images).Single();
        Assert.True(image.Width <= 468f + Tolerance);
        Assert.True(image.Height <= 648f + Tolerance, $"height {image.Height} is taller than the text area");
        Assert.Equal(5000f / 9000f, image.Width / image.Height, 0.001f);
    }

    [Fact]
    public void AFloatThatCoversTheWholeTextAreaStillTerminatesAndKeepsTheText()
    {
        var layout = Lay(Para(Anchored(468f, 648f, vFrom: "margin", hFrom: "margin"), Text(Words(300))) + Para(Text("tail")));
        Assert.True(layout.IsComplete);
        Assert.True(layout.LaidOutPages is >= 1 and <= 40, $"{layout.LaidOutPages} pages");
        var all = string.Join("\n", Pages(layout).Select(p => p.Text));
        Assert.Contains("word300", all, StringComparison.Ordinal);
        Assert.Contains("tail", all, StringComparison.Ordinal);
    }

    [Fact]
    public void ALaterParagraphWrapsAroundAFloatOfAnEarlierOne()
    {
        var body = Para(Anchored(200f, 100f, "<wp:wrapSquare wrapText=\"bothSides\"/>", "column", 0f, "paragraph", 0f), Text("a")) + Para(Text(Words(120)));
        var page = Lay(body).Page(0);
        var picture = Assert.Single(Images(page));
        var beside = LineStarts(page).Skip(1).Where(s => s.Baseline < picture.Y + picture.Height).ToList();
        Assert.NotEmpty(beside);
        Assert.All(beside, s => Assert.True(s.Left >= 272f - Tolerance));
    }

    [Fact]
    public void ATailOnTheNextPageIsNotNarrowedByTheFloatOfThePageBefore()
    {
        var layout = Lay(Para(Anchored(200f, 100f), Text(Words(1500))));
        Assert.True(layout.LaidOutPages >= 2);
        var first = LineStarts(layout.Page(0));
        var second = LineStarts(layout.Page(1));
        Assert.Contains(first, s => s.Left >= 272f - Tolerance);
        Assert.All(second, s => Assert.Equal(72f, s.Left, Tolerance));
        Assert.Equal(72f + 100f, Images(layout.Page(0)).Single().Y + Images(layout.Page(0)).Single().Height, Tolerance);
        Assert.Contains("word1500", layout.Page(layout.LaidOutPages - 1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ParagraphsWithoutWrappingFloatsAreLaidOutAsBefore()
    {
        var plain = Lay(Para(Text(Words(200))) + Para(Text(Words(50))));
        var withNone = Lay(Para(Anchored(50f, 50f, "<wp:wrapNone/>"), Text(Words(200))) + Para(Text(Words(50))));
        Assert.Equal(plain.Page(0).Text, withNone.Page(0).Text);
        Assert.Equal(Runs(plain.Page(0)).Select(r => r.Baseline), Runs(withNone.Page(0)).Select(r => r.Baseline));
    }

    [Fact]
    public void TheWritingViewDoesNotWrapAndPutsThePictureAboveItsParagraph()
    {
        var layout = Lay(Para(Anchored(200f, 100f), Text(Words(60))), new LayoutOptions(LayoutMode.Flow));
        var page = layout.Page(0);
        var picture = Assert.Single(Images(page));
        Assert.Equal(200f, picture.Width, Tolerance);
        var firstBaseline = Baselines(page)[0];
        Assert.True(firstBaseline > picture.Y + picture.Height, "the text starts below the picture");
        Assert.All(LineStarts(page), s => Assert.Equal(0f, s.Left, Tolerance));
    }

    [Fact]
    public void UnsupportedPictureFormatIsAPlaceholder()
    {
        var bytes = new Dictionary<string, byte[]> { ["emf"] = Emf(), ["wmf"] = Wmf(), ["ok"] = Png() };
        var options = new LayoutOptions(ImageBytes: id => bytes[Local(id)]);
        var body = Para(InlinePicture(120f, 60f, "emf"), Text(" x ")) + Para(InlinePicture(80f, 40f, "wmf")) + Para(InlinePicture(50f, 50f, "ok")) +
            Para(Anchored(90f, 30f, "<wp:wrapNone/>", "page", 300f, "page", 500f, "emf"));
        var page = Lay(body, options).Page(0);
        var placeholders = Placeholders(page);
        Assert.Equal(3, placeholders.Count);
        Assert.Contains(placeholders, p => Math.Abs(p.Width - 120f) < Tolerance && Math.Abs(p.Height - 60f) < Tolerance);
        Assert.Contains(placeholders, p => Math.Abs(p.Width - 80f) < Tolerance && Math.Abs(p.Height - 40f) < Tolerance);
        Assert.Contains(placeholders, p => Math.Abs(p.Width - 90f) < Tolerance && Math.Abs(p.Height - 30f) < Tolerance && Math.Abs(p.X - 300f) < Tolerance);
        Assert.Equal("document#ok", Assert.Single(Images(page)).Image.Id);
    }

    [Fact]
    public void CorruptImageBytesArePlaceholderNotAnException()
    {
        var truncated = Png()[..20];
        var bytes = new Dictionary<string, byte[]> { ["junk"] = [1, 2, 3, 4, 5], ["cut"] = truncated, ["empty"] = [] };
        var options = new LayoutOptions(ImageBytes: id => bytes.TryGetValue(Local(id), out var b) ? b : throw new InvalidOperationException("no such part"));
        var body = Para(InlinePicture(70f, 30f, "junk")) + Para(InlinePicture(71f, 31f, "cut")) + Para(InlinePicture(72f, 32f, "empty")) + Para(InlinePicture(73f, 33f, "missing")) +
            Para(Text("still here"));
        var page = Lay(body, options).Page(0);
        Assert.Equal(4, Placeholders(page).Count);
        Assert.Empty(Images(page));
        Assert.Contains("still here", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingExtentTakesTheSizeOfThePictureHeader()
    {
        var bytes = Png(40, 20);
        var options = new LayoutOptions(ImageBytes: _ => bytes);
        var page = Lay(Para(InlinePicture(0f, 0f, "a")), options).Page(0);
        var image = Assert.Single(Images(page));
        Assert.Equal(30f, image.Width, Tolerance);
        Assert.Equal(15f, image.Height, Tolerance);
    }

    [Fact]
    public void TextBoxLaysOutItsParagraphsInsideItsBox()
    {
        var box = TextBoxRun();
        var page = Lay(Para(box, Text("Body text"))).Page(0);
        var fill = Assert.Single(page.Items.OfType<FillItem>());
        Assert.Equal(72f + 18f, fill.X, Tolerance);
        Assert.Equal(72f, fill.Y, Tolerance);
        Assert.Equal(183f, fill.Width, Tolerance);
        Assert.Equal(100f, fill.Height, Tolerance);
        Assert.Equal(0xFFFFFF00u, fill.Color);
        Assert.Contains("Floating text box", page.Text, StringComparison.Ordinal);
        Assert.Contains("second line", page.Text, StringComparison.Ordinal);

        // Both lines of the box are inside its rectangle (the insets of the box keep them off the border); the body text is at the margin.
        var inside = Runs(page).Where(r => r.X > fill.X - Tolerance).ToList();
        Assert.Equal(2, inside.Select(r => r.Baseline).Distinct().Count());
        Assert.All(inside, r => Assert.True(r.X >= fill.X + 7f - Tolerance && r.Baseline <= fill.Y + fill.Height));
        Assert.Equal(72f, Runs(page).Where(r => r.X < fill.X).Min(r => r.X), Tolerance);
    }

    private const string VmlNamespaces = "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    private static string VmlShape(float width, float height, bool picture) =>
        $"<w:r><w:pict {VmlNamespaces}><v:shape style=\"position:absolute;margin-left:10pt;margin-top:5pt;width:{width.ToString(CultureInfo.InvariantCulture)}pt;height:{height.ToString(CultureInfo.InvariantCulture)}pt\">" +
        (picture ? "<v:imagedata r:id=\"vml\"/>" : string.Empty) + "</v:shape></w:pict></w:r>";

    [Fact]
    public void ImageIdsAreQualifiedByTheirStoryAndEachStoryGetsItsOwnVerdict()
    {
        var asked = new List<string>();
        var png40 = Png(40, 20);
        var png80 = Png(80, 40);
        ReadOnlyMemory<byte> Bytes(string id)
        {
            asked.Add(id);
            return id switch
            {
                "document#rId1" => png40,
                "header:hA#rId1" => png80,
                "header:hB#rId1" => Emf(),
                _ => throw new InvalidOperationException(id),
            };
        }

        var parts = new Dictionary<string, string>
        {
            ["hA"] = ChromeTestKit.HeaderPart(Para(InlinePicture(0f, 0f, "rId1"))),
            ["hB"] = ChromeTestKit.HeaderPart(Para(InlinePicture(50f, 25f, "rId1"))),
        };
        var body = $"<w:p><w:pPr>{Sect(ChromeTestKit.Header("default", "hA"))}</w:pPr>{InlinePicture(0f, 0f, "rId1")}</w:p>" + Para(Text("second section")) +
            Sect(ChromeTestKit.Header("default", "hB"));
        var layout = Finish(DocumentLayout.Create(ChromeTestKit.Document(body, parts), Fonts, new LayoutOptions(ImageBytes: Bytes)));

        Assert.Equal(2, layout.LaidOutPages);
        var first = Images(layout.Page(0)).OrderBy(i => i.Y).ToList();
        Assert.Equal(["header:hA#rId1", "document#rId1"], first.Select(i => i.Image.Id));
        Assert.Equal(60f, first[0].Width, Tolerance);
        Assert.Equal(30f, first[1].Width, Tolerance);
        Assert.Empty(Images(layout.Page(1)));
        var box = Assert.Single(Placeholders(layout.Page(1)));
        Assert.Equal(50f, box.Width, Tolerance);
        Assert.Equal(25f, box.Height, Tolerance);
        Assert.Equal(3, asked.Distinct().Count());
        Assert.All(asked.GroupBy(a => a), g => Assert.Single(g));
    }

    [Fact]
    public void FloatsInAHeaderAreShownInline()
    {
        var parts = new Dictionary<string, string>
        {
            ["hA"] = ChromeTestKit.HeaderPart(Para(Anchored(30f, 30f, id: "logo"), Text("Header"), VmlShape(50f, 20f, false))),
        };
        var body = Para(Text("body")) + Sect(ChromeTestKit.Header("default", "hA"));
        var page = Finish(DocumentLayout.Create(ChromeTestKit.Document(body, parts), Fonts, new LayoutOptions())).Page(0);
        var logo = Assert.Single(Images(page));
        Assert.Equal("header:hA#logo", logo.Image.Id);
        Assert.Equal(30f, logo.Width, Tolerance);
        var shape = Assert.Single(Placeholders(page));
        Assert.Equal(50f, shape.Width, Tolerance);
        Assert.Equal(20f, shape.Height, Tolerance);
        Assert.Contains("Header", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatsInATableCellAreShownInlineAndATextBoxIsAMarkedBox()
    {
        var cell = $"<w:tc><w:tcPr><w:tcW w:w=\"6000\" w:type=\"dxa\"/></w:tcPr><w:p>{Anchored(30f, 20f, id: "cellpic")}{Text("cell text")}</w:p><w:p>{TextBoxRun()}</w:p></w:tc>";
        var body = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"6000\"/></w:tblGrid><w:tr>" + cell + "</w:tr></w:tbl>";
        var page = Lay(body).Page(0);
        var picture = Assert.Single(Images(page));
        Assert.Equal("document#cellpic", picture.Image.Id);
        Assert.Equal(30f, picture.Width, Tolerance);
        Assert.Contains("cell text", page.Text, StringComparison.Ordinal);

        // As in the writing view, a text box that cannot be placed is a marked box of its size (183 x 100 pt); its text stays in the model.
        var box = Assert.Single(Placeholders(page));
        Assert.Equal(183f, box.Width, Tolerance);
        Assert.Equal(100f, box.Height, Tolerance);
    }

    [Fact]
    public void AFloatAnchoredAroundAPageBreakIsPlacedOnceOnItsOwnPage()
    {
        var pageBreak = "<w:r><w:br w:type=\"page\"/></w:r>";
        var after = Lay(Para(Text("before"), pageBreak, Anchored(50f, 40f, "<wp:wrapNone/>", "page", 100f, "page", 100f, "late"), Text("after")));
        Assert.Equal(2, after.LaidOutPages);
        Assert.Empty(Images(after.Page(0)));
        var late = Assert.Single(Images(after.Page(1)));
        Assert.Equal(100f, late.X, Tolerance);
        Assert.Equal(100f, late.Y, Tolerance);
        Assert.Single(Pages(after).SelectMany(Images));

        var before = Lay(Para(Text("before"), Anchored(50f, 40f, "<wp:wrapNone/>", "page", 100f, "page", 100f, "early"), pageBreak, Text("after")));
        Assert.Equal(2, before.LaidOutPages);
        Assert.Single(Images(before.Page(0)));
        Assert.Empty(Images(before.Page(1)));
    }

    [Fact]
    public async Task ATextBoxOfTheCorpusShowsItsText()
    {
        var path = Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus", "poi-shapes-with-text", "poi-shapes-with-text.docx");
        using var file = DocxFile.Open(path);
        var layout = DocumentLayout.Create(file.Model, Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, TestContext.Current.CancellationToken);
        var text = string.Join("\n", Pages(layout).Select(p => p.Text));
        Assert.Contains("Floating text box", text, StringComparison.Ordinal);
        var page = Pages(layout).First(p => p.Text.Contains("Floating text box", StringComparison.Ordinal));
        Assert.NotEmpty(page.Items.OfType<FillItem>());
    }
}
