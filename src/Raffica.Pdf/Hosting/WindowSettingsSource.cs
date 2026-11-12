using Raffica.Pdf.Settings;
using Raffica.Shell.Frame;

namespace Raffica.Pdf.Hosting;

/// <summary>
/// The settings each window of the process starts with. The first window gets the ones read at the start of the process; every later window reads both files
/// again, because the window before it wrote its placement and the compact bar when it closed. Call <see cref="Next"/> on the UI thread.
/// </summary>
internal sealed class WindowSettingsSource(string? settingsFile, ShellSettings first, Raffica.Core.Settings.AppSettings firstApp)
{
    private int _made;

    public (ShellSettings Shell, Raffica.Core.Settings.AppSettings App) Next() =>
        _made++ == 0
            ? (first, firstApp)
            : (new SettingsStore(PdfSettingsFiles.ShellPathFor(settingsFile)).Load(),
               new Raffica.Core.Settings.SettingsStore(PdfSettingsFiles.PdfSettingsPathFor(settingsFile)).LoadReadOnly());
}
