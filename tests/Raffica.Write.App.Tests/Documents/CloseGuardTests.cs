using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Hosting;
using Raffica.Write;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.WriteApp.Tests.Dialogs;
using Xunit;

namespace Raffica.WriteApp.Tests.Documents;

/// <summary>The close question of a document with unsaved changes (Task 19 of milestone 4.3a): window close, tab close and quit, every answer, the order of several tabs.</summary>
public sealed class CloseGuardTests
{
    private static readonly TimeSpan Gate = TimeSpan.FromSeconds(30);

    private static async Task AskedAsync(WriteWindow window) => await SaveHarness.WaitForCardAsync(window);

    [AvaloniaFact]
    public async Task DirtyWindowCloseAsksAndEachAnswerBehaves()
    {
        // Cancel: the window stays, the document stays dirty and nothing was written.
        using (var fixture = new WindowFixture())
        {
            var window = fixture.Show();
            var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
            SaveHarness.Edit(tab);
            var before = File.ReadAllBytes(path);

            window.Close();
            await AskedAsync(window);
            Assert.True(window.IsVisible);
            Assert.Equal(WriteStrings.UnsavedTitle, window.Host.ActiveDialog!.Title);
            Assert.Contains(WriteStrings.Fill(WriteStrings.UnsavedMessageFormat, "doc.docx"), WindowFixture.CardTexts(window));
            SaveHarness.Click(window, "PART_Cancel");
            await WindowFixture.WaitAsync(() => !window.IsClosing && !window.QuitRequested);
            Assert.True(window.IsVisible);
            Assert.False(window.IsClosing);
            Assert.Single(window.Workspace.Tabs);
            Assert.True(tab.Tab.IsDirty);
            Assert.Equal(before, File.ReadAllBytes(path));

            // Esc is Cancel too.
            window.Close();
            await AskedAsync(window);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            WindowFixture.Pump();
            await WindowFixture.WaitAsync(() => !window.Host.IsOpen);
            Assert.True(window.IsVisible);
            Assert.True(tab.Tab.IsDirty);
        }

        // Don't save: the window closes and the file is as it was.
        using (var fixture = new WindowFixture())
        {
            var window = fixture.Show();
            var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
            SaveHarness.Edit(tab);
            var before = File.ReadAllBytes(path);

            window.Close();
            await AskedAsync(window);
            SaveHarness.Click(window, "PART_DontSave");
            await WindowFixture.WaitAsync(() => !window.IsVisible);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        // Save: the file is written, then the window closes.
        using (var fixture = new WindowFixture())
        {
            var window = fixture.Show();
            var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
            SaveHarness.Edit(tab);

            window.Close();
            await AskedAsync(window);
            SaveHarness.Click(window, "PART_Save");
            await WindowFixture.WaitAsync(() => !window.IsVisible);
            Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task CleanWindowClosesAtOnce()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        Assert.False(tab.Tab.IsDirty);

        window.Close();

        Assert.False(window.Host.IsOpen, "a clean document asks nothing");
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task ClosingAnUnchangedUntitledTabAsksNothing()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await window.Flow.NewDocumentAsync(PaperSize.A4, CancellationToken.None);
        WindowFixture.Pump();
        Assert.NotNull(tab);

        await SaveHarness.AwaitAsync(window.CloseTabAsync(tab));

        Assert.False(window.Host.IsOpen);
        Assert.Empty(window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task ClosingAnEditedOneAsksAndSaveRunsSaveAs()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await window.Flow.NewDocumentAsync(PaperSize.A4, CancellationToken.None);
        WindowFixture.Pump();
        Assert.NotNull(tab);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        tab.EnsureEditor();
        SaveHarness.Edit(tab);
        var target = Path.Combine(fixture.Folder, "closed.docx");
        var picked = 0;
        window.SavePicker = _ =>
        {
            picked++;
            return Task.FromResult<SavePick?>(new SavePick(target, Existed: false));
        };

        var task = window.CloseTabAsync(tab);
        await AskedAsync(window);
        Assert.Contains(WriteStrings.Fill(WriteStrings.UnsavedMessageFormat, "Untitled"), WindowFixture.CardTexts(window));
        SaveHarness.Click(window, "PART_Save");
        await SaveHarness.AwaitAsync(task);

        Assert.Equal(1, picked);
        Assert.Empty(window.Workspace.Tabs);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(target), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TwoDirtyTabsAreAskedInOrder()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (first, firstPath) = await SaveHarness.OpenCopyAsync(fixture, window, "first.docx");
        var (second, secondPath) = await SaveHarness.OpenCopyAsync(fixture, window, "second.docx");
        SaveHarness.Edit(first);
        SaveHarness.Edit(second);
        _ = await SaveHarness.OpenCopyAsync(fixture, window, "clean.docx");
        var firstBefore = File.ReadAllBytes(firstPath);

        var task = window.RunCloseAsync(quit: false);
        await AskedAsync(window);
        Assert.Contains(WriteStrings.Fill(WriteStrings.UnsavedMessageFormat, "first.docx"), WindowFixture.CardTexts(window));
        Assert.Same(first, window.Workspace.Active);
        SaveHarness.Click(window, "PART_DontSave");

        await WindowFixture.WaitAsync(() => window.Host.IsOpen && WindowFixture.CardTexts(window).Contains(WriteStrings.Fill(WriteStrings.UnsavedMessageFormat, "second.docx")));
        Assert.Same(second, window.Workspace.Active);

        // Cancel at the second question stops the close and keeps both tabs open and dirty.
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(task);
        Assert.True(window.IsVisible);
        Assert.Equal(3, window.Workspace.Tabs.Count);
        Assert.True(first.Tab.IsDirty);
        Assert.True(second.Tab.IsDirty);
        Assert.Equal(firstBefore, File.ReadAllBytes(firstPath));

        // Asked again: Save for the first, Don't save for the second; both answered, the window closes.
        task = window.RunCloseAsync(quit: false);
        await AskedAsync(window);
        SaveHarness.Click(window, "PART_Save");
        await WindowFixture.WaitAsync(() => window.Host.IsOpen && WindowFixture.CardTexts(window).Contains(WriteStrings.Fill(WriteStrings.UnsavedMessageFormat, "second.docx")));
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(firstPath), StringComparison.Ordinal);
        SaveHarness.Click(window, "PART_DontSave");
        await SaveHarness.AwaitAsync(task);
        await WindowFixture.WaitAsync(() => !window.IsVisible);
        Assert.DoesNotContain(SaveHarness.Typed, SaveHarness.TextOf(secondPath), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task QuitAsksThroughTheHost()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var shutdowns = 0;
        var host = new WindowHost(_ => throw new InvalidOperationException("no new window is made"), () => shutdowns++, Avalonia.Threading.Dispatcher.UIThread);
        host.Adopt(window);

        // Cmd+Q: the quit goes through the close questions; Cancel keeps the window and the app.
        var task = window.RunCloseAsync(quit: true);
        await AskedAsync(window);
        Assert.True(window.QuitRequested);
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(task);
        Assert.False(window.QuitRequested);
        Assert.Equal(0, shutdowns);
        Assert.True(window.IsVisible);
        Assert.Same(window, host.Current);

        // Quit again, Save: the file is written, the window closes and the host ends the app.
        task = window.RunCloseAsync(quit: true);
        await AskedAsync(window);
        SaveHarness.Click(window, "PART_Save");
        await SaveHarness.AwaitAsync(task);
        await WindowFixture.WaitAsync(() => !window.IsVisible);
        WindowFixture.Pump();
        Assert.Equal(1, shutdowns);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);
        Assert.Null(host.Current);
    }

    [AvaloniaFact]
    public async Task ClosingATabAsksOnceAndEachAnswerBehaves()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var before = File.ReadAllBytes(path);

        // Two requests while the question is open are one question.
        var first = window.CloseTabAsync(tab);
        var again = window.CloseTabAsync(tab);
        Assert.Same(first, again);
        await AskedAsync(window);
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(first);
        Assert.Single(window.Workspace.Tabs);
        Assert.False(window.Host.IsOpen);

        var close = window.CloseTabAsync(tab);
        await AskedAsync(window);
        SaveHarness.Click(window, "PART_DontSave");
        await SaveHarness.AwaitAsync(close);
        Assert.Empty(window.Workspace.Tabs);
        Assert.Equal(before, File.ReadAllBytes(path));

        var (saved, savedPath) = await SaveHarness.OpenCopyAsync(fixture, window, "saved.docx");
        SaveHarness.Edit(saved);
        close = window.CloseTabAsync(saved);
        await AskedAsync(window);
        SaveHarness.Click(window, "PART_Save");
        await SaveHarness.AwaitAsync(close);
        Assert.Empty(window.Workspace.Tabs);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(savedPath), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ACleanTabClosesAtOnceFromTheCommandAndTheButton()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        _ = await SaveHarness.OpenCopyAsync(fixture, window);

        window.CloseActiveTab();

        Assert.Empty(window.Workspace.Tabs);
        Assert.False(window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task ASaveThatFailsStopsTheClose()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            var task = window.RunCloseAsync(quit: false);
            await AskedAsync(window);
            SaveHarness.Click(window, "PART_Save");

            // The error card of the save follows the question; the user has to read it, and the window stays.
            await WindowFixture.WaitAsync(() => window.Host.IsOpen && window.Host.ActiveDialog!.Title == WriteStrings.SaveErrorTitle);
            SaveHarness.Click(window, "PART_Ok");
            await SaveHarness.AwaitAsync(task);
            Assert.True(window.IsVisible);
            Assert.False(window.IsClosing);
            Assert.True(tab.Tab.IsDirty);
            Assert.Single(window.Workspace.Tabs);
        }
        finally
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    [AvaloniaFact]
    public async Task TheCloseWaitsForARunningSaveAndDoesNotDiscardIt()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        tab.Runner.LeaseWaitTimeout = Gate;
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        tab.Docx.AfterModelRead = () =>
        {
            reached.Set();
            release.Wait(Gate);
        };
        var save = window.SaveActiveAsync();
        await WindowFixture.WaitAsync(() => reached.IsSet);

        window.Close();
        await Task.Delay(100);
        WindowFixture.Pump();
        Assert.True(window.IsVisible, "the window closed under a running save");
        Assert.False(window.Host.IsOpen, "no question while the save may still finish it");

        release.Set();
        Assert.IsType<Saved>(await SaveHarness.AwaitAsync(save));

        // The save made the document clean, so nothing is left to ask and the window goes.
        await WindowFixture.WaitAsync(() => !window.IsVisible);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TextTypedWhileTheCloseSaveRunsKeepsTheTabOpen()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        tab.Runner.LeaseWaitTimeout = Gate;
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        tab.Docx.AfterModelRead = () =>
        {
            reached.Set();
            release.Wait(Gate);
        };

        var task = window.RunCloseAsync(quit: false);
        await AskedAsync(window);
        SaveHarness.Click(window, "PART_Save");
        await WindowFixture.WaitAsync(() => reached.IsSet);
        var session = tab.Editor!;
        Assert.Null(session.Apply(e => e.InsertText(session.Selection!.Value, "late "), 0));
        release.Set();
        await SaveHarness.AwaitAsync(task);

        // What was typed after the model was read is queued and not in the file; closing now would lose it, so the window stays.
        Assert.True(window.IsVisible);
        Assert.True(tab.Tab.IsDirty);
        Assert.DoesNotContain("late ", SaveHarness.TextOf(path), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AHandoverArrivingWhileTheQuestionIsOpenWaitsForTheAnswer()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var other = fixture.Copy(WindowFixture.Poi, "arrived.docx");

        var task = window.RunCloseAsync(quit: false);
        await AskedAsync(window);
        window.OnHandover([other]);
        await WindowFixture.WaitAsync(() => window.PendingHandover.Count == 1);
        Assert.Single(window.Workspace.Tabs);

        // Cancel: the question is over and the file opens in the window that stays.
        SaveHarness.Click(window, "PART_Cancel");
        await SaveHarness.AwaitAsync(task);
        await WindowFixture.WaitAsync(() => window.Workspace.Tabs.Count == 2);
        Assert.Empty(window.PendingHandover);
    }

    [AvaloniaFact]
    public async Task AHandoverThatArrivesOnceTheCloseIsConfirmedIsLeftForTheNextWindow()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var other = fixture.Copy(WindowFixture.Poi, "arrived.docx");

        var task = window.RunCloseAsync(quit: false);
        await AskedAsync(window);
        window.OnHandover([other]);
        await WindowFixture.WaitAsync(() => window.PendingHandover.Count == 1);
        SaveHarness.Click(window, "PART_DontSave");
        await SaveHarness.AwaitAsync(task);
        await WindowFixture.WaitAsync(() => !window.IsVisible);

        Assert.Equal([other], window.TakeUndelivered());
    }

    [AvaloniaFact]
    public async Task SaveInTheQuestionOfAFaultedTabIsSaveAs()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var before = File.ReadAllBytes(path);
        tab.Runner.FailEdit(new InvalidOperationException("an edit failed"));
        WindowFixture.Pump();
        var target = Path.Combine(fixture.Folder, "kept.docx");
        var picks = 0;
        window.SavePicker = _ =>
        {
            picks++;
            return Task.FromResult<SavePick?>(new SavePick(target, Existed: false));
        };

        var task = window.RunCloseAsync(quit: false);
        await AskedAsync(window);
        SaveHarness.Click(window, "PART_Save");
        await SaveHarness.AwaitAsync(task);
        await WindowFixture.WaitAsync(() => !window.IsVisible);

        Assert.Equal(1, picks);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(target), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public void CardsAreBuiltByCardFooter()
    {
        var keys = CardFooterTests.Entries().Select(e => e.Key).ToList();
        Assert.Equal(["ChangedOriginalCard.Create", "SaveErrorCard.Create", "UnsavedChangesCard.Create", "RecoveryCard.Create", "NumberPagesCard.Create", "PageSetupCard.Create", "TableInsertCard.Create", "TableBordersCard.Create", "TablePropertiesCard.Create"], keys);
        foreach (var (key, make) in CardFooterTests.Entries())
        {
            var dialog = make();
            var footer = Assert.IsType<Avalonia.Controls.StackPanel>(dialog.Footer);
            Assert.Equal(Avalonia.Layout.Orientation.Horizontal, footer.Orientation);
            Assert.Equal(Raffica.Shell.Controls.CardFooter.Spacing, footer.Spacing);
            var buttons = footer.Children.OfType<Avalonia.Controls.Button>().ToList();
            Assert.Contains(buttons, b => b.IsCancel);
            Assert.Contains(buttons, b => b.IsDefault);
            Assert.Equal(CardFooterTests.DefaultButtons[key], buttons.Single(b => b.IsDefault).Name);
        }
    }
}
