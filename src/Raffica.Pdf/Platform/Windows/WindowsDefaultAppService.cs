using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Raffica.Core.Distribution;

namespace Raffica.Pdf.Platform.Windows;

/// <summary>
/// Default app for PDF files on Windows. Windows keeps that choice to the user, so this only reads <c>UserChoiceLatest</c>
/// (or <c>UserChoice</c> where the former does not exist), makes sure Raffica is registered as an app for PDF files and opens
/// the system page where the user picks it. Raffica never writes <c>UserChoice</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDefaultAppService : IDefaultAppService
{
    /// <summary>The settings page of the default apps, with Raffica's name as it is registered in <c>RegisteredApplications</c>.</summary>
    internal const string SettingsUri = "ms-settings:defaultapps?registeredAppUser=Raffica";

    private const string UserChoiceKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice";
    private const string UserChoiceLatestKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoiceLatest\ProgId";
    private const string OpenCommandKey = @"Software\Classes\" + FileTypeRegistration.ProgId + @"\shell\open\command";

    private readonly RegistryKey _root;
    private readonly RegistryKey? _machineRoot;
    private readonly string _exePath;
    private readonly Func<string, Task> _openUri;
    private readonly Action _notifyAssociationsChanged;

    public WindowsDefaultAppService()
        : this(
            Registry.CurrentUser,
            Environment.ProcessPath ?? throw new InvalidOperationException("The path of the program is unknown."),
            OpenWithShell,
            ShellNative.NotifyAssociationsChanged,
            Registry.LocalMachine)
    {
    }

    /// <summary>Creates the service over a registry root (<c>HKCU</c>, or a sandbox key in tests) with its two outside calls.</summary>
    /// <param name="root">Where registration and <c>UserChoice</c> live.</param>
    /// <param name="exePath">The program to register.</param>
    /// <param name="openUri">Opens a URI such as the settings page.</param>
    /// <param name="notifyAssociationsChanged">Tells the shell that file types changed.</param>
    /// <param name="machineRoot">Where an installation for all users registers (<c>HKLM</c>, or a sandbox key in tests); null: there is none.</param>
    public WindowsDefaultAppService(RegistryKey root, string exePath, Func<string, Task> openUri, Action notifyAssociationsChanged, RegistryKey? machineRoot = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentNullException.ThrowIfNull(openUri);
        ArgumentNullException.ThrowIfNull(notifyAssociationsChanged);
        _root = root;
        _machineRoot = machineRoot;
        _exePath = exePath;
        _openUri = openUri;
        _notifyAssociationsChanged = notifyAssociationsChanged;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<DefaultAppStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.Run(ReadStatus, CancellationToken.None);

    public Task<DefaultAppResult> MakeDefaultAsync(CancellationToken cancellationToken) =>
        Task.Run(() => SetDefaultAsync(cancellationToken), CancellationToken.None);

    private static Task OpenWithShell(string uri)
    {
        // The shell resolves the ms-settings: protocol, so this one start needs ShellExecute.
        using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    // Registry, shell notification and process start run on the thread pool, never on the caller's (UI) thread.
    private DefaultAppStatus ReadStatus()
    {
        try
        {
            if (ReadChosenProgId() != FileTypeRegistration.ProgId)
            {
                return DefaultAppStatus.NotDefault;
            }

            // UserChoice survives a moved or deleted program folder; then the registered command is stale and a double-click fails.
            return IsRegisteredForThisProgram() ? DefaultAppStatus.IsDefault : DefaultAppStatus.NotDefault;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return DefaultAppStatus.Unknown;
        }
    }

    // Newer Windows 11 builds write the choice to UserChoiceLatest\ProgId and leave UserChoice on the previous app, so the
    // newer key wins when it exists.
    private string? ReadChosenProgId()
    {
        using var latest = _root.OpenSubKey(UserChoiceLatestKey);
        if (latest?.GetValue("ProgId") is string latestProgId)
        {
            return latestProgId;
        }

        using var key = _root.OpenSubKey(UserChoiceKey);
        return key?.GetValue("ProgId") as string;
    }

    // The registered open command starts this very program. The user's registration wins over the one for all users (as in
    // the merged view of HKCR); an installation for all users has none of its own below HKCU and must not get a copy.
    private bool IsRegisteredForThisProgram()
    {
        var expected = FileTypeRegistration.Entries(_exePath).First(e => e.KeyPath == OpenCommandKey).Value;
        return (OpenCommand(_root) ?? OpenCommand(_machineRoot)) == expected;
    }

    private static string? OpenCommand(RegistryKey? root)
    {
        using var key = root?.OpenSubKey(OpenCommandKey);
        return key?.GetValue(null) as string;
    }

    private async Task<DefaultAppResult> SetDefaultAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!IsRegisteredForThisProgram())
            {
                new WindowsFileTypeRegistry(_root).Write(RegistrationScope.User, FileTypeRegistration.Entries(_exePath));
                _notifyAssociationsChanged();
            }

            await _openUri(SettingsUri).ConfigureAwait(false);
            return DefaultAppResult.OpenedSystemSettings;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException or Win32Exception or InvalidOperationException)
        {
            return DefaultAppResult.Failed;
        }
    }
}
