using Raffica.Core.Settings;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Pdf.Settings;

/// <summary>
/// Where the PDF app keeps its files: the shell settings (theme, language), the app settings and the recent files. With
/// <c>--settings-file &lt;path&gt;</c> the other two files lie in the folder of that file, so measurement runs and tests never touch
/// the folders of the user.
/// </summary>
internal static class PdfSettingsFiles
{
    private const string PdfSettingsName = "pdf-settings.json";
    private const string RecentName = "recent.json";

    /// <summary>The shell settings file in the default place.</summary>
    public static string ShellPath => ShellSettingsStore.DefaultPath("Pdf");

    /// <summary>The app settings file in the config folder of the user.</summary>
    public static string PdfSettingsPath => Path.Combine(UserDataPaths.ConfigDirectory(), PdfSettingsName);

    /// <summary>The recent files in the config folder of the user.</summary>
    public static string RecentPath => Path.Combine(UserDataPaths.ConfigDirectory(), RecentName);

    /// <summary>The shell settings file: the one given with <c>--settings-file</c>, else <see cref="ShellPath"/>.</summary>
    public static string ShellPathFor(string? settingsFile) => string.IsNullOrWhiteSpace(settingsFile) ? ShellPath : settingsFile;

    /// <summary>The app settings file next to the given shell settings file, else <see cref="PdfSettingsPath"/>.</summary>
    public static string PdfSettingsPathFor(string? settingsFile) =>
        FolderOf(settingsFile) is { } folder ? Path.Combine(folder, PdfSettingsName) : PdfSettingsPath;

    /// <summary>The recent files next to the given shell settings file, else <see cref="RecentPath"/>.</summary>
    public static string RecentPathFor(string? settingsFile) =>
        FolderOf(settingsFile) is { } folder ? Path.Combine(folder, RecentName) : RecentPath;

    /// <summary>
    /// The config folder of the update files (<c>update.json</c>, the <c>updates</c> staging folder, <c>session.json</c>): the folder of the given shell settings file, else
    /// <see cref="UserDataPaths.ConfigDirectory()"/>, so measurement runs and tests never touch the folders of the user.
    /// </summary>
    public static string ConfigDirectoryFor(string? settingsFile) => FolderOf(settingsFile) ?? UserDataPaths.ConfigDirectory();

    /// <summary>The folder of the saved signatures: <c>signatures</c> next to the given shell settings file, else <see cref="UserDataPaths.SignaturesDirectory()"/>.</summary>
    public static string SignaturesDirectoryFor(string? settingsFile) =>
        FolderOf(settingsFile) is { } folder ? Path.Combine(folder, "signatures") : UserDataPaths.SignaturesDirectory();

    /// <summary>The folder of the language models the user imported: <c>tessdata</c> next to the given shell settings file, else <see cref="UserDataPaths.TessdataDirectory()"/>.</summary>
    public static string TessdataDirectoryFor(string? settingsFile) =>
        FolderOf(settingsFile) is { } folder ? Path.Combine(folder, "tessdata") : UserDataPaths.TessdataDirectory();

    // A switch with a malformed path still must not send the files into the folder of the user: they go to the temp folder.
    private static string? FolderOf(string? settingsFile)
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
