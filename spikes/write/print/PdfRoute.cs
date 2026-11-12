using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Layout.Common;
using PrintSpike.Interop;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using SkiaSharp;

namespace PrintSpike;

/// <summary>Route b (and the format CUPS takes): the laid-out pages drawn into a PDF in memory.</summary>
public static class PdfRoute
{
    /// <summary>The pages <paramref name="from"/> to <paramref name="to"/> (1-based, default all) as a PDF in memory; page numbers in the footers are those of the whole document.</summary>
    public static MemoryStream Render(PageBreaker layout, int from = 1, int to = int.MaxValue)
    {
        layout.LayoutAll();
        var stream = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(stream))
        {
            for (int i = Math.Max(0, from - 1); i < Math.Min(layout.PageCount, to); i++)
            {
                using var canvas = doc.BeginPage(layout.Setup.Width, layout.Setup.Height);
                PageRenderer.Draw(layout, i, canvas);
                doc.EndPage();
            }
            doc.Close();
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Windows, route b: the PDF in memory is opened as Raffica's PdfDocument (through a temporary file, PDFium opens files) and printed into the
    /// device context the way Raffica PDF's WindowsPrintService does. Returns the number of pages.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static async Task<int> PrintToDeviceAsync(MemoryStream pdf, nint dc, string documentName, string? outputFile)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "print-spike-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(tmp, pdf.ToArray());
        try
        {
            var engine = new PdfEngine();
            var document = await engine.OpenAsync(tmp);
            double dpiX = Gdi.GetDeviceCaps(dc, Gdi.LogicalPixelsX), dpiY = Gdi.GetDeviceCaps(dc, Gdi.LogicalPixelsY);
            var area = new PaperArea(Gdi.GetDeviceCaps(dc, Gdi.HorizontalResolution) * 72.0 / dpiX, Gdi.GetDeviceCaps(dc, Gdi.VerticalResolution) * 72.0 / dpiY);
            unsafe
            {
                fixed (char* name = documentName)
                fixed (char* output = outputFile)
                {
                    var info = new DocInfo { Size = sizeof(DocInfo), DocName = name, Output = outputFile is null ? null : output };
                    if (Gdi.StartDoc(dc, &info) <= 0) throw new InvalidOperationException("StartDoc failed, error " + Marshal.GetLastWin32Error());
                }
            }
            for (int i = 0; i < document.PageCount; i++)
            {
                Gdi.StartPage(dc);
                var size = await document.GetPageSizeAsync(i);
                var placement = PrintPlacement.Plan(size, area, PrintScaling.FitToPaper).ToDevice(dpiX, dpiY);
                await document.PrintPageToDeviceAsync(i, dc, placement);
                Gdi.EndPage(dc);
            }
            Gdi.EndDoc(dc);
            return document.PageCount;
        }
        finally { try { File.Delete(tmp); } catch (IOException) { } }
    }
}
