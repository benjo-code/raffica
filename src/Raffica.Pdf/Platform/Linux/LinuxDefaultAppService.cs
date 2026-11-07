using Raffica.Core.Distribution;

namespace Raffica.Pdf.Platform.Linux;

/// <summary>
/// Default app for PDF files on Linux through <c>xdg-mime</c>. From an AppImage it first installs the desktop file and the icons
/// into the user's data folder. Programs start without a shell and with a time limit; a machine without <c>xdg-mime</c> gets
/// <see cref="DefaultAppStatus.Unknown"/> and <see cref="DefaultAppResult.NotAvailable"/>.
/// </summary>
internal sealed class LinuxDefaultAppService : IDefaultAppService
{
    /// <summary>The name of the desktop file (the one of the package and of the AppImage).</summary>
    internal const string DesktopFileName = "raffica-pdf.desktop";

    private const string IconName = "raffica-pdf";
    private const string MimeType = "application/pdf";

    private static readonly int[] IconSizes = [16, 22, 24, 32, 48, 64, 96, 128, 256, 512];

    private readonly Func<string, string?> _environment;
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _timeout;
    private readonly Func<string, string?> _findProgram;

    public LinuxDefaultAppService()
        : this(Environment.GetEnvironmentVariable, new SystemProcessRunner(), TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>Creates the service with its own view of the environment (PATH, HOME, XDG_DATA_HOME, APPIMAGE, APPDIR), its process runner and time limit.</summary>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="runner">Starts programs.</param>
    /// <param name="timeout">How long one program may run.</param>
    /// <param name="findProgram">Finds a program by name (tests); null searches <c>PATH</c> for an executable file.</param>
    public LinuxDefaultAppService(Func<string, string?> environment, IProcessRunner runner, TimeSpan timeout, Func<string, string?>? findProgram = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(runner);
        _environment = environment;
        _runner = runner;
        _timeout = timeout;
        _findProgram = findProgram ?? FindOnPath;
    }

    public bool IsSupported => OperatingSystem.IsLinux();

    public Task<DefaultAppStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.Run(() => QueryStatusAsync(cancellationToken), CancellationToken.None);

    public Task<DefaultAppResult> MakeDefaultAsync(CancellationToken cancellationToken) =>
        Task.Run(() => SetDefaultAsync(cancellationToken), CancellationToken.None);

    /// <summary>
    /// Whether the desktop file at <paramref name="file"/> starts <paramref name="appImage"/>. Never throws: what cannot be
    /// read or compared counts as "yes", so a damaged folder does not bring the question back every time.
    /// </summary>
    internal static bool DesktopFileStartsAppImage(string file, string appImage)
    {
        try
        {
            return File.Exists(file) && ExecLine(File.ReadAllText(file)) == ExecLine(DesktopEntry.Render(appImage, IconName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // ArgumentException: the path of the AppImage has a control character, which no desktop entry can hold.
            return true;
        }
    }

    // The Exec= line only: other lines may change with a newer Raffica without making the choice stale.
    private static string? ExecLine(string desktopFile) =>
        desktopFile.Split('\n').FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal));

    // Both run on the thread pool: the file work and the PATH search must never block the caller (the UI thread).
    private async Task<DefaultAppStatus> QueryStatusAsync(CancellationToken cancellationToken)
    {
        var xdgMime = _findProgram("xdg-mime");
        if (xdgMime is null)
        {
            return DefaultAppStatus.Unknown;
        }

        var run = await _runner.RunAsync(xdgMime, ["query", "default", MimeType], _timeout, cancellationToken).ConfigureAwait(false);
        if (run is not { ExitCode: 0 })
        {
            return DefaultAppStatus.Unknown;
        }

        if (run.Output.Trim() != DesktopFileName)
        {
            return DefaultAppStatus.NotDefault;
        }

        // From an AppImage the choice is only good while the user's desktop file starts this very file: after the
        // AppImage was moved or renamed, the old Exec= points nowhere. NotDefault brings the card back; setting the
        // default again writes the file anew (TryInstallDesktopFile).
        var appImage = _environment("APPIMAGE");
        var dataHome = DataHome();
        return string.IsNullOrEmpty(appImage) || dataHome is null || DesktopFileStartsAppImage(Path.Combine(dataHome, "applications", DesktopFileName), appImage)
            ? DefaultAppStatus.IsDefault
            : DefaultAppStatus.NotDefault;
    }

    private async Task<DefaultAppResult> SetDefaultAsync(CancellationToken cancellationToken)
    {
        var xdgMime = _findProgram("xdg-mime");
        if (xdgMime is null)
        {
            return DefaultAppResult.NotAvailable;
        }

        var dataHome = DataHome();
        var applications = dataHome is null ? null : Path.Combine(dataHome, "applications");
        var appImage = _environment("APPIMAGE");
        if (!string.IsNullOrEmpty(appImage))
        {
            if (dataHome is null || applications is null || !TryInstallDesktopFile(appImage, dataHome, applications))
            {
                return DefaultAppResult.Failed;
            }
        }

        // xdg-mime default writes the choice into mimeapps.list without looking for the desktop file; without one
        // (a build run from a folder, a ZIP) the choice would point at nothing and the card would claim success.
        if (string.IsNullOrEmpty(appImage) && !DesktopFileExists(dataHome))
        {
            return DefaultAppResult.NotAvailable;
        }

        var run = await _runner.RunAsync(xdgMime, ["default", DesktopFileName, MimeType], _timeout, cancellationToken).ConfigureAwait(false);
        if (run is not { ExitCode: 0 })
        {
            return DefaultAppResult.Failed;
        }

        // Optional: refreshes the cache of desktop files; a failure here does not undo the choice.
        var update = _findProgram("update-desktop-database");
        if (update is not null && applications is not null && Directory.Exists(applications))
        {
            await _runner.RunAsync(update, [applications], _timeout, cancellationToken).ConfigureAwait(false);
        }

        return DefaultAppResult.Done;
    }

    private string? DataHome()
    {
        var data = _environment("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(data) && Path.IsPathRooted(data))
        {
            return data;
        }

        var home = _environment("HOME");
        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".local", "share");
    }

    // The user's data folder, then XDG_DATA_DIRS (default /usr/local/share:/usr/share, where the .deb installs).
    private bool DesktopFileExists(string? dataHome)
    {
        var folders = new List<string>();
        if (dataHome is not null)
        {
            folders.Add(dataHome);
        }

        var dirs = _environment("XDG_DATA_DIRS");
        folders.AddRange((string.IsNullOrEmpty(dirs) ? "/usr/local/share:/usr/share" : dirs)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathRooted));

        return folders.Any(folder => File.Exists(Path.Combine(folder, "applications", DesktopFileName)));
    }

    private bool TryInstallDesktopFile(string appImage, string dataHome, string applications)
    {
        try
        {
            Directory.CreateDirectory(applications);
            File.WriteAllText(Path.Combine(applications, DesktopFileName), DesktopEntry.Render(appImage, IconName));

            var appDir = _environment("APPDIR");
            if (string.IsNullOrEmpty(appDir))
            {
                return true;
            }

            foreach (var size in IconSizes)
            {
                var relative = Path.Combine("icons", "hicolor", $"{size}x{size}", "apps", IconName + ".png");
                var source = Path.Combine(appDir, "usr", "share", relative);
                if (!File.Exists(source))
                {
                    continue;
                }

                var target = Path.Combine(dataHome, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // ArgumentException: the path of the AppImage has a control character, which no desktop entry can hold.
            return false;
        }
    }

    private string? FindOnPath(string program)
    {
        var path = _environment("PATH");
        if (string.IsNullOrEmpty(path) || OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var folder in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, program);
            try
            {
                if (File.Exists(candidate) && (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                {
                    return candidate;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unreadable PATH entry is just skipped.
            }
        }

        return null;
    }
}
