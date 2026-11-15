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
    public static string SettingsDefaultView => Table.Get("SettingsDefaultView");
    public static string SettingsDefaultZoom => Table.Get("SettingsDefaultZoom");
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
    public static string ReasonNothingToUndo => Table.Get("ReasonNothingToUndo");
    public static string ReasonNothingToRedo => Table.Get("ReasonNothingToRedo");
    public static string CmdSave => Table.Get("CmdSave");
    public static string CmdSaveAs => Table.Get("CmdSaveAs");
    public static string SaveDialogTitle => Table.Get("SaveDialogTitle");
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
    public static string PageViewNameFormat => Table.Get("PageViewNameFormat");
    public static string PageViewPageNameFormat => Table.Get("PageViewPageNameFormat");

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
