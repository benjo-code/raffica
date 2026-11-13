using Raffica.Core.Pdf;

namespace WriteCorpus;

/// <summary>The PDFium engine of Raffica.Core could not be started (native library missing or not loadable).</summary>
internal sealed class PdfEngineUnavailableException(Exception inner) : Exception("The PDF engine (PDFium) could not be started.", inner);

/// <summary>Reads the page facts of a PDF through Raffica.Core (the engine of the PDF app; PDFium is found the way the product finds it).</summary>
internal static class PdfPageFacts
{
    /// <exception cref="PdfEngineUnavailableException">PDFium could not be loaded.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable PDF.</exception>
    public static async Task<PageFacts> ReadAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pdfPath);
        try
        {
            await using var document = await new PdfEngine().OpenAsync(pdfPath, null, cancellationToken).ConfigureAwait(false);
            var pages = new List<string>(document.PageCount);
            for (var i = 0; i < document.PageCount; i++)
            {
                var text = await document.GetPageTextAsync(i, cancellationToken).ConfigureAwait(false);
                pages.Add(text.GetText(0, text.Chars.Count));
            }

            return new PageFacts(pages.Count, pages);
        }
        catch (PdfOpenException ex)
        {
            throw new InvalidDataException("The file is not a readable PDF (" + ex.Error + ").", ex);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            throw new PdfEngineUnavailableException(ex);
        }
    }
}
