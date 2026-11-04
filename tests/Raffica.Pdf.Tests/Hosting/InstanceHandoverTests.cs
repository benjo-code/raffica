using Raffica.Core.Hosting;
using Raffica.Pdf.Hosting;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class InstanceHandoverTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-handover-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // A name of its own per test: never the name of the product, so a running app or another test cannot interfere.
    private static string UniqueName() => "raffica-test-" + Guid.NewGuid().ToString("N");

    private string TouchFile(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static async Task<IReadOnlyList<string>> ReceiveAsync(SingleInstance primary, Action send)
    {
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(paths => received.TrySetResult(paths));
        send();
        return await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SecondInstanceSendsItsFilesToTheFirst()
    {
        var name = UniqueName();
        using var primary = SingleInstance.TryBecomePrimary(name);
        Assert.NotNull(primary);
        var file = TouchFile("a.pdf");

        var received = await ReceiveAsync(primary, () => Assert.True(InstanceHandover.TryHandOver(name, [file], TimeSpan.FromSeconds(5))));

        Assert.Equal([file], received);
    }

    [Fact]
    public void NoRunningInstanceMeansNoHandover()
    {
        Assert.False(InstanceHandover.TryHandOver(UniqueName(), [TouchFile("a.pdf")], TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task RelativePathsAreResolvedAgainstTheSendersFolder()
    {
        var name = UniqueName();
        using var primary = SingleInstance.TryBecomePrimary(name);
        Assert.NotNull(primary);
        var relative = Path.Combine("some", "relative.pdf");

        var received = await ReceiveAsync(primary, () => Assert.True(InstanceHandover.TryHandOver(name, [relative], TimeSpan.FromSeconds(5))));

        var path = Assert.Single(received);
        Assert.True(Path.IsPathRooted(path));
        Assert.Equal(Path.GetFullPath(relative), path);
    }

    [Fact]
    public void OddPathsAreReportedAndNeverThrow()
    {
        var good = TouchFile("with spaces in it.pdf");
        var notPdf = TouchFile("notes.txt");
        var folder = Path.Combine(_folder, "folder.pdf");
        Directory.CreateDirectory(folder);
        var odd = new List<string>
        {
            Path.Combine("no", "such", "relative.pdf"),
            Path.Combine(_folder, "missing.pdf"),
            folder,
            "https://example.com/file.pdf",
            "",
            "   ",
            "bad\0name.pdf",
            good,
            notPdf,
        };
        for (var i = 0; i < 64; i++)
        {
            odd.Add(Path.Combine(_folder, $"missing-{i}.pdf"));
        }

        var reported = new List<string>();
        var accepted = InstanceHandover.Accept(odd, reported.Add);

        // Only existing regular files are opened (a non-PDF is one: opening it ends in the error card of the open flow).
        Assert.Equal([good, notPdf], accepted);
        Assert.Equal(odd.Count - 2, reported.Count);
        Assert.DoesNotContain(good, reported);
    }

    [Fact]
    public void AcceptHandlesAnEmptyMessage()
    {
        Assert.Empty(InstanceHandover.Accept([], _ => Assert.Fail("nothing to report")));
    }
}
