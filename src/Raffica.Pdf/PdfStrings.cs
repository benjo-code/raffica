using System.Globalization;
using Raffica.Core.Pdf;
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
    public static string AnnotateLockedReason => Table.Get("AnnotateLockedReason");
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
    public static string PrintFailedPagesFormat => Table.Get("PrintFailedPagesFormat");
    public static string PrintFailedMoreFormat => Table.Get("PrintFailedMoreFormat");
    public static string HelpTitle => Table.Get("HelpTitle");
    public static string HelpListName => Table.Get("HelpListName");

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
}
