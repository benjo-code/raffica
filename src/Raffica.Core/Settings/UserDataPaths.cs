namespace Raffica.Core.Settings;

/// <summary>Where Raffica keeps its local user files (settings, recent files). Nothing leaves this folder.</summary>
public static class UserDataPaths
{
    /// <summary>The config folder of the current user on this system.</summary>
    public static string ConfigDirectory() =>
        ConfigDirectory(Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());

    /// <summary>
    /// The config folder: <c>%APPDATA%\Raffica</c> on Windows, otherwise <c>$XDG_CONFIG_HOME/raffica</c> with the
    /// fallback <c>$HOME/.config/raffica</c>. An empty variable counts as not set. Without any usable variable the
    /// folder lies in the temp folder, so callers always get a path.
    /// </summary>
    public static string ConfigDirectory(Func<string, string?> getEnvironmentVariable, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        if (isWindows)
        {
            var appData = Read(getEnvironmentVariable, "APPDATA");
            if (appData is not null)
            {
                return Path.Combine(appData, "Raffica");
            }

            var profile = Read(getEnvironmentVariable, "USERPROFILE");
            return profile is not null
                ? Path.Combine(profile, "AppData", "Roaming", "Raffica")
                : Path.Combine(Path.GetTempPath(), "Raffica");
        }

        var xdg = Read(getEnvironmentVariable, "XDG_CONFIG_HOME");
        if (xdg is not null)
        {
            return Path.Combine(xdg, "raffica");
        }

        var home = Read(getEnvironmentVariable, "HOME");
        return home is not null
            ? Path.Combine(home, ".config", "raffica")
            : Path.Combine(Path.GetTempPath(), "raffica");
    }

    /// <summary>The folder of the saved signatures (<c>signatures</c> in <see cref="ConfigDirectory()"/>); read only when a menu of signatures opens.</summary>
    public static string SignaturesDirectory() => Path.Combine(ConfigDirectory(), "signatures");

    /// <summary>The folder of the language models the user imported for text recognition (<c>tessdata</c> in <see cref="ConfigDirectory()"/>); created by the first import.</summary>
    public static string TessdataDirectory() => Path.Combine(ConfigDirectory(), "tessdata");

    private static string? Read(Func<string, string?> getEnvironmentVariable, string name)
    {
        var value = getEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
