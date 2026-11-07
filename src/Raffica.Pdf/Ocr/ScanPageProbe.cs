using Raffica.Core.Ocr;
using Raffica.Core.Pdf;

namespace Raffica.Pdf.Ocr;

/// <summary>Tells whether a page is a picture of text (a scan): little text of its own, no layer of Raffica and an image that covers most of the page.</summary>
internal static class ScanPageProbe
{
    private const double MinCoverage = 0.8;

    public static async Task<bool> IsPictureAsync(PdfDocument document, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var info = await document.GetOcrPageInfoAsync(page, cancellationToken).ConfigureAwait(false);
        return info.TextCharacters < OcrPlanner.TextThreshold && !info.HasOwnLayer && info.ImageCoverage > MinCoverage;
    }
}
