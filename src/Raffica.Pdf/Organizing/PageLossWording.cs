using System.Globalization;
using Raffica.Core.Pages;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// Words what applying a page plan loses, in the UI language: one sentence per <see cref="PageLoss"/>, chosen by its <see cref="PageLossKind"/>
/// (the model is the command line's <c>PageLossText</c>, which words the same losses in English). The detail of a loss is a key, never document
/// content: <c>source:N</c> is named with the inserted file's display name, <c>docmdp:N</c> says the change level, the <see cref="PageLossDetail"/>
/// constants refine a kind. No text calls a signature "valid" or anything "legally binding".
/// </summary>
internal static class PageLossWording
{
    // A list of pages is shown in full up to this many pages, else the first ones and the count of the rest.
    private const int PagesShown = 6;
    private const int PagesShownWhenShortened = 5;

    /// <summary>Words one loss as a sentence (and the pages it concerns).</summary>
    /// <param name="loss">The loss.</param>
    /// <param name="fileName">The display name of the inserted file with this number, or null when it is not known.</param>
    public static string Describe(PageLoss loss, Func<int, string?>? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(loss);
        var source = SourceLabel(loss.Detail, fileName);
        var pages = loss.Pages is { Count: > 0 } list ? list : null;
        return loss.Kind switch
        {
            PageLossKind.SignatureInvalidated => PdfStrings.LossSignature + Level(loss.Detail),
            PageLossKind.PageLabelsShifted when source is not null => PdfStrings.Fill(PdfStrings.LossLabelsSourceFormat, source),
            PageLossKind.PageLabelsShifted => PdfStrings.LossLabelsShifted,
            PageLossKind.ForeignOutlineDropped => PdfStrings.Fill(PdfStrings.LossForeignOutlineFormat, source ?? "?"),
            PageLossKind.BookmarkToDeletedPage => PdfStrings.LossBookmarkDeleted + Pages(pages, original: true),
            PageLossKind.DuplicatedFormFields when loss.Detail == PageLossDetail.Inserted => PdfStrings.LossFormInserted + Pages(pages, original: false),
            PageLossKind.DuplicatedFormFields => PdfStrings.LossFormDuplicated + Pages(pages, original: false),
            PageLossKind.FormFieldNameCollision when loss.Detail == PageLossDetail.Unchecked => PdfStrings.LossNameUnchecked,
            PageLossKind.FormFieldNameCollision => PdfStrings.LossNameCollision + Pages(pages, original: false),
            PageLossKind.EncryptionNotPreserved when loss.Blocks => PdfStrings.Fill(PdfStrings.LossEncryptionBlocksFormat, source ?? "?"),
            PageLossKind.EncryptionNotPreserved => PdfStrings.Fill(PdfStrings.LossEncryptionWarnFormat, source ?? "?"),
            PageLossKind.InternalLinkLost when loss.Detail == PageLossDetail.CopiedPage => PdfStrings.LossLinkCopied + Pages(pages, original: false),
            PageLossKind.InternalLinkLost => PdfStrings.LossLinkDeleted + Pages(pages, original: false),
            PageLossKind.OrphanFormFields => PdfStrings.LossOrphan + Pages(pages, original: true),
            _ => PdfStrings.LossOther,
        };
    }

    /// <summary>The reason a report that <see cref="PageEditReport.IsRefused"/> is refused: the sentences of the losses that block.</summary>
    public static string RefusalOf(PageEditReport report, Func<int, string?>? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        return string.Join(" ", report.Losses.Where(static loss => loss.Blocks).Select(loss => Describe(loss, fileName)));
    }

    /// <summary>Formats 0-based pages as 1-based text: "3", "3 and 5", "3, 5 and 9"; a long list shows its first pages and the count of the rest.</summary>
    public static string PageList(IReadOnlyList<int> zeroBasedPages)
    {
        ArgumentNullException.ThrowIfNull(zeroBasedPages);
        var numbers = zeroBasedPages.Distinct().Order().Select(page => (page + 1).ToString(CultureInfo.InvariantCulture)).ToArray();
        var and = PdfStrings.LossListAnd;
        if (numbers.Length > PagesShown)
        {
            return PdfStrings.Fill(PdfStrings.LossListMoreFormat, string.Join(", ", numbers.Take(PagesShownWhenShortened)), numbers.Length - PagesShownWhenShortened);
        }

        return numbers.Length <= 1 ? string.Concat(numbers) : string.Join(", ", numbers[..^1]) + and + numbers[^1];
    }

    // The pages after the sentence: positions of the new file for most kinds, pages of the open document for the ones about deleted pages.
    private static string Pages(IReadOnlyList<int>? pages, bool original)
    {
        if (pages is null)
        {
            return string.Empty;
        }

        var one = pages.Distinct().Count() == 1;
        var template = (original, one) switch
        {
            (true, true) => PdfStrings.LossPageOriginalFormat,
            (true, false) => PdfStrings.LossPagesOriginalFormat,
            (false, true) => PdfStrings.LossPageResultFormat,
            _ => PdfStrings.LossPagesResultFormat,
        };
        return PdfStrings.Fill(template, PageList(pages));
    }

    private static string Level(string? detail) =>
        detail is not null && detail.StartsWith("docmdp:", StringComparison.Ordinal) && int.TryParse(detail.AsSpan("docmdp:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            ? " " + PdfStrings.Fill(PdfStrings.LossSignatureLevelFormat, level)
            : string.Empty;

    // The display name of an inserted file in quotes, else its number.
    private static string? SourceLabel(string? detail, Func<int, string?>? fileName)
    {
        if (detail is null || !detail.StartsWith("source:", StringComparison.Ordinal))
        {
            return null;
        }

        var number = detail["source:".Length..];
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && fileName?.Invoke(id) is { } name ? $"\"{name}\"" : number;
    }
}
