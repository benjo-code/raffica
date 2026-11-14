using Raffica.Shell.Frame;

namespace Raffica.Hosting;

/// <summary>
/// The settings each window of the process starts with. The first window gets the ones read at the start of the process; every later window reads both files
/// again, because the window before it wrote its placement and the compact bar when it closed. Call <see cref="Next"/> on the UI thread.
/// </summary>
/// <typeparam name="TAppSettings">The app's own settings type (Core's <c>AppSettings</c> for the PDF app).</typeparam>
/// <param name="files">The file names of the app.</param>
/// <param name="settingsFile">The <c>--settings-file</c> value, or <see langword="null"/>.</param>
/// <param name="first">The shell settings read at the start of the process.</param>
/// <param name="firstApp">The app settings read at the start of the process.</param>
/// <param name="loadApp">Reads the app settings for the given <c>--settings-file</c> value (the app maps it to its settings path).</param>
internal sealed class WindowSettingsSource<TAppSettings>(AppFiles files, string? settingsFile, ShellSettings first, TAppSettings firstApp, Func<string?, TAppSettings> loadApp)
{
    private int _made;

    public (ShellSettings Shell, TAppSettings App) Next() =>
        _made++ == 0
            ? (first, firstApp)
            : (new SettingsStore(files.ShellPathFor(settingsFile)).Load(), loadApp(settingsFile));
}
