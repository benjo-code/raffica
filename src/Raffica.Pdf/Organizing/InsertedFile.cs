using Raffica.Core.Pages;
using Raffica.Core.Pdf;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// A file whose pages were inserted into the plan: the entry Core needs to apply the plan (path, password, the stamp taken at the open) and the
/// document itself, kept open for the analysis and the thumbnails until the session ends.
/// </summary>
internal sealed class InsertedFile(string name, PageSourceFile file, PdfDocument document, int pageCount) : IDisposable
{
    /// <summary>The name the pane and the cards show (the file name).</summary>
    public string Name { get; } = name;

    public PageSourceFile File { get; } = file;

    public PdfDocument Document { get; } = document;

    /// <summary>The number of pages of the whole file, not of the inserted range.</summary>
    public int PageCount { get; } = pageCount;

    public void Dispose() => Document.Dispose();
}
