using System.Globalization;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;

namespace Raffica.Pdf.Viewing;

/// <summary>
/// The pure parts of the view commands: how the mouse wheel moves the zoom and the texts of the status bar cells (position, count, zoom)
/// and of the page size. Numbers follow the UI language (<see cref="PdfStrings.Culture"/>).
/// </summary>
internal static class ViewCommands
{
    // A guard for absurd wheel deltas; the whole zoom table has twelve steps.
    private const int MaxWheelSteps = 12;

    private const double MillimetresPerPoint = 25.4 / 72.0;

    /// <summary>
    /// The zoom after a Ctrl + wheel turn: one step of <see cref="ZoomSteps"/> per whole notch (positive zooms in). The parts of a notch that
    /// a touch pad reports are collected in <paramref name="remainder"/> until they make a step. A delta that is not a number changes nothing.
    /// </summary>
    public static double ZoomByWheel(double zoom, double delta, ref double remainder)
    {
        if (!double.IsFinite(delta))
        {
            return zoom;
        }

        remainder += delta;
        var steps = (int)Math.Clamp(Math.Truncate(remainder), -MaxWheelSteps, MaxWheelSteps);
        remainder = Math.Clamp(remainder - Math.Truncate(remainder), -1, 1);
        for (var i = 0; i < Math.Abs(steps); i++)
        {
            zoom = steps > 0 ? ZoomSteps.Next(zoom) : ZoomSteps.Previous(zoom);
        }

        return zoom;
    }

    /// <summary>The zoom cell: the zoom as a whole percentage, for example <c>125%</c>.</summary>
    public static string ZoomText(double zoom) =>
        (double.IsFinite(zoom) ? Math.Round(zoom * 100, MidpointRounding.AwayFromZero) : 100).ToString("0", PdfStrings.Culture) + "%";

    /// <summary>The position cell: <c>Page 3 of 12</c> (the page is the number the user sees, counted from 1).</summary>
    public static string PositionText(int page, int pageCount) =>
        PdfStrings.Fill(PdfStrings.StatusPositionFormat, page, pageCount);

    /// <summary>The count cell: the number of pages and, when it is known, the size of the current page.</summary>
    public static string CountText(int pageCount, PdfPageSize? size)
    {
        var pages = pageCount == 1
            ? PdfStrings.StatusPageCountOne
            : PdfStrings.Fill(PdfStrings.StatusPageCountFormat, pageCount);
        return size is { } known ? PdfStrings.Fill(PdfStrings.StatusCountWithSizeFormat, pages, PageSizeText(known, precise: false)) : pages;
    }

    /// <summary>The size of a page in millimetres, <c>210 × 297 mm</c>; <paramref name="precise"/> keeps one decimal (<c>215.9 × 279.4 mm</c>).</summary>
    public static string PageSizeText(PdfPageSize size, bool precise)
    {
        var decimals = precise ? 1 : 0;
        return PdfStrings.Fill(PdfStrings.PageSizeFormat, Millimetres(size.Width, decimals), Millimetres(size.Height, decimals));
    }

    private static string Millimetres(double points, int decimals)
    {
        var value = double.IsFinite(points) ? points * MillimetresPerPoint : 0;
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString(decimals == 0 ? "0" : "0.#", PdfStrings.Culture);
    }
}
