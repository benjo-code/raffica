using Raffica.Core.Updates;
using Raffica.Pdf.Platform.MacOS;
using Raffica.Pdf.Tests.Platform;
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

    [Fact]
    public async Task OnlyOnMacOsTheStagingFolderIsReleasedBeforeItIsDeleted()
    {
        var runner = new MacRunner();
        var staging = Path.Combine(Path.GetTempPath(), "raffica-services-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, MacAppUpdater.MountFolderName));
        try
        {
            Assert.Null(UpdateServices.StagingReleaseFor(isMacOS: false, runner));

            var release = UpdateServices.StagingReleaseFor(isMacOS: true, runner);
            Assert.NotNull(release);
            await release(staging);

            Assert.Equal([$"/usr/bin/hdiutil detach {Path.Combine(staging, "mnt")} -force"], runner.Lines);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }
}
