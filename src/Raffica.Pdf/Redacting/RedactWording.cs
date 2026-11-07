using Raffica.Core.Redaction;
using Raffica.Pdf.Organizing;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// Words what Core says about a redaction in the UI language: every enum that reaches a card has one text per member (a member without wording throws,
/// and <c>RedactWordingTests</c> walks every member of every enum, so a new member cannot go unworded). The cards speak from these tables, never from the
/// English sentences of Core. A text never contains the redacted text or a value of the document: the detail of a cleanup item is shown only for the
/// kinds that are counts, and the detail of a finding or a not-checked line (a filter name, a reason) is never shown at all.
/// No text claims that something is safe, complete or guaranteed, and no text calls a signature "valid".
/// </summary>
internal static class RedactWording
{
    /// <summary>Words a refusal of the analysis (the pages are 0-based; <paramref name="unverified"/>: the page keys could not be checked).</summary>
    public static string Text(RedactionRefusalKind kind, IReadOnlyList<int>? pages = null, bool unverified = false)
    {
        var where = Pages(pages);
        return kind switch
        {
            RedactionRefusalKind.Encrypted => PdfStrings.RedactRefusedEncrypted,
            RedactionRefusalKind.UnremovablePageKeys when unverified => PdfStrings.RedactRefusedKeysUnverified,
            RedactionRefusalKind.UnremovablePageKeys => PdfStrings.Fill(PdfStrings.RedactRefusedKeysFormat, where),
            RedactionRefusalKind.PageNotRedactable => PdfStrings.Fill(PdfStrings.RedactRefusedNotRedactableFormat, where),
            RedactionRefusalKind.OverlayRepeatsTerm => PdfStrings.Fill(PdfStrings.RedactRefusedOverlayFormat, where),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This refusal has no text."),
        };
    }

    /// <summary>Words one refusal of the analysis.</summary>
    public static string Text(RedactionRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Text(refusal.Kind, refusal.Pages, refusal.PageKeysUnverified);
    }

    /// <summary>Words what the engine removed as a whole (a phrase that follows "Page N: ").</summary>
    public static string Text(RedactionFallbackKind kind) => kind switch
    {
        RedactionFallbackKind.TextObjectRemovedWhole => PdfStrings.RedactFallbackTextObject,
        RedactionFallbackKind.ImageRemovedWhole => PdfStrings.RedactFallbackImage,
        RedactionFallbackKind.PathRemovedWhole => PdfStrings.RedactFallbackPath,
        RedactionFallbackKind.FormObjectRemovedWhole => PdfStrings.RedactFallbackForm,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This fallback has no text."),
    };

    /// <summary>Words one loss of the rebuild with its count.</summary>
    public static string Text(RedactionLossKind kind, int count) => kind switch
    {
        RedactionLossKind.InternalLinksLoseTheirTarget => PdfStrings.Fill(PdfStrings.RedactLossLinksFormat, count),
        RedactionLossKind.FormFieldsNotFillable => PdfStrings.Fill(PdfStrings.RedactLossFormsFormat, count),
        RedactionLossKind.ColorSpaceBecomesRgb => PdfStrings.Fill(PdfStrings.RedactLossRgbFormat, count),
        RedactionLossKind.SignatureInvalidated => PdfStrings.Fill(PdfStrings.RedactLossSignatureFormat, count),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This loss has no text."),
    };

    /// <summary>
    /// Words one thing the cleanup removes. A field of the information dictionary is named, never shown with its value (<paramref name="detail"/>
    /// is ignored); the others are counts, and <paramref name="detail"/> is the number the scan found.
    /// </summary>
    public static string Text(CleanupKind kind, string detail = "")
    {
        ArgumentNullException.ThrowIfNull(detail);
        return kind switch
        {
            CleanupKind.Author => PdfStrings.RedactCleanAuthor,
            CleanupKind.Title => PdfStrings.RedactCleanTitle,
            CleanupKind.Subject => PdfStrings.RedactCleanSubject,
            CleanupKind.Keywords => PdfStrings.RedactCleanKeywords,
            CleanupKind.Creator => PdfStrings.RedactCleanCreator,
            CleanupKind.Producer => PdfStrings.RedactCleanProducer,
            CleanupKind.Xmp => PdfStrings.Fill(PdfStrings.RedactCleanXmpFormat, detail),
            CleanupKind.Bookmarks => PdfStrings.Fill(PdfStrings.RedactCleanBookmarksFormat, detail),
            CleanupKind.Attachments => PdfStrings.Fill(PdfStrings.RedactCleanAttachmentsFormat, detail),
            CleanupKind.JavaScript => PdfStrings.Fill(PdfStrings.RedactCleanJavaScriptFormat, detail),
            CleanupKind.PageLabels => PdfStrings.Fill(PdfStrings.RedactCleanLabelsFormat, detail),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This cleanup item has no text."),
        };
    }

