using System.Globalization;
using System.Resources;

namespace ShellProbe;

/// <summary>
/// UI texts in English (source) and German. Both live in the main assembly, see ShellProbe.csproj; <see cref="Culture"/>
/// picks the language.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager English = new("ShellProbe.Strings.en", typeof(Strings).Assembly);
    private static readonly ResourceManager German = new("ShellProbe.Strings.de", typeof(Strings).Assembly);

    private static ResourceManager s_current = English;

    /// <summary>Sets the UI language: German for the two-letter language <c>de</c>, English for everything else.</summary>
    public static CultureInfo Culture
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            s_current = value.TwoLetterISOLanguageName == "de" ? German : English;
        }
    }

    /// <summary>The text of a key; a missing key is an error, never a silent fallback to English.</summary>
    public static string Get(string key) =>
        s_current.GetString(key, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Missing string: {key}");

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
