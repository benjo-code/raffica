using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Raffica.Write;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;
using Raffica.Write.Recovery;
using Raffica.WriteApp.Tests.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

/// <summary>The offer and Restore of milestone 4.3c (Task 8): the flow with a fake window, and the real window with real tabs.</summary>
public sealed class RecoveryFlowTests
{
    private static readonly DateTimeOffset s_started = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
    private const int DeadPid = 4_000_001;
    private const int LivePid = 4_000_002;
    private const int SelfPid = 4_000_003;

    private sealed class MapProbe : IProcessProbe
    {
        public int Calls { get; private set; }

        public DateTimeOffset? StartTimeOf(int processId)
        {
            Calls++;
            return processId == LivePid ? s_started : null;
        }
    }

    private sealed class FakeUi(RecoveryChoice choice) : IRecoveryUi
    {
        public List<IReadOnlyList<RecoveryEntry>> Asked { get; } = [];

        public List<RecoveryEntry> Opened { get; } = [];

        public List<RecoveryEntry> Errors { get; } = [];

        public Task<RecoveryChoice> AskAsync(IReadOnlyList<RecoveryEntry> entries)
        {
            Asked.Add(entries);
            return Task.FromResult(choice);
        }

        public Exception? Throw { get; set; }

        public Task<WriteTab?> OpenRecoveredAsync(RecoveryEntry entry)
        {
            Opened.Add(entry);
            if (Throw is { } exception)
            {
                throw exception;
            }

            return Task.FromResult<WriteTab?>(null);
        }

        public void ShowOpenError(RecoveryEntry entry) => Errors.Add(entry);
    }

    private static async Task<RecoveryEntry> AddCopyAsync(RecoveryStore store, string title, string? original, int pid = DeadPid, int minutesAgo = 5, byte[]? bytes = null)
    {
        var id = RecoveryStore.NewId();
        store.EnsureFolder();
        await File.WriteAllBytesAsync(store.DocxPathOf(id), bytes ?? await File.ReadAllBytesAsync(WindowFixture.Poi, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var entry = new RecoveryEntry(id, title, original, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), 10, pid, s_started);
        await store.WriteSidecarAsync(entry);
        return entry;
    }

    private static string Original(WindowFixture fixture, string name) => Path.Combine(fixture.Folder, "work", name);

    private static RecoveryStore StoreIn(string folder) => new(Path.Combine(folder, "recovery"));

    private static string[] IdsOf(RecoveryStore store) => [.. store.List().Entries.Select(e => e.Id).Order(StringComparer.Ordinal)];

    // The flow with a fake window.

    [Fact]
    public async Task OneCardForSeveralEntries()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null, minutesAgo: 3);
        await AddCopyAsync(store, "b.docx", null, minutesAgo: 2);
        await AddCopyAsync(store, "c.docx", null, minutesAgo: 1);
        var ui = new FakeUi(RecoveryChoice.Later);

