using Raffica.Core.Settings;
using Raffica.Hosting;

namespace Raffica.Pdf.Settings;

/// <summary>
/// Where the PDF app keeps its files: the shell settings (theme, language), the app settings and the recent files. With
/// <c>--settings-file &lt;path&gt;</c> the other two files lie in the folder of that file, so measurement runs and tests never touch
/// the folders of the user.
/// </summary>
internal static class PdfSettingsFiles
{
    /// <summary>The shell settings file in the default place.</summary>
    public static string ShellPath => AppFiles.Pdf.ShellPathFor(null);

    /// <summary>The app settings file in the config folder of the user.</summary>
    public static string PdfSettingsPath => AppFiles.Pdf.AppSettingsPathFor(null);

    /// <summary>The recent files in the config folder of the user.</summary>
    public static string RecentPath => AppFiles.Pdf.RecentPathFor(null);

    /// <summary>The shell settings file: the one given with <c>--settings-file</c>, else <see cref="ShellPath"/>.</summary>
    public static string ShellPathFor(string? settingsFile) => AppFiles.Pdf.ShellPathFor(settingsFile);

    /// <summary>The app settings file next to the given shell settings file, else <see cref="PdfSettingsPath"/>.</summary>
    public static string PdfSettingsPathFor(string? settingsFile) =>
        AppFiles.Pdf.AppSettingsPathFor(settingsFile);

    /// <summary>The recent files next to the given shell settings file, else <see cref="RecentPath"/>.</summary>
    public static string RecentPathFor(string? settingsFile) =>
        AppFiles.Pdf.RecentPathFor(settingsFile);

    /// <summary>
    /// The config folder of the update files (<c>update.json</c>, the <c>updates</c> staging folder, <c>session.json</c>): the folder of the given shell settings file, else
    /// <see cref="UserDataPaths.ConfigDirectory()"/>, so measurement runs and tests never touch the folders of the user.
    /// </summary>
    public static string ConfigDirectoryFor(string? settingsFile) => AppFiles.ConfigDirectoryFor(settingsFile);

    /// <summary>The folder of the saved signatures: <c>signatures</c> next to the given shell settings file, else <see cref="UserDataPaths.SignaturesDirectory()"/>.</summary>
    public static string SignaturesDirectoryFor(string? settingsFile) =>
        AppFiles.FolderOf(settingsFile) is { } folder ? Path.Combine(folder, "signatures") : UserDataPaths.SignaturesDirectory();

    /// <summary>The folder of the language models the user imported: <c>tessdata</c> next to the given shell settings file, else <see cref="UserDataPaths.TessdataDirectory()"/>.</summary>
    public static string TessdataDirectoryFor(string? settingsFile) =>
        AppFiles.FolderOf(settingsFile) is { } folder ? Path.Combine(folder, "tessdata") : UserDataPaths.TessdataDirectory();
}
