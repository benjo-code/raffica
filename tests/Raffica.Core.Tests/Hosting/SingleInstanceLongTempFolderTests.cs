using System.Threading.Channels;
using Raffica.Core.Hosting;
using Xunit;

namespace Raffica.Core.Tests.Hosting;

/// <summary>
/// Changes TMPDIR for the whole process, so it lives in the serial group: no other test reads the temp folder meanwhile.
/// </summary>
[Collection(ProcessWideMeasurementGroup.Name)]
public sealed class SingleInstanceLongTempFolderTests
{
    private static readonly TimeSpan Due = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task LongTempFolderStillHandsOverFiles()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix only: Windows pipes are not socket files");
        var original = Environment.GetEnvironmentVariable("TMPDIR");
        var root = Path.Combine(Path.GetTempPath(), "raffica-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, new string('d', Math.Max(1, 80 - root.Length - 1)));
        Directory.CreateDirectory(folder);
        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", folder);
            var name = "raffica-" + new string('u', 20) + "-" + Guid.NewGuid().ToString("N")[..12];
            using var primary = SingleInstance.TryBecomePrimary(name) ?? throw new InvalidOperationException("Expected to become primary.");
            var received = Channel.CreateUnbounded<IReadOnlyList<string>>();
            primary.StartListening(paths => received.Writer.TryWrite(paths));

            Assert.True(SingleInstance.TrySend(name, ["long.pdf"], Due));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(Due);
            Assert.Equal(["long.pdf"], await received.Reader.ReadAsync(timeout.Token));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMPDIR", original);
            Directory.Delete(root, recursive: true);
        }
    }
}
