using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class OpenFlowTests : IDisposable
{
    private readonly PdfEngine _engine = new();
    private readonly Workspace _workspace = new(new CloseGuard(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true)));
    private readonly List<(string Path, PdfOpenError Error)> _errors = [];
    private readonly List<(string Name, bool Incorrect)> _asked = [];
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "raffica-pdf-openflow-" + Guid.NewGuid().ToString("N"));

    public OpenFlowTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        foreach (var tab in _workspace.Tabs.ToList())
        {
            tab.Dispose();
        }

        Directory.Delete(_temp, recursive: true);
    }

    public static TheoryData<string> BadFiles => ["missing", "not-a-pdf", "empty", "directory", "deleted"];

    private OpenFlow NewFlow(Queue<string?>? passwords = null, Func<PdfEngine>? engine = null) =>
        new(_workspace, engine ?? (() => _engine), (name, incorrect) =>
        {
            _asked.Add((name, incorrect));
            return Task.FromResult(passwords is { Count: > 0 } ? passwords.Dequeue() : null);
        }, (path, error) => _errors.Add((path, error)));

    [Fact]
    public async Task OpeningAFileAddsAndSelectsATab()
    {
        var path = TestData.Pdf("hello_world.pdf");
        var tab = await NewFlow().OpenAsync(path);

        Assert.NotNull(tab);
        Assert.Same(tab, _workspace.Active);
        Assert.Equal([tab], _workspace.Tabs);
        Assert.Equal(ViewerState.Ready, tab.Session.State);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task AlreadyOpenFileIsSelectedNotOpenedTwice()
    {
        var flow = NewFlow();
        var first = await flow.OpenAsync(TestData.Pdf("hello_world.pdf"));
        var other = await flow.OpenAsync(TestData.Pdf("text-rich.pdf"));
        Assert.Same(other, _workspace.Active);

        var again = await flow.OpenAsync(Path.Combine(TestData.Root, "testdata", "pdfs", "..", "pdfs", "hello_world.pdf"));

        Assert.Same(first, again);
        Assert.Same(first, _workspace.Active);
        Assert.Equal(2, _workspace.Tabs.Count);
    }

    [Fact]
    public async Task ConcurrentOpensOfTheSameFileMakeOneTab()
    {
        var flow = NewFlow();
        var path = TestData.Pdf("hello_world.pdf");
        var results = await Task.WhenAll(flow.OpenAsync(path), flow.OpenAsync(path));

        Assert.Same(results[0], results[1]);
        Assert.Single(_workspace.Tabs);
    }

    [Fact]
    public async Task PasswordIsAskedAndTheRightOneOpens()
    {
        var tab = await NewFlow(new Queue<string?>(["hôtel"])).OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf"));

        Assert.NotNull(tab);
        Assert.Equal([("encrypted_hello_world_r3.pdf", false)], _asked);
        Assert.Same(tab, _workspace.Active);
        Assert.Equal(ViewerState.Ready, tab.Session.State);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task WrongPasswordAsksAgainWithTheIncorrectHint()
    {
        var tab = await NewFlow(new Queue<string?>(["wrong", "hôtel"])).OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf"));

        Assert.NotNull(tab);
        Assert.Equal([("encrypted_hello_world_r3.pdf", false), ("encrypted_hello_world_r3.pdf", true)], _asked);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task CancelingThePasswordLeavesNoTab()
    {
        Assert.Null(await NewFlow(new Queue<string?>([null])).OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf")));
        Assert.Empty(_workspace.Tabs);
        Assert.Empty(_errors);

        // Canceling the second question (after a wrong password) ends the same way.
        Assert.Null(await NewFlow(new Queue<string?>(["wrong", null])).OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf")));
        Assert.Empty(_workspace.Tabs);
        Assert.Empty(_errors);
    }

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task EveryOpenErrorEndsInACardAndNoTab(string kind)
    {
        var keep = await NewFlow().OpenAsync(TestData.Pdf("hello_world.pdf"));
        Assert.NotNull(keep);
        Func<PdfEngine>? engine = null;
        string path;
        PdfOpenError expected;
        switch (kind)
        {
            case "missing":
                path = Path.Combine(_temp, "missing.pdf");
                expected = PdfOpenError.FileNotFound;
                break;
            case "not-a-pdf":
                path = TestData.Pdf("not-a-pdf.pdf");
                expected = PdfOpenError.NotAPdf;
                break;
            case "empty":
                path = TestData.Pdf("empty.pdf");
                expected = PdfOpenError.NotAPdf;
                break;
            case "directory":
                path = _temp;
                expected = PdfOpenError.CannotRead;
                break;
            default:
                var gone = Path.Combine(_temp, "gone.pdf");
                path = gone;
                File.Copy(TestData.Pdf("hello_world.pdf"), gone);
                engine = () =>
                {
                    File.Delete(gone);
                    return _engine;
                };
                expected = PdfOpenError.FileNotFound;
                break;
        }

        var tab = await NewFlow(engine: engine).OpenAsync(path);

        Assert.Null(tab);
        var error = Assert.Single(_errors);
        Assert.Equal(path, error.Path);
        Assert.Equal(expected, error.Error);
        Assert.Equal([keep], _workspace.Tabs);
        Assert.Same(keep, _workspace.Active);
    }

    [Fact]
    public async Task SeveralFilesOpenInOrderAndTheLastIsActive()
    {
        var paths = new[] { TestData.Pdf("hello_world.pdf"), Path.Combine(_temp, "missing.pdf"), TestData.Pdf("text-rich.pdf") };
        await NewFlow().OpenManyAsync(paths);

        Assert.Equal(["hello_world.pdf", "text-rich.pdf"], _workspace.Tabs.Select(t => t.Title));
        Assert.Equal("text-rich.pdf", _workspace.Active!.Title);
        Assert.Single(_errors);
    }
}
