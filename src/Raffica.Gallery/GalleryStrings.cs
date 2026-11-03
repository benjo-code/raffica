using System.Globalization;
using Raffica.Shell.Localization;

namespace Raffica.Gallery;

/// <summary>The labels and sample texts of the gallery itself (English source, German translation); the shell controls bring their own.</summary>
public static class GalleryStrings
{
    private static readonly StringTable Table = new("Raffica.Gallery.Strings", typeof(GalleryStrings).Assembly);

    /// <summary>The UI language; set once at startup, a change applies after a restart.</summary>
    public static CultureInfo Culture
    {
        get => Table.Culture;
        set => Table.Culture = value;
    }

    public static string WindowTitle => Table.Get("WindowTitle");
    public static string SectionButtons => Table.Get("SectionButtons");
    public static string StateRest => Table.Get("StateRest");
    public static string StateHover => Table.Get("StateHover");
    public static string StatePressedOn => Table.Get("StatePressedOn");
    public static string StateFocus => Table.Get("StateFocus");
    public static string StateDisabled => Table.Get("StateDisabled");
    public static string RowButton => Table.Get("RowButton");
    public static string RowBoxed => Table.Get("RowBoxed");
    public static string RowPrimary => Table.Get("RowPrimary");
    public static string RowDestructive => Table.Get("RowDestructive");
    public static string RowIcon => Table.Get("RowIcon");
    public static string RowToggle => Table.Get("RowToggle");
    public static string RowCheckBox => Table.Get("RowCheckBox");
    public static string RowRadio => Table.Get("RowRadio");
    public static string RowSegmented => Table.Get("RowSegmented");
    public static string SampleInsert => Table.Get("SampleInsert");
    public static string SampleBrowse => Table.Get("SampleBrowse");
    public static string SampleApply => Table.Get("SampleApply");
    public static string SampleTrackChanges => Table.Get("SampleTrackChanges");
    public static string SamplePortrait => Table.Get("SamplePortrait");
    public static string SampleMarks => Table.Get("SampleMarks");
    public static string SamplePages => Table.Get("SamplePages");
    public static string SampleResults => Table.Get("SampleResults");
    public static string IconBold => Table.Get("IconBold");
    public static string SectionInputs => Table.Get("SectionInputs");
    public static string SectionMenus => Table.Get("SectionMenus");
    public static string StateTypingOpen => Table.Get("StateTypingOpen");
    public static string RowTextField => Table.Get("RowTextField");
    public static string RowTextWithValue => Table.Get("RowTextWithValue");
    public static string RowSelect => Table.Get("RowSelect");
    public static string RowSelectPlaceholder => Table.Get("RowSelectPlaceholder");
    public static string RowFlyout => Table.Get("RowFlyout");
    public static string RowMenuBar => Table.Get("RowMenuBar");
    public static string RowContextMenu => Table.Get("RowContextMenu");
    public static string RowMenuInline => Table.Get("RowMenuInline");
    public static string RowTooltip => Table.Get("RowTooltip");
    public static string SamplePlaceholder => Table.Get("SamplePlaceholder");
    public static string SampleReport => Table.Get("SampleReport");
    public static string SampleSelectHint => Table.Get("SampleSelectHint");
    public static string SampleParagraph => Table.Get("SampleParagraph");
    public static string SampleHeading1 => Table.Get("SampleHeading1");
    public static string SampleHeading2 => Table.Get("SampleHeading2");
    public static string SampleQuote => Table.Get("SampleQuote");
    public static string MenuInsert => Table.Get("MenuInsert");
    public static string MenuFile => Table.Get("MenuFile");
    public static string MenuEdit => Table.Get("MenuEdit");
    public static string ItemOpen => Table.Get("ItemOpen");
    public static string ItemClose => Table.Get("ItemClose");
    public static string ItemCut => Table.Get("ItemCut");
    public static string ItemCopy => Table.Get("ItemCopy");
    public static string ItemPaste => Table.Get("ItemPaste");
    public static string ItemPasteSpecial => Table.Get("ItemPasteSpecial");
    public static string ItemTextOnly => Table.Get("ItemTextOnly");
    public static string ItemValuesOnly => Table.Get("ItemValuesOnly");
    public static string ItemInsertRows => Table.Get("ItemInsertRows");
    public static string ItemInsertColumns => Table.Get("ItemInsertColumns");
    public static string ItemInsertComment => Table.Get("ItemInsertComment");
    public static string ItemFormatCells => Table.Get("ItemFormatCells");
    public static string ItemPrint => Table.Get("ItemPrint");
    public static string ItemDeleteRows => Table.Get("ItemDeleteRows");
    public static string ContextHint => Table.Get("ContextHint");
    public static string TipTitle => Table.Get("TipTitle");
    public static string TipDescription => Table.Get("TipDescription");
    public static string SectionDialogs => Table.Get("SectionDialogs");
    public static string DialogOpen => Table.Get("DialogOpen");
    public static string SampleDialogTitle => Table.Get("SampleDialogTitle");
    public static string SampleDialogText => Table.Get("SampleDialogText");
    public static string NoteCrash => Table.Get("NoteCrash");
    public static string NoteCompare => Table.Get("NoteCompare");
    public static string NoteFontLead => Table.Get("NoteFontLead");
    public static string NoteFontText => Table.Get("NoteFontText");
    public static string NoteShowFonts => Table.Get("NoteShowFonts");
    public static string NoteSaveLead => Table.Get("NoteSaveLead");
    public static string NoteSaveText => Table.Get("NoteSaveText");
    public static string NoteSaveAs => Table.Get("NoteSaveAs");
}
