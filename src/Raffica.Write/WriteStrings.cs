using System.Globalization;
using Raffica.Core.Saving;
using Raffica.Shell.Localization;
using Raffica.Write.Documents;
using Raffica.Write.Docx;

namespace Raffica.Write;

/// <summary>The texts of the Write app (English source, German translation); the shell controls bring their own.</summary>
internal static class WriteStrings
{
    private static readonly StringTable Table = new("Raffica.Write.Strings", typeof(WriteStrings).Assembly);

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
    public static string OpenFileButton => Table.Get("OpenFileButton");
    public static string OpenDialogTitle => Table.Get("OpenDialogTitle");
    public static string OpenFilterDocx => Table.Get("OpenFilterDocx");
    public static string ErrorTitle => Table.Get("ErrorTitle");
    public static string OpenFailuresTitle => Table.Get("OpenFailuresTitle");
    public static string OpenFailuresMessage => Table.Get("OpenFailuresMessage");
    public static string OpenFailuresMoreFormat => Table.Get("OpenFailuresMoreFormat");
    public static string RecoveryErrorTitle => Table.Get("RecoveryErrorTitle");
    public static string RecoveryErrorReason => Table.Get("RecoveryErrorReason");
    public static string RecoveryFailuresTitle => Table.Get("RecoveryFailuresTitle");
    public static string RecoveryFailuresMessage => Table.Get("RecoveryFailuresMessage");
    public static string SettingsDefaultView => Table.Get("SettingsDefaultView");
    public static string SettingsDefaultZoom => Table.Get("SettingsDefaultZoom");
    public static string SettingsAutosaveLabel => Table.Get("SettingsAutosaveLabel");
    public static string ViewPage => Table.Get("ViewPage");
    public static string ViewWriting => Table.Get("ViewWriting");
    public static string ZoomActualSize => Table.Get("ZoomActualSize");
    public static string ZoomFitWidth => Table.Get("ZoomFitWidth");
    public static string ZoomFitPage => Table.Get("ZoomFitPage");
    public static string MenuFile => Table.Get("MenuFile");
    public static string MenuOpen => Table.Get("MenuOpen");
    public static string MenuOpenRecent => Table.Get("MenuOpenRecent");
    public static string MenuNoRecent => Table.Get("MenuNoRecent");
    public static string MenuCloseTab => Table.Get("MenuCloseTab");
    public static string MenuSettings => Table.Get("MenuSettings");
    public static string MenuWindow => Table.Get("MenuWindow");
    public static string MenuMinimize => Table.Get("MenuMinimize");
    public static string MenuZoomWindow => Table.Get("MenuZoomWindow");
    public static string MenuBringAllToFront => Table.Get("MenuBringAllToFront");
    public static string TabHome => Table.Get("TabHome");
    public static string TabView => Table.Get("TabView");
    public static string GroupFile => Table.Get("GroupFile");
    public static string GroupSearch => Table.Get("GroupSearch");
    public static string GroupZoom => Table.Get("GroupZoom");
    public static string GroupViews => Table.Get("GroupViews");
    public static string GroupPanes => Table.Get("GroupPanes");
    public static string CmdNew => Table.Get("CmdNew");
    public static string DescNew => Table.Get("DescNew");
    public static string ReasonClosing => Table.Get("ReasonClosing");
    public static string CmdOpen => Table.Get("CmdOpen");
    public static string CmdCloseTab => Table.Get("CmdCloseTab");
    public static string CmdSettings => Table.Get("CmdSettings");
    public static string CmdSearch => Table.Get("CmdSearch");
    public static string CmdZoomIn => Table.Get("CmdZoomIn");
    public static string CmdZoomOut => Table.Get("CmdZoomOut");
    public static string CmdActualSize => Table.Get("CmdActualSize");
    public static string CmdFitWidth => Table.Get("CmdFitWidth");
    public static string CmdFitPage => Table.Get("CmdFitPage");
    public static string CmdPageView => Table.Get("CmdPageView");
    public static string CmdWritingView => Table.Get("CmdWritingView");
    public static string CmdOutline => Table.Get("CmdOutline");
    public static string ReasonZoomMax => Table.Get("ReasonZoomMax");
    public static string ReasonZoomMin => Table.Get("ReasonZoomMin");
    public static string ReasonOutlineSoon => Table.Get("ReasonOutlineSoon");
    public static string StatusPositionFormat => Table.Get("StatusPositionFormat");
    public static string StatusPositionMoreFormat => Table.Get("StatusPositionMoreFormat");
    public static string PaneOutline => Table.Get("PaneOutline");
    public static string OutlineLoading => Table.Get("OutlineLoading");
    public static string OutlineEmpty => Table.Get("OutlineEmpty");
    public static string OutlineEntryNameFormat => Table.Get("OutlineEntryNameFormat");
    public static string OutlineDropAboveFormat => Table.Get("OutlineDropAboveFormat");
    public static string OutlineDropBelowFormat => Table.Get("OutlineDropBelowFormat");
    public static string OutlineActionsName => Table.Get("OutlineActionsName");
    public static string OutlineNoRowSelected => Table.Get("OutlineNoRowSelected");
    public static string OutlineWaiting => Table.Get("OutlineWaiting");
    public static string EditStillOpening => Table.Get("EditStillOpening");
    public static string EditUnavailable => Table.Get("EditUnavailable");
    public static string EditFailed => Table.Get("EditFailed");
    public static string MenuEdit => Table.Get("MenuEdit");
    public static string GroupUndo => Table.Get("GroupUndo");
    public static string GroupClipboard => Table.Get("GroupClipboard");
    public static string CmdUndo => Table.Get("CmdUndo");
    public static string CmdRedo => Table.Get("CmdRedo");
    public static string CmdCut => Table.Get("CmdCut");
    public static string CmdCopy => Table.Get("CmdCopy");
    public static string CmdPaste => Table.Get("CmdPaste");
    public static string CmdSelectAll => Table.Get("CmdSelectAll");
    public static string DescUndo => Table.Get("DescUndo");
    public static string DescRedo => Table.Get("DescRedo");
    public static string DescCut => Table.Get("DescCut");
    public static string DescCopy => Table.Get("DescCopy");
    public static string DescPaste => Table.Get("DescPaste");
    public static string DescSelectAll => Table.Get("DescSelectAll");
    public static string ReasonEditOpening => Table.Get("ReasonEditOpening");
    public static string ReasonEditSaving => Table.Get("ReasonEditSaving");
    public static string ReasonNoSelection => Table.Get("ReasonNoSelection");
    public static string MenuFormat => Table.Get("MenuFormat");
    public static string ReasonNothingToUndo => Table.Get("ReasonNothingToUndo");
    public static string GroupFont => Table.Get("GroupFont");
    public static string GroupParagraph => Table.Get("GroupParagraph");
    public static string GroupStyles => Table.Get("GroupStyles");
    public static string CmdFormatBold => Table.Get("CmdFormatBold");
    public static string CmdFormatItalic => Table.Get("CmdFormatItalic");
    public static string CmdFormatUnderline => Table.Get("CmdFormatUnderline");
    public static string CmdFormatStrike => Table.Get("CmdFormatStrike");
    public static string CmdFormatSuperscript => Table.Get("CmdFormatSuperscript");
    public static string CmdFormatSubscript => Table.Get("CmdFormatSubscript");
    public static string CmdFormatSmallCaps => Table.Get("CmdFormatSmallCaps");
    public static string CmdFormatAllCaps => Table.Get("CmdFormatAllCaps");
    public static string CmdFormatAlignLeft => Table.Get("CmdFormatAlignLeft");
    public static string CmdFormatAlignCenter => Table.Get("CmdFormatAlignCenter");
    public static string CmdFormatAlignRight => Table.Get("CmdFormatAlignRight");
    public static string CmdFormatJustify => Table.Get("CmdFormatJustify");
    public static string CmdFormatIndentMore => Table.Get("CmdFormatIndentMore");
    public static string CmdFormatIndentLess => Table.Get("CmdFormatIndentLess");
    public static string CmdFormatKeepNext => Table.Get("CmdFormatKeepNext");
    public static string CmdFormatKeepLines => Table.Get("CmdFormatKeepLines");
    public static string CmdFormatPageBreakBefore => Table.Get("CmdFormatPageBreakBefore");
    public static string CmdFormatWidowControl => Table.Get("CmdFormatWidowControl");
    public static string CmdFormatClearDirect => Table.Get("CmdFormatClearDirect");
    public static string CmdFormatUpdateStyle => Table.Get("CmdFormatUpdateStyle");
    public static string CmdFormatFont => Table.Get("CmdFormatFont");
    public static string CmdFormatSize => Table.Get("CmdFormatSize");
    public static string CmdFormatSizeUp => Table.Get("CmdFormatSizeUp");
    public static string CmdFormatSizeDown => Table.Get("CmdFormatSizeDown");
    public static string CmdFormatColor => Table.Get("CmdFormatColor");
    public static string CmdFormatHighlight => Table.Get("CmdFormatHighlight");
    public static string CmdFormatSpaceBefore => Table.Get("CmdFormatSpaceBefore");
    public static string CmdFormatSpaceAfter => Table.Get("CmdFormatSpaceAfter");
    public static string CmdFormatLineSpacing => Table.Get("CmdFormatLineSpacing");
    public static string CmdFormatParagraphStyle => Table.Get("CmdFormatParagraphStyle");
    public static string CmdFormatCharacterStyle => Table.Get("CmdFormatCharacterStyle");
    public static string CmdFormatting => Table.Get("CmdFormatting");
    public static string ColorBlack => Table.Get("ColorBlack");
    public static string ColorGray => Table.Get("ColorGray");
    public static string ColorWhite => Table.Get("ColorWhite");
    public static string ColorDarkRed => Table.Get("ColorDarkRed");
    public static string ColorRed => Table.Get("ColorRed");
    public static string ColorOrange => Table.Get("ColorOrange");
    public static string ColorYellow => Table.Get("ColorYellow");
    public static string ColorLightGreen => Table.Get("ColorLightGreen");
    public static string ColorGreen => Table.Get("ColorGreen");
    public static string ColorLightBlue => Table.Get("ColorLightBlue");
    public static string ColorBlue => Table.Get("ColorBlue");
    public static string ColorDarkBlue => Table.Get("ColorDarkBlue");
    public static string ColorPurple => Table.Get("ColorPurple");
    public static string HlNone => Table.Get("HlNone");
    public static string HlYellow => Table.Get("HlYellow");
    public static string HlGreen => Table.Get("HlGreen");
    public static string HlCyan => Table.Get("HlCyan");
    public static string HlMagenta => Table.Get("HlMagenta");
    public static string HlBlue => Table.Get("HlBlue");
    public static string HlRed => Table.Get("HlRed");
    public static string HlDarkBlue => Table.Get("HlDarkBlue");
    public static string HlDarkCyan => Table.Get("HlDarkCyan");
    public static string HlDarkGreen => Table.Get("HlDarkGreen");
    public static string HlDarkMagenta => Table.Get("HlDarkMagenta");
    public static string HlDarkRed => Table.Get("HlDarkRed");
    public static string HlDarkYellow => Table.Get("HlDarkYellow");
    public static string HlDarkGray => Table.Get("HlDarkGray");
    public static string HlLightGray => Table.Get("HlLightGray");
    public static string HlBlack => Table.Get("HlBlack");
    public static string HlWhite => Table.Get("HlWhite");
    public static string LineSingle => Table.Get("LineSingle");
    public static string LineDouble => Table.Get("LineDouble");
    public static string LineAtLeastFormat => Table.Get("LineAtLeastFormat");
    public static string LineExactFormat => Table.Get("LineExactFormat");
    public static string PointsFormat => Table.Get("PointsFormat");
    public static string StyleNone => Table.Get("StyleNone");
    public static string ReasonStyleNotDefined => Table.Get("ReasonStyleNotDefined");
    public static string ReasonStyleMixed => Table.Get("ReasonStyleMixed");
    public static string ReasonNoFormattingToClear => Table.Get("ReasonNoFormattingToClear");
    public static string ReasonNothingToUpdate => Table.Get("ReasonNothingToUpdate");
    public static string ReasonNoStyles => Table.Get("ReasonNoStyles");
    public static string GroupLists => Table.Get("GroupLists");
    public static string GroupOutline => Table.Get("GroupOutline");
    public static string CmdNumberHeadings => Table.Get("CmdNumberHeadings");
    public static string DescNumberHeadings => Table.Get("DescNumberHeadings");
    public static string CmdBullets => Table.Get("CmdBullets");
    public static string DescBullets => Table.Get("DescBullets");
    public static string CmdNumbers => Table.Get("CmdNumbers");
    public static string DescNumbers => Table.Get("DescNumbers");
    public static string CmdRestartNumbering => Table.Get("CmdRestartNumbering");
    public static string DescRestartNumbering => Table.Get("DescRestartNumbering");
    public static string CmdContinueNumbering => Table.Get("CmdContinueNumbering");
    public static string DescContinueNumbering => Table.Get("DescContinueNumbering");
    public static string CmdMoveUp => Table.Get("CmdMoveUp");
    public static string DescMoveUp => Table.Get("DescMoveUp");
    public static string CmdMoveDown => Table.Get("CmdMoveDown");
    public static string DescMoveDown => Table.Get("DescMoveDown");
    public static string CmdPromote => Table.Get("CmdPromote");
    public static string DescPromote => Table.Get("DescPromote");
    public static string CmdDemote => Table.Get("CmdDemote");
    public static string DescDemote => Table.Get("DescDemote");
    public static string ReasonNotInAList => Table.Get("ReasonNotInAList");
    public static string ReasonNothingToContinue => Table.Get("ReasonNothingToContinue");
    public static string ReasonAlreadyContinues => Table.Get("ReasonAlreadyContinues");
    public static string ReasonListAcrossContainers => Table.Get("ReasonListAcrossContainers");
    public static string ReasonNoHeadingStyles => Table.Get("ReasonNoHeadingStyles");
    public static string ReasonNoHeadingAtCaret => Table.Get("ReasonNoHeadingAtCaret");
    public static string ReasonMoveEmptyFinalSection => Table.Get("ReasonMoveEmptyFinalSection");
    public static string ReasonMoveInControl => Table.Get("ReasonMoveInControl");
    public static string ReasonMoveNotTopLevel => Table.Get("ReasonMoveNotTopLevel");
    public static string ReasonMoveAtTop => Table.Get("ReasonMoveAtTop");
    public static string ReasonMoveAtEnd => Table.Get("ReasonMoveAtEnd");
    public static string ReasonMoveCannot => Table.Get("ReasonMoveCannot");
    public static string ReasonShiftNotHeadingStyle => Table.Get("ReasonShiftNotHeadingStyle");
    public static string ReasonPromoteOutOfRange => Table.Get("ReasonPromoteOutOfRange");
    public static string ReasonDemoteOutOfRange => Table.Get("ReasonDemoteOutOfRange");
    public static string ReasonShiftNothing => Table.Get("ReasonShiftNothing");
    public static string NoteStructureFailed => Table.Get("NoteStructureFailed");
    public static string TabLayout => Table.Get("TabLayout");
    public static string MenuLayout => Table.Get("MenuLayout");
    public static string ReasonSectionNotInBody => Table.Get("ReasonSectionNotInBody");
    public static string ReasonSectionPrefix => Table.Get("ReasonSectionPrefix");
    public static string ReasonSectionStartsHere => Table.Get("ReasonSectionStartsHere");
    public static string ReasonSectionNothingToMerge => Table.Get("ReasonSectionNothingToMerge");
    public static string ReasonSectionFirst => Table.Get("ReasonSectionFirst");
    public static string NoteSectionNoChange => Table.Get("NoteSectionNoChange");
    public static string NoteSectionBreakKey => Table.Get("NoteSectionBreakKey");
    public static string PageNumberWordPage => Table.Get("PageNumberWordPage");
    public static string PageNumberWordOf => Table.Get("PageNumberWordOf");
    public static string PageNumberCountsEveryPage => Table.Get("PageNumberCountsEveryPage");
    public static string GroupPageSetup => Table.Get("GroupPageSetup");
    public static string GroupSections => Table.Get("GroupSections");
    public static string GroupHeaderFooter => Table.Get("GroupHeaderFooter");
    public static string CmdNumberPagesFrom => Table.Get("CmdNumberPagesFrom");
    public static string DescNumberPagesFrom => Table.Get("DescNumberPagesFrom");
    public static string CmdSectionBreak => Table.Get("CmdSectionBreak");
    public static string DescSectionBreak => Table.Get("DescSectionBreak");
    public static string CmdRemoveSection => Table.Get("CmdRemoveSection");
    public static string DescRemoveSection => Table.Get("DescRemoveSection");
    public static string CmdDifferentFirstPage => Table.Get("CmdDifferentFirstPage");
    public static string DescDifferentFirstPage => Table.Get("DescDifferentFirstPage");
    public static string CmdLeftRightPages => Table.Get("CmdLeftRightPages");
    public static string DescLeftRightPages => Table.Get("DescLeftRightPages");
    public static string CmdPageSize => Table.Get("CmdPageSize");
    public static string DescPageSize => Table.Get("DescPageSize");
    public static string CmdPageMargins => Table.Get("CmdPageMargins");
    public static string DescPageMargins => Table.Get("DescPageMargins");
    public static string CmdPageOrientation => Table.Get("CmdPageOrientation");
    public static string DescPageOrientation => Table.Get("DescPageOrientation");
    public static string CmdPageColumns => Table.Get("CmdPageColumns");
    public static string DescPageColumns => Table.Get("DescPageColumns");
    public static string CmdPageSetup => Table.Get("CmdPageSetup");
    public static string DescPageSetup => Table.Get("DescPageSetup");
    public static string CmdInsertPageNumber => Table.Get("CmdInsertPageNumber");
    public static string DescInsertPageNumber => Table.Get("DescInsertPageNumber");
    public static string MarginNormal => Table.Get("MarginNormal");
    public static string MarginNarrow => Table.Get("MarginNarrow");
    public static string MarginModerate => Table.Get("MarginModerate");
    public static string MarginWide => Table.Get("MarginWide");
    public static string OrientationPortrait => Table.Get("OrientationPortrait");
    public static string OrientationLandscape => Table.Get("OrientationLandscape");
    public static string ColumnsOne => Table.Get("ColumnsOne");
    public static string ColumnsTwo => Table.Get("ColumnsTwo");
    public static string ColumnsThree => Table.Get("ColumnsThree");
    public static string NumPagesTitle => Table.Get("NumPagesTitle");
    public static string NumPagesStartLabel => Table.Get("NumPagesStartLabel");
    public static string NumPagesContinue => Table.Get("NumPagesContinue");
    public static string NumPagesFormatLabel => Table.Get("NumPagesFormatLabel");
    public static string NumPagesFormatArabic => Table.Get("NumPagesFormatArabic");
    public static string NumPagesFormatLowerRoman => Table.Get("NumPagesFormatLowerRoman");
    public static string NumPagesFormatUpperRoman => Table.Get("NumPagesFormatUpperRoman");
    public static string NumPagesFormatLowerLetter => Table.Get("NumPagesFormatLowerLetter");
    public static string NumPagesFormatUpperLetter => Table.Get("NumPagesFormatUpperLetter");
    public static string NumPagesPrefixLabel => Table.Get("NumPagesPrefixLabel");
    public static string NumPagesPrefixHint => Table.Get("NumPagesPrefixHint");
    public static string NumPagesPageStartLine => Table.Get("NumPagesPageStartLine");
    public static string NumPagesFollowOne => Table.Get("NumPagesFollowOne");
    public static string NumPagesFollowMany => Table.Get("NumPagesFollowMany");
    public static string NumPagesOwnOne => Table.Get("NumPagesOwnOne");
    public static string NumPagesOwnMany => Table.Get("NumPagesOwnMany");
    public static string NumPagesInvalidStart => Table.Get("NumPagesInvalidStart");
    public static string NumPagesInvalidPrefix => Table.Get("NumPagesInvalidPrefix");
    public static string NumPagesOk => Table.Get("NumPagesOk");
    public static string PageSetupTitle => Table.Get("PageSetupTitle");
    public static string PsSizeLabel => Table.Get("PsSizeLabel");
    public static string PsCustom => Table.Get("PsCustom");
    public static string PsWidthFormat => Table.Get("PsWidthFormat");
    public static string PsHeightFormat => Table.Get("PsHeightFormat");
    public static string PsOrientationLabel => Table.Get("PsOrientationLabel");
    public static string PsMarginsLabel => Table.Get("PsMarginsLabel");
    public static string PsTopFormat => Table.Get("PsTopFormat");
    public static string PsBottomFormat => Table.Get("PsBottomFormat");
    public static string PsLeftFormat => Table.Get("PsLeftFormat");
    public static string PsRightFormat => Table.Get("PsRightFormat");
    public static string PsColumnsLabel => Table.Get("PsColumnsLabel");
    public static string PsColumnCount => Table.Get("PsColumnCount");
    public static string PsColumnSpacingFormat => Table.Get("PsColumnSpacingFormat");
    public static string PsColumnLine => Table.Get("PsColumnLine");
    public static string PsColumnsUnequal => Table.Get("PsColumnsUnequal");
    public static string PsUnitCm => Table.Get("PsUnitCm");
    public static string PsUnitInch => Table.Get("PsUnitInch");
    public static string PsApplyLabel => Table.Get("PsApplyLabel");
    public static string PsApplyThis => Table.Get("PsApplyThis");
    public static string PsApplyWhole => Table.Get("PsApplyWhole");
    public static string PsApplyFrom => Table.Get("PsApplyFrom");
    public static string PsFromHereNote => Table.Get("PsFromHereNote");
    public static string PsInvalidValueFormat => Table.Get("PsInvalidValueFormat");
    public static string PsOutOfRangeFormat => Table.Get("PsOutOfRangeFormat");
    public static string PsNoRoomForText => Table.Get("PsNoRoomForText");
    public static string PsColumnsDoNotFit => Table.Get("PsColumnsDoNotFit");
    public static string PsOk => Table.Get("PsOk");
    public static string NoteCardStale => Table.Get("NoteCardStale");
    public static string StatusStoryFormat => Table.Get("StatusStoryFormat");
    public static string StatusStorySharedOneFormat => Table.Get("StatusStorySharedOneFormat");
    public static string StatusStorySharedManyFormat => Table.Get("StatusStorySharedManyFormat");
    public static string SectionListRangeFormat => Table.Get("SectionListRangeFormat");
    public static string SectionListAndFormat => Table.Get("SectionListAndFormat");
    public static string NoteSharedFooter => Table.Get("NoteSharedFooter");
    public static string NoteSharedHeader => Table.Get("NoteSharedHeader");
    public static string CmdSections => Table.Get("CmdSections");
    public static string SectionsHint => Table.Get("SectionsHint");
    public static string SectionsTooMany => Table.Get("SectionsTooMany");
    public static string SectionTitleFormat => Table.Get("SectionTitleFormat");
    public static string SectionCurrent => Table.Get("SectionCurrent");
    public static string SectionStartsOnPageFormat => Table.Get("SectionStartsOnPageFormat");
    public static string SectionPageNotLaidOut => Table.Get("SectionPageNotLaidOut");
    public static string SectionNumbersStartFormat => Table.Get("SectionNumbersStartFormat");
    public static string SectionNumbersContinue => Table.Get("SectionNumbersContinue");
    public static string SectionNumbersContinueFormat => Table.Get("SectionNumbersContinueFormat");
    public static string SectionSetupFormat => Table.Get("SectionSetupFormat");
    public static string SectionColumnsOne => Table.Get("SectionColumnsOne");
    public static string SectionColumnsFormat => Table.Get("SectionColumnsFormat");
    public static string SectionHeaderOwn => Table.Get("SectionHeaderOwn");
    public static string SectionHeaderSame => Table.Get("SectionHeaderSame");
    public static string SectionFooterOwn => Table.Get("SectionFooterOwn");
    public static string SectionFooterSame => Table.Get("SectionFooterSame");
    public static string SectionFirstPage => Table.Get("SectionFirstPage");
    public static string SectionFirstOwnTip => Table.Get("SectionFirstOwnTip");
    public static string SectionRemoveMarker => Table.Get("SectionRemoveMarker");
    public static string SectionRemoveTipFormat => Table.Get("SectionRemoveTipFormat");
    public static string SectionNumberFrom => Table.Get("SectionNumberFrom");
    public static string SectionTakesOverFormat => Table.Get("SectionTakesOverFormat");
    public static string SectionButtonNameFormat => Table.Get("SectionButtonNameFormat");
    public static string SectionsDocumentHeading => Table.Get("SectionsDocumentHeading");
    public static string SectionLeftRight => Table.Get("SectionLeftRight");
    public static string NoteSectionRowStale => Table.Get("NoteSectionRowStale");
    public static string PsColumnCountInvalid => Table.Get("PsColumnCountInvalid");
    public static string PsEqualizeOne => Table.Get("PsEqualizeOne");
    public static string PsEqualizeMany => Table.Get("PsEqualizeMany");
    public static string BreakNextPage => Table.Get("BreakNextPage");
    public static string BreakContinuous => Table.Get("BreakContinuous");
    public static string BreakEvenPage => Table.Get("BreakEvenPage");
    public static string BreakOddPage => Table.Get("BreakOddPage");
    public static string PageNumberPlaceFooter => Table.Get("PageNumberPlaceFooter");
    public static string PageNumberPlaceHeader => Table.Get("PageNumberPlaceHeader");
    public static string PageNumberAlignLeft => Table.Get("PageNumberAlignLeft");
    public static string PageNumberAlignCenter => Table.Get("PageNumberAlignCenter");
    public static string PageNumberAlignRight => Table.Get("PageNumberAlignRight");
    public static string PageNumberItemFormat => Table.Get("PageNumberItemFormat");
    public static string ReasonNothingToRedo => Table.Get("ReasonNothingToRedo");
    public static string CmdSave => Table.Get("CmdSave");
    public static string CmdSaveAs => Table.Get("CmdSaveAs");
    public static string SaveDialogTitle => Table.Get("SaveDialogTitle");
    public static string UntitledName => Table.Get("UntitledName");
    public static string UntitledNumberedFormat => Table.Get("UntitledNumberedFormat");
    public static string ReasonNothingToSave => Table.Get("ReasonNothingToSave");
    public static string ReasonSaveFaulted => Table.Get("ReasonSaveFaulted");
    public static string ReasonSaveAsFaulted => Table.Get("ReasonSaveAsFaulted");
    public static string SaveErrorTitle => Table.Get("SaveErrorTitle");
    public static string SaveErrorKept => Table.Get("SaveErrorKept");
    public static string SaveFailOpenElsewhere => Table.Get("SaveFailOpenElsewhere");
    public static string SaveFailUnexpected => Table.Get("SaveFailUnexpected");
    public static string SaveLeftoverNoteFormat => Table.Get("SaveLeftoverNoteFormat");
    public static string ChangedTitle => Table.Get("ChangedTitle");
    public static string ChangedMessageFormat => Table.Get("ChangedMessageFormat");
    public static string ChangedOverwrite => Table.Get("ChangedOverwrite");
    public static string UnsavedTitle => Table.Get("UnsavedTitle");
    public static string UnsavedMessageFormat => Table.Get("UnsavedMessageFormat");
    public static string UnsavedDontSave => Table.Get("UnsavedDontSave");
    public static string RecoveryTitle => Table.Get("RecoveryTitle");
    public static string RecoveryBody => Table.Get("RecoveryBody");
    public static string RecoveryRestore => Table.Get("RecoveryRestore");
    public static string RecoveryDiscard => Table.Get("RecoveryDiscard");
    public static string RecoveryLater => Table.Get("RecoveryLater");
    public static string RecoveryMoreFormat => Table.Get("RecoveryMoreFormat");
    public static string RecoveryEntryFormat => Table.Get("RecoveryEntryFormat");
    public static string PageViewNameFormat => Table.Get("PageViewNameFormat");
    public static string PageViewPageNameFormat => Table.Get("PageViewPageNameFormat");
    public static string FormattingNoSelection => Table.Get("FormattingNoSelection");
    public static string FormattingCaretHint => Table.Get("FormattingCaretHint");
    public static string FormattingTooLarge => Table.Get("FormattingTooLarge");
    public static string FormattingParagraphStyle => Table.Get("FormattingParagraphStyle");
    public static string FormattingCharacterStyle => Table.Get("FormattingCharacterStyle");
    public static string FormattingStyleMixed => Table.Get("FormattingStyleMixed");
    public static string FormattingStyleUndefinedFormat => Table.Get("FormattingStyleUndefinedFormat");
    public static string FormattingRowsHeading => Table.Get("FormattingRowsHeading");
    public static string FormattingNoRows => Table.Get("FormattingNoRows");
    public static string FormattingRemoveFormat => Table.Get("FormattingRemoveFormat");
    public static string FormattingRowNameFormat => Table.Get("FormattingRowNameFormat");
    public static string FormattingPartly => Table.Get("FormattingPartly");
    public static string FormattingMixedValues => Table.Get("FormattingMixedValues");
    public static string FormattingSharedNote => Table.Get("FormattingSharedNote");

