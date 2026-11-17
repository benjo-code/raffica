using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Raffica.Core.Viewing;
using Raffica.Write.Documents;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>A pixel copy of a rendered frame (BGRA).</summary>
internal sealed class FrameShot(int width, int height, byte[] bytes, bool rgba = false)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>The colour at a pixel as 0xAARRGGBB.</summary>
    public uint At(int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        var (r, b) = rgba ? (bytes[i], bytes[i + 2]) : (bytes[i + 2], bytes[i]);
        return (uint)(bytes[i + 3] << 24 | r << 16 | bytes[i + 1] << 8 | b);
    }

    /// <summary>How many pixels of a rectangle are darker than a mid grey (text).</summary>
    public int DarkPixels(int left, int top, int width, int height)
    {
        var count = 0;
        for (var y = top; y < top + height && y < Height; y++)
        {
            for (var x = left; x < left + width && x < Width; x++)
            {
                var c = At(x, y);
                var luminance = (((c >> 16) & 0xFF) + ((c >> 8) & 0xFF) + (c & 0xFF)) / 3;
                if (luminance < 100)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>A hash of the pixels of a rectangle (to see that a region changed).</summary>
    public long Hash(int left, int top, int width, int height)
    {
        long hash = 17;
        for (var y = top; y < top + height && y < Height; y++)
        {
            for (var x = left; x < left + width && x < Width; x++)
            {
                hash = (hash * 31) + At(x, y);
            }
        }

        return hash;
    }
}

/// <summary>
/// A tab and its page view in a headless window, opened the way the app opens a file (a prefix, page 1 laid out), without the rest of the window. The view
/// is given a plain desk colour of its own because the styles of the app are not loaded in the tests.
/// </summary>
internal sealed class ViewHarness : IDisposable
{
    public static readonly Color Desk = Color.FromRgb(0xC8, 0xC8, 0xF0);

    public ViewHarness(string path, double width = 900, double height = 600, WriteView mode = WriteView.Page, ZoomMode zoom = ZoomMode.FitWidth, OpenWork? work = null, Raffica.Write.Layout.ImageCacheLimits? pictureLimits = null)
    {
        Work = work ?? new OpenWork();
        var attempt = DocumentOpener.Open(Path.GetFullPath(path), Work, DocumentOpener.DefaultMaxFileLength, CancellationToken.None, pictureLimits: pictureLimits);
        Assert.NotNull(attempt.Document);
        Tab = new WriteTab(path, attempt.Document!);
        View = new PageView(Tab, mode, zoom) { Background = new SolidColorBrush(Desk) };
        View.FirstSharpPage += () =>
        {
            FirstSharpCount++;
            DecodesAtFirstSharp = Tab.Pictures.DecodeCount;
        };
        Window = new Window { Width = width, Height = height, Content = View, SizeToContent = SizeToContent.Manual };
        Window.Show();
        View.Focus();
        Pump();
    }

    /// <summary>How many times <see cref="PageView.FirstSharpPage"/> was raised (counted from before the window was shown).</summary>
    public int FirstSharpCount { get; private set; }

    /// <summary>How many pictures the cache had decoded when <see cref="PageView.FirstSharpPage"/> was raised.</summary>
    public int DecodesAtFirstSharp { get; private set; }

    public OpenWork Work { get; }

    public WriteTab Tab { get; }

    public PageView View { get; }

    public Window Window { get; }

    // Also the jobs of the Background priority (the first-page event is posted there, as in the PDF view).
    public static void Pump()
    {
        WindowFixture.Pump();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }

    /// <summary>Renders a frame and copies its pixels.</summary>
    public FrameShot Capture()
    {
        Pump();
        var bitmap = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        using var locked = bitmap.Lock();
        var bytes = new byte[locked.RowBytes * locked.Size.Height];
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);
        return new FrameShot(locked.Size.Width, locked.Size.Height, bytes, locked.Format == Avalonia.Platform.PixelFormat.Rgba8888);
    }

    /// <summary>Starts the rest of the layout and waits until the page layout is complete.</summary>
    public async Task LayOutEverythingAsync()
    {
        View.Runner.Start();
        await WaitAsync(() => View.Runner.PageLayout.IsComplete, 30000);
        await View.Runner.PageTask;
        Pump();
    }

    public static async Task WaitAsync(Func<bool> condition, int milliseconds = 10000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 10)
        {
            await Task.Delay(10);
            Pump();
        }

        Assert.True(condition(), "the condition was not met in time");
    }

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Window.Close();
        }

        Tab.Dispose();
        Pump();
    }

    /// <summary>A DOCX of five pages, one page break before each paragraph but the first, with "Page N of M" in the footer (PAGE and NUMPAGES fields).</summary>
    public static string WriteNumPagesDocument(string folder, int pages = 5)
    {
        var path = Path.Combine(folder, "numpages.docx");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
                </Relationships>
                """);
            var body = new StringBuilder();
            for (var i = 1; i <= pages; i++)
            {
                body.Append("<w:p>").Append(i > 1 ? "<w:pPr><w:pageBreakBefore/></w:pPr>" : string.Empty)
                    .Append("<w:r><w:t>Text on page ").Append(i).Append("</w:t></w:r></w:p>");
            }

            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>{body}
                    <w:sectPr>
                      <w:footerReference w:type="default" r:id="rId1"/>
                      <w:pgSz w:w="12240" w:h="15840"/>
                      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """);
            Entry(zip, "word/footer1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:t xml:space="preserve">Page </w:t></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:fldChar w:fldCharType="begin"/></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:fldChar w:fldCharType="separate"/></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:t>1</w:t></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:fldChar w:fldCharType="end"/></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:t xml:space="preserve"> of </w:t></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:fldChar w:fldCharType="begin"/></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:instrText xml:space="preserve"> NUMPAGES </w:instrText></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:fldChar w:fldCharType="separate"/></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:t>1</w:t></w:r>
                    <w:r><w:rPr><w:b/><w:sz w:val="40"/></w:rPr><w:fldChar w:fldCharType="end"/></w:r>
                  </w:p>
                </w:ftr>
                """);
        }

        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart());
    }
}
