using Avalonia.Headless.XUnit;
using Raffica.Core.Saving;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Documents;

/// <summary>Save and Save as of a tab (Task 19 of milestone 4.3a): the file written, the dirty state, the lease, and what a failed save leaves.</summary>
public sealed class SaveFlowTests
{
    private static readonly TimeSpan Gate = TimeSpan.FromSeconds(30);

    private static string ReadOnlyCopy(string path)
    {
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        return path;
    }

    private static void Writable(string path) => File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

    private static void ChangeOnDisk(string path) => File.WriteAllBytes(path, [.. File.ReadAllBytes(path), 0, 0, 0]);

    [AvaloniaFact]
    public async Task SaveWritesTheEditedTextAndClearsDirty()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        Assert.False(tab.Tab.IsDirty);
        SaveHarness.Edit(tab);
        Assert.True(tab.Tab.IsDirty);
        Assert.DoesNotContain(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);

        var outcome = await SaveHarness.AwaitAsync(window.SaveActiveAsync());

        Assert.Equal(new Saved(path), outcome);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);
        Assert.False(tab.Tab.IsDirty);
        Assert.False(tab.Docx.Model.Edits.IsDirty);
        Assert.True(tab.Editor!.CanUndo, "a save keeps the history");
        Assert.False(window.Host.IsOpen);
        Assert.False(tab.Runner.IsLeased);
        Assert.True(tab.Editor.CanEdit);
    }

    [AvaloniaFact]
    public async Task UndoBackToSavedIsNotDirty()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab, "one ");
        await SaveHarness.AwaitAsync(window.SaveActiveAsync());
        Assert.False(tab.Tab.IsDirty);

        SaveHarness.Edit(tab, "two ");
        Assert.True(tab.Tab.IsDirty);
        var session = tab.Editor!;

        // Undo of the edit after the save is the saved state again; one more undo passes it, and a redo comes back to it.
        session.Apply(e => e.History.Undo(), 0);
        WindowFixture.Pump();
        Assert.False(tab.Tab.IsDirty);
        session.Apply(e => e.History.Undo(), 0);
        WindowFixture.Pump();
        Assert.True(tab.Tab.IsDirty);
        session.Apply(e => e.History.Redo(), 0);
        WindowFixture.Pump();
        Assert.False(tab.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SavingTakesTheEditLeaseOnlyWhileTheModelIsReadAndTypingMeanwhileIsQueuedAndReplayed()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab, "one ");
        var session = tab.Editor!;
        var runner = tab.Runner;
        runner.LeaseWaitTimeout = Gate;

        // The save thread takes the lease (the hook runs on it, right after the lease was taken) and gives it back once the model has been read.
        var leaseHeldAtStart = false;
        var savingAtStart = false;
        var leaseThread = 0;
        window.Saves.AfterLeaseTaken = t =>
        {
            leaseHeldAtStart = t.Runner.IsLeased;
            savingAtStart = t.Editor!.IsSaving;
            leaseThread = Environment.CurrentManagedThreadId;
        };

        // The save is held on its pool thread right after the model was read: the lease is back, the scope is not.
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var leasedAtRead = true;
        var readThread = 0;
        tab.Docx.AfterModelRead = () =>
        {
            leasedAtRead = runner.IsLeased;
            readThread = Environment.CurrentManagedThreadId;
            reached.Set();
            release.Wait(Gate);
        };

        var task = window.SaveActiveAsync();
        await WindowFixture.WaitAsync(() => reached.IsSet);
        Assert.True(leaseHeldAtStart, "the save takes the edit lease before it reads the model");
        Assert.True(savingAtStart);
        Assert.NotEqual(Environment.CurrentManagedThreadId, leaseThread);
        Assert.Equal(leaseThread, readThread);
        Assert.False(leasedAtRead, "the lease is given back as soon as the model has been read");
        Assert.False(runner.IsLeased);
        Assert.False(session.CanEdit);
        Assert.Equal(EditBlockedReason.Saving, session.BlockedReason);

        // Typing during the write is queued and does not reach the file.
        Assert.Null(session.Apply(e => e.InsertText(session.Selection!.Value, "later "), 0));
        Assert.Equal(1, session.PendingCount);
        release.Set();
        var outcome = await SaveHarness.AwaitAsync(task);

        Assert.IsType<Saved>(outcome);
        var saved = SaveHarness.TextOf(path);
        Assert.Contains("one ", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("later ", saved, StringComparison.Ordinal);

        // The scope ended after the revision was marked saved: the queued text is in the document now and the tab is dirty again.
        Assert.Equal(0, session.PendingCount);
        Assert.True(session.CanEdit);
        Assert.True(tab.Tab.IsDirty);
        Assert.Contains("later ", SaveHarness.TextOfModel(tab), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ReadOnlyTargetKeepsDocumentDirtyAndShowsTheErrorCard()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var before = File.ReadAllBytes(path);
        var names = SaveHarness.Names(fixture.Folder);
        var steps = tab.Editor!.CanUndo;
        var revision = tab.Docx.Model.Edits.Revision;
        ReadOnlyCopy(path);
        try
        {
            var task = window.SaveActiveAsync();
            await SaveHarness.WaitForCardAsync(window);

            var texts = WindowFixture.CardTexts(window);
            Assert.Contains(WriteStrings.ForSaveFailure(SaveFailureReason.NotWritable), texts);
            Assert.Contains(WriteStrings.SaveErrorKept, texts);
            Assert.Equal(WriteStrings.SaveErrorTitle, window.Host.ActiveDialog!.Title);
            SaveHarness.Click(window, "PART_Ok");
            var outcome = await SaveHarness.AwaitAsync(task);

            Assert.Equal(new SaveFailed(new SaveProblem(SaveProblemKind.Failure, SaveFailureReason.NotWritable)), outcome);
            Assert.True(tab.Tab.IsDirty);
            Assert.True(tab.Docx.Model.Edits.IsDirty);
            Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
            Assert.Equal(steps, tab.Editor.CanUndo);
            Assert.True(tab.Editor.CanEdit, "a failed save ends the save scope");
            Assert.False(tab.Runner.IsLeased);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(names, SaveHarness.Names(fixture.Folder));
        }
        finally
        {
            Writable(path);
        }

        // The user fixes the file and saves again: the same document is written.
        var again = await SaveHarness.AwaitAsync(window.SaveActiveAsync());
        Assert.Equal(new Saved(path), again);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData("Cancel")]
    [InlineData("Overwrite")]
    [InlineData("SaveAs")]
    public async Task ChangedOnDiskAsksOverwriteSaveAsCancel(string choice)
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        ChangeOnDisk(path);
        var external = File.ReadAllBytes(path);
        var other = Path.Combine(fixture.Folder, "other.docx");
        var picks = 0;
        window.SavePicker = _ =>
        {
            picks++;
            return Task.FromResult<SavePick?>(new SavePick(other, Existed: false));
        };
        var revision = tab.Docx.Model.Edits.Revision;

        var task = window.SaveActiveAsync();
        await SaveHarness.WaitForCardAsync(window);
        Assert.Equal(WriteStrings.ChangedTitle, window.Host.ActiveDialog!.Title);
        Assert.Contains(WriteStrings.Fill(WriteStrings.ChangedMessageFormat, "doc.docx"), WindowFixture.CardTexts(window));

        // Overwrite is never the default; Save as is.
        Assert.True(SaveHarness.ButtonOf(window, "PART_SaveAs").IsDefault);
        Assert.False(SaveHarness.ButtonOf(window, "PART_Overwrite").IsDefault);
        var answer = Enum.Parse<ChangedChoice>(choice);
        SaveHarness.Click(window, "PART_" + choice);
        var outcome = await SaveHarness.AwaitAsync(task);

        switch (answer)
        {
            case ChangedChoice.Cancel:
                Assert.IsType<SaveCancelled>(outcome);
                Assert.Equal(external, File.ReadAllBytes(path));
                Assert.True(tab.Tab.IsDirty);
                Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
                Assert.True(tab.Editor!.CanUndo);
                Assert.Equal(0, picks);
                Assert.False(File.Exists(other));
                break;
            case ChangedChoice.Overwrite:
                Assert.Equal(new Saved(path), outcome);
                Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);
                Assert.False(tab.Tab.IsDirty);
                Assert.Equal(0, picks);
                break;
            default:
                Assert.Equal(new SavedAs(other), outcome);
                Assert.Equal(1, picks);
                Assert.Equal(external, File.ReadAllBytes(path));
                Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(other), StringComparison.Ordinal);
                Assert.False(tab.Tab.IsDirty);
                Assert.Equal(other, tab.Path);
                break;
        }

        Assert.False(window.Host.IsOpen);
        Assert.False(tab.Runner.IsLeased);
        Assert.Empty(Directory.EnumerateFiles(fixture.Folder, ".pw-*"));
    }

    [AvaloniaFact]
    public async Task TargetAppearedAfterThePickerKeepsDirty()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var target = Path.Combine(fixture.Folder, "new.docx");
        var before = File.ReadAllBytes(path);
        var revision = tab.Docx.Model.Edits.Revision;

        // The user typed a name that did not exist; another program creates the file before the write.
        window.SavePicker = _ =>
        {
            File.WriteAllBytes(target, [7, 7, 7]);
            return Task.FromResult<SavePick?>(new SavePick(target, Existed: false));
        };
        var task = window.SaveAsActiveAsync();
        await SaveHarness.WaitForCardAsync(window);
        Assert.Contains(WriteStrings.ForSaveFailure(SaveFailureReason.TargetExists), WindowFixture.CardTexts(window));
        SaveHarness.Click(window, "PART_Ok");
        var outcome = await SaveHarness.AwaitAsync(task);

        Assert.Equal(new SaveFailed(new SaveProblem(SaveProblemKind.Failure, SaveFailureReason.TargetExists)), outcome);
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(target));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(tab.Tab.IsDirty);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.True(tab.Editor!.CanUndo);
        Assert.Equal(path, tab.Path);
        Assert.Equal(path, tab.Docx.OriginPath);
        Assert.Empty(Directory.EnumerateFiles(fixture.Folder, ".pw-*"));

        // A file the user confirmed to replace at the picker is replaced.
        window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(target, Existed: true));
        var replaced = await SaveHarness.AwaitAsync(window.SaveAsActiveAsync());
        Assert.Equal(new SavedAs(target), replaced);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(target), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SaveAsMakesTheTabBelongToTheNewFile()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab, "one ");
        var before = File.ReadAllBytes(path);
        var target = Path.Combine(fixture.Folder, "renamed.docx");
        window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(target, Existed: false));

        var outcome = await SaveHarness.AwaitAsync(window.SaveAsActiveAsync());

        Assert.Equal(new SavedAs(target), outcome);
        Assert.Equal(target, tab.Path);
        Assert.Equal("renamed.docx", tab.Title);
        Assert.Equal("renamed.docx", tab.Tab.Title);
        Assert.Equal(target, tab.Tab.Path);
        Assert.Equal(target, tab.Docx.OriginPath);
        Assert.False(tab.Tab.IsDirty);
        Assert.Same(tab, window.Workspace.Find(target));
        Assert.Null(window.Workspace.Find(path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Contains("one ", SaveHarness.TextOf(target), StringComparison.Ordinal);
        await WindowFixture.WaitAsync(() => window.Recent.ForStartView(10).Any(r => Path.Combine(r.Folder, r.Name) == target));

        // The next Save goes to the new file, and the old one is left alone.
        SaveHarness.Edit(tab, "two ");
        var saved = await SaveHarness.AwaitAsync(window.SaveActiveAsync());
        Assert.Equal(new Saved(target), saved);
        Assert.Contains("two ", SaveHarness.TextOf(target), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public async Task SaveAsToTheOpenedPathIsAnOverwriteOfTheOriginalAndIsChecked()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        ChangeOnDisk(path);
        var external = File.ReadAllBytes(path);
        window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(path, Existed: true));

        var task = window.SaveAsActiveAsync();
        await SaveHarness.WaitForCardAsync(window);
        Assert.Equal(WriteStrings.ChangedTitle, window.Host.ActiveDialog!.Title);
        SaveHarness.Click(window, "PART_Cancel");
        Assert.IsType<SaveCancelled>(await SaveHarness.AwaitAsync(task));
        Assert.Equal(external, File.ReadAllBytes(path));
        Assert.True(tab.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SaveAsOntoAFileAnotherTabHasOpenIsRefused()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        var (_, otherPath) = await SaveHarness.OpenCopyAsync(fixture, window, "second.docx");
        window.Workspace.Select(tab);
        SaveHarness.Edit(tab);
        var other = File.ReadAllBytes(otherPath);
        window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(otherPath, Existed: true));

        var task = window.SaveAsActiveAsync();
        await SaveHarness.WaitForCardAsync(window);
        Assert.Contains(WriteStrings.SaveFailOpenElsewhere, WindowFixture.CardTexts(window));
        SaveHarness.Click(window, "PART_Ok");
        var outcome = await SaveHarness.AwaitAsync(task);

        Assert.Equal(new SaveFailed(new SaveProblem(SaveProblemKind.OpenElsewhere)), outcome);
        Assert.Equal(other, File.ReadAllBytes(otherPath));
        Assert.Equal(path, tab.Path);
        Assert.True(tab.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SaveAsCancelledAtThePickerChangesNothing()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var before = File.ReadAllBytes(path);
        window.SavePicker = _ => Task.FromResult<SavePick?>(null);

        var outcome = await SaveHarness.AwaitAsync(window.SaveAsActiveAsync());

        Assert.IsType<SaveCancelled>(outcome);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(tab.Tab.IsDirty);
        Assert.Equal(path, tab.Path);
        Assert.False(window.Saves.IsRunning(tab));
    }

    [AvaloniaFact]
    public async Task SaveIsRefusedWhileOnlyAPrefixIsOpenAndSaysWhy()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        using var gate = new ManualResetEventSlim();
        var path = fixture.Copy(WindowFixture.Thesis, "doc.docx");
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        WindowFixture.Pump();
        tab.Runner.BeforeFullOpen = () => gate.Wait(Gate);
        tab.Runner.Start();
        try
        {
            Assert.True(tab.Docx.IsPrefix || !tab.Runner.FullModelReady);
            foreach (var saveAs in new[] { false, true })
            {
                var outcome = await SaveHarness.AwaitAsync(saveAs ? window.SaveAsActiveAsync() : window.SaveActiveAsync());
                Assert.Equal(new SaveRefused(SaveRefusal.StillOpening), outcome);
            }

            var command = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == WriteCommandIds.Save);
            Assert.False(command.IsEnabled);
            Assert.Equal(WriteStrings.ReasonEditOpening, command.CurrentDisabledReason);
            Assert.False(window.RunCommand(WriteCommandIds.SaveAs));
        }
        finally
        {
            gate.Set();
        }

        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady);
    }

    [AvaloniaFact]
    public async Task NothingToSaveAndARunningSaveDisableSaveWithTheirReasons()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        var rules = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToDictionary(c => c.Id);
        window.Ribbon.RefreshCommandStates();

        Assert.False(rules[WriteCommandIds.Save].IsEnabled);
        Assert.Equal(WriteStrings.ReasonNothingToSave, rules[WriteCommandIds.Save].CurrentDisabledReason);
        Assert.True(rules[WriteCommandIds.SaveAs].IsEnabled, "Save as of an unchanged document is a copy");
        Assert.Equal(new SaveRefused(SaveRefusal.NothingToSave), await SaveHarness.AwaitAsync(window.SaveActiveAsync()));

        SaveHarness.Edit(tab);
        window.Ribbon.RefreshCommandStates();
        Assert.True(rules[WriteCommandIds.Save].IsEnabled);

        // While a save runs both commands wait and a second save is refused.
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        tab.Runner.LeaseWaitTimeout = Gate;
        tab.Docx.AfterModelRead = () =>
        {
            reached.Set();
            release.Wait(Gate);
        };
        var task = window.SaveActiveAsync();
        await WindowFixture.WaitAsync(() => reached.IsSet);
        window.Ribbon.RefreshCommandStates();
        Assert.False(rules[WriteCommandIds.Save].IsEnabled);
        Assert.False(rules[WriteCommandIds.SaveAs].IsEnabled);
        Assert.Equal(WriteStrings.ReasonEditSaving, rules[WriteCommandIds.Save].CurrentDisabledReason);
        Assert.Equal(WriteStrings.ReasonEditSaving, rules[WriteCommandIds.SaveAs].CurrentDisabledReason);
        Assert.Equal(new SaveRefused(SaveRefusal.Running), await SaveHarness.AwaitAsync(window.SaveActiveAsync()));
        release.Set();
        Assert.IsType<Saved>(await SaveHarness.AwaitAsync(task));
        window.Ribbon.RefreshCommandStates();
        Assert.Equal(WriteStrings.ReasonNothingToSave, rules[WriteCommandIds.Save].CurrentDisabledReason);
    }

    [AvaloniaFact]
    public async Task AFaultedSessionCannotSaveOverTheFileButSaveAsStaysAvailable()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var before = File.ReadAllBytes(path);
        tab.Runner.FailEdit(new InvalidOperationException("an edit failed"));
        WindowFixture.Pump();
        Assert.Equal(EditBlockedReason.Faulted, tab.Editor!.BlockedReason);
        window.Ribbon.RefreshCommandStates();
        var commands = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToDictionary(c => c.Id);

        Assert.False(commands[WriteCommandIds.Save].IsEnabled);
        Assert.Equal(WriteStrings.ReasonSaveFaulted, commands[WriteCommandIds.Save].CurrentDisabledReason);
        Assert.True(commands[WriteCommandIds.SaveAs].IsEnabled);
        Assert.Equal(new SaveRefused(SaveRefusal.Unavailable), await SaveHarness.AwaitAsync(window.SaveActiveAsync()));
        Assert.Equal(before, File.ReadAllBytes(path));

        // Save as is the user's explicit act: it writes what is in memory under a new name (checked like every save) and the tab then belongs to that file.
        var target = Path.Combine(fixture.Folder, "kept.docx");
        window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(target, Existed: false));
        var outcome = await SaveHarness.AwaitAsync(window.SaveAsActiveAsync());
        Assert.Equal(new SavedAs(target), outcome);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(target), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(target, tab.Path);
    }

    [AvaloniaFact]
    public async Task ADocumentThatFailedToOpenCannotBeSavedAtAll()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);

        // A layout fault that did not come from an edit (the file could not be read after page 1): neither command is available.
        var state = SaveCommandState.Of(tab, flowRunning: false);
        Assert.Null(state.Refusal(saveAs: true));
        var faulted = state with { Faulted = true, FaultedByEdit = false };
        Assert.Equal(SaveRefusal.Unavailable, faulted.Refusal(saveAs: true));
        Assert.Equal(SaveRefusal.Unavailable, faulted.Refusal(saveAs: false));
        Assert.Null((state with { Faulted = true, FaultedByEdit = true }).Refusal(saveAs: true));
        Assert.Equal(SaveRefusal.Unavailable, (state with { Faulted = true, FaultedByEdit = true }).Refusal(saveAs: false));
        Assert.Equal(SaveRefusal.NoDocument, SaveCommandState.Of(null, false).Refusal(saveAs: true));
    }

    [AvaloniaFact]
    public async Task TheShortcutsRunSaveAndSaveAs()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        var view = window.ViewOf(tab)!;
        view.Focus();
        SaveHarness.Edit(tab);
        var picks = 0;
        window.SavePicker = _ =>
        {
            picks++;
            return Task.FromResult<SavePick?>(null);
        };

        var save = WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.Save).ShortcutFor(macOs: false)!;
        var saveAs = WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.SaveAs).ShortcutFor(macOs: false)!;
        Assert.Equal("Ctrl+S", save.Text(macOs: false));
        Assert.Equal("Ctrl+Shift+S", saveAs.Text(macOs: false));
        Assert.Equal("Meta+S", save.Text(macOs: true));
        Assert.Equal("Meta+Shift+S", saveAs.Text(macOs: true));
        Assert.Same(WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.Save), WriteCommandCatalog.Find(Avalonia.Input.Key.S, Avalonia.Input.KeyModifiers.Control, Avalonia.Input.KeyModifiers.Control, macOs: false));
        Assert.Same(WriteCommandCatalog.Commands.Single(c => c.Id == WriteCommandIds.SaveAs), WriteCommandCatalog.Find(Avalonia.Input.Key.S, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift, Avalonia.Input.KeyModifiers.Control, macOs: false));

        Assert.True(window.RunCommand(WriteCommandIds.Save));
        await WindowFixture.WaitAsync(() => !tab.Tab.IsDirty);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(path), StringComparison.Ordinal);
        Assert.True(window.RunCommand(WriteCommandIds.SaveAs));
        await WindowFixture.WaitAsync(() => picks == 1 && !window.Saves.IsRunning(tab));
    }

    [AvaloniaFact]
    public async Task APreeditDuringTheWriteDoesNotWaitForTheSave()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var session = tab.Editor!;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(10);
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        tab.Docx.AfterModelRead = () =>
        {
            reached.Set();
            release.Wait(Gate);
        };
        var task = window.SaveActiveAsync();
        await WindowFixture.WaitAsync(() => reached.IsSet);

        // The input method shows its preedit while the file is written: it takes the lease, which the save no longer holds.
        var start = session.Selection!.Value.Start;
        Assert.True(session.SetPreedit(start, "ni", 0));
        Assert.True(session.SetPreedit(start, null, 0));
        release.Set();
        Assert.IsType<Saved>(await SaveHarness.AwaitAsync(task));
    }

    // Holds the save on its pool thread inside the read of the model (it has the lease); the test gives the gate back after a while from another thread, so a request of
    // the UI thread for the lease blocks for that time and then goes on.
    private static async Task<(Task<SaveOutcome> Save, ManualResetEventSlim Release)> HoldTheReadAsync(WriteWindow window, WriteTab tab)
    {
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(20);
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        tab.Docx.BeforeModelRead = () =>
        {
            reached.Set();
            release.Wait(Gate);
        };
        var task = window.SaveActiveAsync();
        await WindowFixture.WaitAsync(() => reached.IsSet);
        Assert.True(tab.Runner.IsLeased, "the saving thread holds the lease while it reads the model");
        return (task, release);
    }

    [AvaloniaFact]
    public async Task APreeditWhileThePoolThreadReadsTheModelWaitsAndDoesNotFaultTheSession()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var session = tab.Editor!;
        var (task, release) = await HoldTheReadAsync(window, tab);
        using var gate = release;
        var start = session.Selection!.Value.Start;

        // The input method shows its preedit now, on the UI thread: it asks for the lease the pool thread holds, waits for it and then goes on (before the fix the UI thread
        // was the owner of the lease, and the request threw and faulted the session).
        _ = Task.Run(() =>
        {
            Thread.Sleep(300);
            gate.Set();
        });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var shown = session.SetPreedit(start, "ni", 0);
        clock.Stop();

        Assert.True(shown);
        Assert.True(clock.ElapsedMilliseconds >= 200, "the preedit did not wait for the lease");
        Assert.True(session.SetPreedit(start, null, 0));
        Assert.Null(tab.Runner.Fault);
        Assert.NotEqual(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.IsType<Saved>(await SaveHarness.AwaitAsync(task));
        Assert.False(tab.Runner.IsLeased);
        Assert.False(tab.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task ARebuildOfThePagesLayoutWhilePoolThreadReadsTheModelWaitsAndDoesNotThrow()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        var runner = tab.Runner;
        runner.EnsureFlow();
        await WindowFixture.WaitAsync(() => runner.FlowLayout is not null);
        var session = tab.Editor!;

        // An edit in the writing view leaves the pages layout stale; switching back to the pages asks for the lease to make it again.
        session.ActiveMode = Raffica.Write.Layout.LayoutMode.Flow;
        SaveHarness.Edit(tab);
        Assert.True(runner.PageLayoutIsStale);
        var (task, release) = await HoldTheReadAsync(window, tab);
        using var gate = release;
        _ = Task.Run(() =>
        {
            Thread.Sleep(300);
            gate.Set();
        });

        session.ActiveMode = Raffica.Write.Layout.LayoutMode.Pages;

        Assert.False(runner.PageLayoutIsStale);
        Assert.Null(runner.Fault);
        Assert.IsType<Saved>(await SaveHarness.AwaitAsync(task));
        Assert.False(runner.IsLeased);
    }

    [AvaloniaFact]
    public async Task ASaveThatFailsBeforeOrWhileItReadsTheModelGivesTheLeaseBack()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        var runner = tab.Runner;
        runner.LeaseWaitTimeout = TimeSpan.FromSeconds(20);

        void AssertLeaseFree()
        {
            Assert.False(runner.IsLeased);
            using var lease = runner.PauseForEdit();
            Assert.True(runner.IsLeased);
        }

        // A read-only target is refused before the model is read: no lease was ever taken.
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            var task = window.SaveActiveAsync();
            await SaveHarness.WaitForCardAsync(window);
            SaveHarness.Click(window, "PART_Ok");
            Assert.IsType<SaveFailed>(await SaveHarness.AwaitAsync(task));
            AssertLeaseFree();
        }
        finally
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }

        // A failure after the lease was taken (the hook of the caller throws): the thread that took it gives it back.
        tab.Docx.BeforeModelRead = () => throw new InvalidOperationException("after the lease");
        var failing = window.SaveActiveAsync();
        await SaveHarness.WaitForCardAsync(window);
        Assert.Equal(WriteStrings.SaveErrorTitle, window.Host.ActiveDialog!.Title);
        SaveHarness.Click(window, "PART_Ok");
        Assert.IsType<SaveFailed>(await SaveHarness.AwaitAsync(failing));
        tab.Docx.BeforeModelRead = null;
        AssertLeaseFree();
        // A changed original, answered Cancel.
        File.WriteAllBytes(path, [.. File.ReadAllBytes(path), 0]);
        var changed = window.SaveActiveAsync();
        await SaveHarness.WaitForCardAsync(window);
        SaveHarness.Click(window, "PART_Cancel");
        Assert.IsType<SaveCancelled>(await SaveHarness.AwaitAsync(changed));
        AssertLeaseFree();

        Assert.True(tab.Tab.IsDirty);
        Assert.True(tab.Editor!.CanEdit);
    }

    [AvaloniaFact]
    public async Task ASavedFileCanBeOpenedAndLaidOutAgain()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab, "Roundtrip ");
        var target = Path.Combine(fixture.Folder, "again.docx");
        window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(target, Existed: false));
        Assert.IsType<SavedAs>(await SaveHarness.AwaitAsync(window.SaveAsActiveAsync()));

        var reopened = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        Assert.NotSame(tab, reopened);
        Assert.Contains("Roundtrip ", SaveHarness.TextOf(target), StringComparison.Ordinal);
        Assert.DoesNotContain("Roundtrip ", SaveHarness.TextOf(path), StringComparison.Ordinal);
    }

    // Spec section 9: an edited thesis, saved through the real flow, read again and laid out equals the layout of the edited model.
    [AvaloniaFact]
    public async Task AnEditedThesisSavedThroughTheFlowLaysOutAsTheEditedModelDoes()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window, "thesis.docx", WindowFixture.Thesis);
        var session = tab.EnsureEditor();
        var paragraphs = tab.Docx.Model.Body.Blocks.Select((b, i) => (b, i)).Where(x => x.b is Paragraph).Select(x => (Paragraph)x.b).ToList();
        Assert.True(paragraphs.Count > 700);

        TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);
        TextRange Caret(Paragraph paragraph, int offset) => new(At(paragraph, offset), At(paragraph, offset));
        int Len(Paragraph paragraph) => ParagraphProjection.Build(paragraph, tab.Runner.PageLayout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;

        // Typing, Enter, a delete across three paragraphs and a copy and paste, each through the session like a key would.
        var typed = paragraphs[40];
        Assert.NotNull(session.Apply(e => e.InsertText(Caret(typed, Math.Min(3, Len(typed))), "Edited "), 0));
        var split = paragraphs[300];
        Assert.NotNull(session.Apply(e => e.InsertText(Caret(split, Len(split) / 2), "\n"), 0));
        paragraphs = [.. tab.Docx.Model.Body.Blocks.OfType<Paragraph>()];
        var from = paragraphs[500];
        var to = paragraphs[502];
        Assert.NotNull(session.Apply(e => e.Delete(new TextRange(At(from, Math.Min(2, Len(from))), At(to, Math.Min(1, Len(to))))), 0));
        paragraphs = [.. tab.Docx.Model.Body.Blocks.OfType<Paragraph>()];
        var source = paragraphs[100];
        var target = paragraphs[600];
        Assert.NotNull(session.Apply(e => e.Paste(Caret(target, Math.Min(2, Len(target))), e.Copy(new TextRange(At(source, 0), At(source, Len(source)))), _ => true), 0));
        Assert.True(tab.Tab.IsDirty);

        var outcome = await SaveHarness.AwaitAsync(window.SaveActiveAsync());
        Assert.Equal(new Saved(path), outcome);
        Assert.False(tab.Tab.IsDirty);

        // The edited model's layout (from scratch, whole) against the layout of the file read again.
        var fonts = new OpenWork().Fonts();
        using var reopened = DocxFile.Open(path);
        var edited = DocumentLayout.Create(tab.Docx.Model, fonts, new LayoutOptions(LayoutMode.Pages));
        await edited.LayOutRestAsync(null, CancellationToken.None);
        var again = DocumentLayout.Create(reopened.Model, fonts, new LayoutOptions(LayoutMode.Pages));
        await again.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(edited.LaidOutPages, again.LaidOutPages);
        for (var i = 0; i < edited.LaidOutPages; i++)
        {
            Assert.True(edited.Page(i).Text == again.Page(i).Text, $"text of page {i} differs after save and reopen");
            Assert.True(edited.Page(i).Items.Count == again.Page(i).Items.Count, $"item count of page {i} differs after save and reopen");
        }
    }
}
