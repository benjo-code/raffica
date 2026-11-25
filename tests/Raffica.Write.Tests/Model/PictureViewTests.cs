using System.IO.Compression;
using System.Xml.Linq;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Model;

/// <summary>The typed reading of a picture drawing and the header probe that decides whether bytes are a picture.</summary>
public sealed class PictureViewTests
{
    private const string Namespaces = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    private static Run RunWith(string container, string pic)
    {
        var run = new Run();
        run.Content.Add(new Picture(XElement.Parse($"<w:drawing {Namespaces}>{container.Replace("PIC", pic, StringComparison.Ordinal)}</w:drawing>")));
        return run;
    }

    private const string PlainPic = "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"x\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rId9\"/><a:srcRect l=\"1000\" t=\"2000\" r=\"3000\" b=\"4000\"/></pic:blipFill><pic:spPr><a:xfrm rot=\"5400000\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"100\" cy=\"200\"/></a:xfrm></pic:spPr></pic:pic></a:graphicData></a:graphic>";

    [Fact]
    public void AnInlinePictureIsReadWithItsExtentRotationCropAltTextAndMedia()
    {
        var run = RunWith("<wp:inline><wp:extent cx=\"100\" cy=\"200\"/><wp:docPr id=\"3\" name=\"n\" descr=\"a cat\"/>PIC</wp:inline>", PlainPic);

        var info = PictureView.Of(run, 0)!;

        Assert.Equal(new EmuSize(100, 200), info.ExtentEmu);
        Assert.Equal(5400000, info.Rotation);
        Assert.True(info.IsRotated);
        Assert.Equal(new PictureCrop(1000, 2000, 3000, 4000), info.Crop);
        Assert.Equal(PictureWrap.Inline, info.Wrap);
        Assert.Null(info.Anchor);
        Assert.Equal("a cat", info.AltText);
        Assert.Equal("rId9", info.Media);
    }

    [Theory]
    [InlineData("<wp:wrapSquare wrapText=\"bothSides\"/>", "0", PictureWrap.Square)]
    [InlineData("<wp:wrapTight wrapText=\"left\"/>", "0", PictureWrap.Square)]
    [InlineData("<wp:wrapTopAndBottom/>", "0", PictureWrap.TopAndBottom)]
    [InlineData("<wp:wrapNone/>", "0", PictureWrap.InFront)]
    [InlineData("<wp:wrapNone/>", "1", PictureWrap.Behind)]
    public void AnAnchoredPictureIsReadWithItsWrapAndOffsets(string wrap, string behind, PictureWrap expected)
    {
        var run = RunWith($"<wp:anchor behindDoc=\"{behind}\" relativeHeight=\"77\"><wp:positionH relativeFrom=\"page\"><wp:posOffset>12</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"margin\"><wp:align>top</wp:align></wp:positionV><wp:extent cx=\"5\" cy=\"6\"/>{wrap}<wp:docPr id=\"1\" name=\"n\"/>PIC</wp:anchor>", PlainPic);

        var info = PictureView.Of(run, 0)!;

        Assert.Equal(expected, info.Wrap);
        Assert.True(info.IsFloating);
        Assert.Equal(new PictureAnchorInfo("page", 12, "margin", null, 77, behind == "1"), info.Anchor);
    }

    [Fact]
    public void OnlyAPlainPictureIsAnObject()
    {
        var shape = RunWith("<wp:inline><wp:extent cx=\"1\" cy=\"1\"/><wp:docPr id=\"1\" name=\"n\"/>PIC</wp:inline>", "<a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"/></a:graphic>");
        var noExtent = RunWith("<wp:inline><wp:docPr id=\"1\" name=\"n\"/>PIC</wp:inline>", PlainPic);
        var text = new Run();
        text.Content.Add(new TextContent("x"));

        Assert.Null(PictureView.Of(shape, 0));
        Assert.Null(PictureView.Of(noExtent, 0));
        Assert.Null(PictureView.Of(text, 0));
        Assert.Null(PictureView.Of(text, 5));
    }

    [Theory]
    [InlineData("png", 40, 30)]
    [InlineData("gif", 41, 31)]
    [InlineData("bmp", 42, 32)]
    [InlineData("bmp-top-down", 43, 33)]
    [InlineData("jpeg", 45, 35)]
    public void TheProbeReadsTheTypeAndSizeOfEachFormat(string kind, int width, int height)
    {
        var (bytes, type) = kind switch
        {
            "png" => (TestPictures.Png(width, height), "image/png"),
            "gif" => (TestPictures.GifHeader(width, height), "image/gif"),
            "bmp" => (TestPictures.BmpHeader(width, height), "image/bmp"),
            "bmp-top-down" => (TestPictures.BmpHeader(width, -height), "image/bmp"),
            _ => (TestPictures.JpegHeader(width, height), "image/jpeg"),
        };

        Assert.Equal(new ImageHeaderInfo(type, width, height), ImageHeaderProbe.Read(bytes));
    }

    [Fact]
    public void TheProbeRefusesEverythingThatIsNotAPictureWithAHeader()
    {
        var png = TestPictures.Png(10, 10);
        Assert.Null(ImageHeaderProbe.Read([]));
        Assert.Null(ImageHeaderProbe.Read(png.AsSpan(0, 20)));
        Assert.Null(ImageHeaderProbe.Read("not a picture at all, just words"u8));
        Assert.Null(ImageHeaderProbe.Read(TestPictures.PngHeaderOnly(0, 10)));
        Assert.Null(ImageHeaderProbe.Read(TestPictures.GifHeader(0, 5)));
        Assert.Null(ImageHeaderProbe.Read(TestPictures.JpegHeader(10, 10).AsSpan(0, 12)));
        Assert.Null(ImageHeaderProbe.Read(TestPictures.TiffHeader(10, 10).AsSpan(0, 20)));
        var garbage = new byte[64];
        Array.Fill(garbage, (byte)0xFF);
        Assert.Null(ImageHeaderProbe.Read(garbage));
    }

    // The probe the editor gates an insert with and the codec the layout draws with state the same size for every picture of the corpus they both read.
    [Fact]
    public void TheProbeAndTheLayoutsHeaderReaderAgreeOnEveryCorpusPicture()
    {
        var missed = new List<string>();
        var compared = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var archive = ZipFile.OpenRead(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            foreach (var part in archive.Entries.Where(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)))
            {
                using var stream = part.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                var probe = ImageHeaderProbe.Read(bytes);
                var codec = ImageHeader.Read(bytes);
                if (probe is { } read)
                {
                    Assert.True(codec is not null, $"{entry.Id}/{part.FullName}: the probe reads {read.Width} x {read.Height} where the codec reads nothing");
                    Assert.True(codec.Value.Width == read.Width && codec.Value.Height == read.Height, $"{entry.Id}/{part.FullName}: the probe reads {read.Width} x {read.Height}, the codec {codec.Value.Width} x {codec.Value.Height}");
                    compared++;
                }
                else if (codec is not null && (part.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || part.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || part.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || part.FullName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || part.FullName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)))
                {
                    missed.Add($"{entry.Id}/{part.FullName} ({bytes.Length} bytes, codec {(codec is null ? "none" : codec.Value.Width + "x" + codec.Value.Height)})");
                }
            }
        }

        Assert.True(missed.Count == 0, "the probe reads nothing of: " + string.Join("; ", missed));
        Assert.True(compared > 0, "no corpus picture was compared");
    }
}
