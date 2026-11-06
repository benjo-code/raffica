using System.Globalization;
using Raffica.Core.Ocr;

namespace Raffica.Pdf.Ocr;

/// <summary>What a finished run says, as texts: the title, the summary and the details (one line each). Never contains the recognized text.</summary>
/// <param name="Title">The title of the card.</param>
/// <param name="Summary">The counts of the run.</param>
/// <param name="Lines">The lines below it: what was written, the pages that were not recognized (one line per page), notes on pages and on the undo history.</param>
internal sealed record OcrReportText(string Title, string Summary, IReadOnlyList<string> Lines);

/// <summary>
/// The words of the text recognition in the UI language. Core's English sentences are never shown: every <see cref="OcrSkipReason"/> and every
/// <see cref="OcrFailureKind"/> that reaches a card has a text of its own here. Nothing in this class takes the recognized words, so a report cannot contain them,
/// and none of the texts claims that the result is right.
/// </summary>
internal static class OcrWording
{
    /// <summary>The most page numbers named in one line of the report; the rest is counted.</summary>
    private const int PagesNamed = 20;

    /// <summary>Why a page was not recognized. <paramref name="detail"/> is the resolution Core names ("72 dpi") for a resolution that is too low.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The reason has no text.</exception>
    public static string Text(OcrSkipReason reason, string? detail) => reason switch
    {
        OcrSkipReason.HasText => PdfStrings.OcrSkipHasText,
        OcrSkipReason.AlreadyRecognized => PdfStrings.OcrSkipAlreadyRecognized,
        OcrSkipReason.TooLarge => PdfStrings.OcrSkipTooLarge,
        OcrSkipReason.ResolutionTooLow => DpiOf(detail) is { } dpi ? PdfStrings.Fill(PdfStrings.OcrSkipResolutionTooLowFormat, dpi) : PdfStrings.OcrSkipResolutionTooLow,
        OcrSkipReason.Timeout => PdfStrings.OcrSkipTimeout,
        OcrSkipReason.Failed => PdfStrings.OcrSkipFailed,
        OcrSkipReason.NoSupportedCharacters => PdfStrings.OcrSkipNoSupportedCharacters,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "This reason has no text."),
    };

    /// <summary>Why the whole run could not start or went on (the library or a language file). <paramref name="languages"/> names the files that are missing, when known.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The kind has no text.</exception>
    public static string Text(OcrFailureKind kind, IReadOnlyList<string>? languages = null) => kind switch
    {
        OcrFailureKind.LibraryMissing => PdfStrings.OcrNoLibraryReason,
        OcrFailureKind.LibraryUnusable => PdfStrings.OcrFailLibraryUnusable,
        OcrFailureKind.LanguageInvalid => PdfStrings.OcrFailLanguageInvalid,
        OcrFailureKind.LanguageMissing => languages is { Count: > 0 } ? PdfStrings.Fill(PdfStrings.OcrFailLanguageMissingFormat, string.Join(", ", languages)) : PdfStrings.OcrFailLanguageMissingNone,
        OcrFailureKind.DataCorrupt => PdfStrings.OcrFailDataCorrupt,
        OcrFailureKind.Failed => PdfStrings.OcrFailFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This failure has no text."),
    };

    /// <summary>The words of a failure.</summary>
    public static string Text(OcrFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return Text(failure.Kind, failure.Languages);
    }

    /// <summary>The report of a run: the counts, what was written (when the run was applied), every skipped page with its reason and the notes on pages.</summary>
    /// <param name="report">The report of the runner.</param>
    /// <param name="applied">What writing did, or <see langword="null"/> when nothing was written.</param>
    /// <param name="canceled">Whether the run was canceled (and its finished pages kept).</param>
    public static OcrReportText Report(OcrReport report, OcrApplyResult? applied, bool canceled)
    {
        ArgumentNullException.ThrowIfNull(report);
        var title = canceled ? PdfStrings.OcrReportCanceledTitle : PdfStrings.OcrReportTitle;
        var summary = PdfStrings.Fill(
            PdfStrings.OcrReportSummaryFormat,
            report.PagesRecognized,
            report.Words,
            Math.Round(report.MeanConfidence, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.CurrentCulture));
        var lines = new List<string>();
        if (applied is null)
        {
            lines.Add(PdfStrings.OcrReportNothingWritten);
        }
        else
        {
            lines.Add(PdfStrings.Fill(PdfStrings.OcrReportWrittenFormat, applied.WordsWritten, applied.Pages.Count));
            if (applied.WordsLeftOut > 0)
            {
                lines.Add(PdfStrings.Fill(PdfStrings.OcrReportLeftOutFormat, applied.WordsLeftOut));
            }

            if (applied.AlreadyRecognized.Count > 0)
            {
                lines.Add(PdfStrings.Fill(PdfStrings.OcrReportKeptLayerFormat, Pages(applied.AlreadyRecognized)));
            }
        }

        foreach (var skipped in report.Skipped)
        {
            lines.Add(SkippedLine(skipped));
        }

        AddPages(lines, PdfStrings.OcrReportLowFormat, report.LowConfidencePages);
        AddPages(lines, PdfStrings.OcrReportEmptyFormat, report.PagesWithoutWords);
        AddPages(lines, PdfStrings.OcrReportLittleFormat, report.LittleTextPages);
        AddPages(lines, PdfStrings.OcrReportLoweredFormat, report.LoweredPages);
        if (applied is not null)
        {
            lines.Add(applied.Undoable ? PdfStrings.OcrReportUndoable : PdfStrings.OcrReportNotUndoable);
        }

        return new OcrReportText(title, summary, lines);
    }

    /// <summary>The line of one page that was not recognized: its number (from 1) and the reason.</summary>
    public static string SkippedLine(OcrSkippedPage skipped)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        return PdfStrings.Fill(PdfStrings.OcrReportSkippedLineFormat, skipped.Page + 1, Text(skipped.Reason, skipped.Detail));
    }

    private static void AddPages(List<string> lines, string format, IReadOnlyList<int> pages)
    {
        if (pages.Count > 0)
        {
            lines.Add(PdfStrings.Fill(format, Pages(pages)));
        }
    }

    // The page numbers (from 1) in order, at most PagesNamed of them, then the count of the rest.
    private static string Pages(IReadOnlyList<int> pages)
    {
        var ordered = pages.Order().Select(static p => (p + 1).ToString(CultureInfo.CurrentCulture)).ToList();
        var shown = string.Join(", ", ordered.Take(PagesNamed));
        return ordered.Count > PagesNamed ? PdfStrings.Fill(PdfStrings.OcrReportMoreFormat, shown, ordered.Count - PagesNamed) : shown;
    }

    // Core names a resolution as "72 dpi"; only the number is taken, the unit and the sentence are ours.
    private static int? DpiOf(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var digits = new string([.. detail.TakeWhile(char.IsDigit)]);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var dpi) && dpi > 0 ? dpi : null;
    }
}
