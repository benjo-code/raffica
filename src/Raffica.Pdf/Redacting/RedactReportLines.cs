using System.Globalization;
using Raffica.Core.Redaction;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// The lines of the Apply card and of the result card, in the UI language and in the order of the report of Core (<see cref="RedactionReportText"/> is
/// the model, which words the same things in English for the command line). The cards show these lines under their headings; the numbers are the
/// analysis'. No line contains the redacted text or a value of the document.
/// </summary>
internal static class RedactReportLines
{
    private static string Number(long value) => value.ToString(CultureInfo.CurrentCulture);

    /// <summary>Puts the values into {0}, {1}, ... of a template (the table texts take more than three values here).</summary>
    internal static string Fill(string template, params string[] values)
    {
        var text = template;
        for (var i = 0; i < values.Length; i++)
        {
            text = text.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", values[i], StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>One line per marked page: what is removed there, by counts. Pages turned into images have their own lines.</summary>
    public static IReadOnlyList<string> Removed(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var lines = new List<string>();
        foreach (var page in analysis.Pages)
        {
            var parts = new List<string>();
            Add(parts, PdfStrings.RedactCountChars, page.CharsRemoved);
            Add(parts, PdfStrings.RedactCountSplit, page.TextObjectsSplit);
            Add(parts, PdfStrings.RedactCountTextWhole, page.TextObjectsRemovedWhole);
            Add(parts, PdfStrings.RedactCountImagesOver, page.ImagesOverwritten);
            Add(parts, PdfStrings.RedactCountImagesWhole, page.ImagesRemovedWhole);
            Add(parts, PdfStrings.RedactCountPathsCut, page.PathsCropped);
            Add(parts, PdfStrings.RedactCountPathsWhole, page.PathsRemovedWhole);
            Add(parts, PdfStrings.RedactCountForms, page.FormObjectsTouched);
            Add(parts, PdfStrings.RedactCountAnnots, page.AnnotationsRemoved);
            var number = Number(page.Page + 1);
            lines.Add(parts.Count == 0
                ? PdfStrings.Fill(PdfStrings.RedactPageNothingFormat, number)
                : PdfStrings.Fill(PdfStrings.RedactPageLineFormat, number, string.Join("; ", parts)));
        }

        return lines;
    }

    private static void Add(List<string> parts, string format, int count)
    {
        if (count > 0)
        {
            parts.Add(PdfStrings.Fill(format, Number(count)));
        }
    }

    /// <summary>What the engine removed as a whole instead of precisely, one line each with its page.</summary>
    public static IReadOnlyList<string> RemovedWhole(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return [.. analysis.Fallbacks.Select(f => PdfStrings.Fill(PdfStrings.RedactPageLineFormat, Number(f.Page + 1), RedactWording.Text(f.Kind)))];
    }

    /// <summary>The pages that become images: resolution, size, the estimate and what is lost with them.</summary>
    public static IReadOnlyList<string> RasterPages(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return [.. (analysis.Raster ?? []).Select(Raster)];
    }

    /// <summary>One page that becomes an image.</summary>
    public static string Raster(RasterPageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var megabytes = Math.Max(1, (info.EstimatedBytes + (1024 * 1024) - 1) / (1024 * 1024));
        var text = Fill(PdfStrings.RedactRasterInfoFormat, Number(info.Page + 1), Number(info.Dpi), Number(info.WidthPx), Number(info.HeightPx), Number(megabytes));
        return info.Lowered ? text + " " + PdfStrings.RedactRasterLowered : text;
    }

    /// <summary>The image areas that are pixelated or blurred, the ones that are filled solid instead and why. Empty for a plan that fills image areas solid.</summary>
    public static IReadOnlyList<string> ImageAreas(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (analysis.ImageTreatment == RedactionImageTreatment.Solid)
        {
            return [];
        }

        var treated = analysis.Pages.Sum(static p => p.ImageAreasTreated);
        var solid = analysis.Pages.Sum(static p => p.ImageAreasSolid);
        var lines = new List<string>
        {
            PdfStrings.Fill(analysis.ImageTreatment == RedactionImageTreatment.Pixelate ? PdfStrings.RedactImgPixelatedFormat : PdfStrings.RedactImgBlurredFormat, Number(treated)),
        };
        foreach (var group in analysis.Pages.SelectMany(static p => p.ImageSolidReasons ?? []).GroupBy(static r => r).OrderBy(static g => g.Key))
        {
            lines.Add(PdfStrings.Fill(PdfStrings.RedactImgSolidFormat, Number(group.Count()), RedactWording.Text(group.Key)));
        }

        if (solid > 0 && lines.Count == 1)
        {
            lines.Add(PdfStrings.Fill(PdfStrings.RedactImgSolidFormat, Number(solid), PdfStrings.RedactSolidUnreadable));
        }

        // The mandatory caution: text inside a treated image can stay legible.
        lines.Add(PdfStrings.RedactImageCaution);
        return lines;
    }

    /// <summary>Overlay text that is left out (a treated image under the area, a text that does not fit, the limit).</summary>
    public static IReadOnlyList<string> Overlay(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var lines = new List<string>();
        if (analysis.OverlaysNotShown > 0)
        {
            lines.Add(PdfStrings.Fill(PdfStrings.RedactOverlayNotShownFormat, Number(analysis.OverlaysNotShown)));
        }

        if (analysis.OverlaysNotFitting > 0)
        {
            lines.Add(PdfStrings.Fill(PdfStrings.RedactOverlayNotFitFormat, Number(analysis.OverlaysNotFitting)));
            if (analysis.OverlaysOverLimit > 0)
            {
                lines.Add(PdfStrings.Fill(PdfStrings.RedactOverlayLimitFormat, Number(analysis.OverlaysOverLimit)));
            }
        }

        return lines;
    }

    /// <summary>
    /// What the cleanup removes (always; never a switch). <paramref name="confirmed"/>: after the write, where XMP and JavaScript are left out because
    /// the read-back cannot confirm them (the check says so under "Not checked"). A field of the information dictionary is named, never shown.
    /// </summary>
    public static IReadOnlyList<string> Cleaned(RedactionAnalysis analysis, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var lines = new List<string>();
        foreach (var item in analysis.Cleanup)
        {
            if (confirmed && item.Kind is CleanupKind.Xmp or CleanupKind.JavaScript)
            {
                continue;
            }

            lines.Add(RedactWording.Text(item));
        }

        if (lines.Count == 0)
        {
            lines.Add(analysis.CleanupComplete ? PdfStrings.RedactCleanNone : PdfStrings.RedactCleanIncomplete);
        }
        else if (!analysis.CleanupComplete)
        {
            lines.Add(PdfStrings.RedactCleanIncomplete);
        }

        return lines;
    }

    /// <summary>What the rebuild loses.</summary>
    public static IReadOnlyList<string> Lost(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return [.. analysis.Losses.Select(loss => RedactWording.Text(loss.Kind, loss.Count))];
    }

    /// <summary>What is not cleaned and comes along with the pages.</summary>
    public static IReadOnlyList<string> NotCleaned(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var lines = new List<string> { PdfStrings.RedactNotCleanedMeta, PdfStrings.RedactNotCleanedActions, PdfStrings.RedactNotCleanedAnnots };
        if (analysis.AttachmentAnnotations > 0)
        {
            lines.Add(PdfStrings.Fill(PdfStrings.RedactNotCleanedAttachFormat, Number(analysis.AttachmentAnnotations)));
        }

        if (analysis.MediaAnnotations > 0)
        {
            lines.Add(PdfStrings.Fill(PdfStrings.RedactNotCleanedMediaFormat, Number(analysis.MediaAnnotations)));
        }

        if (analysis.Losses.Any(static l => l.Kind == RedactionLossKind.FormFieldsNotFillable))
        {
            lines.Add(PdfStrings.RedactNotCleanedFormValues);
        }

        if (analysis.Losses.Any(static l => l.Kind == RedactionLossKind.SignatureInvalidated))
        {
            lines.Add(PdfStrings.RedactNotCleanedSignature);
        }

        return lines;
    }

    /// <summary>What the check did: only for a result that passed it, and only what the check does (the analysis names the plan it checked).</summary>
    public static IReadOnlyList<string> Checked(RedactionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var lines = new List<string>();
        if (analysis.Marks > analysis.MarksWithoutTerm)
        {
            lines.Add(PdfStrings.RedactChkPageText);
            lines.Add(PdfStrings.RedactChkBytes);
        }

        lines.Add(analysis.ImageTreatment == RedactionImageTreatment.Solid ? PdfStrings.RedactChkAreasSolid : PdfStrings.RedactChkAreasTreated);
        if (analysis.Raster is not { Count: > 0 } raster)
        {
            lines.Add(PdfStrings.RedactChkPagesAll);
        }
        else
        {
            if (analysis.Pages.Any(page => raster.All(r => r.Page != page.Page)))
            {
                lines.Add(PdfStrings.RedactChkPagesInPlace);
            }

            lines.Add(PdfStrings.Fill(PdfStrings.RedactChkRasterFormat, string.Join(", ", raster.Select(r => Number(r.Page + 1)))));
        }

        lines.Add(PdfStrings.RedactChkKeys);
        lines.Add(PdfStrings.RedactChkCleanup);
        return lines;
    }

    /// <summary>What the check did not check, in the order of the check, then what the interface adds (marks without a text, a scan that could not read everything).</summary>
    public static IReadOnlyList<string> NotChecked(RedactionAnalysis analysis, RedactionCheck check)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(check);
        var lines = new List<string>();
        if (check.NotCheckedItems is { } items)
        {
            lines.AddRange(items.Select(RedactWording.Text));
        }
        else
        {
            // A check made of text alone has no data: say the one thing that is true.
            lines.AddRange(check.NotChecked.Select(static _ => PdfStrings.RedactNcOther));
        }

        if (analysis.MarksWithoutTerm > 0)
        {
            lines.Add(PdfStrings.Fill(PdfStrings.RedactNcNoTermFormat, Number(analysis.MarksWithoutTerm)));
        }

        if (!analysis.CleanupComplete)
        {
            lines.Add(PdfStrings.RedactCleanIncomplete);
        }

        return lines;
    }
}
