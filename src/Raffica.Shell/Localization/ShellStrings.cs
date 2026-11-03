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
    public static string SearchShortcut => Table.Get("SearchShortcut");
    public static string SearchNoResults => Table.Get("SearchNoResults");
    public static string NewDocument => Table.Get("NewDocument");
    public static string CloseDocument => Table.Get("CloseDocument");
    public static string Minimize => Table.Get("Minimize");
    public static string Maximize => Table.Get("Maximize");
    public static string Restore => Table.Get("Restore");

    /// <summary>"Search (Ctrl+K)": the tooltip of the search field; a plain formatted string until the key-name helper of the keyboard task replaces the shortcut text.</summary>
    public static string SearchTooltip(string shortcut) => string.Format(Culture, Table.Get("SearchTooltip"), shortcut);

    /// <summary>"More commands in {0}": the name of the overflow button of a compact ribbon group.</summary>
    public static string RibbonGroupMore(string group) => string.Format(Culture, Table.Get("RibbonGroupMore"), group);
}
