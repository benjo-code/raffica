using Raffica.Core.Updates;
using Xunit;

namespace Raffica.Core.Tests.Updates;

public sealed class PackageFormTests
{
    private static readonly string InstallDirectory = Path.Combine(Path.GetTempPath(), "Raffica-app");

    private static readonly string ExePath = Path.Combine(InstallDirectory, "Raffica.App.exe");

    private static PackageForm Windows(bool uninstallerExists) =>
        PackageFormDetector.Detect(
            isWindows: true,
            ExePath,
            path => uninstallerExists && path == Path.Combine(InstallDirectory, "unins000.exe"),
            _ => null);

    private static PackageForm Linux(string? processPath, string? appImage, string? appDir = null) =>
        PackageFormDetector.Detect(
            isWindows: false,
            processPath,
            _ => false,
            name => name switch { "APPIMAGE" => appImage, "APPDIR" => appDir, _ => null });

    [Fact]
    public void AnUninstallerNextToTheProgramMeansTheInstaller() =>
        Assert.Equal(PackageForm.WindowsInstaller, Windows(uninstallerExists: true));

    [Fact]
    public void NoUninstallerMeansThePortableArchive() =>
        Assert.Equal(PackageForm.WindowsPortable, Windows(uninstallerExists: false));

    [Fact]
    public void WindowsWithoutAProcessPathIsPortable() =>
        Assert.Equal(
            PackageForm.WindowsPortable,
            PackageFormDetector.Detect(isWindows: true, processPath: null, _ => true, _ => null));

    [Theory]
    [InlineData("/tmp/.mount_x/usr/lib/raffica-pdf/RafficaPdf", "/home/u/Raffica.AppImage", "/tmp/.mount_x", PackageForm.AppImage)]
    [InlineData("/tmp/.mount_x/usr/lib/raffica-pdf/RafficaPdf", "/home/u/Raffica.AppImage", "/tmp/.mount_x/", PackageForm.AppImage)]
    [InlineData("/usr/lib/raffica-pdf/RafficaPdf", "", null, PackageForm.Deb)]
    [InlineData("/usr/lib/raffica-pdf/RafficaPdf", null, null, PackageForm.Deb)]
    [InlineData("/usr/lib/raffica-other/Raffica", null, null, PackageForm.Unknown)]
    [InlineData("/opt/raffica/Raffica", null, null, PackageForm.Unknown)]
    [InlineData("/opt/raffica/Raffica", "", null, PackageForm.Unknown)]
    [InlineData(null, null, null, PackageForm.Unknown)]
    public void LinuxFormFollowsTheEnvironmentAndThePath(string? processPath, string? appImage, string? appDir, PackageForm expected) =>
        Assert.Equal(expected, Linux(processPath, appImage, appDir));

    // Every child of an AppImage inherits APPIMAGE (and APPDIR): a Raffica that was started by one is not that AppImage.
    [Theory]
    [InlineData("/usr/lib/raffica-pdf/RafficaPdf", null, PackageForm.Deb)]
    [InlineData("/usr/lib/raffica-pdf/RafficaPdf", "/tmp/.mount_other", PackageForm.Deb)]
    [InlineData("/opt/raffica/Raffica", null, PackageForm.Unknown)]
    [InlineData("/opt/raffica/Raffica", "", PackageForm.Unknown)]
    [InlineData("/opt/raffica/Raffica", "/tmp/.mount_other", PackageForm.Unknown)]
    [InlineData("/tmp/.mount_abc/usr/lib/raffica-pdf/RafficaPdf", "/tmp/.mount_ab", PackageForm.Unknown)]
    [InlineData("/tmp/.mount_ab/usr/lib/raffica-pdf/RafficaPdf", "/tmp/.mount_abc", PackageForm.Unknown)]
    [InlineData("/tmp/.mount_x/../evil/Raffica", "/tmp/.mount_x", PackageForm.Unknown)]
    [InlineData("/opt/raffica/Raffica", "/", PackageForm.Unknown)]
    [InlineData("/tmp/.mount_x", "/tmp/.mount_x", PackageForm.Unknown)]
    [InlineData(null, "/tmp/.mount_x", PackageForm.Unknown)]
    public void AnInheritedAppImageVariableAloneIsNotTheAppImage(string? processPath, string? appDir, PackageForm expected) =>
        Assert.Equal(expected, Linux(processPath, "/home/u/Other.AppImage", appDir));

    [Fact]
    public void DebDirectoryIsTheRafficaPdfFolder() =>
        Assert.Equal("/usr/lib/raffica-pdf/", PackageFormDetector.DebDirectory);

    // The old folder of the predecessor layout is no deb install any more.
    [Fact]
    public void TheOldRafficaFolderIsNotTheDeb() =>
        Assert.Equal(PackageForm.Unknown, Linux("/usr/lib/raffica/Raffica", null));

    [Fact]
    public void AnAppImageChildStartedFromTheDebIsDeb() =>
        Assert.Equal(PackageForm.Deb, Linux("/usr/lib/raffica-pdf/RafficaPdf", "/home/u/Other.AppImage", "/tmp/.mount_other"));

