using Raffica.Pdf.Platform.MacOS;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class MacStagingMountTests : IDisposable
{
    private readonly string _staging = Path.Combine(Path.GetTempPath(), "raffica-staging-" + Guid.NewGuid().ToString("N"));

    public MacStagingMountTests() => Directory.CreateDirectory(_staging);

    public void Dispose() => Directory.Delete(_staging, recursive: true);

    [Fact]
    public async Task AMountFolderIsDetachedWithForce()
    {
        var mount = Path.Combine(_staging, MacAppUpdater.MountFolderName);
        Directory.CreateDirectory(mount);
        var runner = new MacRunner();

        await MacStagingMount.ReleaseAsync(runner, _staging);

        Assert.Equal([$"/usr/bin/hdiutil detach {mount} -force"], runner.Lines);
    }

    [Fact]
    public async Task WithoutAMountFolderNothingIsStarted()
    {
        var runner = new MacRunner();

        await MacStagingMount.ReleaseAsync(runner, _staging);

        Assert.Empty(runner.Runs);
    }
}
