using System.Globalization;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

/// <summary>The texts of the PDF app (English source, German translation); the shell controls bring their own.</summary>
internal static class PdfStrings
{
    private static readonly StringTable Table = new("Raffica.Pdf.Strings", typeof(PdfStrings).Assembly);

    /// <summary>The UI language; set once at startup, a change applies after a restart.</summary>
    public static CultureInfo Culture
    {
        get => Table.Culture;
        set => Table.Culture = value;
    }

    /// <summary>The text of a key; throws <see cref="InvalidOperationException"/> for a missing key.</summary>
    public static string Get(string key) => Table.Get(key);

    /// <summary>The text of a key in a given language (the current UI language is not touched); throws for a missing key.</summary>
    public static string GetIn(string key, CultureInfo culture) => Table.GetIn(key, culture);

    public static string WindowTitle => Table.Get("WindowTitle");
    public static string AppMarkName => Table.Get("AppMarkName");
    public static string PanePages => Table.Get("PanePages");
    public static string PaneOutline => Table.Get("PaneOutline");
    public static string PageThumbnailNameFormat => Table.Get("PageThumbnailNameFormat");
    public static string PageThumbnailCurrentNameFormat => Table.Get("PageThumbnailCurrentNameFormat");
    public static string OutlineEmpty => Table.Get("OutlineEmpty");
    public static string OutlineNoPage => Table.Get("OutlineNoPage");
    public static string OutlineExpanded => Table.Get("OutlineExpanded");
    public static string OutlineCollapsed => Table.Get("OutlineCollapsed");
    public static string OutlineUntitled => Table.Get("OutlineUntitled");
    public static string PageViewNameFormat => Table.Get("PageViewNameFormat");
    public static string TabHome => Table.Get("TabHome");
    public static string TabView => Table.Get("TabView");
    public static string GroupFile => Table.Get("GroupFile");
    public static string GroupFind => Table.Get("GroupFind");
    public static string GroupGoTo => Table.Get("GroupGoTo");
    public static string GroupPages => Table.Get("GroupPages");
    public static string GroupZoom => Table.Get("GroupZoom");
    public static string GroupLayout => Table.Get("GroupLayout");
    public static string GroupPage => Table.Get("GroupPage");
    public static string GroupPanes => Table.Get("GroupPanes");
    public static string CmdOpen => Table.Get("CmdOpen");
    public static string CmdPrint => Table.Get("CmdPrint");
    public static string CmdSearch => Table.Get("CmdSearch");
    public static string CmdSearchNext => Table.Get("CmdSearchNext");
    public static string CmdSearchPrevious => Table.Get("CmdSearchPrevious");
    public static string CmdGoToPage => Table.Get("CmdGoToPage");
    public static string CmdPreviousPage => Table.Get("CmdPreviousPage");
    public static string CmdNextPage => Table.Get("CmdNextPage");
    public static string CmdFirstPage => Table.Get("CmdFirstPage");
    public static string CmdLastPage => Table.Get("CmdLastPage");
    public static string CmdZoomIn => Table.Get("CmdZoomIn");
    public static string CmdZoomOut => Table.Get("CmdZoomOut");
    public static string CmdFitWidth => Table.Get("CmdFitWidth");
    public static string CmdFitPage => Table.Get("CmdFitPage");
    public static string CmdActualSize => Table.Get("CmdActualSize");
    public static string CmdContinuous => Table.Get("CmdContinuous");
    public static string CmdSinglePage => Table.Get("CmdSinglePage");
    public static string CmdTwoPages => Table.Get("CmdTwoPages");
    public static string CmdTwoPagesCover => Table.Get("CmdTwoPagesCover");
    public static string SettingsPageAppearance => Table.Get("SettingsPageAppearance");
    public static string SettingsPageLight => Table.Get("SettingsPageLight");
    public static string SettingsPageFollowTheme => Table.Get("SettingsPageFollowTheme");
    public static string SettingsDefaultLayout => Table.Get("SettingsDefaultLayout");
    public static string SettingsDefaultZoom => Table.Get("SettingsDefaultZoom");
    public static string SettingsAuthorName => Table.Get("SettingsAuthorName");
    public static string CmdDarkPage => Table.Get("CmdDarkPage");
    public static string CmdPagesPane => Table.Get("CmdPagesPane");
    public static string CmdOutlinePane => Table.Get("CmdOutlinePane");
    public static string UnsavedTitle => Table.Get("UnsavedTitle");
    public static string UnsavedMessageFormat => Table.Get("UnsavedMessageFormat");
    public static string UnsavedSave => Table.Get("UnsavedSave");
    public static string UnsavedDontSave => Table.Get("UnsavedDontSave");
    public static string PasswordTitle => Table.Get("PasswordTitle");
    public static string PasswordMessageFormat => Table.Get("PasswordMessageFormat");
    public static string PasswordLabel => Table.Get("PasswordLabel");
    public static string PasswordIncorrect => Table.Get("PasswordIncorrect");
    public static string ErrorTitle => Table.Get("ErrorTitle");
    public static string ErrorFileNotFound => Table.Get("ErrorFileNotFound");
    public static string ErrorCannotRead => Table.Get("ErrorCannotRead");
    public static string ErrorNotAPdf => Table.Get("ErrorNotAPdf");
    public static string ErrorPasswordRequired => Table.Get("ErrorPasswordRequired");
    public static string ErrorPasswordIncorrect => Table.Get("ErrorPasswordIncorrect");
    public static string ErrorUnsupportedEncryption => Table.Get("ErrorUnsupportedEncryption");
    public static string ErrorUnknown => Table.Get("ErrorUnknown");
    public static string OpenDialogTitle => Table.Get("OpenDialogTitle");
    public static string OpenFilterPdf => Table.Get("OpenFilterPdf");
    public static string StatusPositionFormat => Table.Get("StatusPositionFormat");
    public static string StatusPageCountOne => Table.Get("StatusPageCountOne");
    public static string StatusPageCountFormat => Table.Get("StatusPageCountFormat");
    public static string StatusCountWithSizeFormat => Table.Get("StatusCountWithSizeFormat");
    public static string PageSizeFormat => Table.Get("PageSizeFormat");
    public static string GoToPageTitle => Table.Get("GoToPageTitle");
    public static string GoToPageLabelFormat => Table.Get("GoToPageLabelFormat");
    public static string GoToPageInvalidFormat => Table.Get("GoToPageInvalidFormat");
    public static string InfoTitle => Table.Get("InfoTitle");
    public static string InfoFileName => Table.Get("InfoFileName");
    public static string InfoFolder => Table.Get("InfoFolder");
    public static string InfoPages => Table.Get("InfoPages");
    public static string InfoPageSize => Table.Get("InfoPageSize");
    public static string InfoPdfVersion => Table.Get("InfoPdfVersion");
    public static string InfoFileSize => Table.Get("InfoFileSize");
    public static string InfoDocumentTitle => Table.Get("InfoDocumentTitle");
    public static string InfoAuthor => Table.Get("InfoAuthor");
    public static string InfoCreated => Table.Get("InfoCreated");
    public static string InfoNotSet => Table.Get("InfoNotSet");
    public static string SearchBarName => Table.Get("SearchBarName");
    public static string SearchMatchCase => Table.Get("SearchMatchCase");
    public static string SearchWholeWord => Table.Get("SearchWholeWord");
    public static string SearchPreviousHit => Table.Get("SearchPreviousHit");
    public static string SearchNextHit => Table.Get("SearchNextHit");
    public static string SearchCloseBar => Table.Get("SearchCloseBar");
    public static string SearchNoHits => Table.Get("SearchNoHits");
    public static string SearchHitOne => Table.Get("SearchHitOne");
    public static string SearchHitsFormat => Table.Get("SearchHitsFormat");
    public static string SearchLimitFormat => Table.Get("SearchLimitFormat");
    public static string SearchSearching => Table.Get("SearchSearching");
    public static string LinkTitle => Table.Get("LinkTitle");
    public static string LinkMessage => Table.Get("LinkMessage");
    public static string LinkAddressName => Table.Get("LinkAddressName");
    public static string LinkOpen => Table.Get("LinkOpen");
    public static string LinkWarningUserInfo => Table.Get("LinkWarningUserInfo");
    public static string LinkWarningPunycodeFormat => Table.Get("LinkWarningPunycodeFormat");
    public static string PaletteGroupGoTo => Table.Get("PaletteGroupGoTo");
    public static string PaletteGoToPageFormat => Table.Get("PaletteGoToPageFormat");
    public static string PaletteGoToPageDetailFormat => Table.Get("PaletteGoToPageDetailFormat");
    public static string PaletteGroupTabs => Table.Get("PaletteGroupTabs");
    public static string PaletteGroupRecent => Table.Get("PaletteGroupRecent");
    public static string PaletteGroupOutline => Table.Get("PaletteGroupOutline");
    public static string PaletteOutlinePageFormat => Table.Get("PaletteOutlinePageFormat");
    public static string PaletteGroupDocument => Table.Get("PaletteGroupDocument");
    public static string GroupTabs => Table.Get("GroupTabs");
    public static string GroupText => Table.Get("GroupText");
    public static string GroupHelp => Table.Get("GroupHelp");
    public static string CmdCloseTab => Table.Get("CmdCloseTab");
    public static string CmdSettings => Table.Get("CmdSettings");
    public static string CmdNextTab => Table.Get("CmdNextTab");
    public static string CmdPreviousTab => Table.Get("CmdPreviousTab");
    public static string CmdSelectAll => Table.Get("CmdSelectAll");
    public static string CmdCopy => Table.Get("CmdCopy");
    public static string CmdHelp => Table.Get("CmdHelp");
    public static string TabAnnotate => Table.Get("TabAnnotate");
    public static string GroupHistory => Table.Get("GroupHistory");
    public static string GroupMarkup => Table.Get("GroupMarkup");
    public static string GroupDraw => Table.Get("GroupDraw");
    public static string GroupEdit => Table.Get("GroupEdit");
    public static string GroupDelete => Table.Get("GroupDelete");
    public static string CmdSave => Table.Get("CmdSave");
    public static string CmdSaveAs => Table.Get("CmdSaveAs");
    public static string CmdUndo => Table.Get("CmdUndo");
    public static string CmdRedo => Table.Get("CmdRedo");
    public static string CmdAnnotateMode => Table.Get("CmdAnnotateMode");
    public static string CmdHighlight => Table.Get("CmdHighlight");
    public static string CmdUnderline => Table.Get("CmdUnderline");
    public static string CmdStrikeOut => Table.Get("CmdStrikeOut");
    public static string CmdSelectMark => Table.Get("CmdSelectMark");
    public static string CmdFreehand => Table.Get("CmdFreehand");
    public static string CmdRectangle => Table.Get("CmdRectangle");
    public static string CmdEllipse => Table.Get("CmdEllipse");
    public static string CmdLine => Table.Get("CmdLine");
    public static string CmdArrow => Table.Get("CmdArrow");
    public static string CmdTextBox => Table.Get("CmdTextBox");
    public static string CmdAnnotateDone => Table.Get("CmdAnnotateDone");
    public static string CmdMarksPane => Table.Get("CmdMarksPane");
    public static string CmdDeleteMark => Table.Get("CmdDeleteMark");
    public static string GroupSign => Table.Get("GroupSign");
    public static string CmdSign => Table.Get("CmdSign");
    public static string AnnotateLockedReason => Table.Get("AnnotateLockedReason");
    public static string AnnotateModeFormat => Table.Get("AnnotateModeFormat");
    public static string AnnotateCardTitle => Table.Get("AnnotateCardTitle");
    public static string AnnotateLimitReached => Table.Get("AnnotateLimitReached");
    public static string AnnotateFailed => Table.Get("AnnotateFailed");
    public static string StyleStripColorName => Table.Get("StyleStripColorName");
    public static string StyleStripColorWidthName => Table.Get("StyleStripColorWidthName");
    public static string StyleStripColorSizeName => Table.Get("StyleStripColorSizeName");
    public static string MarkupColorYellow => Table.Get("MarkupColorYellow");
    public static string MarkupColorGreen => Table.Get("MarkupColorGreen");
    public static string MarkupColorBlue => Table.Get("MarkupColorBlue");
    public static string MarkupColorPink => Table.Get("MarkupColorPink");
    public static string MarkupColorOrange => Table.Get("MarkupColorOrange");
    public static string DrawColorRed => Table.Get("DrawColorRed");
    public static string DrawColorBlack => Table.Get("DrawColorBlack");
    public static string DrawColorBlue => Table.Get("DrawColorBlue");
    public static string DrawColorGreen => Table.Get("DrawColorGreen");
    public static string DrawColorOrange => Table.Get("DrawColorOrange");
    public static string StyleWidthThin => Table.Get("StyleWidthThin");
    public static string StyleWidthMedium => Table.Get("StyleWidthMedium");
    public static string StyleWidthThick => Table.Get("StyleWidthThick");
    public static string StyleSizeSmall => Table.Get("StyleSizeSmall");
    public static string StyleSizeMedium => Table.Get("StyleSizeMedium");
    public static string StyleSizeLarge => Table.Get("StyleSizeLarge");
    public static string StylePointsFormat => Table.Get("StylePointsFormat");
    public static string SelectLockedReason => Table.Get("SelectLockedReason");
    public static string SelectFailed => Table.Get("SelectFailed");
    public static string TextBoxRejectedFormat => Table.Get("TextBoxRejectedFormat");
    public static string TextBoxTooLong => Table.Get("TextBoxTooLong");
    public static string TextBoxTooManyLines => Table.Get("TextBoxTooManyLines");
    public static string TextBoxFieldName => Table.Get("TextBoxFieldName");
    public static string FormCardTitle => Table.Get("FormCardTitle");
    public static string FormRejectedFormat => Table.Get("FormRejectedFormat");
    public static string FormControlCharacterFormat => Table.Get("FormControlCharacterFormat");
    public static string FormTooLongFormat => Table.Get("FormTooLongFormat");
    public static string FormOneLineOnly => Table.Get("FormOneLineOnly");
    public static string FormNotKept => Table.Get("FormNotKept");
    public static string FormNotKeptNotRestored => Table.Get("FormNotKeptNotRestored");
    public static string FormLocked => Table.Get("FormLocked");
    public static string FormNotFound => Table.Get("FormNotFound");
    public static string FormHistoryFull => Table.Get("FormHistoryFull");
    public static string FormInvalid => Table.Get("FormInvalid");
    public static string FormFillForbidden => Table.Get("FormFillForbidden");
    public static string TabForms => Table.Get("TabForms");
    public static string GroupFill => Table.Get("GroupFill");
    public static string CmdFormsNext => Table.Get("CmdFormsNext");
    public static string CmdFormsPrevious => Table.Get("CmdFormsPrevious");
    public static string CmdFormsNextRequired => Table.Get("CmdFormsNextRequired");
    public static string CmdFormsHighlight => Table.Get("CmdFormsHighlight");
    public static string FormsNoFormReason => Table.Get("FormsNoFormReason");
    public static string FormsNoRequiredReason => Table.Get("FormsNoRequiredReason");
    public static string FormTipReadOnly => Table.Get("FormTipReadOnly");
    public static string FormTipSignature => Table.Get("FormTipSignature");
    public static string FormTipButton => Table.Get("FormTipButton");
    public static string FormTipUnsupported => Table.Get("FormTipUnsupported");
    public static string FormTipRequired => Table.Get("FormTipRequired");
    public static string FormNameFormat => Table.Get("FormNameFormat");
    public static string FormStateChecked => Table.Get("FormStateChecked");
    public static string FormStateNotChecked => Table.Get("FormStateNotChecked");
    public static string FormNoticeScripts => Table.Get("FormNoticeScripts");
    public static string FormNoticeXfa => Table.Get("FormNoticeXfa");
    public static string FormNoticeSignature => Table.Get("FormNoticeSignature");
    public static string FormNoticeButtons => Table.Get("FormNoticeButtons");
    public static string FormNoticeDismiss => Table.Get("FormNoticeDismiss");
    public static string FormRequiredOne => Table.Get("FormRequiredOne");
    public static string FormRequiredManyFormat => Table.Get("FormRequiredManyFormat");
    public static string FormHighlightToggle => Table.Get("FormHighlightToggle");
    public static string PaneMarks => Table.Get("PaneMarks");
    public static string MarkFilterName => Table.Get("MarkFilterName");
    public static string MarkFilterAll => Table.Get("MarkFilterAll");
    public static string MarkFilterHighlight => Table.Get("MarkFilterHighlight");
    public static string MarkFilterUnderline => Table.Get("MarkFilterUnderline");
    public static string MarkFilterStrikeOut => Table.Get("MarkFilterStrikeOut");
    public static string MarkFilterDrawings => Table.Get("MarkFilterDrawings");
    public static string MarkFilterOther => Table.Get("MarkFilterOther");
    public static string MarkKindHighlight => Table.Get("MarkKindHighlight");
    public static string MarkKindUnderline => Table.Get("MarkKindUnderline");
    public static string MarkKindStrikeOut => Table.Get("MarkKindStrikeOut");
    public static string MarkKindFreehand => Table.Get("MarkKindFreehand");
    public static string MarkKindRectangle => Table.Get("MarkKindRectangle");
    public static string MarkKindEllipse => Table.Get("MarkKindEllipse");
    public static string MarkKindLine => Table.Get("MarkKindLine");
    public static string MarkKindArrow => Table.Get("MarkKindArrow");
    public static string MarkKindTextBox => Table.Get("MarkKindTextBox");
    public static string MarkKindSignature => Table.Get("MarkKindSignature");
    public static string MarkKindOtherFormat => Table.Get("MarkKindOtherFormat");
    public static string MarkGroupFormat => Table.Get("MarkGroupFormat");
    public static string MarkPageTagFormat => Table.Get("MarkPageTagFormat");
    public static string MarkLockedLabel => Table.Get("MarkLockedLabel");
    public static string MarkSummaryOneFormat => Table.Get("MarkSummaryOneFormat");
    public static string MarkSummaryOnePageFormat => Table.Get("MarkSummaryOnePageFormat");
    public static string MarkSummaryPagesFormat => Table.Get("MarkSummaryPagesFormat");
    public static string MarksEmpty => Table.Get("MarksEmpty");
    public static string MarksNoneOfKind => Table.Get("MarksNoneOfKind");
    public static string MarksHelp => Table.Get("MarksHelp");
    public static string MarkCommentName => Table.Get("MarkCommentName");
    public static string MarkCommentHint => Table.Get("MarkCommentHint");
    public static string MarkRepliesReason => Table.Get("MarkRepliesReason");
    public static string MarkOtherReason => Table.Get("MarkOtherReason");
    public static string MarkTextBoxLockedReason => Table.Get("MarkTextBoxLockedReason");
    public static string MarkTextBoxNote => Table.Get("MarkTextBoxNote");
    public static string MarkCommentFailed => Table.Get("MarkCommentFailed");
    public static string PrintLockedReason => Table.Get("PrintLockedReason");
    public static string PrintErrorStart => Table.Get("PrintErrorStart");
    public static string PrintErrorPage => Table.Get("PrintErrorPage");
    public static string PrintErrorDialog => Table.Get("PrintErrorDialog");
    public static string PrintNoService => Table.Get("PrintNoService");
    public static string PrintNoPrinter => Table.Get("PrintNoPrinter");
    public static string PrintCardTitle => Table.Get("PrintCardTitle");
    public static string PrintCardPrinter => Table.Get("PrintCardPrinter");
    public static string PrintCardPages => Table.Get("PrintCardPages");
    public static string PrintPagesAll => Table.Get("PrintPagesAll");
    public static string PrintPagesCurrent => Table.Get("PrintPagesCurrent");
    public static string PrintPagesRange => Table.Get("PrintPagesRange");
    public static string PrintRangeLabel => Table.Get("PrintRangeLabel");
    public static string PrintCardCopies => Table.Get("PrintCardCopies");
    public static string PrintCardCollate => Table.Get("PrintCardCollate");
    public static string PrintCardScaling => Table.Get("PrintCardScaling");
    public static string PrintScalingFit => Table.Get("PrintScalingFit");
    public static string PrintScalingActual => Table.Get("PrintScalingActual");
    public static string PrintCardDuplex => Table.Get("PrintCardDuplex");
    public static string PrintDuplexDefault => Table.Get("PrintDuplexDefault");
    public static string PrintDuplexOff => Table.Get("PrintDuplexOff");
    public static string PrintDuplexLong => Table.Get("PrintDuplexLong");
    public static string PrintDuplexShort => Table.Get("PrintDuplexShort");
    public static string PrintCardOrientation => Table.Get("PrintCardOrientation");
    public static string PrintOrientationDefault => Table.Get("PrintOrientationDefault");
    public static string PrintOrientationPortrait => Table.Get("PrintOrientationPortrait");
    public static string PrintOrientationLandscape => Table.Get("PrintOrientationLandscape");
    public static string PrintRangeInvalidFormat => Table.Get("PrintRangeInvalidFormat");
    public static string PrintCopiesInvalid => Table.Get("PrintCopiesInvalid");
    public static string PrintJobTitleFormat => Table.Get("PrintJobTitleFormat");
    public static string PrintJobPreparing => Table.Get("PrintJobPreparing");
    public static string PrintJobProgressFormat => Table.Get("PrintJobProgressFormat");
    public static string PrintJobCancelling => Table.Get("PrintJobCancelling");
    public static string PrintBusy => Table.Get("PrintBusy");
    public static string SaveBusy => Table.Get("SaveBusy");
    public static string SaveJobTitleFormat => Table.Get("SaveJobTitleFormat");
    public static string SaveDialogTitle => Table.Get("SaveDialogTitle");
    public static string PrintFailedPagesFormat => Table.Get("PrintFailedPagesFormat");
    public static string PrintFailedMoreFormat => Table.Get("PrintFailedMoreFormat");
    public static string HelpTitle => Table.Get("HelpTitle");
    public static string HelpListName => Table.Get("HelpListName");
    public static string OrganizeListName => Table.Get("OrganizeListName");
    public static string OrganizeCounterChangedFormat => Table.Get("OrganizeCounterChangedFormat");
    public static string OrganizeCounterChangedOneFormat => Table.Get("OrganizeCounterChangedOneFormat");
    public static string OrganizeCounterSameFormat => Table.Get("OrganizeCounterSameFormat");
    public static string OrganizeCounterSameOne => Table.Get("OrganizeCounterSameOne");
    public static string OrganizeGoneFormat => Table.Get("OrganizeGoneFormat");
    public static string OrganizeTurnedFormat => Table.Get("OrganizeTurnedFormat");
    public static string OrganizeBlank => Table.Get("OrganizeBlank");
    public static string OrganizeBlankSizeFormat => Table.Get("OrganizeBlankSizeFormat");
    public static string OrganizeFromFileFormat => Table.Get("OrganizeFromFileFormat");
    public static string OrganizeCopy => Table.Get("OrganizeCopy");
    public static string OrganizeLastPageRefused => Table.Get("OrganizeLastPageRefused");
    public static string OrganizeLimitRefusedFormat => Table.Get("OrganizeLimitRefusedFormat");

