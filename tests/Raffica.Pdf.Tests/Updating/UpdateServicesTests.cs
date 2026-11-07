using Raffica.Core.Updates;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateServicesTests
{
    [Fact]
    public void AMeasurementRunOfAReleaseBuildKeepsTheUpdateFlowOff()
    {
        var measuring = PdfOptions.Parse(["--measure-startup", "--settings-file", Path.Combine(Path.GetTempPath(), "settings.json"), "a.pdf"]);
        var normal = PdfOptions.Parse(["a.pdf"]);

        // The build of the tests is a release build of the updater: https addresses of GitHub, so no loopback exception applies.
        Assert.StartsWith("https://", UpdateTrust.ReleasesLatestUrl, StringComparison.Ordinal);
        Assert.True(UpdateServices.MeasurementKeepsUpdatesOff(measuring));
        Assert.False(UpdateServices.MeasurementKeepsUpdatesOff(normal));
    }
}
