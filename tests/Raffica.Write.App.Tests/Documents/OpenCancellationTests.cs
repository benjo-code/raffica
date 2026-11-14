using Avalonia.Headless.XUnit;
using Raffica.Hosting;
using Raffica.Write.Documents;
using Xunit;
using RecentFilesStore = Raffica.Core.Settings.RecentFilesStore;

namespace Raffica.WriteApp.Tests.Documents;

/// <summary>An open that is still reading a file when the window closes ends without a tab, a recent entry or a card, and lets go of the document (Review Focus 4 of the plan).</summary>
public sealed class OpenCancellationTests
{
    private sealed class GatedOpener
    {
        private readonly Func<string, bool> _gated;

        public GatedOpener(Func<string, bool> gated) => _gated = gated;

        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public List<OpenedDocument> Made { get; } = [];

        public List<string> Calls { get; } = [];

        public OpenAttempt Open(string path, OpenWork work, long max, CancellationToken token)
        {
            lock (Calls)
            {
                Calls.Add(Path.GetFileName(path));
            }

            if (_gated(path))
            {
                Started.Set();
                Release.Wait(TimeSpan.FromSeconds(20), CancellationToken.None);
            }

            // The gate is the slow read; the work itself is the real one and does not see the cancellation (a file that finishes late).
            var attempt = DocumentOpener.Open(path, work, max, CancellationToken.None);
            if (attempt.Document is { } document)
            {
                lock (Made)
                {
                    Made.Add(document);
                }
            }

            return attempt;
        }
    }

    private static async Task WaitForGateAsync(GatedOpener opener) =>
        Assert.True(await Task.Run(() => opener.Started.Wait(TimeSpan.FromSeconds(20), CancellationToken.None), TestContext.Current.CancellationToken), "the gated open did not start");

    private static bool IsDisposed(OpenedDocument document)
    {
        try
        {
            _ = document.Images.Read("document#rId1");
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return false;
        }
    }

    private static string[] RecentPaths(WindowFixture fixture) =>
        [.. new RecentFilesStore(AppFiles.Write.RecentPathFor(fixture.SettingsFile)).Load().Items.Select(i => i.Path)];

    [AvaloniaFact]
    public async Task AFileThatFinishesAfterTheWindowClosedAddsNoTab()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var opener = new GatedOpener(_ => true);
        window.Flow.Opener = opener.Open;
        var path = fixture.Copy(WindowFixture.Poi, "one.docx");

        var open = window.OpenFilesAsync([path]);
        await WaitForGateAsync(opener);
        window.Close();
        WindowFixture.Pump();
        opener.Release.Set();
        await open;
        WindowFixture.Pump();

        Assert.Empty(window.Workspace.Tabs);
        Assert.Empty(window.Tabs.Items);
        Assert.False(window.Host.IsOpen);
        Assert.Empty(RecentPaths(fixture));
        Assert.Empty(window.Recent.ForStartView(10));
        var made = Assert.Single(opener.Made);
        Assert.True(IsDisposed(made), "the late document is let go");
    }

    [AvaloniaFact]
    public async Task AFileThatFailsAfterTheWindowClosedShowsNoCard()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var opener = new GatedOpener(_ => true);
        window.Flow.Opener = opener.Open;

        var open = window.OpenFilesAsync([Path.Combine(fixture.Folder, "missing.docx")]);
        await WaitForGateAsync(opener);
        window.Close();
        WindowFixture.Pump();
        opener.Release.Set();
        await open;
        WindowFixture.Pump();

        Assert.False(window.Host.IsOpen);
        Assert.Empty(window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task TheFilesQueuedBehindAnOpenThatWasCancelledNeverStart()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var opener = new GatedOpener(path => path.EndsWith("a.docx", StringComparison.Ordinal));
        window.Flow.Opener = opener.Open;

        var open = window.OpenFilesAsync([fixture.Copy(WindowFixture.Poi, "a.docx"), fixture.Copy(WindowFixture.Poi, "b.docx")]);
        await WaitForGateAsync(opener);
        window.Close();
        WindowFixture.Pump();
        opener.Release.Set();
        await open;
        WindowFixture.Pump();

        Assert.Equal(["a.docx"], opener.Calls);
        Assert.Empty(window.Workspace.Tabs);
        Assert.Empty(RecentPaths(fixture));
    }

    [AvaloniaFact]
    public async Task WhenTheWindowClosesAfterTheFirstFileTheSecondAddsNoTabAndTheFirstIsDisposedWithTheWindow()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var opener = new GatedOpener(path => path.EndsWith("b.docx", StringComparison.Ordinal));
        window.Flow.Opener = opener.Open;
        var a = fixture.Copy(WindowFixture.Poi, "a.docx");

        var open = window.OpenFilesAsync([a, fixture.Copy(WindowFixture.Poi, "b.docx")]);
        await WaitForGateAsync(opener);
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        var first = window.Workspace.Tabs[0];
        window.Close();
        WindowFixture.Pump();
        opener.Release.Set();
        await open;
        WindowFixture.Pump();

        Assert.Same(first, Assert.Single(window.Workspace.Tabs));
        Assert.True(first.IsDisposed);
        Assert.Equal(2, opener.Made.Count);
        Assert.True(IsDisposed(opener.Made[1]), "the late document is let go");
        Assert.False(window.Host.IsOpen);

        // Only the file that was open before the close is a recent entry.
        Assert.Equal([a], RecentPaths(fixture));
    }

    [AvaloniaFact]
    public async Task ASealedWorkspaceRefusesATabAndACancelledTokenThrowsFromOpenAsync()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.Flow.OpenAsync(WindowFixture.Poi, cancelled.Token));
        Assert.Empty(window.Workspace.Tabs);

        // The flow checks the seal itself: a tab that is refused is disposed and the open ends in cancellation.
        window.Workspace.Seal();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.Flow.OpenAsync(WindowFixture.Poi));
        Assert.Empty(window.Workspace.Tabs);
        Assert.True(window.Workspace.IsSealed);
    }
}
