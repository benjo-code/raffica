using Avalonia.Headless.XUnit;
using Raffica.Write;
using Raffica.Write.Documents;
using Raffica.Write.Recovery;
using Raffica.WriteApp.Tests.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

/// <summary>The scheduler with a real window and real tabs (Task 6 of milestone 4.3c): the wiring, the close paths and the lease against a composition.</summary>
public sealed class AutosaveSchedulerWindowTests
{
    private static (WriteWindow Window, ManualRecoveryClock Clock) Show(WindowFixture fixture)
    {
        var clock = new ManualRecoveryClock();
        var window = fixture.Show();
        window.RecoveryClockFactory = () => clock;
        return (window, clock);
    }

    private static async Task CopyAsync(WriteWindow window, ManualRecoveryClock clock)
    {
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave!.WhenIdleAsync());
    }

    private static RecoveryStore StoreOf(WriteWindow window) => new(window.RecoveryFolder);

    [AvaloniaFact]
    public async Task TheFirstEditCreatesTheSchedulerAndStartsTheClock()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        Assert.Null(window.Autosave);
        Assert.Equal(0, clock.Started);

        SaveHarness.Edit(tab);

        Assert.NotNull(window.Autosave);
        Assert.Equal(1, clock.Started);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.Interval);
    }

    [AvaloniaFact]
    public async Task TheFolderFollowsTheSettingsFile()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        Assert.Equal(RecoveryStore.FolderFor(fixture.SettingsFile), window.RecoveryFolder);
        Assert.StartsWith(fixture.Folder, window.RecoveryFolder, StringComparison.Ordinal);

        SaveHarness.Edit(tab);
        await CopyAsync(window, clock);

        Assert.Single(StoreOf(window).List().Entries);
        Assert.True(Directory.Exists(Path.Combine(fixture.Folder, "recovery")));
    }

    [AvaloniaFact]
    public async Task ASaveDeletesTheCopyOfTheTab()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        await CopyAsync(window, clock);
        Assert.Single(StoreOf(window).List().Entries);

        Assert.IsType<Saved>(await SaveHarness.AwaitAsync(window.SaveActiveAsync()));

        Assert.False(tab.IsDirty);
        Assert.Empty(StoreOf(window).List().Entries);
    }

    [AvaloniaFact]
    public async Task DeactivationAsksForACopyAtOnce()
    {
        using var fixture = new WindowFixture();
        var (window, _) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);

        window.Autosave!.TickNow();
        await SaveHarness.AwaitAsync(window.Autosave.WhenIdleAsync());

        Assert.Single(StoreOf(window).List().Entries);
    }

    [AvaloniaFact]
    public async Task TheSettingIsReadLive()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        Assert.True(window.Settings.Shell.Autosave);

        // The shell's own settings object is what the scheduler asks: off writes nothing.
        window.Settings.SetAutosaveForTests(false);
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave!.WhenIdleAsync());
        Assert.Empty(StoreOf(window).List().Entries);

        window.Settings.SetAutosaveForTests(true);
        await CopyAsync(window, clock);
        Assert.Single(StoreOf(window).List().Entries);
    }

    [AvaloniaFact]
    public async Task SnapshotDuringAPreeditDoesNotDeadlock()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(10);
        SaveHarness.Edit(tab);
        var session = tab.EnsureEditor();

        // The pool thread holds the lease (inside the read of the model) until the UI thread has asked for its own.
        using var taken = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        tab.Docx.BeforeModelRead = () =>
        {
            taken.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };
        clock.Fire();
        await WindowFixture.WaitAsync(() => taken.IsSet);
        Assert.True(RecoverySnapshot.IsRunning(tab.Docx));

        // A composition starts on the UI thread: it asks for the lease and waits for the pool's, which a helper lets go after a moment.
        var releaser = Task.Run(async () =>
        {
            await Task.Delay(300);
            release.Set();
        });
        var shown = session.SetPreedit(session.Caret!.Value, "かん", 0);
        await releaser;

        Assert.True(shown);
        await SaveHarness.AwaitAsync(window.Autosave!.WhenIdleAsync());
        Assert.Single(StoreOf(window).List().Entries);
        Assert.Equal(0, window.Autosave.FailureCount);
        Assert.False(tab.Runner.IsLeased);
        session.SetPreedit(session.Caret!.Value, null, 0);
    }

    [AvaloniaFact]
    public async Task ClosingWithDontSaveDeletesTheCopy()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        await CopyAsync(window, clock);
        var before = File.ReadAllBytes(path);
        Assert.Single(StoreOf(window).List().Entries);

        var closing = window.CloseTabAsync(tab);
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_DontSave");
        await SaveHarness.AwaitAsync(closing);

        Assert.Empty(window.Workspace.Tabs);
        Assert.Empty(StoreOf(window).List().Entries);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public async Task CloseWithCancelKeepsIt()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        await CopyAsync(window, clock);

        var closing = window.CloseTabAsync(tab);
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(closing);

        Assert.Single(window.Workspace.Tabs);
        Assert.Single(StoreOf(window).List().Entries);

        // The window's own close, answered Cancel, keeps it too.
        window.Close();
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_Cancel");
        await WindowFixture.WaitAsync(() => !window.IsClosing && !window.QuitRequested);
        Assert.Single(StoreOf(window).List().Entries);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowWithDontSaveDeletesTheCopyAndABareCloseOfTheTabDoesNot()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        await CopyAsync(window, clock);

        window.Close();
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_DontSave");
        await WindowFixture.WaitAsync(() => !window.IsVisible);

        Assert.Empty(StoreOf(window).List().Entries);
        Assert.True(clock.Stopped);
    }
}
