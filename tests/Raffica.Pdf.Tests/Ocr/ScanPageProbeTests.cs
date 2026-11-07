using Raffica.Core.Pdf;
using Raffica.Pdf.Ocr;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class ScanPageProbeTests
{
    [Fact]
    public async Task ScanIsAPictureAndTextPageIsNot()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = new PdfEngine();

        using (var scan = await engine.OpenAsync(TestData.Pdf("ocr-scan.pdf"), null, ct))
        {
            Assert.True(await ScanPageProbe.IsPictureAsync(scan, 0, ct));
        }

        using (var text = await engine.OpenAsync(TestData.Pdf("text-basic.pdf"), null, ct))
        {
            Assert.False(await ScanPageProbe.IsPictureAsync(text, 0, ct));
        }

        // The mixed file has pages of both kinds.
        using var mixed = await engine.OpenAsync(TestData.Pdf("ocr-mixed.pdf"), null, ct);
        var results = new List<bool>();
        for (var page = 0; page < mixed.PageCount; page++)
        {
            results.Add(await ScanPageProbe.IsPictureAsync(mixed, page, ct));
        }

        Assert.Contains(true, results);
        Assert.Contains(false, results);
    }
}