    /// <summary>Puts the values into the places {0} and {1} of a text of the table; numbers follow the UI language.</summary>
    public static string Fill(string template, object? first, object? second = null) =>
        template
            .Replace("{0}", Convert.ToString(first, Culture), StringComparison.Ordinal)
            .Replace("{1}", Convert.ToString(second, Culture), StringComparison.Ordinal);

    /// <summary>The automation name of the thumbnail of a page (1-based number); the page shown in the document says so.</summary>
    public static string PageThumbnailName(int number, bool current) =>
        Fill(current ? PageThumbnailCurrentNameFormat : PageThumbnailNameFormat, number);

    /// <summary>The automation name of an outline entry: the title, its state (open or closed) and that it has no page, in the UI language.</summary>
    public static string OutlineEntryName(string title, bool hasChildren, bool expanded, bool hasPage)
    {
        var name = string.IsNullOrWhiteSpace(title) ? OutlineUntitled : title;
        if (hasChildren)
        {
            name += ", " + (expanded ? OutlineExpanded : OutlineCollapsed);
        }

        return hasPage ? name : name + ", " + OutlineNoPage;
    }

    /// <summary>The automation name of the page view of a document, so a screen reader tells the tabs apart.</summary>
    public static string PageViewName(string title) => PageViewNameFormat.Replace("{0}", title, StringComparison.Ordinal);

