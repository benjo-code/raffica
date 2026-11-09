using System.Runtime.Versioning;
using Microsoft.Win32;
using Raffica.Core.Distribution;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Linux;
using Raffica.Pdf.Platform.Windows;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class DefaultAppServiceTests : IDisposable
{
    private const string AppImage = "/home/ada/Apps/Raffica PDF.AppImage";

    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-defaultapp-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FakeRunner(params ProcessOutcome?[] outcomes) : IProcessRunner
    {
        private int _next;

        public List<(string File, string[] Args)> Calls { get; } = [];

        public Task<ProcessOutcome?> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add((file, [.. args]));
            var outcome = outcomes.Length == 0 ? new ProcessOutcome(0, string.Empty) : outcomes[Math.Min(_next, outcomes.Length - 1)];
            _next++;
            return Task.FromResult(outcome);
        }
    }

    private const string AppDirOfAppImage = "/tmp/.mount_RafficaAbc";
    private const string ProcessOfAppImage = AppDirOfAppImage + "/usr/bin/RafficaPdf";

    // appImage given without an explicit appDir/process: this program runs from inside the AppImage (the normal case).
    private LinuxDefaultAppService Linux(FakeRunner runner, bool hasXdgMime = true, string? appImage = null, string? appDir = null, string? processPath = null) =>
        new(
            name => name switch
            {
                "XDG_DATA_HOME" => Path.Combine(_folder, "data"),
                "XDG_DATA_DIRS" => Path.Combine(_folder, "system"),
                "APPIMAGE" => appImage,
                "APPDIR" => appDir ?? (appImage is null ? null : AppDirOfAppImage),
                _ => null,
            },
            runner,
            TimeSpan.FromSeconds(5),
            program => hasXdgMime && program == "xdg-mime" ? "/usr/bin/xdg-mime" : null,
            processPath ?? (appImage is null ? null : ProcessOfAppImage));

    private string DesktopFile => Path.Combine(_folder, "data", "applications", LinuxDefaultAppService.DesktopFileName);

    private void InstallDesktopFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile)!);
        File.WriteAllText(DesktopFile, "[Desktop Entry]\n");
    }

    [Fact]
    public async Task LinuxUsesXdgMimeQueryAndDefault()
    {
        var ct = TestContext.Current.CancellationToken;

        // The query: the desktop file of Raffica as the default is the default; any other file is not.
        var isDefault = new FakeRunner(new ProcessOutcome(0, "raffica-pdf.desktop\n"));
        Assert.Equal(DefaultAppStatus.IsDefault, await Linux(isDefault).GetStatusAsync(ct));
        var query = Assert.Single(isDefault.Calls);
        Assert.Equal("/usr/bin/xdg-mime", query.File);
        Assert.Equal(["query", "default", "application/pdf"], query.Args);

        Assert.Equal(DefaultAppStatus.NotDefault, await Linux(new FakeRunner(new ProcessOutcome(0, "org.gnome.Evince.desktop\n"))).GetStatusAsync(ct));

        // A query that fails, or does not end in time (the runner answers null), cannot be told.
        Assert.Equal(DefaultAppStatus.Unknown, await Linux(new FakeRunner(new ProcessOutcome(3, string.Empty))).GetStatusAsync(ct));
        Assert.Equal(DefaultAppStatus.Unknown, await Linux(new FakeRunner((ProcessOutcome?)null)).GetStatusAsync(ct));

        // The set: with the desktop file installed (the .deb) the choice is written by xdg-mime default.
        InstallDesktopFile();
        var set = new FakeRunner();
        Assert.Equal(DefaultAppResult.Done, await Linux(set).MakeDefaultAsync(ct));
        Assert.Equal(["default", "raffica-pdf.desktop", "application/pdf"], set.Calls[0].Args);
    }

    [Fact]
    public async Task LinuxWithoutXdgMimeIsUnknownAndNotAvailableAndStartsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new FakeRunner();
        var service = Linux(runner, hasXdgMime: false);

        Assert.Equal(DefaultAppStatus.Unknown, await service.GetStatusAsync(ct));
        Assert.Equal(DefaultAppResult.NotAvailable, await service.MakeDefaultAsync(ct));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task LinuxFailedOrTimedOutSetEndsFailedAndAMissingDesktopFileNotAvailable()
    {
        var ct = TestContext.Current.CancellationToken;

        // Run from a folder or a ZIP, no desktop file anywhere: xdg-mime would write a choice that points at nothing.
        var none = new FakeRunner();
        Assert.Equal(DefaultAppResult.NotAvailable, await Linux(none).MakeDefaultAsync(ct));
        Assert.Empty(none.Calls);

        InstallDesktopFile();
        Assert.Equal(DefaultAppResult.Failed, await Linux(new FakeRunner(new ProcessOutcome(1, string.Empty))).MakeDefaultAsync(ct));
        Assert.Equal(DefaultAppResult.Failed, await Linux(new FakeRunner((ProcessOutcome?)null)).MakeDefaultAsync(ct));
    }

    [Fact]
    public async Task LinuxAppImageWritesTheDesktopFileAndAMovedAppImageIsNotTheDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new FakeRunner();
        Assert.Equal(DefaultAppResult.Done, await Linux(runner, appImage: AppImage).MakeDefaultAsync(ct));

        var written = await File.ReadAllTextAsync(DesktopFile, ct);
        Assert.Contains("Exec=\"" + AppImage + "\" %F", written, StringComparison.Ordinal);
        Assert.Contains("Icon=raffica-pdf", written, StringComparison.Ordinal);
        Assert.Equal(["default", "raffica-pdf.desktop", "application/pdf"], runner.Calls[0].Args);

        // The same AppImage is still the default; another path (the file was moved) brings the offer back.
        var query = new FakeRunner(new ProcessOutcome(0, "raffica-pdf.desktop\n"));
        Assert.Equal(DefaultAppStatus.IsDefault, await Linux(query, appImage: AppImage).GetStatusAsync(ct));
        Assert.Equal(DefaultAppStatus.NotDefault, await Linux(query, appImage: "/home/ada/Elsewhere/Raffica.AppImage").GetStatusAsync(ct));
    }

    // 01-S2: APPIMAGE/APPDIR inherited from a parent AppImage while this program runs from somewhere else must not be trusted.
    [Fact]
    public async Task LinuxIgnoresAnInheritedAppImageThatIsNotThisProgram()
    {
        var ct = TestContext.Current.CancellationToken;
        const string foreign = "/home/mallory/Foreign.AppImage";
        const string foreignDir = "/tmp/.mount_Foreign";
        const string mine = "/opt/raffica/RafficaPdf";

        // Set: no desktop file is written for the foreign file; with none installed the result is the non-AppImage one.
        var runner = new FakeRunner();
        Assert.Equal(DefaultAppResult.NotAvailable, await Linux(runner, appImage: foreign, appDir: foreignDir, processPath: mine).MakeDefaultAsync(ct));
        Assert.False(File.Exists(DesktopFile));
        Assert.Empty(runner.Calls);

        // With a desktop file of a package installed, the plain path sets the default and still writes nothing of the foreign file.
        InstallDesktopFile();
        var set = new FakeRunner();
        Assert.Equal(DefaultAppResult.Done, await Linux(set, appImage: foreign, appDir: foreignDir, processPath: mine).MakeDefaultAsync(ct));
        Assert.DoesNotContain(foreign, await File.ReadAllTextAsync(DesktopFile, ct), StringComparison.Ordinal);

        // Status: the foreign variable does not make the choice stale.
        var query = new FakeRunner(new ProcessOutcome(0, "raffica-pdf.desktop\n"));
        Assert.Equal(DefaultAppStatus.IsDefault, await Linux(query, appImage: foreign, appDir: foreignDir, processPath: mine).GetStatusAsync(ct));
    }

    [Fact]
    public async Task LinuxAppImageWithAnUnwritableDataFolderFails()
    {
        // The data folder is a file: nothing can be created below it, and the result is a failure, not an exception.
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_folder, "data"), "not a folder", ct);
        var runner = new FakeRunner();

        Assert.Equal(DefaultAppResult.Failed, await Linux(runner, appImage: AppImage).MakeDefaultAsync(ct));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task WindowsReadsUserChoiceAndOpensSettingsNotWritesIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The registry is Windows only.");
            return;
        }

        await RunWindowsAsync(TestContext.Current.CancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunWindowsAsync(CancellationToken ct)
    {
        const string exe = @"C:\Program Files\Raffica PDF\RafficaPdf.exe";
        const string userChoice = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice";
        var name = @"Software\RafficaTests\" + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            var opened = new List<string>();
            var notified = 0;
            var service = new WindowsDefaultAppService(root, exe, uri =>
            {
                opened.Add(uri);
                return Task.CompletedTask;
            }, () => notified++);
            Assert.True(service.IsSupported);

            // Nothing chosen yet: another app is the default.
            Assert.Equal(DefaultAppStatus.NotDefault, await service.GetStatusAsync(ct));

            // Make: registers Raffica as an app for PDF files, opens the page of the system, writes no choice.
            Assert.Equal(DefaultAppResult.OpenedSystemSettings, await service.MakeDefaultAsync(ct));
            Assert.Equal(["ms-settings:defaultapps?registeredAppUser=Raffica"], opened);
            Assert.Equal(1, notified);
            using (var progId = root.OpenSubKey(@"Software\Classes\Raffica.PDF\shell\open\command"))
            {
                Assert.Equal($"\"{exe}\" \"%1\"", progId?.GetValue(null));
            }

            Assert.Null(root.OpenSubKey(userChoice));
            Assert.Null(root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoiceLatest"));

            // A second request finds the registration current and does not write or notify again.
            Assert.Equal(DefaultAppResult.OpenedSystemSettings, await service.MakeDefaultAsync(ct));
            Assert.Equal(1, notified);

            // The user chose Raffica in the settings page: the status follows UserChoice ...
            using (var key = root.CreateSubKey(userChoice))
            {
                key.SetValue("ProgId", "Raffica.PDF");
            }

            Assert.Equal(DefaultAppStatus.IsDefault, await service.GetStatusAsync(ct));

            // ... UserChoiceLatest wins when it exists ...
            using (var key = root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoiceLatest\ProgId"))
            {
                key.SetValue("ProgId", "AcroExch.Document.DC");
            }

            Assert.Equal(DefaultAppStatus.NotDefault, await service.GetStatusAsync(ct));
            root.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoiceLatest");

            // ... and the program that moved away is not the default any more (the registered command is stale).
            var moved = new WindowsDefaultAppService(root, @"D:\Moved\RafficaPdf.exe", _ => Task.CompletedTask, () => { });
            Assert.Equal(DefaultAppStatus.NotDefault, await moved.GetStatusAsync(ct));
            Assert.Equal("Raffica.PDF", root.OpenSubKey(userChoice)?.GetValue("ProgId"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    // 06-B-2: an installation for all users registers below HKLM; the user's HKCU has nothing, and nothing may be written there.
    [Fact]
    public async Task WindowsAllUsersRegistrationIsReadAndNotCopiedToTheUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The registry is Windows only.");
            return;
        }

        await RunWindowsAllUsersAsync(TestContext.Current.CancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunWindowsAllUsersAsync(CancellationToken ct)
    {
        const string exe = @"C:\Program Files\Raffica PDF\RafficaPdf.exe";
        const string command = @"Software\Classes\Raffica.PDF\shell\open\command";
        const string userChoice = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice";
        var name = @"Software\RafficaTests\" + Guid.NewGuid().ToString("N");
        using var user = Registry.CurrentUser.CreateSubKey(name + @"\user");
        using var machine = Registry.CurrentUser.CreateSubKey(name + @"\machine");
        try
        {
            var notified = 0;
            var service = new WindowsDefaultAppService(user, exe, _ => Task.CompletedTask, () => notified++, machine);
            new WindowsFileTypeRegistry(machine).Write(RegistrationScope.Machine, FileTypeRegistration.Entries(exe));
            using (var key = user.CreateSubKey(userChoice))
            {
                key.SetValue("ProgId", "Raffica.PDF");
            }

            // The all-users registration counts: the app is the default once the user chose it.
            Assert.Equal(DefaultAppStatus.IsDefault, await service.GetStatusAsync(ct));

            // Making it the default opens the settings page and writes nothing below the user's root.
            Assert.Equal(DefaultAppResult.OpenedSystemSettings, await service.MakeDefaultAsync(ct));
            Assert.Null(user.OpenSubKey(command));
            Assert.Equal(0, notified);

            // An all-users registration of another path does not count: the user's registration is written as before.
            const string otherExe = @"D:\Other\RafficaPdf.exe";
            var other = new WindowsDefaultAppService(user, otherExe, _ => Task.CompletedTask, () => notified++, machine);
            Assert.Equal(DefaultAppStatus.NotDefault, await other.GetStatusAsync(ct));
            Assert.Equal(DefaultAppResult.OpenedSystemSettings, await other.MakeDefaultAsync(ct));
            using (var written = user.OpenSubKey(command))
            {
                Assert.Equal($"\"{otherExe}\" \"%1\"", written?.GetValue(null));
            }

            Assert.Equal(1, notified);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task WindowsThatCannotOpenTheSettingsPageEndsFailed()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The registry is Windows only.");
            return;
        }

        await RunWindowsOpenFailsAsync(TestContext.Current.CancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunWindowsOpenFailsAsync(CancellationToken ct)
    {
        var name = @"Software\RafficaTests\" + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(name);
        try
        {
            var service = new WindowsDefaultAppService(
                root,
                @"C:\Raffica\RafficaPdf.exe",
                _ => throw new System.ComponentModel.Win32Exception("no handler"),
                () => { });
            Assert.Equal(DefaultAppResult.Failed, await service.MakeDefaultAsync(ct));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void TheFactoryNeverReturnsNullAndTheStubIsNotSupportedWhereThereIsNoService()
    {
        var service = PlatformDefaultAppServices.Create();
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), service.IsSupported);
    }
}