        await new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken);

        var asked = Assert.Single(ui.Asked);
        Assert.Equal(["c.docx", "b.docx", "a.docx"], asked.Select(e => e.Title));
    }

    [Fact]
    public async Task EscKeepsTheCopies()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        await AddCopyAsync(store, "b.docx", null);
        var before = IdsOf(store);
        var ui = new FakeUi(RecoveryChoice.Later);

        await new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(before, IdsOf(store));
        Assert.Empty(ui.Opened);
    }

    [Fact]
    public async Task DiscardDeletesAllListedEntriesAndNothingElse()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        await AddCopyAsync(store, "b.docx", null);
        var live = await AddCopyAsync(store, "live.docx", null, pid: LivePid);
        await File.WriteAllTextAsync(Path.Combine(store.Folder, "notes.txt"), "mine", TestContext.Current.CancellationToken);
        var ui = new FakeUi(RecoveryChoice.Discard);

        await new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal([live.Id], IdsOf(store));
        Assert.True(File.Exists(Path.Combine(store.Folder, "notes.txt")));
        Assert.Empty(ui.Opened);
    }

    [Fact]
    public async Task LiveInstancesCopiesAreNeverOfferedOrDeleted()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var live = await AddCopyAsync(store, "live.docx", null, pid: LivePid);
        var own = await AddCopyAsync(store, "own.docx", null, pid: SelfPid);
        var ui = new FakeUi(RecoveryChoice.Discard);

        await new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken);

        // This process's own copies are offered (a copy of this very process is not "another instance"), the live other one is not.
        Assert.Equal([own.Id], Assert.Single(ui.Asked).Select(e => e.Id));
        Assert.Equal([live.Id], IdsOf(store));

        var only = StoreIn(Path.Combine(fixture.Folder, "second"));
        var other = await AddCopyAsync(only, "live.docx", null, pid: LivePid);
        var quiet = new FakeUi(RecoveryChoice.Restore);
        await new RecoveryFlow(only, new MapProbe(), quiet, SelfPid).RunAsync(TestContext.Current.CancellationToken);
        Assert.Empty(quiet.Asked);
        Assert.Equal([other.Id], IdsOf(only));
    }

    [Fact]
    public async Task NoFolderMeansNoCardAndNoFolder()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var ui = new FakeUi(RecoveryChoice.Restore);

        await new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(ui.Asked);
        Assert.False(Directory.Exists(store.Folder));
    }

    [Fact]
    public async Task ARestoreThatFailsForOneCopyReportsItAndGoesOn()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        await AddCopyAsync(store, "b.docx", null);
        var ui = new FakeUi(RecoveryChoice.Restore);

        await new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken);

        // The fake opens nothing (null): every copy is tried, every one is reported, none is deleted.
        Assert.Equal(2, ui.Opened.Count);
        Assert.Equal(2, ui.Errors.Count);
        Assert.Equal(2, IdsOf(store).Length);
    }

    [Fact]
    public async Task DiscardDeletesAllFiftyOfferedEntriesEvenThoughTheCardListsTwentyAndNothingElse()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var bytes = await File.ReadAllBytesAsync(WindowFixture.Poi, TestContext.Current.CancellationToken);
        for (var i = 0; i < 50; i++)
        {
            await AddCopyAsync(store, $"doc{i:00}.docx", null, bytes: bytes);
        }

        var live = await AddCopyAsync(store, "live.docx", null, pid: LivePid);
        await File.WriteAllTextAsync(Path.Combine(store.Folder, "notes.txt"), "mine", TestContext.Current.CancellationToken);
        var ui = new FakeUi(RecoveryChoice.Discard);

        await new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(50, Assert.Single(ui.Asked).Count);
        Assert.Equal([live.Id], IdsOf(store));
        Assert.True(File.Exists(Path.Combine(store.Folder, "notes.txt")));
    }

    [Fact]
    public async Task ACancelledOpenEndsTheOfferWithoutAnErrorForAGoodCopy()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        await AddCopyAsync(store, "b.docx", null);
        var ui = new FakeUi(RecoveryChoice.Restore) { Throw = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => new RecoveryFlow(store, new MapProbe(), ui, SelfPid).RunAsync(TestContext.Current.CancellationToken));

        Assert.Single(ui.Opened);
        Assert.Empty(ui.Errors);
        Assert.Equal(2, IdsOf(store).Length);
    }

    [Fact]
    public async Task OfferedOncePerProcess()
    {
        var coordinator = new RecoveryCoordinator();
        var runs = 0;
        Task Run()
        {
            runs++;
            return Task.CompletedTask;
        }

        await coordinator.OfferOnce(Run);
        await coordinator.OfferOnce(Run);

        Assert.Equal(1, runs);
        Assert.True(coordinator.WasOffered);
    }

    // The real window.

    private static (WriteWindow Window, ManualRecoveryClock Clock, MapProbe Probe) Prepare(WindowFixture fixture, bool offer = true)
    {
        var clock = new ManualRecoveryClock();
        var probe = new MapProbe();
        var coordinator = new RecoveryCoordinator();
        fixture.BeforeShow = window =>
        {
            window.RecoveryClockFactory = () => clock;
            window.RecoveryProbe = probe;
            window.SelfProcessId = SelfPid;
            if (offer)
            {
                window.RecoveryOffer = coordinator;
            }
        };
        return (null!, clock, probe);
    }

    [AvaloniaFact]
    public async Task AWindowWithNoFileOffersAfterOpened()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        _ = Prepare(fixture);

        var window = fixture.Show();
        await SaveHarness.WaitForCardAsync(window);

        Assert.Equal(WriteStrings.RecoveryTitle, window.Host.ActiveDialog!.Title);
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(window.RecoveryOfferTask!);
        Assert.Single(store.List().Entries);
    }

    [AvaloniaFact]
    public async Task EscOnTheCardKeepsTheCopies()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var entry = await AddCopyAsync(store, "a.docx", null);
        _ = Prepare(fixture);
        var window = fixture.Show();
        await SaveHarness.WaitForCardAsync(window);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => !window.Host.IsOpen);
        await SaveHarness.AwaitAsync(window.RecoveryOfferTask!);

        Assert.Equal([entry.Id], IdsOf(store));
        Assert.Empty(window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task NothingIsReadBeforeTheFirstSharpPage()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        var (_, _, probe) = Prepare(fixture);
        var sharp = false;
        var callsAtSharpPage = -1;
        var previous = fixture.BeforeShow;
        fixture.BeforeShow = window =>
        {
            previous!(window);
            window.FirstSharpPageDrawn += _ =>
            {
                sharp = true;
                callsAtSharpPage = probe.Calls;
            };
        };

        var window = fixture.Show(null, fixture.Copy(WindowFixture.Poi, "doc.docx"));
        await SaveHarness.WaitForCardAsync(window);

        // The card came after the first sharp page, and the folder was not read before it.
        Assert.True(sharp);
        Assert.Equal(0, callsAtSharpPage);
        Assert.Equal(WriteStrings.RecoveryTitle, window.Host.ActiveDialog!.Title);
    }

    [AvaloniaFact]
    public async Task AMeasurementRunNeverOffers()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        var coordinator = new RecoveryCoordinator();
        var probe = new MapProbe();
        var file = fixture.Copy(WindowFixture.Poi, "doc.docx");
        var options = WriteOptions.Parse(["--settings-file", fixture.SettingsFile, "--measure-startup", file]);
        var window = new WriteWindow(options, new Raffica.Shell.Frame.SettingsStore(fixture.SettingsFile).Load()) { RecoveryOffer = coordinator, RecoveryProbe = probe };
        try
        {
            window.RequestRecoveryOffer();
            WindowFixture.Pump();

            Assert.False(coordinator.WasOffered);
            Assert.Null(window.RecoveryOfferTask);
            Assert.Equal(0, probe.Calls);
            Assert.False(window.Host.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OfferedOncePerProcessAcrossWindows()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        _ = Prepare(fixture);
        var first = fixture.Show();
        await SaveHarness.WaitForCardAsync(first);
        SaveHarness.Click(first, "PART_Cancel");

        var second = fixture.Show();
        await Task.Delay(100);
        WindowFixture.Pump();

        Assert.False(second.Host.IsOpen);
        Assert.Null(second.RecoveryOfferTask is { IsCompleted: false } ? second.RecoveryOfferTask : null);
    }

    private static async Task<WriteWindow> RestoreAsync(WindowFixture fixture, WriteWindow? shown = null)
    {
        var window = shown ?? fixture.Show();
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_Restore");
        await SaveHarness.AwaitAsync(window.RecoveryOfferTask!);
        WindowFixture.Pump();
        return window;
    }

    [AvaloniaFact]
    public async Task RestoreOpensDirtyTabsTitledAsBefore()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "x", Original(fixture, "report.docx"), minutesAgo: 2);
        await AddCopyAsync(store, "Untitled", null, minutesAgo: 1);
        _ = Prepare(fixture);

        var window = await RestoreAsync(fixture);

        var tabs = window.Workspace.Tabs.ToList();
        Assert.Equal(2, tabs.Count);
        Assert.All(tabs, t => Assert.True(t.IsDirty));
        Assert.All(tabs, t => Assert.True(t.IsUntitled));
        Assert.All(tabs, t => Assert.True(t.Tab.IsDirty));
        Assert.Contains(tabs, t => t.Title == "report.docx" && t.SuggestedSavePath == Original(fixture, "report.docx"));
        Assert.Contains(tabs, t => t.Title == WriteStrings.UntitledName && t.SuggestedSavePath is null);
    }

    [AvaloniaFact]
    public async Task SaveOnARestoredTabIsSaveAsWithTheOldNameSuggested()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var target = Path.Combine(fixture.Folder, "report.docx");
        var entry = await AddCopyAsync(store, "x", target, minutesAgo: 1);
        _ = Prepare(fixture);
        var window = await RestoreAsync(fixture);
        var tab = Assert.Single(window.Workspace.Tabs);
        string? suggested = null;
        window.SavePicker = t =>
        {
            suggested = t.SuggestedSavePath;
            return Task.FromResult<SavePick?>(new SavePick(target, Existed: false));
        };

        var outcome = await SaveHarness.AwaitAsync(window.SaveActiveAsync());

        Assert.IsType<SavedAs>(outcome);
        Assert.Equal(target, suggested);
        Assert.True(File.Exists(target));
        Assert.Equal(target, tab.Path);
        Assert.False(tab.IsDirty);

        // A saved tab no longer needs its copy.
        Assert.DoesNotContain(entry.Id, IdsOf(store));
    }

    [AvaloniaFact]
    public async Task ARestoredCopyIsDeletedOnlyAfterSaveOrClose()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var entry = await AddCopyAsync(store, "x", Original(fixture, "report.docx"));
        var (_, clock, _) = Prepare(fixture);
        var window = await RestoreAsync(fixture);
        var tab = Assert.Single(window.Workspace.Tabs);

        // A second crash right now still finds the copy; so does a tick (the copy is the tab's own, replaced in place).
        Assert.Equal([entry.Id], IdsOf(store));
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave!.WhenIdleAsync());
        Assert.Equal([entry.Id], IdsOf(store));
        Assert.Equal(1, window.Autosave.TrackedCount);

        var closing = window.CloseTabAsync(tab);
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_DontSave");
        await SaveHarness.AwaitAsync(closing);

        Assert.Empty(store.List().Entries);
    }

    [AvaloniaFact]
    public async Task CancellingTheCloseQuestionKeepsARestoredCopy()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var entry = await AddCopyAsync(store, "x", null);
        _ = Prepare(fixture);
        var window = await RestoreAsync(fixture);
        var tab = Assert.Single(window.Workspace.Tabs);

        var closing = window.CloseTabAsync(tab);
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(closing);

        Assert.Equal([entry.Id], IdsOf(store));
    }

    [AvaloniaFact]
    public async Task ACorruptCopyIsReportedAndKept()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var bad = await AddCopyAsync(store, "bad.docx", Original(fixture, "bad.docx"), minutesAgo: 1, bytes: [1, 2, 3, 4]);
        var good = await AddCopyAsync(store, "good.docx", Original(fixture, "good.docx"), minutesAgo: 2);
        _ = Prepare(fixture);
        var window = fixture.Show();
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_Restore");
        await WindowFixture.WaitAsync(() => window.Host.IsOpen && window.Host.ActiveDialog!.Title == WriteStrings.RecoveryErrorTitle);

        // The card is about the recovery copy: it never says that the file of that name is damaged.
        var texts = WindowFixture.CardTexts(window);
        Assert.Contains("bad.docx", texts);
        Assert.Contains(WriteStrings.RecoveryErrorReason, texts);
        Assert.DoesNotContain(WriteStrings.ForOpenError(OpenError.Corrupt), texts);
        SaveHarness.Click(window, "PART_Ok");
        await SaveHarness.AwaitAsync(window.RecoveryOfferTask!);

        // The good copy opened after the bad one failed; the bad file is still in the folder.
        Assert.Equal("good.docx", Assert.Single(window.Workspace.Tabs).Title);
        Assert.Contains(bad.Id, IdsOf(store));
        Assert.True(File.Exists(store.DocxPathOf(bad.Id)));
        Assert.Contains(good.Id, IdsOf(store));
    }

    [AvaloniaFact]
    public async Task ManyCorruptCopiesAreOneCardAndAreKept()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        for (var i = 0; i < 12; i++)
        {
            await AddCopyAsync(store, $"bad{i:00}.docx", Original(fixture, $"bad{i:00}.docx"), minutesAgo: 100 - i, bytes: [1, 2, 3, 4]);
        }

        _ = Prepare(fixture);
        var window = fixture.Show();
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_Restore");
        await WindowFixture.WaitAsync(() => window.Host.IsOpen && window.Host.ActiveDialog!.Title == WriteStrings.RecoveryFailuresTitle);

        var texts = WindowFixture.CardTexts(window);
        Assert.Contains(WriteStrings.RecoveryFailuresMessage, texts);
        Assert.Contains(WriteStrings.Fill(WriteStrings.OpenFailuresMoreFormat, 7), texts);
        SaveHarness.Click(window, "PART_Ok");
        await SaveHarness.AwaitAsync(window.RecoveryOfferTask!);

        // That was the only card: nothing else is queued behind it, and every copy is still there.
        Assert.False(window.Host.IsOpen);
        Assert.Empty(window.Workspace.Tabs);
        Assert.Equal(12, IdsOf(store).Length);
    }

    [AvaloniaFact]
    public async Task AFileThatDoesNotOpenStillLetsTheWindowOfferTheCopies()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, "a.docx", null);
        _ = Prepare(fixture);

        var window = fixture.Show(null, Path.Combine(fixture.Folder, "missing.docx"));
        await WindowFixture.WaitAsync(() => window.RecoveryOfferTask is not null);
        await WindowFixture.WaitAsync(() => window.Host.IsOpen);
        Assert.Equal(WriteStrings.ErrorTitle, window.Host.ActiveDialog!.Title);
        SaveHarness.Click(window, "PART_Ok");
        await WindowFixture.WaitAsync(() => window.Host.IsOpen && window.Host.ActiveDialog!.Title == WriteStrings.RecoveryTitle);
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(window.RecoveryOfferTask!);

        Assert.Single(store.List().Entries);
    }

    [AvaloniaFact]
    public async Task AWindowThatIsClosingOpensNoCopyAndMakesNoFailure()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var entry = await AddCopyAsync(store, "a.docx", null);
        _ = Prepare(fixture, offer: false);
        var window = fixture.Show();
        window.Close();
        WindowFixture.Pump();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.OpenRecoveredAsync(entry));

        Assert.Empty(window.Workspace.Tabs);
        Assert.Equal([entry.Id], IdsOf(store));
    }

    [AvaloniaFact]
    public async Task AVeryLongTitleIsShortenedOnTheCard()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        await AddCopyAsync(store, new string('x', 100_000) + ".docx", null);
        _ = Prepare(fixture);
        var window = fixture.Show();
        await SaveHarness.WaitForCardAsync(window);

        var longest = WindowFixture.CardTexts(window).Max(t => t.Length);

        Assert.True(longest < 1000, $"a line of the card has {longest} characters");
        Assert.Contains(WindowFixture.CardTexts(window), t => t.StartsWith(new string('x', WriteWindow.MaxShownNameLength), StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ManyEntriesAreListedBounded()
    {
        using var fixture = new WindowFixture();
        var store = StoreIn(fixture.Folder);
        var bytes = await File.ReadAllBytesAsync(WindowFixture.Poi, TestContext.Current.CancellationToken);
        for (var i = 0; i < 50; i++)
        {
            await AddCopyAsync(store, $"doc{i:00}.docx", Original(fixture, $"doc{i:00}.docx"), minutesAgo: 100 - i, bytes: bytes);
        }

        _ = Prepare(fixture);
        var window = fixture.Show();
        await SaveHarness.WaitForCardAsync(window);

        var list = (StackPanel)window.Host.ActiveDialog!.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_List");
        Assert.Equal(RecoveryCard.MaxListed + 1, list.Children.Count);
        Assert.Equal(WriteStrings.Fill(WriteStrings.RecoveryMoreFormat, 30), Assert.IsType<TextBlock>(list.Children[^1]).Text);
        Assert.Equal("… and 30 more", Assert.IsType<TextBlock>(list.Children[^1]).Text);
        Assert.StartsWith("doc49.docx", Assert.IsType<TextBlock>(list.Children[0]).Text, StringComparison.Ordinal);

        SaveHarness.Click(window, "PART_Restore");
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 50);
        await SaveHarness.AwaitAsync(window.RecoveryOfferTask!, 120000);

        Assert.Equal(50, window.Workspace.Tabs.Count);
        Assert.All(window.Workspace.Tabs, t => Assert.True(t.IsDirty));
        Assert.Equal(50, IdsOf(store).Length);
    }
}