    [Fact]
    public void AnAppDirWithoutTheAppImageVariableIsNotTheAppImage() =>
        Assert.Equal(PackageForm.Unknown, Linux("/tmp/.mount_x/usr/lib/raffica-pdf/RafficaPdf", null, "/tmp/.mount_x"));

    private static UpdateManifest ManifestWithAllKinds() =>
        new(
            1,
            "0.2.0",
            UpdateFileKinds.All.Select(kind => new UpdateFile(kind, "f-" + kind, 10, new string('a', 64))).ToList());

    [Theory]
    [InlineData(PackageForm.WindowsInstaller, UpdateFileKinds.WindowsSetup)]
    [InlineData(PackageForm.AppImage, UpdateFileKinds.LinuxAppImage)]
    public void PickChoosesTheFileOfTheForm(PackageForm form, string kind) =>
        Assert.Equal(kind, UpdateChecker.Pick(ManifestWithAllKinds(), form)?.Kind);

    [Theory]
    [InlineData(PackageForm.WindowsPortable)]
    [InlineData(PackageForm.Deb)]
    [InlineData(PackageForm.Unknown)]
    public void PickGivesNothingForFormsThatDoNotUpdateThemselves(PackageForm form) =>
        Assert.Null(UpdateChecker.Pick(ManifestWithAllKinds(), form));

    [Fact]
    public void PickGivesNothingWhenTheManifestLacksTheFile()
    {
        var manifest = new UpdateManifest(1, "0.2.0", [new UpdateFile(UpdateFileKinds.LinuxDeb, "x.deb", 10, new string('a', 64))]);

        Assert.Null(UpdateChecker.Pick(manifest, PackageForm.WindowsInstaller));
    }

    private static PackageForm Mac(string? processPath, bool isArm64) =>
        PackageFormDetector.Detect(isWindows: false, processPath, _ => false, _ => null, isMacOS: true, isArm64: isArm64);

    [Theory]
    [InlineData("/Applications/Raffica PDF.app/Contents/MacOS/RafficaPdf", true, PackageForm.MacAppArm64)]
    [InlineData("/Applications/Raffica PDF.app/Contents/MacOS/RafficaPdf", false, PackageForm.MacAppX64)]
    [InlineData("/Users/x/Apps/Raffica PDF.app/Contents/MacOS/RafficaPdf", true, PackageForm.MacAppArm64)]
    [InlineData("/Users/x/Apps/Raffica PDF.app/Contents/MacOS/RafficaPdf", false, PackageForm.MacAppX64)]
    public void MacFormFollowsThePathAndTheProcessArchitecture(string path, bool isArm64, PackageForm expected) =>
        Assert.Equal(expected, Mac(path, isArm64));

    [Fact]
    public void AMacProcessOutsideABundleIsUnknown() =>
        Assert.Equal(PackageForm.Unknown, Mac("/Users/x/src/out/RafficaPdf", true));

    [Theory]
    [InlineData("/Applications/Raffica PDF.app/Contents/MacOS/RafficaPdf", "/Applications/Raffica PDF.app")]
    [InlineData("/a/One.app/Contents/MacOS/x/Two.app/Contents/MacOS/y", "/a/One.app")]
    [InlineData("/Users/x/src/out/RafficaPdf", null)]
    [InlineData(null, null)]
    public void BundlePathOfCutsAtTheFirstAppSegment(string? path, string? expected) =>
        Assert.Equal(expected, PackageFormDetector.BundlePathOf(path));

    [Theory]
    [InlineData(PackageForm.MacAppArm64, UpdateFileKinds.MacOsArm64Dmg)]
    [InlineData(PackageForm.MacAppX64, UpdateFileKinds.MacOsX64Dmg)]
    public void PickChoosesTheDmgOfTheArchitecture(PackageForm form, string kind) =>
        Assert.Equal(kind, UpdateChecker.Pick(ManifestWithAllKinds(), form)?.Kind);

    [Theory]
    [InlineData(PackageForm.MacAppArm64, UpdateFileKinds.MacOsX64Dmg)]
    [InlineData(PackageForm.MacAppX64, UpdateFileKinds.MacOsArm64Dmg)]
    public void PickGivesNothingWhenOnlyTheOtherArchitectureIsInTheManifest(PackageForm form, string otherKind)
    {
        var manifest = new UpdateManifest(1, "0.2.0", [new UpdateFile(otherKind, "x.dmg", 10, new string('a', 64))]);

        Assert.Null(UpdateChecker.Pick(manifest, form));
    }

    [Theory]
    [InlineData(PackageForm.MacAppArm64)]
    [InlineData(PackageForm.MacAppX64)]
    public void AnOldManifestWithoutMacKindsGivesNothingForMac(PackageForm form)
    {
        var manifest = new UpdateManifest(
            1,
            "0.2.0",
            [
                new UpdateFile(UpdateFileKinds.WindowsSetup, "a.exe", 10, new string('a', 64)),
                new UpdateFile(UpdateFileKinds.LinuxAppImage, "a.AppImage", 10, new string('a', 64)),
            ]);

        Assert.Null(UpdateChecker.Pick(manifest, form));
    }
}
