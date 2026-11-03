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
}