    /// <summary>The key of the wording of an open error (one text per <see cref="OpenError"/> value).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has no text.</exception>
    public static string OpenErrorKey(OpenError error) => error switch
    {
        OpenError.NotFound => "OpenErrorNotFound",
        OpenError.AccessDenied => "OpenErrorAccessDenied",
        OpenError.NotADocx => "OpenErrorNotADocx",
        OpenError.Corrupt => "OpenErrorCorrupt",
        OpenError.TooLarge => "OpenErrorTooLarge",
        OpenError.Other => "OpenErrorOther",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "This open error has no text."),
    };

    /// <summary>The words of an open error in the UI language; never the text of an exception or of the document.</summary>
    public static string ForOpenError(OpenError error) => Table.Get(OpenErrorKey(error));

    /// <summary>The key of the wording of a failed save (several reasons that only the PDF app can meet share <c>SaveFailOther</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has no text: a new reason must be worded here.</exception>
    public static string SaveFailureKey(SaveFailureReason reason) => reason switch
    {
        SaveFailureReason.NotWritable => "SaveFailNotWritable",
        SaveFailureReason.DiskFull => "SaveFailDiskFull",
        SaveFailureReason.WriteFailed => "SaveFailWriteFailed",
        SaveFailureReason.TargetExists => "SaveFailTargetExists",
        SaveFailureReason.OriginalChanged => "SaveFailOriginalChanged",
        SaveFailureReason.VerificationFailed or SaveFailureReason.SourceModified or SaveFailureReason.WriterNotSupported
            or SaveFailureReason.EncryptedNotSupported or SaveFailureReason.Canceled or SaveFailureReason.RedactionFailed => "SaveFailOther",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "This save failure has no text."),
    };

    /// <summary>The key of the wording of a save that stopped at its own check.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has no text.</exception>
    public static string SaveCheckKey(DocxSaveReason reason) => reason switch
    {
        DocxSaveReason.PartsChanged => "SaveFailCheck",
        DocxSaveReason.ModelNotReadable => "SaveFailUnreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "This save check has no text."),
    };

    /// <summary>The words of a failed save in the UI language; never the text of an exception or of the document.</summary>
    public static string ForSaveFailure(SaveFailureReason reason) => Table.Get(SaveFailureKey(reason));

    /// <summary>The words of a save that stopped at its own check.</summary>
    public static string ForSaveCheck(DocxSaveReason reason) => Table.Get(SaveCheckKey(reason));

    /// <summary>Fills the <c>{0}</c> of a template.</summary>
    public static string Fill(string template, object? first) =>
        template.Replace("{0}", Convert.ToString(first, Culture), StringComparison.Ordinal);

    /// <summary>Fills the <c>{0}</c> and <c>{1}</c> of a template.</summary>
    public static string Fill(string template, object? first, object? second) =>
        Fill(template, first).Replace("{1}", Convert.ToString(second, Culture), StringComparison.Ordinal);

    /// <summary>Fills the <c>{0}</c>, <c>{1}</c> and <c>{2}</c> of a template.</summary>
    public static string Fill(string template, object? first, object? second, object? third) =>
        Fill(template, first, second).Replace("{2}", Convert.ToString(third, Culture), StringComparison.Ordinal);
}
