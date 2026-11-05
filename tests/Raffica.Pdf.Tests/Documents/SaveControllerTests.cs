using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Forms;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Core.Text;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Printing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Forms;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class SaveControllerTests
{
    // OriginalChanged asks a question and Canceled ends silently; both have their own tests.
    private static readonly SaveFailureReason[] OtherReasons =
        [.. Enum.GetValues<SaveFailureReason>().Where(r => r is not (SaveFailureReason.OriginalChanged or SaveFailureReason.Canceled))];

    public static TheoryData<SaveFailureReason> EveryOtherReason => new(OtherReasons);

    private static async Task<Dialog> NextCardAsync(DialogHost host)
    {
        await PrintHost.UntilAsync(() => host.ActiveDialog is not null);
        return host.ActiveDialog!;
    }

    private static void Click(Dialog card, string name) =>
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task<int> HighlightsAsync(string path)
    {
        using var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(path);
        var count = 0;
        for (var page = 0; page < session.Document!.PageCount; page++)
        {
            count += (await session.Document.GetAnnotationsAsync(page, TestContext.Current.CancellationToken)).Count(a => a.Kind == AnnotationKind.Highlight);
        }

        return count;
    }

    private static Task<PdfAnnotation?> HighlightAsync(PdfTab tab) => AnnotatedFile.HighlightAsync(tab.Annotations);

    private static SaveRunner Failing(SaveFailureReason reason, List<bool>? overwrites = null) => (_, _, overwrite, _, _) =>
    {
        overwrites?.Add(overwrite);
        throw new SaveFailureException(reason, "injected");
    };

    private static async Task<PdfTab> OpenDirtyAsync(WindowFixture windows, string path)
    {
        var tab = await windows.Window.Flow.OpenAsync(path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.NotNull(await HighlightAsync(tab));
        await tab.Annotations.FlushAsync();
        Assert.True(tab.IsDirty);
        return tab;
    }

    private static string[] TempFiles(string folder) => Directory.GetFiles(folder, ".pw-*.tmp");

    [AvaloniaFact]
    public async Task SaveWithOnlyARefusedDraftDoesNotWriteAndKeepsTheTabDirty()
    {
        var writes = 0;
        SaveRunner runner = (_, _, _, _, _) =>
        {
            writes++;
            return Task.FromResult<AnnotationSaveResult>(null!);
        };

        // A signed file: a save would ask about the signature first. The tab is dirty only because a text box draft was refused.
        using var windows = WindowFixture.Create(saveRunner: runner);
        var tab = await windows.OpenAsync("signed-docmdp-1.pdf");
        using var draft = tab.Annotations.BeginUnresolved();
        Assert.True(tab.IsDirty);
        Assert.False(tab.Annotations.Editor.IsDirty);

        // A card (the signed-file question) would hold the save open: wait for either the end or a card, never for the user.
        var save = windows.Window.SaveActiveAsync();
        await ViewFixture.WaitAsync(() => save.IsCompleted || windows.Window.Host.ActiveDialog is not null);
        Assert.Null(windows.Window.Host.ActiveDialog);
        var saved = await save;
        ViewFixture.Pump();

        Assert.False(saved);
        Assert.Equal(0, writes);
        Assert.True(tab.IsDirty);
        Assert.True(tab.Annotations.HasUnresolved);
        Assert.False(windows.Window.Saves.IsSaving);
    }

    [AvaloniaFact]
    public async Task SaveCleansTheTabAndKeepsScrollAndZoom()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenDirtyAsync(windows, copy.Path);
        var view = windows.Window.ViewOf(tab)!;
        view.SetZoom(1.5);
        ViewFixture.Pump();
        var zoom = view.Zoom;
        var layout = view.Layout;
        var page = view.CurrentPage;

        Assert.True(await windows.Window.SaveActiveAsync());
        ViewFixture.Pump();

        Assert.False(tab.IsDirty);
        Assert.False(windows.Window.Saves.IsSaving);
        Assert.Equal(zoom, view.Zoom);
        Assert.Equal(layout, view.Layout);
        Assert.Equal(page, view.CurrentPage);
        Assert.Equal(ViewerState.Ready, tab.Session.State);
    }

    [AvaloniaFact]
    public async Task SavedFileReopensWithTheMarks()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var before = await HighlightsAsync(copy.Path);
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenDirtyAsync(windows, copy.Path);

        Assert.True(await windows.Window.SaveActiveAsync());

        Assert.Equal(before + 1, await HighlightsAsync(copy.Path));
        Assert.Empty(TempFiles(copy.Folder));
    }

    [AvaloniaFact]
    public async Task SaveAsChangesPathAndTitle()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var target = Path.Combine(copy.Folder, "copy-of-it.pdf");
        var originalLength = new FileInfo(copy.Path).Length;
        using var windows = WindowFixture.Create(realSave: true, savePicker: _ => Task.FromResult<string?>(target));
        var tab = await OpenDirtyAsync(windows, copy.Path);

        Assert.True(await windows.Window.SaveActiveAsAsync());
        ViewFixture.Pump();

        Assert.Equal(target, tab.Path);
        Assert.Equal("copy-of-it.pdf", tab.Title);
        Assert.Equal("copy-of-it.pdf", tab.Tab.Title);
        Assert.Equal(target, tab.Tab.Path);
        Assert.False(tab.IsDirty);
        Assert.True(File.Exists(target));
        Assert.Equal(originalLength, new FileInfo(copy.Path).Length);
        Assert.Equal(1, await HighlightsAsync(target) - await HighlightsAsync(copy.Path));
    }

    [AvaloniaFact]
    public async Task SaveAsCanceledInThePickerDoesNothing()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true, savePicker: _ => Task.FromResult<string?>(null));
        var tab = await OpenDirtyAsync(windows, copy.Path);

        Assert.False(await windows.Window.SaveActiveAsAsync());

        Assert.True(tab.IsDirty);
        Assert.Equal(copy.Path, tab.Path);
        Assert.Null(windows.Window.Host.ActiveDialog);
    }

    [AvaloniaTheory]
    [MemberData(nameof(EveryOtherReason))]
    public async Task EveryFailureReasonEndsInACardAndTheTabStaysDirty(SaveFailureReason reason)
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(saveRunner: Failing(reason));
        var tab = await OpenDirtyAsync(windows, copy.Path);

        var saving = windows.Window.SaveActiveAsync();
        var card = await NextCardAsync(windows.Window.Host);
        Assert.Equal(PdfStrings.SaveFailure(reason), PrintHost.Find<TextBlock>(card, "PART_Message").Text);
        Click(card, "PART_Ok");

        Assert.False(await saving);
        Assert.True(tab.IsDirty);
        Assert.True(tab.Annotations.CanUndo);
        Assert.False(windows.Window.Saves.IsSaving);
    }

    [AvaloniaFact]
    public async Task ACanceledFailureReasonEndsWithoutACard()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(saveRunner: Failing(SaveFailureReason.Canceled));
        var tab = await OpenDirtyAsync(windows, copy.Path);

        var saving = windows.Window.SaveActiveAsync();
        await PrintHost.UntilAsync(() => saving.IsCompleted || windows.Window.Host.ActiveDialog is not null);

        Assert.Null(windows.Window.Host.ActiveDialog);
        Assert.False(await saving);
        Assert.True(tab.IsDirty);
        Assert.True(tab.Annotations.CanUndo);
        Assert.False(windows.Window.Saves.IsSaving);
    }

    [AvaloniaTheory]
    [InlineData("changed")]
    [InlineData("pathChanged")]
    public async Task AnExceptionAfterTheSaveSucceededIsNoSaveFailure(string failing)
    {
        using var copy = new TempCopy("text-rich.pdf");
        var target = Path.Combine(copy.Folder, "renamed.pdf");
        var before = await HighlightsAsync(copy.Path);
        using var windows = WindowFixture.Create(realSave: true, savePicker: _ => Task.FromResult<string?>(target));
        var tab = await OpenDirtyAsync(windows, copy.Path);
        var calls = 0;
        var thrown = false;

        // Changed is raised three times by a save (saving starts, the save is done, saving ends): the second is the one after Core succeeded.
        windows.Window.Saves.Changed += () =>
        {
            if (failing == "changed" && ++calls == 2)
            {
                thrown = true;
                throw new InvalidOperationException("boom");
            }
        };
        windows.Window.Saves.PathChanged += _ =>
        {
            if (failing == "pathChanged")
            {
                thrown = true;
                throw new InvalidOperationException("boom");
            }
        };

        var saving = failing == "changed" ? windows.Window.SaveActiveAsync() : windows.Window.SaveActiveAsAsync();
        await PrintHost.UntilAsync(() => saving.IsCompleted || windows.Window.Host.ActiveDialog is not null);

        // No card "the save failed, the original is unchanged": the file was written.
        Assert.Null(windows.Window.Host.ActiveDialog);
        Assert.True(await saving);
        Assert.True(thrown, "the handler was never called, so the test proves nothing");
        Assert.False(tab.IsDirty);
        Assert.False(windows.Window.Saves.IsSaving);
        Assert.Equal(before + 1, await HighlightsAsync(failing == "changed" ? copy.Path : target));
    }

    [AvaloniaFact]
    public async Task AThrowingChangedHandlerAtTheStartDoesNotLeaveSavingStuck()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenDirtyAsync(windows, copy.Path);
        var armed = true;
        var thrown = false;
        windows.Window.Saves.Changed += () =>
        {
            if (armed)
            {
                armed = false;
                thrown = true;
                throw new InvalidOperationException("boom");
            }
        };

        var saving = windows.Window.SaveActiveAsync();
        await PrintHost.UntilAsync(() => saving.IsCompleted || windows.Window.Host.ActiveDialog is not null);
        if (windows.Window.Host.ActiveDialog is { } card)
        {
            Click(card, "PART_Ok");
        }

        Assert.False(await saving);
        Assert.True(thrown, "the handler was never called, so the test proves nothing");
        Assert.False(windows.Window.Saves.IsSaving);

        // The next save works.
        Assert.True(await windows.Window.SaveActiveAsync());
        Assert.False(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task AnExceptionOfTheSaveNobodyForeseesEndsInACardToo()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(saveRunner: (_, _, _, _, _) => throw new InvalidOperationException("boom"));
        var tab = await OpenDirtyAsync(windows, copy.Path);

        var saving = windows.Window.SaveActiveAsync();
        var card = await NextCardAsync(windows.Window.Host);
        Assert.Equal(PdfStrings.SaveFailure(SaveFailureReason.WriteFailed), PrintHost.Find<TextBlock>(card, "PART_Message").Text);
        Click(card, "PART_Ok");

        Assert.False(await saving);
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SourceModifiedKeepsTheTabAndItsEdits()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenDirtyAsync(windows, copy.Path);
        var untouched = await HighlightsAsync(copy.Path);

        // Another program rewrites the opened file in place.
        await File.AppendAllTextAsync(copy.Path, "\n% rewritten\n", TestContext.Current.CancellationToken);
        var saving = windows.Window.SaveActiveAsync();
        var card = await NextCardAsync(windows.Window.Host);
        Assert.Equal(PdfStrings.SaveFailure(SaveFailureReason.SourceModified), PrintHost.Find<TextBlock>(card, "PART_Message").Text);
        Click(card, "PART_Ok");

        Assert.False(await saving);
        Assert.True(tab.IsDirty);
        Assert.True(tab.Annotations.CanUndo);
        Assert.Equal(untouched, await HighlightsAsync(copy.Path));
        Assert.Empty(TempFiles(copy.Folder));
    }

    [AvaloniaTheory]
    [InlineData("overwrite")]
    [InlineData("saveas")]
    [InlineData("cancel")]
    public async Task ChangedOriginalAsksOverwriteOrSaveAs(string answer)
    {
        using var copy = new TempCopy("text-rich.pdf");
        var time = new ManualTime();
        var overwrites = new List<bool>();
        var targets = new List<string>();
        var other = Path.Combine(copy.Folder, "other.pdf");
        var picks = 0;
        SaveRunner runner = (_, target, overwrite, _, _) =>
        {
            overwrites.Add(overwrite);
            targets.Add(target);
            return overwrites.Count == 1 ? throw new SaveFailureException(SaveFailureReason.OriginalChanged, "changed") : throw new OperationCanceledException();
        };
        using var windows = WindowFixture.Create(time: time, saveRunner: runner, savePicker: _ => { picks++; return Task.FromResult<string?>(other); });
        var tab = await OpenDirtyAsync(windows, copy.Path);

        var saving = windows.Window.SaveActiveAsync();
        var card = await NextCardAsync(windows.Window.Host);
        Assert.Contains("PART_SaveAs", ButtonNames(card));
        Assert.Contains("PART_Overwrite", ButtonNames(card));
        Assert.Contains("PART_Cancel", ButtonNames(card));
        time.Advance(500);
        Click(card, answer switch { "overwrite" => "PART_Overwrite", "saveas" => "PART_SaveAs", _ => "PART_Cancel" });

        Assert.False(await saving);
        Assert.True(tab.IsDirty);
        switch (answer)
        {
            case "overwrite":
                Assert.Equal([false, true], overwrites);
                Assert.Equal(0, picks);
                break;
            case "saveas":
                Assert.Equal([false, false], overwrites);
                Assert.Equal(1, picks);
                Assert.Equal(other, targets[1]);
                break;
            default:
                Assert.Equal([false], overwrites);
                Assert.Equal(0, picks);
                break;
        }
    }

    private static string[] ButtonNames(Dialog card) =>
        [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(card).OfType<Button>().Select(b => b.Name ?? string.Empty)];

    [AvaloniaFact]
    public async Task CancelLeavesTargetUntouchedAndNoTempFile()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var bytes = await File.ReadAllBytesAsync(copy.Path, TestContext.Current.CancellationToken);
        using var windows = WindowFixture.Create();
        var tab = await OpenDirtyAsync(windows, copy.Path);
        var engine = new PdfEngine();
        SaveJobCard? card = null;

        // The user presses Cancel on the job card just as the save starts: the real save of Core sees a canceled token.
        var slot = new ContentControl();
        var controller = new SaveController(
            new DialogQueue(windows.Window.Host),
            _ => Task.FromResult<string?>(null),
            () => engine,
            c =>
            {
                card = c as SaveJobCard ?? card;
                slot.Content = c;
            },
            runner: (t, target, overwrite, progress, token) =>
            {
                card!.RequestCancel();
                return t.Annotations.Editor.SaveAsync(engine, target, overwrite, progress, token);
            });

        Assert.False(await controller.SaveAsync(tab));

        Assert.Null(windows.Window.Host.ActiveDialog);
        Assert.Null(slot.Content);
        Assert.True(tab.IsDirty);
        Assert.True(tab.Annotations.CanUndo);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(copy.Path, TestContext.Current.CancellationToken));
        Assert.Empty(TempFiles(copy.Folder));
        Assert.False(controller.IsSaving);
    }

    [AvaloniaFact]
    public async Task TheJobCardShowsWhileSavingAndGoesAfterwards()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create();
        var tab = await OpenDirtyAsync(windows, copy.Path);
        var gate = new TaskCompletionSource();
        var shown = new List<Control?>();
        var controller = new SaveController(
            new DialogQueue(windows.Window.Host),
            _ => Task.FromResult<string?>(null),
            () => new PdfEngine(),
            shown.Add,
            runner: async (_, _, _, progress, token) =>
            {
                progress.Report(0.5);
                await gate.Task.WaitAsync(token);
                throw new OperationCanceledException();
            });

        var saving = controller.SaveAsync(tab);
        await PrintHost.UntilAsync(() => shown.Count == 1);
        var card = Assert.IsType<SaveJobCard>(shown[0]);
        Assert.True(controller.IsSaving);
        await PrintHost.UntilAsync(() => card.Fraction > 0.49);

        gate.SetResult();
        Assert.False(await saving);
        Assert.Equal(2, shown.Count);
        Assert.Null(shown[1]);
        Assert.False(controller.IsSaving);
    }

    [AvaloniaFact]
    public async Task SaveAndPrintBlockEachOther()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create();
        var tab = await OpenDirtyAsync(windows, copy.Path);

        // A print job runs: the save is refused with a message and writes nothing.
        var calls = 0;
        var printing = new SaveController(
            new DialogQueue(windows.Window.Host),
            _ => Task.FromResult<string?>(null),
            () => new PdfEngine(),
            printRunning: () => true,
            runner: (_, _, _, _, _) =>
            {
                calls++;
                throw new OperationCanceledException();
            });
        var refused = printing.SaveAsync(tab);
        var card = await NextCardAsync(windows.Window.Host);
        Assert.Equal(PdfStrings.PrintBusy, PrintHost.Find<TextBlock>(card, "PART_Message").Text);
        Click(card, "PART_Ok");
        Assert.False(await refused);
        Assert.Equal(0, calls);

        // A save runs: print is refused with a message, and the commands are disabled.
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var other = WindowFixture.Create(saveRunner: async (_, _, _, _, token) =>
        {
            started.SetResult();
            await gate.Task.WaitAsync(token);
            throw new OperationCanceledException();
        });
        using (other)
        {
            var blocked = await OpenDirtyAsync(other, copy.Path);
            var saving = other.Window.SaveActiveAsync();
            await started.Task;
            Assert.True(other.Window.Saves.IsSaving);
            var rules = Rules(other.Window);
            Assert.False(rules.Allows(Raffica.Pdf.Commands.PdfCommandIds.Print));
            Assert.False(rules.Allows(Raffica.Pdf.Commands.PdfCommandIds.Save));
            Assert.False(rules.Allows(Raffica.Pdf.Commands.PdfCommandIds.SaveAs));
            var secondSave = await other.Window.SaveActiveAsync();
            Assert.False(secondSave);

            gate.SetResult();
            Assert.False(await saving);
            Assert.True(blocked.IsDirty);
        }

    }

    private static Raffica.Pdf.Commands.PdfCommandRules Rules(PdfWindow window) => new(
        () => true,
        () => 1,
        () => null,
        isDirty: () => true,
        busySaving: () => window.Saves.IsSaving,
        saveRunning: () => window.Saves.IsSaving);

    [AvaloniaFact]
    public async Task PrintingIsRefusedWhileASaveRuns()
    {
        using var host = new PrintHost();
        var tab = await host.OpenAsync("text-rich.pdf");
        var service = new FakePrintService();
        var command = new PrintCommand(
            () => service,
            () => tab,
            host.Presenter,
            () => Raffica.Core.Settings.AppSettings.Default,
            _ => { },
            saveRunning: () => true);

        var running = command.RunAsync();
        await PrintHost.UntilAsync(() => host.Host.ActiveDialog is not null);
        Assert.Equal(PdfStrings.SaveBusy, host.MessageText);
        Click(host.Host.ActiveDialog!, "PART_Ok");
        await running;
        Assert.False(command.IsRunning);
    }

    [AvaloniaFact]
    public async Task CloseGuardSaveThenCloseSavesFirst()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var before = await HighlightsAsync(copy.Path);
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenDirtyAsync(windows, copy.Path);

        var closing = windows.Window.Workspace.CloseAsync(tab);
        var prompt = await NextCardAsync(windows.Window.Host);
        Click(prompt, "PART_Save");

        Assert.True(await closing);
        Assert.DoesNotContain(tab, windows.Window.Workspace.Tabs);
        Assert.Equal(before + 1, await HighlightsAsync(copy.Path));
    }

    [AvaloniaFact]
    public async Task CloseGuardSaveThenCloseStaysOpenWhenTheSaveFails()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(saveRunner: Failing(SaveFailureReason.DiskFull));
        var tab = await OpenDirtyAsync(windows, copy.Path);

        var closing = windows.Window.Workspace.CloseAsync(tab);
        Click(await NextCardAsync(windows.Window.Host), "PART_Save");
        var failure = await NextCardAsync(windows.Window.Host);
        Assert.Equal(PdfStrings.SaveFailure(SaveFailureReason.DiskFull), PrintHost.Find<TextBlock>(failure, "PART_Message").Text);
        Click(failure, "PART_Ok");

        Assert.False(await closing);
        Assert.Contains(tab, windows.Window.Workspace.Tabs);
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task CloseGuardSaveThenCloseStaysOpenWhenTheSaveIsCanceled()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create();
        var tab = await OpenDirtyAsync(windows, copy.Path);

        var closing = windows.Window.Workspace.CloseAsync(tab);
        Click(await NextCardAsync(windows.Window.Host), "PART_Save");

        Assert.False(await closing);
        Assert.Contains(tab, windows.Window.Workspace.Tabs);
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SaveWithAPendingNudgeSavesTheNudge()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var before = await HighlightsAsync(copy.Path);
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await windows.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.False(tab.IsDirty);

        // A change that is still waiting (a nudge, a text box draft) is committed by the flush that every save starts with.
        tab.Annotations.FlushRequested += () => _ = CommitAsync(tab);
        Assert.True(await windows.Window.SaveActiveAsync());

        Assert.False(tab.IsDirty);
        Assert.Equal(before + 1, await HighlightsAsync(copy.Path));

        static async Task CommitAsync(PdfTab pending)
        {
            using var handle = pending.Annotations.BeginPending();
            await HighlightAsync(pending);
        }
    }

    [AvaloniaFact]
    public async Task SaveKeepsAParkedRefusedDraftDirty()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenDirtyAsync(windows, copy.Path);
        using var parked = tab.Annotations.BeginUnresolved();

        Assert.True(await windows.Window.SaveActiveAsync());

        Assert.True(tab.IsDirty);
        Assert.False(tab.Annotations.Editor.IsDirty);

        parked.Dispose();
        Assert.False(tab.IsDirty);
    }

    [AvaloniaTheory]
    [InlineData("signed-docmdp-1.pdf")]
    [InlineData("signed-docmdp-2.pdf")]
    public async Task SignedWarningShowsForDocMdp1Or2(string file)
    {
        using var copy = new TempCopy(file);
        var time = new ManualTime();
        var calls = 0;
        using var windows = WindowFixture.Create(time: time, saveRunner: (_, _, _, _, _) =>
        {
            calls++;
            throw new OperationCanceledException();
        });
        _ = await OpenDirtyAsync(windows, copy.Path);

        // Cancel is the default: nothing is saved.
        var saving = windows.Window.SaveActiveAsync();
        var card = await NextCardAsync(windows.Window.Host);
        Assert.Contains("PART_SaveAnyway", ButtonNames(card));
        Click(card, "PART_Cancel");
        Assert.False(await saving);
        Assert.Equal(0, calls);

        // Save anyway (after the arming delay) goes on.
        saving = windows.Window.SaveActiveAsync();
        card = await NextCardAsync(windows.Window.Host);
        time.Advance(500);
        Click(card, "PART_SaveAnyway");
        Assert.False(await saving);
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public async Task SignedNoteShowsOncePerTab()
    {
        using var copy = new TempCopy("signed-docmdp-3.pdf");
        var calls = 0;
        using var windows = WindowFixture.Create(saveRunner: (_, _, _, _, _) =>
        {
            calls++;
            throw new OperationCanceledException();
        });
        _ = await OpenDirtyAsync(windows, copy.Path);

        var first = windows.Window.SaveActiveAsync();
        var note = await NextCardAsync(windows.Window.Host);
        Assert.Contains("PART_Ok", ButtonNames(note));
        Click(note, "PART_Ok");
        Assert.False(await first);
        Assert.Equal(1, calls);

        // The second save of the same document goes straight on.
        Assert.False(await windows.Window.SaveActiveAsync());
        Assert.Null(windows.Window.Host.ActiveDialog);
        Assert.Equal(2, calls);
    }

    [AvaloniaFact]
    public async Task AnUnsignedFileGetsNoSignedCard()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var calls = 0;
        using var windows = WindowFixture.Create(saveRunner: (_, _, _, _, _) =>
        {
            calls++;
            throw new OperationCanceledException();
        });
        var tab = await OpenDirtyAsync(windows, copy.Path);

        Assert.False(await windows.Window.SaveActiveAsync());

        Assert.Equal(1, calls);
        Assert.Null(windows.Window.Host.ActiveDialog);
        Assert.True(tab.IsDirty);
    }

    // --- Required fields (M2.3) ---

    private static async Task FillNameAsync(FormEditFixture f, string text = "Anna")
    {
        Assert.IsType<FormEditResult.Applied>(await f.Forms.SetValueAsync(f.Field("Name"), FormFieldState.OfText(text)));
        await f.SettleAsync();
        Assert.True(f.Tab.IsDirty);
        Assert.True(f.Tab.Annotations.Editor.HasOnlyFormChanges);
    }

    private static async Task FillRequiredAsync(FormEditFixture f)
    {
        await f.Forms.RefreshRequiredAsync();
        foreach (var name in f.Forms.EmptyRequired.ToList())
        {
            var field = f.Field(name);
            var state = field.Kind == FormFieldKind.CheckBox ? FormFieldState.OfChecked(true) : FormFieldState.OfText("x");
            Assert.IsType<FormEditResult.Applied>(await f.Forms.SetValueAsync(field, state));
        }

        await f.SettleAsync();
        await f.Forms.RefreshRequiredAsync();
        Assert.Empty(f.Forms.EmptyRequired);
    }

    private static async Task<Dialog> RequiredCardAsync(FormEditFixture f, Task<bool> save)
    {
        await ViewFixture.WaitAsync(() => save.IsCompleted || f.Window.Host.ActiveDialog is not null);
        Assert.False(save.IsCompleted, "no card was asked");
        var card = f.Window.Host.ActiveDialog!;
        Assert.NotNull(PrintHost.Find<Button>(card, "PART_GoToFirst"));
        return card;
    }

    [AvaloniaFact]
    public async Task GoToFirstStopsTheSaveAndMovesToTheField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var bytes = await File.ReadAllBytesAsync(f.Copy.Path, TestContext.Current.CancellationToken);
        await FillNameAsync(f);
        await f.Forms.RefreshRequiredAsync();
        var required = f.Forms.EmptyRequired.ToList();
        Assert.NotEmpty(required);

        var save = f.Window.SaveActiveAsync();
        var card = await RequiredCardAsync(f, save);
        Click(card, "PART_GoToFirst");

        Assert.False(await save);
        ViewFixture.Pump();
        Assert.True(f.Tab.IsDirty);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(f.Copy.Path, TestContext.Current.CancellationToken));
        Assert.False(f.Window.Saves.IsSaving);
        await ViewFixture.WaitAsync(() => f.Parts.Focus.Current is { } key && required.Contains(key.FieldName));
    }

    [AvaloniaFact]
    public async Task SaveAnywayWritesTheFile()
    {
        using var f = await FormEditFixture.OpenAsync();
        await FillNameAsync(f);

        var save = f.Window.SaveActiveAsync();
        var card = await RequiredCardAsync(f, save);
        Click(card, "PART_SaveAnyway");

        Assert.True(await save);
        ViewFixture.Pump();
        Assert.False(f.Tab.IsDirty);
        await f.ReopenAsync();
        Assert.Equal("Anna", (await f.FromDocumentAsync("Name")).State.Text);
    }

    [AvaloniaFact]
    public async Task NoCardWithoutEmptyRequiredFields()
    {
        using var f = await FormEditFixture.OpenAsync();
        await FillRequiredAsync(f);
        await FillNameAsync(f);

        var save = f.Window.SaveActiveAsync();
        await ViewFixture.WaitAsync(() => save.IsCompleted || f.Window.Host.ActiveDialog is not null);
        Assert.Null(f.Window.Host.ActiveDialog);
        Assert.True(await save);
        Assert.False(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task NoCardForADocumentWithoutAForm()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create(realSave: true);
        var tab = await OpenDirtyAsync(windows, copy.Path);
        Assert.False(tab.Forms.HasForm);

        var save = windows.Window.SaveActiveAsync();
        await ViewFixture.WaitAsync(() => save.IsCompleted || windows.Window.Host.ActiveDialog is not null);
        Assert.Null(windows.Window.Host.ActiveDialog);
        Assert.True(await save);
    }

    [AvaloniaFact]
    public async Task NoCardForSaveAsWithoutAFormChange()
    {
        using var f = await FormEditFixture.OpenAsync();
        Assert.True(f.Forms.HasForm);
        var asked = 0;
        var controller = new SaveController(
            new DialogQueue(f.Window.Host),
            _ => Task.FromResult<string?>(null),
            () => new PdfEngine(),
            askRequired: (_, _) =>
            {
                asked++;
                return Task.FromResult(RequiredChoice.SaveAnyway);
            });

        // No form change in the editor: Save as asks for the target (answered with a cancel) and never about required fields.
        Assert.False(await controller.SaveAsAsync(f.Tab));
        Assert.Equal(0, asked);

        // With a form change it does ask.
        await FillNameAsync(f);
        Assert.False(await controller.SaveAsAsync(f.Tab));
        Assert.Equal(1, asked);
    }

    [AvaloniaFact]
    public async Task TheCardGetsTheNamesOfTheEmptyRequiredFields()
    {
        using var f = await FormEditFixture.OpenAsync();
        await FillNameAsync(f);
        IReadOnlyList<string>? names = null;
        var controller = new SaveController(
            new DialogQueue(f.Window.Host),
            _ => Task.FromResult<string?>(null),
            () => new PdfEngine(),
            askRequired: (_, n) =>
            {
                names = n;
                return Task.FromResult(RequiredChoice.GoToFirst);
            });

        Assert.False(await controller.SaveAsync(f.Tab));
        await f.Forms.RefreshRequiredAsync();
        Assert.NotNull(names);
        Assert.Equal(f.Forms.EmptyRequired, names);
        Assert.True(f.Tab.IsDirty);
    }

    // The signed fixture has no required field: the temp copy gets one by a same-length edit of the Name widget (/Ff 2 = required), so no offset moves.
    private static void MakeNameRequired(string path)
    {
        var text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        const string Old = "/P 5 0 R /F 4 /MK << /BC [0 0 0] /BG [1 1 1] >> /FT /Tx /T (Name) /DA (/Helv";
        const string New = "/P 5 0 R/F 4/MK << /BC [0 0 0] /BG [1 1 1] >>/FT /Tx /Ff 2 /T(Name)/DA(/Helv";
        Assert.Equal(Old.Length, New.Length);
        Assert.Contains(Old, text, StringComparison.Ordinal);
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(text.Replace(Old, New, StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task RequiredCardComesBeforeTheSignedFileQuestion()
    {
        using var f = await FormEditFixture.OpenAsync("forms-signed.pdf", prepare: MakeNameRequired);
        await FillNameAsync(f);

        // The only fillable field is the required one: a value and then a cleared field leave a form change and an empty required field.
        Assert.IsType<FormEditResult.Applied>(await f.Forms.SetValueAsync(f.Field("Name"), FormFieldState.OfText(string.Empty)));
        await f.SettleAsync();
        await f.Forms.RefreshRequiredAsync();
        Assert.NotEmpty(f.Forms.EmptyRequired);

        var save = f.Window.SaveActiveAsync();
        var required = await RequiredCardAsync(f, save);
        Click(required, "PART_SaveAnyway");

        // Only now the question about the signature (a form-only change says so).
        await ViewFixture.WaitAsync(() => save.IsCompleted || (f.Window.Host.ActiveDialog is { } d && !ReferenceEquals(d, required)));
        Assert.False(save.IsCompleted);
        var signed = f.Window.Host.ActiveDialog!;
        Assert.Null(signed.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PART_GoToFirst"));
        Assert.Equal(PdfStrings.SignedFormsTitle, signed.Title);
        Click(signed, "PART_Cancel");
        Assert.False(await save);
        Assert.True(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task CloseWithOpenFieldStillAsks()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Anna");

        // The open edit is committed by the flush of the close: the tab is dirty, so the window asks and the draft is not lost silently.
        f.Window.Close();
        await ViewFixture.WaitAsync(() => f.Window.Host.ActiveDialog is not null);
        var card = f.Window.Host.ActiveDialog!;
        Assert.NotNull(PrintHost.Find<Button>(card, "PART_DontSave"));
        Click(card, "PART_Cancel");
        ViewFixture.Pump();

        Assert.True(f.Window.IsVisible);
        Assert.True(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SaveWithARefusedDraftOpenDoesNotDropIt()
    {
        using var f = await FormEditFixture.OpenAsync();
        await FillNameAsync(f);

        // "Kurz" takes five characters: the draft is refused and stays in its editor.
        var box = (TextBox)f.Open(f.Field("Kurz"));
        FormEditFixture.SetText(box, "123456");
        f.Leave();
        await f.SettleAsync();
        Assert.True(f.Interaction.IsEditing);
        Assert.True(f.Tab.Annotations.HasUnresolved);

        // The required card is asked as for any save and does not swallow the draft; the written file holds the name, the tab stays dirty.
        var save = f.Window.SaveActiveAsync();
        var card = await RequiredCardAsync(f, save);
        Click(card, "PART_SaveAnyway");
        Assert.True(await save);
        ViewFixture.Pump();

        Assert.True(f.Interaction.IsEditing);
        Assert.True(f.Tab.Annotations.HasUnresolved);
        Assert.True(f.Tab.IsDirty);
        Assert.Equal("123456", ((TextBox)f.Interaction.Editor!.Input).Text);
        Assert.Equal("Anna", (await f.FromDocumentAsync("Name")).State.Text);
    }
}
