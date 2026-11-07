using System.Globalization;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Windows;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class UpdateApplierSupportTests
{
    private const string Folder = "/home/zoë/Apps/Raffica PDF";

    private static readonly string[] Languages = ["en", "de"];

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void AReadOnlyTargetFailsWithStorageAndOffersTheDownloadPage(string language)
    {
        // Review Focus 4: a read-only folder (or a mount) and a full disk are failures the user solves by hand: worded, with the download page,
        // and never a restart.
        var culture = CultureInfo.GetCultureInfo(language);
        PdfStrings.Culture = culture;

        var readOnly = UpdateApplierSupport.WriteFailure(new UnauthorizedAccessException("denied"), Folder);
        var full = UpdateApplierSupport.WriteFailure(new IOException("No space left on device", unchecked((int)0x80070070)), Folder);

        foreach (var result in new[] { readOnly, full })
        {
            Assert.False(result.IsRestarting);
            Assert.True(result.OffersDownloadPage);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
            Assert.DoesNotContain("denied", result.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("No space", result.Message, StringComparison.Ordinal);
        }

        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.FolderWriteProtected, culture, Folder), readOnly.Message);
        Assert.Contains(Folder, readOnly.Message, StringComparison.Ordinal);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.Storage, culture), full.Message);
        Assert.NotEqual(readOnly.Message, full.Message);
    }

    [Fact]
    public void ADiskFullIsRecognisedOnWindowsAndUnix()
    {
        Assert.True(UpdateApplierSupport.IsDiskFull(new IOException("full", unchecked((int)0x80070070))));
        Assert.True(UpdateApplierSupport.IsDiskFull(new IOException("full", unchecked((int)0x80070027))));
        Assert.True(UpdateApplierSupport.IsDiskFull(new IOException("full", 28)));
        Assert.False(UpdateApplierSupport.IsDiskFull(new IOException("other", 5)));
        Assert.False(UpdateApplierSupport.IsDiskFull(new UnauthorizedAccessException()));
    }

    [Fact]
    public async Task AReadOnlyHelperFolderOfTheWindowsInstallerOffersTheDownloadPageAndKeepsTheApp()
    {
        // The folder for the helper copy lies below a file: it cannot be made, whatever the reason (a read-only profile, a full disk).
        using var kit = new ApplierKit();
        var program = kit.MakeProgramDirectory();
        var staged = kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: Path.Combine(program, "RafficaPdf.exe"), targetDirectory: program);
        var blocker = Path.Combine(kit.Root, "blocker");
        await File.WriteAllTextAsync(blocker, "a file", TestContext.Current.CancellationToken);
        var starter = new FakeStarter();
        var shutdowns = 0;
        var installer = new WindowsUpdateInstaller(new WindowsUpdateInstallerSetup(
            program, Path.Combine(blocker, "updater"), kit.Config, kit.Keys, ApplierKit.RunningVersion, 1, starter, () => shutdowns++, kit.Clock));
        await File.WriteAllTextAsync(kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await installer.ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(starter.Starts);
        Assert.Equal(0, shutdowns);
        Assert.False(File.Exists(kit.SessionPath));
        Assert.True(File.Exists(Path.Combine(program, "RafficaPdf.exe")));
    }

    [Fact]
    public void EveryApplierProblemHasWordingInBothLanguages()
    {
        foreach (var problem in Enum.GetValues<UpdateApplyProblem>())
        {
            var texts = Languages.Select(language => UpdateWording.ForApplier(problem, CultureInfo.GetCultureInfo(language), Folder)).ToList();
            Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text), problem.ToString()));
            Assert.All(texts, text => Assert.DoesNotContain("{", text, StringComparison.Ordinal));
            Assert.NotEqual(texts[0], texts[1]);
        }
    }

    [Fact]
    public void AcceptedStagingIsOnlyAVersionFolderDirectlyBelowTheUpdatesOfTheConfig()
    {
        using var kit = new ApplierKit();
        var staged = kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup);
        var elsewhere = Path.Combine(kit.Root, "0.2.0");
        Directory.CreateDirectory(elsewhere);
        var named = Path.Combine(kit.Config, UpdateStaging.DirectoryName, "latest");
        Directory.CreateDirectory(named);

        Assert.Equal(staged.Directory, UpdateApplierSupport.AcceptedStaging(staged.Directory + Path.DirectorySeparatorChar, kit.Config));
        Assert.Null(UpdateApplierSupport.AcceptedStaging(elsewhere, kit.Config));
        Assert.Null(UpdateApplierSupport.AcceptedStaging(named, kit.Config));
        Assert.Null(UpdateApplierSupport.AcceptedStaging(Path.Combine(staged.Directory, ".."), kit.Config));
        Assert.Null(UpdateApplierSupport.AcceptedStaging(Path.Combine(kit.Config, UpdateStaging.DirectoryName, "9.9.9"), kit.Config));
    }
}