    /// <summary>The reason a file could not be opened, in the UI language (one text per <see cref="PdfOpenError"/> value, no raw exception text).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has no text.</exception>
    public static string OpenError(PdfOpenError error) => error switch
    {
        PdfOpenError.FileNotFound => ErrorFileNotFound,
        PdfOpenError.CannotRead => ErrorCannotRead,
        PdfOpenError.NotAPdf => ErrorNotAPdf,
        PdfOpenError.PasswordRequired => ErrorPasswordRequired,
        PdfOpenError.PasswordIncorrect => ErrorPasswordIncorrect,
        PdfOpenError.UnsupportedEncryption => ErrorUnsupportedEncryption,
        PdfOpenError.Unknown => ErrorUnknown,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "This open error has no text."),
    };

    public static string SignedWarnTitle => Table.Get("SignedWarnTitle");
    public static string SignedWarnText => Table.Get("SignedWarnText");
    public static string SignedNoteTitle => Table.Get("SignedNoteTitle");
    public static string SignedNoteText => Table.Get("SignedNoteText");
    public static string SignedSaveAnyway => Table.Get("SignedSaveAnyway");
    public static string SignedFormsTitle => Table.Get("SignedFormsTitle");
    public static string SignedFormsText => Table.Get("SignedFormsText");
    public static string RequiredTitle => Table.Get("RequiredTitle");
    public static string RequiredTextOne => Table.Get("RequiredTextOne");
    public static string RequiredTextMany => Table.Get("RequiredTextMany");
    public static string RequiredNames => Table.Get("RequiredNames");
    public static string RequiredMore => Table.Get("RequiredMore");
    public static string RequiredHint => Table.Get("RequiredHint");
    public static string RequiredGoToFirst => Table.Get("RequiredGoToFirst");
    public static string RequiredSaveAnyway => Table.Get("RequiredSaveAnyway");

    /// <summary>The line above the names on the required-fields card: one text for one field, one for several (the count in it).</summary>
    public static string RequiredText(int count) =>
        count == 1 ? RequiredTextOne : RequiredTextMany.Replace("{0}", count.ToString(Culture), StringComparison.Ordinal);

    /// <summary>The names of the empty required fields, at most <paramref name="shown"/>, then how many more there are.</summary>
    public static string RequiredList(IReadOnlyList<string> names, int shown = 5)
    {
        var list = RequiredNames.Replace("{0}", string.Join(", ", names.Take(shown)), StringComparison.Ordinal);
        return names.Count > shown
            ? list + " " + RequiredMore.Replace("{0}", (names.Count - shown).ToString(Culture), StringComparison.Ordinal)
            : list;
    }

    public static string SaveFailNotWritable => Table.Get("SaveFailNotWritable");
    public static string SaveFailDiskFull => Table.Get("SaveFailDiskFull");
    public static string SaveFailWriteFailed => Table.Get("SaveFailWriteFailed");
    public static string SaveFailVerificationFailed => Table.Get("SaveFailVerificationFailed");
    public static string SaveFailOriginalChanged => Table.Get("SaveFailOriginalChanged");
    public static string SaveFailSourceModified => Table.Get("SaveFailSourceModified");
    public static string SaveFailEncryptedNotSupported => Table.Get("SaveFailEncryptedNotSupported");
    public static string SaveFailTargetExists => Table.Get("SaveFailTargetExists");
    public static string SaveFailRedactionFailed => Table.Get("SaveFailRedactionFailed");
    public static string SaveFailWriterNotSupported => Table.Get("SaveFailWriterNotSupported");
    public static string SaveFailCanceled => Table.Get("SaveFailCanceled");
    public static string ChangedTitle => Table.Get("ChangedTitle");
    public static string ChangedMessageFormat => Table.Get("ChangedMessageFormat");
    public static string ChangedOverwrite => Table.Get("ChangedOverwrite");
    public static string SaveFailedTitle => Table.Get("SaveFailedTitle");
    public static string UnsavedLockedTitle => Table.Get("UnsavedLockedTitle");
    public static string UnsavedLockedMessageFormat => Table.Get("UnsavedLockedMessageFormat");
    public static string UnsavedDiscard => Table.Get("UnsavedDiscard");
    public static string SignatureNewTitle => Table.Get("SignatureNewTitle");
    public static string SignatureNewTabs => Table.Get("SignatureNewTabs");
    public static string SignatureNewTabDraw => Table.Get("SignatureNewTabDraw");
    public static string SignatureNewTabImage => Table.Get("SignatureNewTabImage");
    public static string SignatureNewName => Table.Get("SignatureNewName");
    public static string SignatureNewDefaultNameFormat => Table.Get("SignatureNewDefaultNameFormat");
    public static string SignatureNewPadName => Table.Get("SignatureNewPadName");
    public static string SignatureNewClear => Table.Get("SignatureNewClear");
    public static string SignatureNewBack => Table.Get("SignatureNewBack");
    public static string SignatureNewBackTip => Table.Get("SignatureNewBackTip");
    public static string SignatureNewInk => Table.Get("SignatureNewInk");
    public static string SignatureNewChoose => Table.Get("SignatureNewChoose");
    public static string SignatureNewImageNone => Table.Get("SignatureNewImageNone");
    public static string SignatureNewImageLoaded => Table.Get("SignatureNewImageLoaded");
    public static string SignatureNewCutout => Table.Get("SignatureNewCutout");
    public static string SignatureNewCutoutTip => Table.Get("SignatureNewCutoutTip");
    public static string SignatureNewPreview => Table.Get("SignatureNewPreview");
    public static string SignatureNewSave => Table.Get("SignatureNewSave");
    public static string SignatureNewPickTitle => Table.Get("SignatureNewPickTitle");
    public static string SignatureNewPickFilter => Table.Get("SignatureNewPickFilter");
    public static string SignatureNewRefusalLimit => Table.Get("SignatureNewRefusalLimit");
    public static string SignatureNewRefusalTooLarge => Table.Get("SignatureNewRefusalTooLarge");
    public static string SignatureNewRefusalTooManyPixels => Table.Get("SignatureNewRefusalTooManyPixels");
    public static string SignatureNewRefusalUnreadable => Table.Get("SignatureNewRefusalUnreadable");
    public static string SignatureNewRefusalEmpty => Table.Get("SignatureNewRefusalEmpty");
    public static string SignatureNewRefusalAspect => Table.Get("SignatureNewRefusalAspect");
    public static string SignatureNewRefusalUnusable => Table.Get("SignatureNewRefusalUnusable");
    public static string SignatureNewRefusalWriteFailed => Table.Get("SignatureNewRefusalWriteFailed");
    public static string SignatureHintWritten => Table.Get("SignatureHintWritten");
    public static string SignatureHintImage => Table.Get("SignatureHintImage");

    /// <summary>The two sentences of the signature hint as one text (new-signature card and picker); a method, so it is no key of its own.</summary>
    public static string SignatureHintText() => SignatureHintWritten + " " + SignatureHintImage;
    public static string SignaturePickerTitle => Table.Get("SignaturePickerTitle");
    public static string SignaturePickerList => Table.Get("SignaturePickerList");
    public static string SignaturePickerNew => Table.Get("SignaturePickerNew");
    public static string SignaturePickerDelete => Table.Get("SignaturePickerDelete");
    public static string SignaturePickerDeleteTip => Table.Get("SignaturePickerDeleteTip");
    public static string SignaturePickerDeleteConfirm => Table.Get("SignaturePickerDeleteConfirm");
    public static string SignaturePickerDeleteYes => Table.Get("SignaturePickerDeleteYes");
    public static string SignaturePickerDeleteKeep => Table.Get("SignaturePickerDeleteKeep");
    public static string SignaturePickerDeleteFailed => Table.Get("SignaturePickerDeleteFailed");
    public static string SignaturePickerEmpty => Table.Get("SignaturePickerEmpty");
    public static string SignaturePlaceTitle => Table.Get("SignaturePlaceTitle");
    public static string SignaturePlaceHistoryFull => Table.Get("SignaturePlaceHistoryFull");
    public static string SignaturePlaceFailed => Table.Get("SignaturePlaceFailed");

    /// <summary>The reason a save failed, in the UI language (one text per <see cref="SaveFailureReason"/> value); every text says the original file is unchanged.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has no text.</exception>
    public static string SaveFailure(SaveFailureReason reason) => reason switch
    {
        SaveFailureReason.NotWritable => SaveFailNotWritable,
        SaveFailureReason.DiskFull => SaveFailDiskFull,
        SaveFailureReason.WriteFailed => SaveFailWriteFailed,
        SaveFailureReason.VerificationFailed => SaveFailVerificationFailed,
        SaveFailureReason.OriginalChanged => SaveFailOriginalChanged,
        SaveFailureReason.SourceModified => SaveFailSourceModified,
        SaveFailureReason.EncryptedNotSupported => SaveFailEncryptedNotSupported,
        SaveFailureReason.TargetExists => SaveFailTargetExists,
        SaveFailureReason.RedactionFailed => SaveFailRedactionFailed,
        SaveFailureReason.WriterNotSupported => SaveFailWriterNotSupported,
        SaveFailureReason.Canceled => SaveFailCanceled,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "This save failure has no text."),
    };
}
