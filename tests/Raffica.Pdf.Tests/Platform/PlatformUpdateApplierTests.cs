using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Linux;
using Raffica.Pdf.Platform.MacOS;
using Raffica.Pdf.Platform.Windows;
using Raffica.Pdf.Settings;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class PlatformUpdateApplierTests : IDisposable
{
    private readonly ApplierKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private IUpdateApplier? Create(PackageForm form, bool isWindows, bool isLinux, string? processPath, string? appImage = null, string? appDir = null) =>
        PlatformUpdateApplier.Create(form, isWindows, isLinux, processPath, appImage, appDir, _kit.Config, () => { }, new FakeStarter());

    private IUpdateApplier? CreateMac(PackageForm form, bool isMacOS, string? processPath) =>
        PlatformUpdateApplier.Create(form, isWindows: false, isLinux: false, processPath, appImage: null, appDir: null, _kit.Config, () => { }, new FakeStarter(), isMacOS, new FakeRunner());

    [Theory]
    [InlineData(PackageForm.Deb)]
    [InlineData(PackageForm.WindowsPortable)]
    [InlineData(PackageForm.Unknown)]
    public void FormsWithoutAnInstallerGetNoApplierOnAnySystem(PackageForm form)
    {
        Assert.Null(Create(form, isWindows: true, isLinux: false, @"C:\Program Files\Raffica PDF\RafficaPdf.exe"));
        Assert.Null(Create(form, isWindows: false, isLinux: true, "/usr/lib/raffica-pdf/RafficaPdf", "/home/u/a.AppImage", "/tmp/.mount_x"));
    }

    [Fact]
    public void TheInstallerFormGetsTheWindowsInstallerOnWindowsOnly()
    {
        var program = Path.Combine(_kit.Root, "Raffica PDF", "RafficaPdf.exe");

        Assert.IsType<WindowsUpdateInstaller>(Create(PackageForm.WindowsInstaller, isWindows: true, isLinux: false, program));
        Assert.Null(Create(PackageForm.WindowsInstaller, isWindows: false, isLinux: true, program));
        Assert.Null(Create(PackageForm.WindowsInstaller, isWindows: true, isLinux: false, processPath: null));
    }

    [Fact]
    public void TheAppImageGetsItsUpdaterOnlyWhenTheProgramRunsFromInsideItsMount()
    {
        var appDir = Path.Combine(_kit.Root, "mnt", ".mount_Raffic1");
        var inside = Path.Combine(appDir, "usr", "lib", "raffica-pdf", "RafficaPdf");
        var image = Path.Combine(_kit.Root, "Apps", "Raffica.AppImage");

        Assert.IsType<AppImageUpdater>(Create(PackageForm.AppImage, isWindows: false, isLinux: true, inside, image, appDir));

        // The variables are inherited by every child of an AppImage: a program elsewhere never replaces the file they name.
        Assert.Null(Create(PackageForm.AppImage, isWindows: false, isLinux: true, Path.Combine(_kit.Root, "elsewhere", "RafficaPdf"), image, appDir));
        Assert.Null(Create(PackageForm.AppImage, isWindows: false, isLinux: true, inside, appImage: null, appDir));
        Assert.Null(Create(PackageForm.AppImage, isWindows: true, isLinux: false, inside, image, appDir));
    }

    [Theory]
    [InlineData(PackageForm.MacAppArm64)]
    [InlineData(PackageForm.MacAppX64)]
    public void TheMacFormsGetTheMacUpdaterOnlyOnMacOsInsideABundle(PackageForm form)
    {
        const string InsideBundle = "/Applications/Raffica PDF.app/Contents/MacOS/RafficaPdf";

        Assert.IsType<MacAppUpdater>(CreateMac(form, isMacOS: true, InsideBundle));

        // Not on another system, not outside a bundle, not without a program path.
        Assert.Null(CreateMac(form, isMacOS: false, InsideBundle));
        Assert.Null(CreateMac(form, isMacOS: true, "/usr/local/bin/RafficaPdf"));
        Assert.Null(CreateMac(form, isMacOS: true, processPath: null));

        // The other forms never get it, even inside a bundle on macOS.
        Assert.Null(CreateMac(PackageForm.AppImage, isMacOS: true, InsideBundle));
        Assert.Null(CreateMac(PackageForm.WindowsInstaller, isMacOS: true, InsideBundle));
        Assert.Null(CreateMac(PackageForm.Unknown, isMacOS: true, InsideBundle));
    }

    [Fact]
    public void RestoreSessionOpensTheFilesOfTheSessionOnceAndDeletesIt()
    {
        var settings = Path.Combine(_kit.Config, "settings.json");
        var present = Path.Combine(_kit.Home, "present.pdf");
        File.WriteAllText(present, "x");
        var gone = Path.Combine(_kit.Home, "gone.pdf");
        Assert.True(new SessionStore(_kit.SessionPath).TrySave(new SessionSnapshot([new SessionWindow([new SessionTab(present, null), new SessionTab(gone, null)], 0)])));

        var options = Program.RestoreSession(PdfOptions.Parse(["--restore-session", "--settings-file", settings, "extra.pdf"]));

        Assert.Equal([present, "extra.pdf"], options.Files);
        Assert.Equal(settings, options.SettingsFile);
        Assert.False(File.Exists(_kit.SessionPath));
        Assert.Equal(PdfSettingsFiles.ConfigDirectoryFor(settings), _kit.Config);

        // A second start finds no session.
        Assert.Equal(["extra.pdf"], Program.RestoreSession(PdfOptions.Parse(["--restore-session", "--settings-file", settings, "extra.pdf"])).Files);
    }

    [Fact]
    public void RestoreSessionNamesTheTabThatWasActive()
    {
        var settings = Path.Combine(_kit.Config, "settings.json");
        string[] names = ["a.pdf", "b.pdf", "c.pdf"];
        var files = names.Select(name => Path.Combine(_kit.Home, name)).ToList();
        files.ForEach(file => File.WriteAllText(file, "x"));
        Assert.True(new SessionStore(_kit.SessionPath).TrySave(new SessionSnapshot([new SessionWindow([.. files.Select(file => new SessionTab(file, null))], 1)])));

        var options = Program.RestoreSession(PdfOptions.Parse(["--restore-session", "--settings-file", settings]));

        Assert.Equal(files, options.Files);
        Assert.Equal(files[1], options.ActiveFile);
    }

    [Fact]
    public void ARestoreWithAFileOnTheCommandLineKeepsThatFileActive()
    {
        var settings = Path.Combine(_kit.Config, "settings.json");
        var present = Path.Combine(_kit.Home, "present.pdf");
        var other = Path.Combine(_kit.Home, "other.pdf");
        File.WriteAllText(present, "x");
        File.WriteAllText(other, "x");
        Assert.True(new SessionStore(_kit.SessionPath).TrySave(new SessionSnapshot([new SessionWindow([new SessionTab(present, null), new SessionTab(other, null)], 0)])));

        var options = Program.RestoreSession(PdfOptions.Parse(["--restore-session", "--settings-file", settings, "asked.pdf"]));

        Assert.Equal([present, other, "asked.pdf"], options.Files);
        Assert.Null(options.ActiveFile);
    }
}
