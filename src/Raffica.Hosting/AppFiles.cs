using System.Security.Cryptography;
using System.Text;
using Raffica.Core.Hosting;
using Raffica.Core.Settings;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Hosting;

/// <summary>
/// Where an app keeps its files and how it names its single instance: the shell settings (theme, language), the app settings and the recent files.
/// With <c>--settings-file &lt;path&gt;</c> the app settings and the recent files lie in the folder of that file, so measurement runs and tests never touch
/// the folders of the user.
/// </summary>
/// <param name="AppFolder">The app's folder name for the shell settings, <c>Pdf</c> or <c>Write</c>.</param>
/// <param name="SettingsName">The file name of the app settings.</param>
/// <param name="RecentName">The file name of the recent files.</param>
public sealed record AppFiles(string AppFolder, string SettingsName, string RecentName)
{
    /// <summary>The names of Raffica PDF.</summary>
    public static AppFiles Pdf { get; } = new("Pdf", "pdf-settings.json", "recent.json");

    /// <summary>The names of Raffica Write.</summary>
    public static AppFiles Write { get; } = new("Write", "write-settings.json", "write-recent.json");

    /// <summary>The shell settings file: the one given with <c>--settings-file</c>, else the default place of the app.</summary>
    public string ShellPathFor(string? settingsFile) =>
        string.IsNullOrWhiteSpace(settingsFile) ? ShellSettingsStore.DefaultPath(AppFolder) : settingsFile;

    /// <summary>The app settings file next to the given shell settings file, else in the config folder of the user.</summary>
    public string AppSettingsPathFor(string? settingsFile) => Path.Combine(ConfigDirectoryFor(settingsFile), SettingsName);

    /// <summary>The recent files next to the given shell settings file, else in the config folder of the user.</summary>
    public string RecentPathFor(string? settingsFile) => Path.Combine(ConfigDirectoryFor(settingsFile), RecentName);

    /// <summary>The config folder: the folder of the given shell settings file, else <see cref="UserDataPaths.ConfigDirectory()"/>. A malformed path goes to the temp folder.</summary>
    public static string ConfigDirectoryFor(string? settingsFile) => FolderOf(settingsFile) ?? UserDataPaths.ConfigDirectory();

    /// <summary>
    /// One instance per user and app; a run with its own settings file (measurements, tests) is an instance of its own and never meets the app of the user.
    /// </summary>
    public string InstanceName(string? settingsFile)
    {
        var baseName = AppFolder == Pdf.AppFolder ? SingleInstance.DefaultName : "raffica-" + AppFolder.ToLowerInvariant() + "-" + Environment.UserName;
        if (string.IsNullOrWhiteSpace(settingsFile))
        {
            return baseName;
        }

        string full;
        try
        {
            full = Path.GetFullPath(settingsFile);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            full = settingsFile;
        }

        return baseName + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 6);
    }

    // A switch with a malformed path still must not send the files into the folder of the user: they go to the temp folder.
    /// <summary>The folder of the given settings file, or <see langword="null"/> without one; a malformed path gives the temp folder.</summary>
    public static string? FolderOf(string? settingsFile)
    {
        if (string.IsNullOrWhiteSpace(settingsFile))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(settingsFile)) ?? Path.GetTempPath();
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return Path.GetTempPath();
        }
    }
}
