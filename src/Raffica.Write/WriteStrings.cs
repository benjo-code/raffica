using System.Globalization;
using Raffica.Shell.Localization;
using Raffica.Write.Documents;

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

    /// <summary>Fills the <c>{0}</c> of a template.</summary>
    public static string Fill(string template, object? first) =>
        template.Replace("{0}", Convert.ToString(first, Culture), StringComparison.Ordinal);
}
