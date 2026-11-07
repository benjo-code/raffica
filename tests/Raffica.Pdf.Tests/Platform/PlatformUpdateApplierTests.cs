using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Linux;
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
}
