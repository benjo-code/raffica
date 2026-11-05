using Avalonia;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Signatures;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Signing;

/// <summary>
/// Puts a saved signature on the current page: in the centre of the part of the page that is on screen, at the size
/// <see cref="SignaturePlacement.Fit"/> gives, as one step of the history (an image through
/// <see cref="AnnotationEditor.ApplySignatureImageAsync"/>, a drawn one through <see cref="AnnotationEditor.ApplyDrawingAsync"/> with the
/// saved colour). Afterwards the Select tool is active and the new signature is selected, so the handles and the arrow keys finish the job.
/// The place is found with the page view's own mapping between view and page points, so a rotated page and a CropBox need nothing of their
/// own. A placement holds a pending handle of the tab until the Core call ends; a failure (the history is full, the document lists as many
/// annotations as Raffica reads, the stored image cannot be read, anything else) ends in a card, adds nothing and never changes the library.
/// Used on the UI thread only.
/// </summary>
internal sealed class SignaturePlacer(SignatureLibrary library, ToolState tools, DialogQueue dialogs)
{
    /// <summary>The stroke width of a drawn signature in points (the "medium" width of the drawing tools).</summary>
    private static readonly double DrawnWidth = StrokeWidths.PointsOf(StrokeWidth.Medium);

    /// <summary>Replaces the select step after a placement (tests); <see langword="null"/> selects with the controller.</summary>
    internal Func<SelectController, AnnotationLocator, Task>? SelectStep { get; set; }

    /// <summary>Places <paramref name="signature"/> on the page the view counts as current.</summary>
    /// <param name="tab">The document.</param>
    /// <param name="view">The page view of the tab.</param>
    /// <param name="select">The select controller of the tab (it selects the new signature).</param>
    /// <param name="signature">The saved signature.</param>
    /// <returns><see langword="true"/> when the signature was placed and selected; <see langword="false"/> when nothing was placed (the user was told why).</returns>
    public async Task<bool> PlaceAsync(PdfTab tab, PageView view, SelectController select, SavedSignature signature)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(signature);

        var page = view.CurrentPage;
        if (view.SizeOfPage(page) is not { } size)
        {
            await TellAsync(PdfStrings.SignaturePlaceFailed);
            return false;
        }

        PdfAnnotation? created = null;
        string? failure = null;
        var pending = tab.Annotations.BeginPending();
        try
        {
            var centre = CentreOfVisiblePart(view, page, size.Width, size.Height);
            var box = SignaturePlacement.Fit(signature.Aspect, centre, size.Width, size.Height);
            var editor = tab.Annotations.Editor;
            if (signature.Kind == SavedSignatureKind.Image)
            {
                var image = await library.ReadImageAsync(signature.Id);
                created = await editor.ApplySignatureImageAsync(page, box, image, tools.Author);
            }
            else if (signature.Strokes is { Count: > 0 } strokes)
            {
                var geometry = DrawingGeometry.Signature(strokes.Select(s => (IReadOnlyList<DisplayPoint>)[.. s.Select(p => Into(box, p))]), DrawnWidth);
                created = await editor.ApplyDrawingAsync(page, geometry, DrawPalette.ColorOf(signature.Color), null, tools.Author);
            }

            failure = created is null ? PdfStrings.SignaturePlaceFailed : null;
        }
        catch (OperationCanceledException)
        {
            return false; // the document went away or the work was canceled
        }
        catch (AnnotationHistoryFullException)
        {
            failure = PdfStrings.SignaturePlaceHistoryFull;
        }
        catch (PdfAnnotationLimitException)
        {
            failure = PdfStrings.AnnotateLimitReached;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = PdfStrings.SignaturePlaceFailed;
        }
        finally
        {
            pending.Dispose();
        }

        if (failure is not null || created is null)
        {
            await TellAsync(failure ?? PdfStrings.SignaturePlaceFailed);
            return false;
        }

        // The select tool shows the selection, so it comes first; an already active select tool stays (choosing it again would end the mode).
        if (tools.Tool != AnnotateTool.Select)
        {
            tools.Choose(AnnotateTool.Select);
        }

        // Best effort: the signature is placed and is one history step whether or not it could be selected afterwards.
        try
        {
            await (SelectStep?.Invoke(select, new AnnotationLocator(created.Page, created.Index, created.Name))
                ?? select.SelectAsync(new AnnotationLocator(created.Page, created.Index, created.Name)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The handles are a courtesy; the placement stands and is reported as made.
        }

        return true;
    }

    // A point of the unit square of a saved signature inside the box.
    private static DisplayPoint Into(PageRect box, DisplayPoint unit) =>
        new(box.Left + (unit.X * box.Width), box.Top + (unit.Y * box.Height));

    /// <summary>The centre of what is on screen of a page, in page points; the centre of the page when none of it is on screen.</summary>
    internal static DisplayPoint CentreOfVisiblePart(PageView view, int page, double pageWidth, double pageHeight)
    {
        if (view.PageBounds(page) is { } bounds)
        {
            var visible = bounds.Intersect(new Rect(view.Bounds.Size));
            var target = visible.Width > 0 && visible.Height > 0 ? visible.Center : bounds.Center;
            if (view.PageAt(target) is { } spot && spot.Page == page)
            {
                return new DisplayPoint(spot.X, spot.Y);
            }
        }

        return new DisplayPoint(pageWidth / 2, pageHeight / 2);
    }

    private async Task TellAsync(string message)
    {
        try
        {
            await dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.SignaturePlaceTitle, message, () => done(true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The card could not be shown; nothing was placed either way.
        }
    }
}
