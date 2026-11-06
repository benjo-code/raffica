using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// A running save (the incremental one is not modal, its job card sits in the print slot) holds the document: the page commands wait for it, with a reason,
/// and the flows refuse with the busy notice instead of starting under it (a save that adopts the written file closes the document the flow reads).
/// </summary>
public sealed class SaveLockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<Dialog> CardWithAsync(WindowFixture fixture, string name)
    {
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name));
        return fixture.Window.Host.ActiveDialog!;
    }

    // Reads the busy notice, closes it.
    private static async Task ExpectBusyNoticeAsync(WindowFixture fixture, Task<bool> run)
    {
        var notice = await CardWithAsync(fixture, "PART_Message");
        Assert.Equal(PdfStrings.FileJobBusy, PrintHost.Find<TextBlock>(notice, "PART_Message").Text);
        PrintHost.Find<Button>(notice, "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(20), Ct));
    }

    private static async Task ExpectBusyNoticeAsync(WindowFixture fixture, Task run)
    {
        var notice = await CardWithAsync(fixture, "PART_Message");
        Assert.Equal(PdfStrings.FileJobBusy, PrintHost.Find<TextBlock>(notice, "PART_Message").Text);
        PrintHost.Find<Button>(notice, "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await run.WaitAsync(TimeSpan.FromSeconds(20), Ct);
    }

    private static (WindowFixture Fixture, TaskCompletionSource Started, TaskCompletionSource Gate) CreateWindow()
    {
        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        var fixture = WindowFixture.Create(
            saveRunner: async (_, _, _, _, token) =>
            {
                started.TrySetResult();
                await gate.Task.WaitAsync(token);
                throw new OperationCanceledException();
            },
            savePicker: _ => Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), "raffica-savelock-never-written.pdf")));
        return (fixture, started, gate);
    }

    private static async Task<PdfTab> OpenAsync(WindowFixture fixture)
    {
        var tab = await fixture.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        return tab;
    }

    [AvaloniaFact]
    public async Task OutputCommandsAndTheModeWaitForARunningSaveAs()
    {
        var (fixture, started, gate) = CreateWindow();
        using (fixture)
        {
            var window = fixture.Window;
            var tab = await OpenAsync(fixture);
            string[] ids = [PdfCommandIds.ExtractPages, PdfCommandIds.SplitDocument, PdfCommandIds.ExportImages, PdfCommandIds.Organize];
            Assert.All(ids, id => Assert.True(Command(window, id).IsEnabled, id));

            // Save as of a clean document: no mark is unsaved, so only the running save locks the commands.
            var saving = window.SaveActiveAsAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
            ViewFixture.Pump();
            Assert.True(window.Saves.IsSaving);
            Assert.All(ids, id =>
            {
                Assert.False(Command(window, id).IsEnabled, id);
                Assert.Equal(PdfStrings.SaveBusy, Command(window, id).DisabledReason?.Invoke());
            });

            // The mode does not start by a path around the command either.
            window.ToggleOrganize();
            ViewFixture.Pump();
            Assert.True(tab.OrganizeIfCreated is null or { IsActive: false });

            // The flows refuse with the busy notice (the commands are guarded by the rules, a path around them is guarded by the flows).
            await ExpectBusyNoticeAsync(fixture, window.Output.ExtractAsync(tab));
            await ExpectBusyNoticeAsync(fixture, window.Output.SplitAsync(tab));
            await ExpectBusyNoticeAsync(fixture, window.ExportFlow.ExportAsync(tab));
            Assert.False(window.Output.IsRunning);
            Assert.False(window.ExportFlow.IsRunning);

            gate.SetResult();
            Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(20), Ct));
            ViewFixture.Pump();
            Assert.All(ids, id => Assert.True(Command(window, id).IsEnabled, id));
        }
    }

    [AvaloniaFact]
    public async Task ApplyWaitsForARunningSaveAndSaysSo()
    {
        var (fixture, started, gate) = CreateWindow();
        using (fixture)
        {
            var window = fixture.Window;
            var tab = await OpenAsync(fixture);

            // Marks unsaved before the mode; the plan changes; a save of the marks (the first step of an apply, or a close question) runs.
            Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
            await tab.Annotations.FlushAsync();
            window.ToggleOrganize();
            await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
            tab.Organize.Selection.Select(1);
            Assert.True(tab.Organize.Rotate(90).IsDone);
            ViewFixture.Pump();
            Assert.True(Command(window, PdfCommandIds.ApplyPages).IsEnabled);

            var saving = window.Saves.SaveMarksAsync(tab);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
            ViewFixture.Pump();

            // The command waits, with the reason; the flow, reached around it, says the busy sentence (not the one about an open edit).
            Assert.False(Command(window, PdfCommandIds.ApplyPages).IsEnabled);
            Assert.Equal(PdfStrings.SaveBusy, Command(window, PdfCommandIds.ApplyPages).DisabledReason?.Invoke());
            await ExpectBusyNoticeAsync(fixture, window.Applying.RunAsync(tab));
            Assert.False(window.Applying.IsRunning);
            Assert.True(tab.Organize.IsActive);
            Assert.False(tab.Organize.Plan.IsIdentity);

            gate.SetResult();
            Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(20), Ct));
            ViewFixture.Pump();
            Assert.True(Command(window, PdfCommandIds.ApplyPages).IsEnabled);
        }
    }
    [AvaloniaFact]
    public async Task SaveWithRedactionPlanOpensTheApplyCard()
    {
        var (fixture, started, _) = CreateWindow();
        using (fixture)
        {
            var window = fixture.Window;
            var tab = await OpenAsync(fixture);
            Assert.False(Command(window, PdfCommandIds.Save).IsEnabled);

            window.ToggleRedact();
            await ViewFixture.WaitAsync(() => tab.RedactIfCreated is { IsActive: true });
            Assert.True(tab.Redact.AddArea(0, new Raffica.Core.Text.PageRect(50, 100, 150, 120)).IsDone);
            ViewFixture.Pump();

            // Marked redactions are no change of the file, but Save and Save as hand over to the redaction Apply: the incremental save never writes them.
            Assert.True(Command(window, PdfCommandIds.Save).IsEnabled);
            var save = window.SaveActiveAsync();
            var card = await CardWithAsync(fixture, "PART_Original");
            Assert.Equal("Apply redactions", card.Title);
            Assert.Equal("Save in the original file", PrintHost.Find<Button>(card, "PART_Original").Content);
            PrintHost.Find<Button>(card, "PART_Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await save.WaitAsync(TimeSpan.FromSeconds(20), Ct));

            // Save as offers only Save as.
            var saveAs = window.SaveActiveAsAsync();
            card = await CardWithAsync(fixture, "PART_SaveAs");
            Assert.Equal("Apply redactions", card.Title);
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<Control>(), c => c.Name == "PART_Original");
            PrintHost.Find<Button>(card, "PART_Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await saveAs.WaitAsync(TimeSpan.FromSeconds(20), Ct));

            Assert.False(started.Task.IsCompleted, "the incremental save did not run");
            Assert.True(tab.RedactIfCreated is { IsActive: true });
            Assert.False(tab.Redact.Plan.IsEmpty);
        }
    }

    [AvaloniaFact]
    public async Task RedactApplyWaitsForARunningSaveAndSaysSo()
    {
        var (fixture, started, gate) = CreateWindow();
        using (fixture)
        {
            var window = fixture.Window;
            var tab = await OpenAsync(fixture);

            // Marks unsaved before the mode; redactions are marked; a save of the marks (the first step of an apply, or a close question) runs.
            Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
            await tab.Annotations.FlushAsync();
            window.ToggleRedact();
            await ViewFixture.WaitAsync(() => tab.RedactIfCreated is { IsActive: true });
            Assert.True(tab.Redact.AddArea(0, new Raffica.Core.Text.PageRect(50, 100, 150, 120)).IsDone);
            ViewFixture.Pump();
            Assert.True(Command(window, "redact.apply").IsEnabled);

            var saving = window.Saves.SaveMarksAsync(tab);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
            ViewFixture.Pump();

            // The command waits, with the reason; the flow, reached around it, says the busy sentence.
            Assert.False(Command(window, "redact.apply").IsEnabled);
            Assert.Equal(PdfStrings.SaveBusy, Command(window, "redact.apply").DisabledReason?.Invoke());
            await ExpectBusyNoticeAsync(fixture, window.Redacting.RunAsync(tab));
            Assert.False(window.Redacting.IsRunning);
            Assert.True(tab.Redact.IsActive);
            Assert.False(tab.Redact.Plan.IsEmpty);

            gate.SetResult();
            Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(20), Ct));
            ViewFixture.Pump();
            Assert.True(Command(window, "redact.apply").IsEnabled);
        }
    }
}