    /// <summary>Words one item of the cleanup list (the value of a field of the information dictionary is never shown).</summary>
    public static string Text(CleanupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Text(item.Kind, item.Detail);
    }

    /// <summary>Words why an image area got the solid fill (a phrase that follows "because").</summary>
    public static string Text(ImageSolidReason reason) => reason switch
    {
        ImageSolidReason.TooSmall => PdfStrings.RedactSolidTooSmall,
        ImageSolidReason.TurnedOrSheared => PdfStrings.RedactSolidTurned,
        ImageSolidReason.Clipped => PdfStrings.RedactSolidClipped,
        ImageSolidReason.Overlapping => PdfStrings.RedactSolidOverlapping,
        ImageSolidReason.Unreadable => PdfStrings.RedactSolidUnreadable,
        ImageSolidReason.TooManyImages => PdfStrings.RedactSolidTooMany,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "This reason has no text."),
    };

    /// <summary>Words one line of what the check did not check; the English line of Core and its technical detail are not shown.</summary>
    public static string Text(RedactionNotCheckedKind kind, RedactionNotChecked item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return kind switch
        {
            RedactionNotCheckedKind.Other => PdfStrings.RedactNcOther,
            RedactionNotCheckedKind.BlankTexts => PdfStrings.Fill(PdfStrings.RedactNcBlankFormat, item.Count),
            RedactionNotCheckedKind.UnreadableChars => PdfStrings.Fill(PdfStrings.RedactNcUnreadableCharsFormat, item.Count),
            RedactionNotCheckedKind.TruncatedTexts => PdfStrings.Fill(PdfStrings.RedactNcTruncatedFormat, item.Count),
            RedactionNotCheckedKind.FurtherTexts => PdfStrings.Fill(PdfStrings.RedactNcFurtherFormat, item.Count, item.Second),
            RedactionNotCheckedKind.RawShortTexts => PdfStrings.Fill(PdfStrings.RedactNcRawShortFormat, item.Count, item.Second),
            RedactionNotCheckedKind.RawNothingRemoved => PdfStrings.RedactNcRawNothing,
            RedactionNotCheckedKind.RawStillOccur => PdfStrings.Fill(PdfStrings.RedactNcRawStillOccurFormat, item.Count, item.Second),
            RedactionNotCheckedKind.RawFilter => PdfStrings.Fill(PdfStrings.RedactNcRawFilterFormat, item.Count),
            RedactionNotCheckedKind.RawImageStreams => PdfStrings.Fill(PdfStrings.RedactNcRawImageStreamsFormat, item.Count),
            RedactionNotCheckedKind.RawUnreadableStream => PdfStrings.Fill(PdfStrings.RedactNcRawUnreadableFormat, item.Count),
            RedactionNotCheckedKind.RawStreamTooLarge => PdfStrings.Fill(PdfStrings.RedactNcRawTooLargeFormat, item.Count),
            RedactionNotCheckedKind.RawTotalLimit => PdfStrings.Fill(PdfStrings.RedactNcRawTotalFormat, item.Count),
            RedactionNotCheckedKind.RawPredictor => PdfStrings.Fill(PdfStrings.RedactNcRawPredictorFormat, item.Count),
            RedactionNotCheckedKind.RawEncrypted => PdfStrings.RedactNcRawEncrypted,
            RedactionNotCheckedKind.RawFontMapping => PdfStrings.RedactNcRawFontMapping,
            RedactionNotCheckedKind.RawEncodings => PdfStrings.RedactNcRawEncodings,
            RedactionNotCheckedKind.RawSplitText => PdfStrings.RedactNcRawSplit,
            RedactionNotCheckedKind.RawImageAttachments => PdfStrings.RedactNcRawImageAttachments,
            RedactionNotCheckedKind.PixelsBeyond => PdfStrings.Fill(PdfStrings.RedactNcPixelsBeyondFormat, item.Count, item.Second),
            RedactionNotCheckedKind.PixelsOnlyImages => PdfStrings.RedactNcPixelsOnlyImages,
            RedactionNotCheckedKind.OtherViewer => PdfStrings.RedactNcOtherViewer,
            RedactionNotCheckedKind.ApplyRender => item.Count > 0 ? PdfStrings.Fill(PdfStrings.RedactNcApplyRenderRasterFormat, item.Count) : PdfStrings.RedactNcApplyRender,
            RedactionNotCheckedKind.ApplyPageKeys => PdfStrings.RedactNcApplyPageKeys,
            RedactionNotCheckedKind.ApplyXmp => PdfStrings.RedactNcApplyXmp,
            RedactionNotCheckedKind.RasterOutsideAreas => PdfStrings.Fill(PdfStrings.RedactNcRasterOutsideFormat, item.Count),
            RedactionNotCheckedKind.ImageTextLegible => PdfStrings.Fill(PdfStrings.RedactNcImageTextFormat, item.Count, item.Second),
            RedactionNotCheckedKind.OverlayTextNotRead => PdfStrings.RedactNcOverlayNotRead,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This not-checked line has no text."),
        };
    }

    /// <summary>Words one line of what the check did not check.</summary>
    public static string Text(RedactionNotChecked item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Text(item.Kind, item);
    }

    /// <summary>Words what a failed check found, by the kind it is reported as (<paramref name="pages"/> are 0-based; none: the file).</summary>
    public static string Text(RedactionFailureKind kind, IReadOnlyList<int>? pages = null)
    {
        var where = Pages(pages);
        return kind switch
        {
            RedactionFailureKind.UnremovableContent => PdfStrings.Fill(PdfStrings.RedactFailUnremovableFormat, where),
            RedactionFailureKind.TextStillPresent => PdfStrings.Fill(PdfStrings.RedactFailTextFormat, where),
            RedactionFailureKind.CharInsideArea => PdfStrings.Fill(PdfStrings.RedactFailCharFormat, where),
            RedactionFailureKind.ImagePixelNotOverwritten => PdfStrings.Fill(PdfStrings.RedactFailPixelFormat, where),
            RedactionFailureKind.UnreadableStream => PdfStrings.Fill(PdfStrings.RedactFailUnreadableFormat, where),
            RedactionFailureKind.PageKeysStillPresent => PdfStrings.Fill(PdfStrings.RedactFailKeysFormat, where),
            RedactionFailureKind.PageChangedOutsideAreas => PdfStrings.Fill(PdfStrings.RedactFailOutsideFormat, where),
            RedactionFailureKind.RemovedTextUnverifiable => PdfStrings.Fill(PdfStrings.RedactFailUnverifiableFormat, where),
            RedactionFailureKind.CleanupItemStillPresent => PdfStrings.Fill(PdfStrings.RedactFailCleanupFormat, where),
            RedactionFailureKind.RasterPageInvalid => PdfStrings.Fill(PdfStrings.RedactFailRasterFormat, where),
            RedactionFailureKind.ImageTreatmentWrong => PdfStrings.Fill(PdfStrings.RedactFailTreatmentFormat, where),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This failure has no text."),
        };
    }

    /// <summary>
    /// Words a kind of finding of the check (the same words as the failure it is reported as; a finding of the file bytes is the unremovable content). Every kind is
    /// listed here, so a kind that Core adds has no wording until it is given one, and the test of the wording says so.
    /// </summary>
    public static string Text(RedactionFindingKind kind, IReadOnlyList<int>? pages = null) => Text(
        kind switch
        {
            RedactionFindingKind.RawBytesStillPresent => RedactionFailureKind.UnremovableContent,
            RedactionFindingKind.TextStillPresent => RedactionFailureKind.TextStillPresent,
            RedactionFindingKind.CharInsideArea => RedactionFailureKind.CharInsideArea,
            RedactionFindingKind.ImagePixelNotOverwritten => RedactionFailureKind.ImagePixelNotOverwritten,
            RedactionFindingKind.UnreadableStream => RedactionFailureKind.UnreadableStream,
            RedactionFindingKind.PageKeysStillPresent => RedactionFailureKind.PageKeysStillPresent,
            RedactionFindingKind.PageChangedOutsideAreas => RedactionFailureKind.PageChangedOutsideAreas,
            RedactionFindingKind.RemovedTextUnverifiable => RedactionFailureKind.RemovedTextUnverifiable,
            RedactionFindingKind.CleanupItemStillPresent => RedactionFailureKind.CleanupItemStillPresent,
            RedactionFindingKind.RasterPageInvalid => RedactionFailureKind.RasterPageInvalid,
            RedactionFindingKind.ImageTreatmentWrong => RedactionFailureKind.ImageTreatmentWrong,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This finding has no text."),
        },
        pages);

    /// <summary>Words one finding of the check with the pages it concerns; the detail of the finding (English, technical) is not shown.</summary>
    public static string Text(RedactionFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return Text(RedactionRefusalText.KindOf(finding), RedactionRefusalText.PagesOf(finding));
    }

    /// <summary>"page 3", "pages 3 and 5", or "the file" for none (0-based pages in, 1-based text out).</summary>
    public static string Pages(IReadOnlyList<int>? zeroBasedPages)
    {
        if (zeroBasedPages is not { Count: > 0 })
        {
            return PdfStrings.RedactPagesNone;
        }

        var list = PageLossWording.PageList(zeroBasedPages);
        return zeroBasedPages.Distinct().Count() == 1 ? PdfStrings.Fill(PdfStrings.RedactPageOneFormat, list) : PdfStrings.Fill(PdfStrings.RedactPageManyFormat, list);
    }
}
