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
}
