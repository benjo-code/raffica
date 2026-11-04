using System.Globalization;

namespace Raffica.Shell.Localization;

/// <summary>The texts the shell controls bring along (English source, German translation).</summary>
public static class ShellStrings
{
    private static readonly StringTable Table = new("Raffica.Shell.Strings", typeof(ShellStrings).Assembly);

    /// <summary>The UI language; set once at startup, a change applies after a restart.</summary>
    public static CultureInfo Culture
    {
        get => Table.Culture;
        set => Table.Culture = value;
    }

    /// <summary>The value of a key by name (for tools such as the gallery's <c>--print-string</c>).</summary>
    public static string Get(string key) => Table.Get(key);

    /// <summary>The value of a key in an explicit language (used by <see cref="KeyNames"/>).</summary>
    public static string GetIn(string key, CultureInfo culture) => Table.GetIn(key, culture);

    public static string Close => Table.Get("Close");
    public static string Cancel => Table.Get("Cancel");
    public static string Ok => Table.Get("Ok");
    public static string NotificationInfo => Table.Get("NotificationInfo");
    public static string NotificationWarning => Table.Get("NotificationWarning");
    public static string NotificationError => Table.Get("NotificationError");
    public static string Search => Table.Get("Search");
    public static string More => Table.Get("More");
    public static string ZoomOut => Table.Get("ZoomOut");
    public static string ZoomIn => Table.Get("ZoomIn");
    public static string ZoomLevel => Table.Get("ZoomLevel");
    public static string RibbonMore => Table.Get("RibbonMore");
    public static string ContextCut => Table.Get("ContextCut");
    public static string ContextCopy => Table.Get("ContextCopy");
    public static string ContextPaste => Table.Get("ContextPaste");
    public static string ContextSelectAll => Table.Get("ContextSelectAll");
    public static string ContextUndo => Table.Get("ContextUndo");
    public static string ContextRedo => Table.Get("ContextRedo");
    public static string SearchNoResults => Table.Get("SearchNoResults");
    public static string NewDocument => Table.Get("NewDocument");
    public static string CloseDocument => Table.Get("CloseDocument");
    public static string TabUnsavedChanges => Table.Get("TabUnsavedChanges");
    public static string Minimize => Table.Get("Minimize");
    public static string Maximize => Table.Get("Maximize");
    public static string Restore => Table.Get("Restore");
    public static string PaneNavigation => Table.Get("PaneNavigation");
    public static string PaneComments => Table.Get("PaneComments");
    public static string PaneFormat => Table.Get("PaneFormat");
    public static string PaneDocument => Table.Get("PaneDocument");
    public static string FieldCodeHint => Table.Get("FieldCodeHint");
    public static string StartNew => Table.Get("StartNew");
    public static string StartOpen => Table.Get("StartOpen");
    public static string StartRecent => Table.Get("StartRecent");
    public static string StartBrowse => Table.Get("StartBrowse");
    public static string StartRecentEmpty => Table.Get("StartRecentEmpty");
    public static string StartBlankDocument => Table.Get("StartBlankDocument");
    public static string StartBlankSpreadsheet => Table.Get("StartBlankSpreadsheet");
    public static string StartBlankPresentation => Table.Get("StartBlankPresentation");
    public static string StartOpenPdf => Table.Get("StartOpenPdf");
    public static string SettingsTitle => Table.Get("SettingsTitle");
    public static string SettingsTheme => Table.Get("SettingsTheme");
    public static string SettingsThemeLight => Table.Get("SettingsThemeLight");
    public static string SettingsThemeDark => Table.Get("SettingsThemeDark");
    public static string SettingsThemeSystem => Table.Get("SettingsThemeSystem");
    public static string SettingsLanguage => Table.Get("SettingsLanguage");
    public static string SettingsRestartNote => Table.Get("SettingsRestartNote");
    public static string SettingsCompact => Table.Get("SettingsCompact");
    public static string SettingsAutosave => Table.Get("SettingsAutosave");

    /// <summary>"Focus: {0}": the status bar text during F6 pane navigation.</summary>
    public static string FocusPane(string pane) => string.Format(Culture, Table.Get("FocusPane"), pane);

    /// <summary>"Search (Ctrl+K)": the tooltip of the search field; the shortcut inside is formatted by <see cref="KeyNames"/>.</summary>
    public static string SearchTooltip(string shortcut) => string.Format(Culture, Table.Get("SearchTooltip"), shortcut);

    /// <summary>"More commands in {0}": the name of the overflow button of a compact ribbon group.</summary>
    public static string RibbonGroupMore(string group) => string.Format(Culture, Table.Get("RibbonGroupMore"), group);
}
