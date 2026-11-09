using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Core.Hosting;
using Raffica.Pdf.Hosting;
using Raffica.Shell.Frame;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class HandoverWindowTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-handover-window-").FullName;

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

    private PdfWindow Show(params string[] files)
    {
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(_folder, "settings.json"), .. files]);
        var window = new PdfWindow(options, new ShellSettings());
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
            Pump();
        }

        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task AHandedOverFileOpensAsATabAndOddPathsOnlyShowCards()
    {
        var window = Show();
        var path = TestData.Pdf("hello_world.pdf");

        window.OnHandover([path, Path.Combine(_folder, "missing.pdf"), _folder]);

        await WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Equal(Path.GetFullPath(path), window.Workspace.Tabs[0].Path);
        Assert.True(window.Host.IsOpen);
        window.Close();
        Pump();
    }

    [AvaloniaFact]
    public async Task TheHandoverIsCheckedOffTheUiThreadAndOnlyTheAnswerReachesIt()
    {
        var window = Show();
        var path = TestData.Pdf("hello_world.pdf");
        var missing = Path.Combine(_folder, "missing.pdf");

        // As the pipe calls it: on a pool thread (File.Exists runs there), then the UI thread opens the file and shows the card.
        await Task.Run(() => window.OnHandover([path, missing]), TestContext.Current.CancellationToken);

        await WaitAsync(() => window.Workspace.Tabs.Count == 1 && window.Host.IsOpen);
        Assert.Equal(Path.GetFullPath(path), window.Workspace.Tabs[0].Path);
        window.Close();
        Pump();
    }

    [AvaloniaFact]
    public async Task SixtyFourHostilePathsNeverThrowAndOpenNothing()
    {
        var window = Show();
        var hostile = Enumerable.Range(0, 64).Select(i => Path.Combine(_folder, $"nope-{i}.pdf")).Append("https://example.com/x.pdf").ToList();

        window.OnHandover(hostile);
        Pump();

        await Task.Delay(50);
        Assert.Empty(window.Workspace.Tabs);
        window.Close();
        Pump();
    }

    [AvaloniaFact]
    public async Task RecentFilesAreLoadedAfterTheWindowOpenedAndStayInTheSettingsFolder()
    {
        var path = TestData.Pdf("hello_world.pdf");
        var window = Show(path);

        await WaitAsync(() => File.Exists(Path.Combine(_folder, "recent.json")));
        Assert.Single(window.Workspace.Tabs);
        Assert.Contains("hello_world.pdf", File.ReadAllText(Path.Combine(_folder, "recent.json")));
        window.Close();
        Pump();
    }

    [AvaloniaFact]
    public async Task AHandoverSentBeforeTheFirstSharpPageIsAccepted()
    {
        var name = "raffica-test-" + Guid.NewGuid().ToString("N");
        using var primary = SingleInstance.TryBecomePrimary(name);
        Assert.NotNull(primary);

        // A file that asks for its password never gets a first sharp page: the old listener started only after the 3 second fallback, past the one second of a later launch.
        var locked = TestData.Pdf("encrypted_hello_world_r6.pdf");
        var second = TestData.Pdf("text-rich.pdf");
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(_folder, "settings.json"), locked]);
        var window = new PdfWindow(options, new ShellSettings());
        window.Listen(primary);
        var sharp = false;
        window.FirstSharpPageDrawn += _ => sharp = true;
        var arrived = false;
        window.HandoverArrived += () => arrived = true;
        window.Show();
        Pump();

        var sent = await Task.Run(() => InstanceHandover.TryHandOver(name, [second], TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.False(sharp);
        await WaitAsync(() => arrived);
        window.Close();
        Pump();
    }

    [AvaloniaFact]
    public async Task TheTabThatWasActiveBeforeAnUpdateIsActiveAfterTheRestart()
    {
        string[] names = ["hello_world.pdf", "text-rich.pdf", "text-basic.pdf"];
        var files = names.Select(TestData.Pdf).ToList();
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(_folder, "settings.json")]).WithFiles(files, files[0]);
        var window = new PdfWindow(options, new ShellSettings());
        window.Show();

        await WaitAsync(() => window.Workspace.Tabs.Count == 3 && window.Workspace.Active is not null);
        await WaitAsync(() => Path.GetFullPath(files[0]) == window.Workspace.Active!.Path);
        window.Close();
        Pump();
    }
}
