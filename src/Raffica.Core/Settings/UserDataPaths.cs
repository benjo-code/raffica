namespace Raffica.Core.Settings;

/// <summary>The operating system family that decides where user files live.</summary>
public enum UserDataSystem
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>Where Raffica keeps its local user files (settings, recent files, caches). Nothing leaves these folders.</summary>
public static class UserDataPaths
{
    /// <summary>The system this process runs on; every other system than Windows and macOS counts as Linux.</summary>
    public static UserDataSystem Current { get; } =
        OperatingSystem.IsWindows() ? UserDataSystem.Windows
        : OperatingSystem.IsMacOS() ? UserDataSystem.MacOS
        : UserDataSystem.Linux;

    /// <summary>The config folder of the current user on this system.</summary>
    public static string ConfigDirectory() =>
        ConfigDirectory(Environment.GetEnvironmentVariable, Current);

    /// <summary>The cache folder of the current user on this system.</summary>
    public static string CacheDirectory() =>
        CacheDirectory(Environment.GetEnvironmentVariable, Current);

    /// <summary>
    /// The config folder: <c>%APPDATA%\Raffica</c> on Windows, <c>$HOME/Library/Application Support/Raffica</c> on macOS
    /// (<c>XDG_CONFIG_HOME</c> is not read there), otherwise <c>$XDG_CONFIG_HOME/raffica</c> with the fallback
    /// <c>$HOME/.config/raffica</c>. An empty variable counts as not set. Without any usable variable the folder lies in
    /// the temp folder, so callers always get a path.
    /// </summary>
    public static string ConfigDirectory(Func<string, string?> getEnvironmentVariable, UserDataSystem system)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        switch (system)
        {
            case UserDataSystem.Windows:
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

            case UserDataSystem.MacOS:
                {
                    var home = Read(getEnvironmentVariable, "HOME");
                    return home is not null
                        ? Path.Combine(home, "Library", "Application Support", "Raffica")
                        : Path.Combine(Path.GetTempPath(), "Raffica");
                }

            default:
                {
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
        }
    }

    /// <summary>
    /// The cache folder for files that can be recreated: <c>%LOCALAPPDATA%\Raffica\Cache</c> on Windows,
    /// <c>$HOME/Library/Caches/Raffica</c> on macOS, otherwise <c>$XDG_CACHE_HOME/raffica</c> with the fallback
    /// <c>$HOME/.cache/raffica</c>. Without any usable variable the folder lies in the temp folder.
    /// </summary>
    public static string CacheDirectory(Func<string, string?> getEnvironmentVariable, UserDataSystem system)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        switch (system)
        {
            case UserDataSystem.Windows:
                {
                    var local = Read(getEnvironmentVariable, "LOCALAPPDATA");
                    if (local is not null)
                    {
                        return Path.Combine(local, "Raffica", "Cache");
                    }

                    var profile = Read(getEnvironmentVariable, "USERPROFILE");
                    return profile is not null
                        ? Path.Combine(profile, "AppData", "Local", "Raffica", "Cache")
                        : Path.Combine(Path.GetTempPath(), "Raffica", "Cache");
                }

            case UserDataSystem.MacOS:
                {
                    var home = Read(getEnvironmentVariable, "HOME");
                    return home is not null
                        ? Path.Combine(home, "Library", "Caches", "Raffica")
                        : Path.Combine(Path.GetTempPath(), "Raffica");
                }

            default:
                {
                    var xdg = Read(getEnvironmentVariable, "XDG_CACHE_HOME");
                    if (xdg is not null)
                    {
                        return Path.Combine(xdg, "raffica");
                    }

                    var home = Read(getEnvironmentVariable, "HOME");
                    return home is not null
                        ? Path.Combine(home, ".cache", "raffica")
                        : Path.Combine(Path.GetTempPath(), "raffica");
                }
        }
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
