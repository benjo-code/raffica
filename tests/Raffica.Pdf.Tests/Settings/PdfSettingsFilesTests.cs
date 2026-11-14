using Raffica.Core.Settings;
using Raffica.Pdf.Settings;
using Xunit;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Pdf.Tests.Settings;

public sealed class PdfSettingsFilesTests
{
    [Fact]
    public void SignaturesFolderLiesNextToTheSettingsFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raffica-sig-test");
        var settings = Path.Combine(folder, "settings.json");

        Assert.Equal(Path.Combine(folder, "signatures"), PdfSettingsFiles.SignaturesDirectoryFor(settings));
        Assert.Equal(UserDataPaths.SignaturesDirectory(), PdfSettingsFiles.SignaturesDirectoryFor(null));
    }

    [Fact]
    public void TessdataLiesNextToTheSettingsFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raffica-tess-test");
        var settings = Path.Combine(folder, "settings.json");

        Assert.Equal(Path.Combine(folder, "tessdata"), PdfSettingsFiles.TessdataDirectoryFor(settings));
        Assert.Equal(UserDataPaths.TessdataDirectory(), PdfSettingsFiles.TessdataDirectoryFor(null));
    }

    [Fact]
    public void MalformedSettingsPathGoesToTheTempFolder()
    {
        var directory = PdfSettingsFiles.SignaturesDirectoryFor("bad\0path");

        Assert.Equal(Path.Combine(Path.GetTempPath(), "signatures"), directory);
    }

    [Fact]
    public void RecentFilesNeverTouchTheUsersFolderWhenASettingsFileIsGiven()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raffica-pdf-files-test");
        var settings = Path.Combine(folder, "nested", "shell.json");

        Assert.Equal(settings, PdfSettingsFiles.ShellPathFor(settings));
        Assert.Equal(Path.Combine(folder, "nested", "pdf-settings.json"), PdfSettingsFiles.PdfSettingsPathFor(settings));
        Assert.Equal(Path.Combine(folder, "nested", "recent.json"), PdfSettingsFiles.RecentPathFor(settings));

        var config = UserDataPaths.ConfigDirectory();
        Assert.DoesNotContain(config, PdfSettingsFiles.RecentPathFor(settings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithoutASettingsFileTheDefaultsAreUsed()
    {
        Assert.Equal(ShellSettingsStore.DefaultPath("Pdf"), PdfSettingsFiles.ShellPathFor(null));
        Assert.Equal(ShellSettingsStore.DefaultPath("Pdf"), PdfSettingsFiles.ShellPath);
        Assert.Equal(Path.Combine(UserDataPaths.ConfigDirectory(), "recent.json"), PdfSettingsFiles.RecentPath);
        Assert.Equal(Path.Combine(UserDataPaths.ConfigDirectory(), "pdf-settings.json"), PdfSettingsFiles.PdfSettingsPath);
    }
}
