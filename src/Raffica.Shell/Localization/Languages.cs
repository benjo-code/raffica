using System.Globalization;

namespace Raffica.Shell.Localization;

/// <summary>Picks the UI language. The persisted setting arrives with milestone 1.2.</summary>
public static class Languages
{
    /// <summary>
    /// <c>de</c> or <c>en</c> if requested; null follows the system (German if the system language is German,
    /// English otherwise). Any other code throws <see cref="ArgumentException"/>.
    /// </summary>
    public static CultureInfo Resolve(string? requested, CultureInfo systemUi)
    {
        ArgumentNullException.ThrowIfNull(systemUi);
        return requested switch
        {
            null => CultureInfo.GetCultureInfo(systemUi.TwoLetterISOLanguageName == "de" ? "de" : "en"),
            "de" => CultureInfo.GetCultureInfo("de"),
            "en" => CultureInfo.GetCultureInfo("en"),
            _ => throw new ArgumentException($"unknown language '{requested}' (expected en or de)", nameof(requested)),
        };
    }
}
