using Raffica.Pdf.Settings;
using Raffica.Shell.Frame;

namespace Raffica.Pdf.Hosting;

/// <summary>
/// The settings a window made later in the process starts with. The first window gets the ones read at the start of the process; the window before a later one
/// wrote the placement and the compact bar when it closed, so a later window reads both files again.
/// </summary>
internal static class FreshSettings
{
    public static (ShellSettings Shell, Raffica.Core.Settings.AppSettings App) Read(string? settingsFile) =>
        (new SettingsStore(PdfSettingsFiles.ShellPathFor(settingsFile)).Load(),
         new Raffica.Core.Settings.SettingsStore(PdfSettingsFiles.PdfSettingsPathFor(settingsFile)).LoadReadOnly());
}
