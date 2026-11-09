using System.Globalization;
using System.Text;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdfium;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Text;

public sealed class PageLinksTests
{
    private readonly PdfEngine engine = new();

    [Fact]
    public async Task RichDocumentLinksAreTheAnchorsAndTheWebAddresses()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var document = await engine.OpenAsync(TestData.Pdf("text-rich.pdf"), cancellationToken: ct);
        var text = await document.GetPageTextAsync(0, ct);
        var all = text.GetText(0, text.Chars.Count);

        Assert.Equal(4, text.Links.Count);

        // The two link annotations: Rect [40 156 140 172] and [40 126 170 142] on a page 400 points high.
        var docs = Assert.Single(text.Links, l => l.Target is UriTarget { Url: "https://example.org/docs" });
        var docsRect = Assert.Single(docs.Rects);
        Assert.Same(docs, text.LinkAt(Centre(docsRect).X, Centre(docsRect).Y));
        var jump = Assert.Single(text.Links, l => l.Target is PageTarget);
        var jumpRect = Assert.Single(jump.Rects);
        Assert.Same(jump, text.LinkAt(Centre(jumpRect).X, Centre(jumpRect).Y));
        Assert.True(docsRect.Bottom < jumpRect.Top, "Dokumentation lies above Weiter zu Seite zwei");

        // Each anchor covers the words it is written over.
        AssertCovers(text, docsRect, all, "Dokumentation");
        AssertCovers(text, jumpRect, all, "Weiter zu Seite zwei");

        // The two addresses PDFium finds in the text cover their characters, and nothing else.
        var raffica = Assert.Single(text.Links, l => l.Target is UriTarget { Url: "https://example.org/pagewing" });
        var www = Assert.Single(text.Links, l => l.Target is UriTarget { Url: "http://www.example.org" });
        foreach (var (link, written) in new[] { (raffica, "https://example.org/pagewing"), (www, "www.example.org") })
        {
            var start = all.IndexOf(written, StringComparison.Ordinal);
            Assert.True(start >= 0, written);
            var rect = Assert.Single(text.RectsOf(start, start + written.Length));
            Assert.Same(link, text.LinkAt(Centre(rect).X, Centre(rect).Y));
            AssertCovers(text, Assert.Single(link.Rects), all, written);
        }

        // No link elsewhere: the heading, the words before the address, the sentence period, a corner.
        foreach (var plain in new[] { "Pagewing Handbuch", "Mehr unter", "Zweite Zeile" })
        {
            var start = all.IndexOf(plain, StringComparison.Ordinal);
            var rect = text.RectsOf(start, start + plain.Length)[0];
            Assert.Null(text.LinkAt(Centre(rect).X, Centre(rect).Y));
        }

