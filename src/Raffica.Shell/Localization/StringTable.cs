using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Raffica.Shell.Localization;

/// <summary>
/// A string table in English and German. Both languages are embedded in one assembly as <c>&lt;baseName&gt;.en</c>
/// and <c>&lt;baseName&gt;.de</c> (no satellite assemblies, which would need runtime assembly loading under Native
/// AOT). <see cref="Culture"/> picks the language; a missing key throws, it never falls back silently.
/// </summary>
public sealed class StringTable(string baseName, Assembly assembly)
{
    // Each ResourceManager only ever sees the invariant culture.
    private readonly ResourceManager _english = new(baseName + ".en", assembly);
    private readonly ResourceManager _german = new(baseName + ".de", assembly);
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    /// <summary>The UI language: German for the two-letter language <c>de</c>, English for everything else.</summary>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _culture = value;
        }
    }

    public string Get(string key)
    {
        var manager = _culture.TwoLetterISOLanguageName == "de" ? _german : _english;
        return manager.GetString(key, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Missing string: {key}");
    }

    /// <summary>The value of a key in a given language, regardless of <see cref="Culture"/> (for formatting with an explicit UI language).</summary>
    public string GetIn(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var manager = culture.TwoLetterISOLanguageName == "de" ? _german : _english;
        return manager.GetString(key, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Missing string: {key}");
    }

    public string Get(string key, params object[] args) => string.Format(_culture, Get(key), args);
}
