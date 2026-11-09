using Raffica.Core.Settings;
using Raffica.Pdf.Settings;
using Xunit;

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
}
