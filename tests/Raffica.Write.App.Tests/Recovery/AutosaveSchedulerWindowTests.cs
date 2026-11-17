using Avalonia.Headless.XUnit;
using Raffica.Write;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Recovery;
using Raffica.WriteApp.Tests.Documents;
using Raffica.WriteApp.Tests.Settings;
using Xunit;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

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

        // The dialog's result is what the scheduler asks: off writes nothing.
        WriteSettingsTests.OpenSettings(window);
        WriteSettingsTests.AutosaveBox(window).IsChecked = false;
        SaveHarness.Click(window, "PART_Ok");
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave!.WhenIdleAsync());
        Assert.Empty(StoreOf(window).List().Entries);

        WriteSettingsTests.OpenSettings(window);
        WriteSettingsTests.AutosaveBox(window).IsChecked = true;
        SaveHarness.Click(window, "PART_Ok");
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
    public async Task ClosingTheWindowWithDontSaveDeletesTheCopyAndStopsTheClock()
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

    // The adapter against real tabs (review focus: the timer fires while no copy may be made).

    private static async Task AssertNoCopyAsync(WriteWindow window, ManualRecoveryClock clock)
    {
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave!.WhenIdleAsync());
        Assert.Empty(StoreOf(window).List().Entries);
        Assert.False(Directory.Exists(window.RecoveryFolder) && Directory.EnumerateFileSystemEntries(window.RecoveryFolder).Any());
        Assert.Equal(0, window.Autosave.FailureCount);
    }

    [AvaloniaFact]
    public async Task ARealFaultedSessionIsSkipped()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        tab.Runner.FailEdit(new InvalidOperationException("test fault"));
        Assert.Equal(Raffica.Write.Editing.EditBlockedReason.Faulted, tab.Editor!.BlockedReason);
        Assert.Equal(SnapshotSkip.Faulted, new WriteTabRecoverySubject(tab).CannotSnapshotNow);

        await AssertNoCopyAsync(window, clock);
    }

    [AvaloniaFact]
    public async Task ARealSessionThatHoldsBeginSaveIsSkipped()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var handle = tab.EnsureEditor().BeginSave();
        Assert.Equal(SnapshotSkip.Saving, new WriteTabRecoverySubject(tab).CannotSnapshotNow);

        await AssertNoCopyAsync(window, clock);

        handle.Dispose();
        await CopyAsync(window, clock);
        Assert.Single(StoreOf(window).List().Entries);
    }

    [AvaloniaFact]
    public async Task ARunningSaveIsSkipped()
    {
        using var fixture = new WindowFixture();
        var (window, clock) = Show(fixture);
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        using var release = new ManualResetEventSlim();
        tab.Docx.BeforeModelRead = () => Assert.True(release.Wait(TimeSpan.FromSeconds(10)));

        var saving = window.SaveActiveAsync();
        await WindowFixture.WaitAsync(() => window.Saves.IsRunning(tab));

        await AssertNoCopyAsync(window, clock);

        release.Set();
        Assert.IsType<Saved>(await SaveHarness.AwaitAsync(saving));
        Assert.Empty(StoreOf(window).List().Entries);
    }

    [AvaloniaFact]
    public async Task APrefixIsSkipped()
    {
        using var fixture = new WindowFixture();
        var attempt = DocumentOpener.Open(WindowFixture.Thesis, new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);
        using var tab = new WriteTab(WindowFixture.Thesis, attempt.Document!);
        Assert.True(tab.Docx.IsPrefix);
        tab.Docx.Model.Edits.Raise();
        var subject = new WriteTabRecoverySubject(tab);
        Assert.True(subject.IsDirty);
        Assert.Equal(SnapshotSkip.Prefix, subject.CannotSnapshotNow);
        Assert.Equal(SnapshotSkip.Prefix, subject.CannotSnapshotInLease);
        var clock = new ManualRecoveryClock();
        var store = new RecoveryStore(Path.Combine(fixture.Folder, "prefix-recovery"));
        using var scheduler = new AutosaveScheduler(store, clock, () => true);
        scheduler.Track(subject);

        clock.Fire();
        await SaveHarness.AwaitAsync(scheduler.WhenIdleAsync());

        Assert.False(Directory.Exists(store.Folder));
        Assert.Equal(0, scheduler.FailureCount);
    }

    // A subject that lets the UI thread act between the early check and the lease: the pool thread waits before it takes the lease.
    private sealed class SlowLease(WriteTabRecoverySubject inner, ManualResetEventSlim leaseRequested, ManualResetEventSlim proceed) : IRecoverySubject, IInLeaseSnapshotCheck
    {
        public string Id => inner.Id;

        public string Title => inner.Title;

        public string? OriginalPath => inner.OriginalPath;

        public DocxFile Docx => inner.Docx;

        public int Revision => inner.Revision;

        public bool IsDirty => inner.IsDirty;

        public SnapshotSkip? CannotSnapshotNow => inner.CannotSnapshotNow;

        public SnapshotSkip? CannotSnapshotInLease => inner.CannotSnapshotInLease;

        public IDisposable TakeModelLease()
        {
            leaseRequested.Set();
            Assert.True(proceed.Wait(TimeSpan.FromSeconds(10)));
            return inner.TakeModelLease();
        }
    }

    [AvaloniaFact]
    public async Task ASaveThatStartsAfterTheEarlyCheckIsSkippedByTheLateCheck()
    {
        using var fixture = new WindowFixture();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, fixture.Show());
        SaveHarness.Edit(tab);
        var session = tab.EnsureEditor();
        using var requested = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        var subject = new SlowLease(new WriteTabRecoverySubject(tab), requested, proceed);
        var store = new RecoveryStore(Path.Combine(fixture.Folder, "late-recovery"));

        // The early check passes on this thread; the pool thread then waits for its lease while the save begins here.
        Assert.Null(subject.CannotSnapshotNow);
        var snapshot = RecoverySnapshot.WriteAsync(subject, store, CancellationToken.None);
        await WindowFixture.WaitAsync(() => requested.IsSet);
        using (session.BeginSave())
        {
            proceed.Set();
            var result = Assert.IsType<SnapshotResult.Skipped>(await SaveHarness.AwaitAsync(snapshot));
            Assert.Equal(SnapshotSkip.Saving, result.Reason);
        }

        Assert.Empty(store.List().Entries);
        Assert.False(File.Exists(store.DocxPathOf(subject.Id)));
        Assert.False(tab.Runner.IsLeased);
    }

    [AvaloniaFact]
    public async Task AMeasuringWindowNeverCreatesTheScheduler()
    {
        using var fixture = new WindowFixture();
        var file = fixture.Copy(WindowFixture.Poi, "m.docx");
        var made = 0;
        var options = WriteOptions.Parse(["--measure-startup", "--settings-file", fixture.SettingsFile, file]);
        var window = new WriteWindow(options, new ShellSettingsStore(fixture.SettingsFile).Load());
        window.RecoveryClockFactory = () =>
        {
            made++;
            return new ManualRecoveryClock();
        };
        window.Show();
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 1);
        var tab = window.Workspace.Tabs[0];
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady);
        tab.EnsureEditor();

        SaveHarness.Edit(tab);

        Assert.True(tab.IsDirty);
        Assert.Null(window.Autosave);
        Assert.Equal(0, made);
        Assert.False(Directory.Exists(window.RecoveryFolder));
        tab.Docx.Model.Edits.MarkSaved();
        window.Close();
        WindowFixture.Pump();
    }

    [AvaloniaFact]
    public async Task AThrowingClockFactoryNeverBreaksTyping()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        window.RecoveryClockFactory = () => throw new InvalidOperationException("no clock");
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);

        SaveHarness.Edit(tab);
        var session = tab.EnsureEditor();
        SaveHarness.Edit(tab, "more");

        Assert.True(tab.Tab.IsDirty);
        Assert.Equal(1, window.RecoveryWiringFailures);
        Assert.Null(window.Autosave);
        Assert.Null(session.BlockedReason);
        Assert.Contains("more", SaveHarness.TextOfModel(tab), StringComparison.Ordinal);
    }
}