        Assert.Null(text.LinkAt(390, 390));
        Assert.Null(text.LinkAt(-5, -5));
    }

    [Fact]
    public async Task UnsupportedActionsAreNotLinks()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var document = await engine.OpenAsync(TestData.Pdf("annotations.pdf"), cancellationToken: ct);

        var text = await document.GetPageTextAsync(0, ct);

        // annotations.pdf has exactly one link annotation, a URI action; it has no launch or script action, so this
        // file only shows that the other annotations (Square, Highlight) are no links. The skipped actions are
        // covered by LaunchScriptAndFileActionsNeverBecomeClickable.
        var link = Assert.Single(text.Links);
        var uri = Assert.IsType<UriTarget>(link.Target);
        Assert.Equal("https://example.com/pagewing", uri.Url);
        Assert.True(ExternalLink.TryParse(uri.Url, out _));
        Assert.All(text.Links, l => Assert.True(l.Target is PageTarget or UriTarget));
        var rect = Assert.Single(link.Rects);
        Assert.InRange(rect.Left, 159, 161);
        Assert.InRange(rect.Top, 139, 141); // 200 - 60
        Assert.InRange(rect.Right, 269, 271);
        Assert.InRange(rect.Bottom, 169, 171); // 200 - 30

        // The Square annotation (Rect [30 30 130 90]) and the Highlight are no links.
        Assert.Null(text.LinkAt(80, 140));
        Assert.Null(text.LinkAt(100, 60));
    }

    [Fact]
    public async Task LaunchScriptAndFileActionsNeverBecomeClickable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"raffica-link-actions-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, ActionsPdf(), TestContext.Current.CancellationToken);
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await using var document = await engine.OpenAsync(path, cancellationToken: ct);

            var text = await document.GetPageTextAsync(0, ct);

            // Launch, GoToR, GoToE and JavaScript actions never reach PageText.Links.
            Assert.All(text.Links, l => Assert.True(l.Target is PageTarget or UriTarget));
            Assert.Equal(1, text.Links.Count(l => l.Target is PageTarget));

            // A URI action may carry any address. Those reach PageText.Links unchanged, so whoever makes a link
            // clickable must run it through ExternalLink first: only the https one passes.
            var uris = text.Links.Select(l => l.Target).OfType<UriTarget>().Select(t => t.Url).ToArray();
            Assert.Equal(3, uris.Length);
            Assert.Equal(4, text.Links.Count); // three addresses and the plain destination; the four actions are gone
            Assert.Contains("javascript:alert(1)", uris);
            Assert.Contains("file:///C:/Windows/System32/calc.exe", uris);
            Assert.Contains("https://ok.example/", uris);
            var clickable = uris.Where(u => ExternalLink.TryParse(u, out _)).ToArray();
            Assert.Equal(["https://ok.example/"], clickable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DestinationPositionUsesTheTransformOfTheTargetPage()
    {
        // Target 1: CropBox [0 36 612 828] on a 864 high MediaBox, so the top of the page lies at y = 828 and /XYZ y = 400 is 428 down.
        // Target 2: /Rotate 90 turns the page, so the displayed top is the left edge: /XYZ x = 50 is 50 down.
        using var folder = new Annotations.TempFolder();
        var path = Annotations.AnnotationTestFiles.WritePdf(
            folder,
            "targets.pdf",
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [6 0 R 7 0 R] >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 864] /CropBox [0 36 612 828] >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 600] /Rotate 90 >>",
                "<< /Type /Annot /Subtype /Link /Rect [40 340 140 360] /Dest [4 0 R /XYZ 100 400 null] >>",
                "<< /Type /Annot /Subtype /Link /Rect [40 300 140 320] /Dest [5 0 R /XYZ 50 300 null] >>",
            ]);
        var ct = TestContext.Current.CancellationToken;
        await using var document = await engine.OpenAsync(path, cancellationToken: ct);

        var text = await document.GetPageTextAsync(0, ct);

        var targets = text.Links.Select(l => l.Target).OfType<PageTarget>().OrderBy(t => t.PageIndex).ToArray();
        Assert.Equal([1, 2], targets.Select(t => t.PageIndex));
        Assert.Equal(428, targets[0].Top!.Value, 0.1);
        Assert.Equal(50, targets[1].Top!.Value, 0.1);
    }

    [Fact]
    public async Task LinksToTheSamePageLoadItOnceAndTheCurrentPageNotAtAll()
    {
        // Page 0 holds five links: three to page 1, one to page 2 and one to itself. Only pages 1 and 2 are loaded, each once.
        using var folder = new Annotations.TempFolder();
        var path = Annotations.AnnotationTestFiles.WritePdf(
            folder,
            "many-links.pdf",
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [6 0 R 7 0 R 8 0 R 9 0 R 10 0 R] >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] >>",
                "<< /Type /Annot /Subtype /Link /Rect [10 10 50 20] /Dest [4 0 R /XYZ 0 100 null] >>",
                "<< /Type /Annot /Subtype /Link /Rect [10 30 50 40] /Dest [4 0 R /XYZ 0 200 null] >>",
                "<< /Type /Annot /Subtype /Link /Rect [10 50 50 60] /Dest [4 0 R /XYZ 0 300 null] >>",
                "<< /Type /Annot /Subtype /Link /Rect [10 70 50 80] /Dest [5 0 R /XYZ 0 100 null] >>",
                "<< /Type /Annot /Subtype /Link /Rect [10 90 50 100] /Dest [3 0 R /XYZ 0 100 null] >>",
            ]);
        var thread = PdfiumThread.Shared;

        var (links, loaded) = await thread.InvokeAsync(
            () =>
            {
                var loads = new List<int>();
                using var document = PdfiumDocument.Open(thread, path, null);
                using var page = document.LoadPage(0);
                using var text = page.LoadTextPage();
                return (PdfiumPageText.ReadLinks(document, page, text, 0, loads.Add), loads);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, links.Count(l => l.Kind == PdfiumLinkKind.Page));
        Assert.Equal([1, 2], loaded.Order());
        Assert.Equal([300.0, 200.0, 100.0], links.Where(l => l.PageIndex == 1).Select(l => l.Top!.Value).Order(Comparer<double>.Create((x, y) => y.CompareTo(x))));
    }

    private static (double X, double Y) Centre(PageRect rect) => ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);

    // The link rectangle covers the line of the written text: it reaches at least from its first to its last character.
    private static void AssertCovers(PageText text, PageRect linkRect, string all, string written)
    {
        var start = all.IndexOf(written, StringComparison.Ordinal);
        Assert.True(start >= 0, written);
        var first = text.Chars[start].Box;
        var last = text.Chars[start + written.Length - 1].Box;
        Assert.True(linkRect.Left <= first.Left + 1 && linkRect.Right >= last.Right - 1, $"{written}: link {linkRect} does not span {first.Left}..{last.Right}");
        var middle = Centre(first);
        Assert.True(linkRect.Contains(middle.X, middle.Y), $"{written}: link {linkRect} misses the first character");
    }

    // One page of 400 x 400 with links of every kind: GoToR, GoToE, Launch, JavaScript, URI actions with a script, a
    // file and a web address, and a plain destination.
    private static byte[] ActionsPdf()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R 6 0 R 7 0 R 8 0 R 9 0 R 10 0 R 11 0 R 12 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 340 140 360] /A << /S /GoToR /F (other.pdf) /D [1 /XYZ 0 100 0] >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 300 140 320] /A << /S /GoToE /T << /R /C /N (attachment) >> /D [1 /XYZ 0 100 0] >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 260 140 280] /A << /S /Launch /F (calc.exe) >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 220 140 240] /A << /S /JavaScript /JS (app.alert\\(1\\)) >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 180 140 200] /A << /S /URI /URI (javascript:alert\\(1\\)) >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 140 140 160] /A << /S /URI /URI (file:///C:/Windows/System32/calc.exe) >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 100 140 120] /A << /S /URI /URI (https://ok.example/) >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [40 60 140 80] /Dest [4 0 R /XYZ 0 100 null] >>",
        ];

        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
