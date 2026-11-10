using Raffica.Pdf.Platform.MacOS;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class MacBundleTests
{
    [Theory]
    [InlineData("/Applications/Raffica PDF.app", "Installed")]
    [InlineData("/private/var/folders/xx/T/AppTranslocation/ABC/d/Raffica PDF.app", "Translocated")]
    [InlineData("/Volumes/Raffica PDF/Raffica PDF.app", "MountedImage")]
    [InlineData("/Volumes/Data/Apps/Raffica PDF.app", "Installed")]
    public void LocationOfTellsInstalledTranslocatedAndMounted(string path, string expected) =>
        Assert.Equal(Enum.Parse<MacBundleLocation>(expected), MacBundle.LocationOf(path));

    [Fact]
    public void PathOfFindsTheBundleOfTheProcessPath()
    {
        Assert.Equal("/Applications/Raffica PDF.app", MacBundle.PathOf("/Applications/Raffica PDF.app/Contents/MacOS/RafficaPdf"));
        Assert.Null(MacBundle.PathOf("/usr/local/bin/RafficaPdf"));
        Assert.Null(MacBundle.PathOf(null));
    }
}
