using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>Extract pages and Split document in the window: the wiring, the locks and that nothing of the flow exists before the command runs.</summary>
public sealed class OutputCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<PdfTab> OpenReadyAsync(WindowFixture fixture, string file = "pages-marked.pdf")
    {
        var tab = await fixture.OpenAsync(file);
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        return tab;
    }

    private static async Task<Dialog> CardWithAsync(WindowFixture fixture, string name)
    {
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name));
        return fixture.Window.Host.ActiveDialog!;
    }

    private static void Click(Dialog card, string name) => PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task NothingOfTheFlowExistsBeforeTheCommandRuns()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        fixture.Window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
        fixture.Window.DiscardPlan();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: false });
        Assert.False(fixture.Window.OutputFlowCreated);

        // Run and cancel the range card: the flow is made by the command, not before.
        var run = fixture.Window.ExtractPagesAsync();
        Click(await CardWithAsync(fixture, "PART_Cancel"), "PART_Cancel");
        await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.True(fixture.Window.OutputFlowCreated);
    }

    [AvaloniaFact]
    public async Task CommandsAreWiredLockedWithAPlanAndExtractOpensTheNewFile()
    {
        using var fixture = WindowFixture.Create();
        var target = Path.Combine(fixture.Folder, "extracted.pdf");
        fixture.Window.ExtractPathPicker = (_, _) => Task.FromResult<string?>(target);
        var tab = await OpenReadyAsync(fixture);

        foreach (var id in new[] { "pages.extract", "pages.split" })
        {
            Assert.NotNull(Command(fixture.Window, id).Run);
            Assert.True(Command(fixture.Window, id).IsEnabled, id);
        }

        // With a plan open both are locked, with the reason, and running them does nothing.
        fixture.Window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
        foreach (var id in new[] { "pages.extract", "pages.split" })
        {
            Assert.False(Command(fixture.Window, id).IsEnabled, id);
            Assert.Equal("Apply or discard the page changes first", Command(fixture.Window, id).CurrentDisabledReason);
        }

        await fixture.Window.ExtractPagesAsync();
        await fixture.Window.SplitDocumentAsync();
        Assert.False(fixture.Window.OutputFlowCreated);
        fixture.Window.DiscardPlan();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: false });

        // Extract page 1 and open the result as a tab.
        var run = fixture.Window.ExtractPagesAsync();
        var range = await CardWithAsync(fixture, "PART_Range");
        PrintHost.Find<TextBox>(range, "PART_Range").Text = "1";
        Click(range, "PART_Extract");
        while (true)
        {
            await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "PART_Open" or "PART_Go"));
            var card = fixture.Window.Host.ActiveDialog!;
            if (card.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Go"))
            {
                Click(card, "PART_Go");
                PrintHost.Pump();
                continue;
            }

            Click(card, "PART_Open");
            break;
        }

        await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await ViewFixture.WaitAsync(() => fixture.Window.Workspace.Tabs.Count == 2);
        Assert.Equal(target, fixture.Window.Workspace.Active!.Path);
    }

    // Starts an extract of page 1 whose write waits until its token is canceled; returns the run, the signal that the write started and the signal of the cancel.
    private static async Task<(Task Run, Task Started, Task Canceled)> StartBlockedExtractAsync(WindowFixture fixture)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Window.ExtractPathPicker = (_, _) => Task.FromResult<string?>(Path.Combine(fixture.Folder, "blocked.pdf"));
        fixture.Window.Output.Writer = async (_, _, _, _, _, _, _, token, _) =>
        {
            token.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return [];
        };
        var run = fixture.Window.ExtractPagesAsync();
        var range = await CardWithAsync(fixture, "PART_Range");
        Click(range, "PART_Extract");
        await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(20), Ct));
        while (!started.Task.IsCompleted)
        {
            // A confirm card with a loss list comes first for a file with bookmarks.
            var card = fixture.Window.Host.ActiveDialog;
            if (card is not null && card.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Go"))
            {
                Click(card, "PART_Go");
            }

            PrintHost.Pump();
            await Task.Delay(10, Ct);
        }

        return (run, started.Task, canceled.Task);
    }

    private static async Task FinishAsync(WindowFixture fixture, Task run)
    {
        Click(await CardWithAsync(fixture, "PART_Ok"), "PART_Ok");
        await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }

    [AvaloniaFact]
    public async Task ClosingTheTabDuringTheJobCancelsIt()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var (run, _, canceled) = await StartBlockedExtractAsync(fixture);
        Assert.False(canceled.IsCompleted);

        var close = fixture.Window.CloseActiveTabAsync();
        await canceled.WaitAsync(TimeSpan.FromSeconds(20), Ct);

        // The job ends as a cancel: the card says so and nothing was written.
        await FinishAsync(fixture, run);
        await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.DoesNotContain(tab, fixture.Window.Workspace.Tabs);
        Assert.False(File.Exists(Path.Combine(fixture.Folder, "blocked.pdf")));
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowDuringTheJobCancelsIt()
    {
        using var fixture = WindowFixture.Create();
        await OpenReadyAsync(fixture);
        var (_, _, canceled) = await StartBlockedExtractAsync(fixture);

        fixture.Window.Close();
        await canceled.WaitAsync(TimeSpan.FromSeconds(20), Ct);
    }

    [AvaloniaFact]
    public async Task ASecondInvocationIsBlockedAndSaveAndPrintAreRefusedWhileItRuns()
    {
        using var fixture = WindowFixture.Create();
        await OpenReadyAsync(fixture);
        Assert.True(Command(fixture.Window, "file.saveAs").IsEnabled);
        var (run, _, canceled) = await StartBlockedExtractAsync(fixture);
        Assert.True(fixture.Window.Output.IsRunning);

        // The same command and the other one do nothing while the job runs (no second card), and the commands that write or print are locked.
        var card = fixture.Window.Host.ActiveDialog;
        await fixture.Window.ExtractPagesAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await fixture.Window.SplitDocumentAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Same(card, fixture.Window.Host.ActiveDialog);
        Assert.False(Command(fixture.Window, "file.saveAs").IsEnabled);
        Assert.False(Command(fixture.Window, "file.print").IsEnabled);
        Assert.False(Command(fixture.Window, "file.save").IsEnabled);

        // Cancel from the job card; afterwards everything is available again.
        Click(card!, "PART_Cancel");
        await canceled.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        await FinishAsync(fixture, run);
        Assert.False(fixture.Window.Output.IsRunning);
        Assert.True(Command(fixture.Window, "file.saveAs").IsEnabled);
    }

    [AvaloniaFact]
    public async Task ExportImagesIsWiredLockedWithAPlanAndLazilyCreated()
    {
        using var fixture = WindowFixture.Create();
        var folder = Path.Combine(fixture.Folder, "images");
        Directory.CreateDirectory(folder);
        fixture.Window.FolderPicker = () => Task.FromResult<string?>(folder);
        var tab = await OpenReadyAsync(fixture);
        Assert.False(fixture.Window.ExportFlowCreated);
        Assert.NotNull(Command(fixture.Window, "pages.exportImages").Run);
        Assert.True(Command(fixture.Window, "pages.exportImages").IsEnabled);

        // With a plan open the command is locked with the reason, and running it does nothing.
        fixture.Window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
        Assert.False(Command(fixture.Window, "pages.exportImages").IsEnabled);
        Assert.Equal("Apply or discard the page changes first", Command(fixture.Window, "pages.exportImages").CurrentDisabledReason);
        await fixture.Window.ExportImagesAsync();
        Assert.False(fixture.Window.ExportFlowCreated);
        fixture.Window.DiscardPlan();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: false });

        // Export page 2 as an image.
        var run = fixture.Window.ExportImagesAsync();
        var card = await CardWithAsync(fixture, "PART_Range");
        PrintHost.Find<TextBox>(card, "PART_Range").Text = "2";
        Click(card, "PART_ChooseFolder");
        await PrintHost.UntilAsync(() => PrintHost.Find<Button>(card, "PART_Export").IsEnabled);
        Click(card, "PART_Export");
        Click(await CardWithAsync(fixture, "PART_Ok"), "PART_Ok");
        await run.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        Assert.True(fixture.Window.ExportFlowCreated);
        Assert.Equal(["pages-marked-p002.png"], Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().ToArray());
    }

    // Starts an export whose write waits until its token is canceled; returns the run, the signal of the cancel and the job card.
    private static async Task<(Task Run, Task Canceled, Dialog Job)> StartBlockedExportAsync(WindowFixture fixture)
    {
        var folder = Path.Combine(fixture.Folder, "images");
        Directory.CreateDirectory(folder);
        fixture.Window.FolderPicker = () => Task.FromResult<string?>(folder);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Window.ExportFlow.Exporter = async (_, _, _, _, _, _, _, _, _, token) =>
        {
            token.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return [];
        };
        var run = fixture.Window.ExportImagesAsync();
        var card = await CardWithAsync(fixture, "PART_Range");
        Click(card, "PART_ChooseFolder");
        await PrintHost.UntilAsync(() => PrintHost.Find<Button>(card, "PART_Export").IsEnabled);
        Click(card, "PART_Export");
        while (!started.Task.IsCompleted)
        {
            PrintHost.Pump();
            await Task.Delay(10, Ct);
        }

        return (run, canceled.Task, fixture.Window.Host.ActiveDialog!);
    }

    [AvaloniaFact]
    public async Task ExportIsInTheBusyListsSaveAndPrintAreRefusedAndTheWindowClosingCancelsIt()
    {
        using var fixture = WindowFixture.Create();
        await OpenReadyAsync(fixture);
        Assert.True(Command(fixture.Window, "file.saveAs").IsEnabled);
        var (_, canceled, job) = await StartBlockedExportAsync(fixture);

        // Save, print and the other file commands are refused while it runs; the same command does nothing (no second card).
        Assert.True(fixture.Window.ExportFlow.IsRunning);
        Assert.False(Command(fixture.Window, "file.saveAs").IsEnabled);
        Assert.False(Command(fixture.Window, "file.print").IsEnabled);
        Assert.False(Command(fixture.Window, "file.save").IsEnabled);
        await fixture.Window.ExportImagesAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Same(job, fixture.Window.Host.ActiveDialog);

        fixture.Window.Close();
        await canceled.WaitAsync(TimeSpan.FromSeconds(20), Ct);
    }

    [AvaloniaFact]
    public async Task ExtractSplitAndApplyAreRefusedWhileAnExportRuns()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var (export, canceled, job) = await StartBlockedExportAsync(fixture);

        // An apply needs an open plan with a change; the plan is made while the export runs (the export started without one).
        tab.Organize.Enter();
        tab.Organize.Selection.Set(tab.Session.Document!.PageCount, [0]);
        Assert.True(tab.Organize.Rotate(90).IsDone);

        // Each is refused with the busy reason; the notices wait behind the job card.
        var extract = fixture.Window.Output.ExtractAsync(tab);
        var split = fixture.Window.Output.SplitAsync(tab);
        var apply = fixture.Window.Applying.RunAsync(tab);
        Assert.False(fixture.Window.Output.IsRunning);
        Assert.False(fixture.Window.Applying.IsRunning);

        Click(job, "PART_Cancel");
        await canceled.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        var busy = 0;
        for (var i = 0; i < 4; i++)
        {
            Dialog card;
            try
            {
                card = await CardWithAsync(fixture, "PART_Ok");
            }
            catch (TimeoutException)
            {
                var active = fixture.Window.Host.ActiveDialog;
                throw new TimeoutException($"i={i} busy={busy} active={(active is null ? "none" : active.Title + ": " + string.Join(" / ", active.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)))}");
            }

            if (card.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == PdfStrings.FileJobBusy))
            {
                busy++;
            }

            Click(card, "PART_Ok");
            await PrintHost.UntilAsync(() => !ReferenceEquals(fixture.Window.Host.ActiveDialog, card));
        }

        Assert.Equal(3, busy);
        Assert.False(await extract.WaitAsync(TimeSpan.FromSeconds(20), Ct));
        Assert.False(await split.WaitAsync(TimeSpan.FromSeconds(20), Ct));
        Assert.False(await apply.WaitAsync(TimeSpan.FromSeconds(20), Ct));
        await export.WaitAsync(TimeSpan.FromSeconds(20), Ct);
    }

    // A value typed into a field and not yet written is a mark: the flows end the edit first (as Print and Apply do), so the lock for unsaved marks sees it
    // and says "Save the marks first" instead of cutting files that miss the value.
    [AvaloniaFact]
    public async Task OpenFieldEditIsEndedFirstAndThenCountsAsAnUnsavedMark()
    {
        using var f = await Forms.FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        Forms.FormEditFixture.SetText(box, "Erika");
        Assert.True(f.Interaction.IsEditing);

        var run = f.Window.Output.ExtractAsync(f.Tab);
        var notice = await CardWithAsync(f.Windows, "PART_Message");
        Assert.Equal(PdfStrings.SaveMarksFirstReason, PrintHost.Find<TextBlock>(notice, "PART_Message").Text);
        Click(notice, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.False(f.Interaction.IsEditing);
        Assert.True(f.Tab.Annotations.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task ClosingTheTabDuringTheExportCancelsItAndTheCardKeepsWhatWasWritten()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var (run, canceled, _) = await StartBlockedExportAsync(fixture);

        var close = fixture.Window.CloseActiveTabAsync();
        await canceled.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        await FinishAsync(fixture, run);
        await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.DoesNotContain(tab, fixture.Window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task ExportAndExtractRefuseEachOther()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var (extract, _, extractCanceled) = await StartBlockedExtractAsync(fixture);

        // The export flow refuses with the busy reason while the extract runs.
        var asked = fixture.Window.ExportFlow.ExportAsync(tab);
        Assert.False(fixture.Window.ExportFlow.IsRunning);

        // The notice waits behind the job card of the extract; cancel the extract from it.
        var job = await CardWithAsync(fixture, "PART_Cancel");
        Click(job, "PART_Cancel");
        await extractCanceled.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        var notice = await CardWithAsync(fixture, "PART_Message");
        Assert.Contains(PdfStrings.FileJobBusy, PrintHost.Find<TextBlock>(notice, "PART_Message").Text);
        Click(notice, "PART_Ok");
        Assert.False(await asked.WaitAsync(TimeSpan.FromSeconds(20), Ct));
        await FinishAsync(fixture, extract);
        Assert.False(fixture.Window.ExportFlow.IsRunning);
    }
}
