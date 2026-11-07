using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Settings;

namespace Raffica.Pdf.Updating;

/// <summary>Composes the update flow of the shipped program from its parts; called on first use (after the first sharp page or by the command), never at startup.</summary>
internal static class UpdateServices
{
    /// <summary>
    /// Whether a measurement run (<see cref="PdfOptions.IsMeasuring"/>) keeps the update flow off. It does in every build that can reach the real release
    /// storage: a measurement must not open a connection. Only a TEST BUILD of the updater (<c>-p:RafficaUpdateTestBuild=true</c>, whose compiled-in addresses are
    /// the loopback server <c>http://127.0.0.1:&lt;port&gt;</c> of <c>tools/update-testserver.cs</c>, see <see cref="UpdateTrust"/>) lets a measurement run
    /// keep it on, so the cost of the check can be measured against a server on this machine. This is derived from the compiled addresses alone: no switch, no
    /// environment variable and no setting is added, and a release build (https GitHub addresses) behaves as before.
    /// </summary>
    public static bool MeasurementKeepsUpdatesOff(PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.IsMeasuring && !UpdateTrust.ReleasesLatestUrl.StartsWith("http://127.0.0.1:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates the flow: the source of the project's release storage (no connection yet), the checker over it with the keys compiled into
    /// <see cref="UpdateTrust"/>, the installation form of the running program and the applier for it. Nothing in the environment or the
    /// command line changes an address or a key.
    /// </summary>
    /// <param name="settingsFile">The <c>--settings-file</c> of the run (the update files lie next to it); null: the config folder of the user.</param>
    /// <param name="enabled">Whether the background check is switched on.</param>
    /// <param name="shutdown">Ends the app once an installer took over (the app passes the end of its lifetime); without it nothing installs, and the flow only points to the download page.</param>
    public static UpdateFlow Create(string? settingsFile, Func<bool> enabled, Action? shutdown = null)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        var config = PdfSettingsFiles.ConfigDirectoryFor(settingsFile);
        var source = GitHubUpdateSource.Create();
        var running = UpdateApplierSupport.RunningVersion();
        var checker = new UpdateChecker(source, new UpdateStatusStore(Path.Combine(config, UpdateStatusStore.FileName)), TimeProvider.System, running, UpdateTrust.Keys);
        var form = PackageFormDetector.Detect(OperatingSystem.IsWindows(), Environment.ProcessPath, File.Exists, Environment.GetEnvironmentVariable);
        var processPath = Environment.ProcessPath;

        // The folder the installer replaces: the program's own folder, or the folder of the AppImage file.
        var appImage = form == PackageForm.AppImage ? Environment.GetEnvironmentVariable("APPIMAGE") : null;
        var target = string.IsNullOrEmpty(appImage) ? Path.GetDirectoryName(processPath) : Path.GetDirectoryName(appImage);
        return new UpdateFlow(checker, source, shutdown is null ? null : PlatformUpdateApplier.Create(form, config, shutdown), form, enabled, TimeProvider.System, config)
        {
            ProcessPath = processPath,
            TargetDirectory = target,
        };
    }
}
