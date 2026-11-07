using Raffica.Core;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Settings;

namespace Raffica.Pdf.Updating;

/// <summary>Composes the update flow of the shipped program from its parts; called on first use (after the first sharp page or by the command), never at startup.</summary>
internal static class UpdateServices
{
    /// <summary>
    /// Creates the flow: the source of the project's release storage (no connection yet), the checker over it with the keys compiled into
    /// <see cref="UpdateTrust"/>, the installation form of the running program and the applier for it. Nothing in the environment or the
    /// command line changes an address or a key.
    /// </summary>
    /// <param name="settingsFile">The <c>--settings-file</c> of the run (the update files lie next to it); null: the config folder of the user.</param>
    /// <param name="enabled">Whether the background check is switched on.</param>
    public static UpdateFlow Create(string? settingsFile, Func<bool> enabled)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        var config = PdfSettingsFiles.ConfigDirectoryFor(settingsFile);
        var source = GitHubUpdateSource.Create();
        var running = UpdateVersion.TryParse(AppInfo.Version, out var version) ? version : default;
        var checker = new UpdateChecker(source, new UpdateStatusStore(Path.Combine(config, UpdateStatusStore.FileName)), TimeProvider.System, running, UpdateTrust.Keys);
        var form = PackageFormDetector.Detect(OperatingSystem.IsWindows(), Environment.ProcessPath, File.Exists, Environment.GetEnvironmentVariable);
        var processPath = Environment.ProcessPath;

        // The folder the installer replaces: the program's own folder, or the folder of the AppImage file.
        var appImage = form == PackageForm.AppImage ? Environment.GetEnvironmentVariable("APPIMAGE") : null;
        var target = string.IsNullOrEmpty(appImage) ? Path.GetDirectoryName(processPath) : Path.GetDirectoryName(appImage);
        return new UpdateFlow(checker, source, PlatformUpdateApplier.Create(form), form, enabled, TimeProvider.System, config)
        {
            ProcessPath = processPath,
            TargetDirectory = target,
        };
    }
}
