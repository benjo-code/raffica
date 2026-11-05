using Avalonia.Media;

namespace Raffica.Pdf.Viewing;

/// <summary>
/// A layer drawn over the pages in the one drawing pass of the page view (live previews of a stroke or a draft, selection handles).
/// Committed annotations are not drawn here, PDFium renders them with the page. <see cref="Render"/> is called once per pass, clipped
/// to the view, after the pages and their marks; positions are in the coordinates of the view (<see cref="PageView.ToView(int, double, double)"/>).
/// </summary>
internal interface IPageOverlay
{
    /// <summary>Raised when what <see cref="Render"/> draws changed; the view draws again.</summary>
    event Action? Changed;

    /// <summary>Draws the layer.</summary>
    /// <param name="context">The drawing context of the view.</param>
    /// <param name="view">The view, for the mapping between pages and the view.</param>
    void Render(DrawingContext context, PageView view);
}
