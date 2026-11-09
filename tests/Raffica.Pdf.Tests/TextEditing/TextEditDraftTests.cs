using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Viewing;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>The open draft: an unresolved edit of the tab that is written before save, print and close, and that undo and redo end instead of touching the history.</summary>
public sealed class TextEditDraftTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task DraftCountsAsUnresolvedAndFlushesBeforeSavePrintAndClose()
    {
        // The count: an untouched field is no change; a changed text is an unresolved edit and the tab counts as changed; the text as it was is none again.
        using (var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf"))
        {
            await rig.EnterModeAsync();
            var run = await rig.RunAsync("Quarterly report 2026");
            var field = rig.OpenField(run);
            Assert.False(rig.Tab.IsDirty);
            Assert.False(rig.Tab.Annotations.HasUnresolved);
            TextEditRig.Type(field, "Quarterly report 2027");
            Assert.True(rig.Tab.Annotations.HasUnresolved);
            Assert.True(rig.Tab.IsDirty);
            Assert.False(rig.Tab.Annotations.Editor.IsDirty, "nothing is written by typing");
            TextEditRig.Type(field, "Quarterly report 2026");
            Assert.False(rig.Tab.Annotations.HasUnresolved);
            Assert.False(rig.Tab.IsDirty);

            // Save: the draft is written first, so the file has the text that was typed.
            TextEditRig.Type(field, "Quarterly report 2028");
            Assert.True(await rig.Window.SaveActiveAsync());
            await rig.SettleAsync();
            Assert.False(rig.Session.HasDraft);
            Assert.False(rig.Tab.IsDirty);
            using var reopened = new ViewerSession(new PdfEngine());
            await reopened.OpenAsync(rig.Path, null);
            Assert.Contains(await reopened.Document!.GetTextRunsAsync(0, Ct), r => r.Text.TrimEnd() == "Quarterly report 2028");
        }

        // Print: the draft is written before the job starts (the print path flushes the annotation session).
        if (OperatingSystem.IsWindows())
        {
            var service = new FakePrintService { DialogResult = new PrintTicket(new PrintSettings { Printer = "Office" }) };
            using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf", service);
            await rig.EnterModeAsync();
            var run = await rig.RunAsync("Quarterly report 2026");
            TextEditRig.Type(rig.OpenField(run), "Quarterly report 2029");
            Assert.False(rig.Tab.Annotations.Editor.HasTextEdits);

            rig.Command("file.print").Run!();
            await PrintHost.UntilAsync(() => service.Jobs.Count == 1);
            Assert.True(rig.Tab.Annotations.Editor.HasTextEdits);
            Assert.False(rig.Session.HasDraft);
            Assert.Contains("Quarterly report 2029", await rig.PageTextAsync(), StringComparison.Ordinal);
            await PrintHost.UntilAsync(() => service.Printed.Count == 1 && !rig.Window.Host.IsOpen);
        }
    }

    [AvaloniaFact]
    public async Task FlushOnTheUiThreadRunsSynchronouslyWhateverContextIsCurrent()
    {
        // Avalonia runs each job under a synchronization context of its own priority, so the context current on the UI thread is often not the one the session
        // captured. The flush must still end the draft before FlushAsync returns its task (Save and Print inspect the pending drafts right after it).
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        TextEditRig.Type(rig.OpenField(run), "Quarterly report 2031");
        Assert.True(rig.Session.HasDraft);

        Task flush;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ForwardingContext());
        try
        {
            flush = rig.Tab.Annotations.FlushAsync();

            // Written on the spot: the commit holds a pending handle already, so the flush has to wait for it. A posted flush would return a finished task.
            Assert.False(flush.IsCompleted, "the draft was committed on the spot, not posted");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await flush;
        await rig.SettleAsync();
        Assert.True(rig.Tab.Annotations.Editor.HasTextEdits);
    }

    // Another context than the one the session captured; it hands the continuations of FlushAsync back to the UI thread.
    private sealed class ForwardingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => Avalonia.Threading.Dispatcher.UIThread.Post(() => d(state));
    }

    [AvaloniaFact]
    public async Task CloseAsksAndFlushes()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        TextEditRig.Type(rig.OpenField(run), "Quarterly report 2030");
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);

        // The close writes the draft first (the flush of the guard), then the tab is dirty and the generic question comes.
        var closing = rig.Window.Workspace.CloseAsync(rig.Tab);
        await ViewFixture.WaitAsync(() => rig.Window.Host.ActiveDialog is not null);
        Assert.True(rig.Tab.Annotations.Editor.HasTextEdits);
        Assert.False(rig.Session.HasDraft);
        var footer = Assert.IsType<StackPanel>(rig.Window.Host.ActiveDialog!.Footer);
        Assert.Contains(footer.Children.OfType<Button>(), b => b.Name == "PART_DontSave");

        // Cancel keeps the tab with its edit.
        Click(footer, "PART_Cancel");
        Assert.False(await closing);
        Assert.Contains("Quarterly report 2030", await rig.PageTextAsync(), StringComparison.Ordinal);

        // Save closes it, and the file has the line.
        closing = rig.Window.Workspace.CloseAsync(rig.Tab);
        await ViewFixture.WaitAsync(() => rig.Window.Host.ActiveDialog is not null);
        Click(Assert.IsType<StackPanel>(rig.Window.Host.ActiveDialog!.Footer), "PART_Save");
        Assert.True(await closing);
        using var reopened = new ViewerSession(new PdfEngine());
        await reopened.OpenAsync(rig.Path, null);
        Assert.Contains(await reopened.Document!.GetTextRunsAsync(0, Ct), r => r.Text.TrimEnd() == "Quarterly report 2030");
    }

    [AvaloniaFact]
    public async Task ARefusedDraftKeepsTheTabOpenAndIsNeverLostWithTheClose()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        TextEditRig.Type(rig.OpenField(run), "Quarterly → 2026");

        var closing = rig.Window.Workspace.CloseAsync(rig.Tab);
        await ViewFixture.WaitAsync(() => rig.Window.Host.ActiveDialog is not null);
        Assert.True(rig.Session.HasDraft, "the refused text stays in the field");
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);

        // Save cannot write it: the tab stays open with the draft instead of closing and losing the text.
        Click(Assert.IsType<StackPanel>(rig.Window.Host.ActiveDialog!.Footer), "PART_Save");
        Assert.False(await closing);
        Assert.True(rig.Session.HasDraft);
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task UndoAndRedoEndTheDraftInsteadOfTouchingTheHistory()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var first = await rig.RunAsync("Quarterly report 2026");
        TextEditRig.Type(rig.OpenField(first), "Quarterly report 2031");
        Assert.Equal(TextEditCommitKind.Applied, (await rig.Session.CommitAsync()).Kind);
        await rig.SettleAsync();
        await ViewFixture.WaitAsync(() => rig.Session.IsLoaded(0));

        // With a draft open an undo ends the draft (like Esc); the edit already written stays.
        var second = await rig.RunAsync("Dear customer, thank you");
        TextEditRig.Type(rig.OpenField(second), "Dear customer, thanks");
        await rig.Tab.Annotations.UndoAsync();
        Assert.False(rig.Session.HasDraft);
        Assert.False(rig.Tab.Annotations.HasUnresolved);
        Assert.Contains("Quarterly report 2031", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.Contains("Dear customer, thank you", await rig.PageTextAsync(), StringComparison.Ordinal);

        // Without a draft the undo takes the written edit back.
        await rig.Tab.Annotations.UndoAsync();
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
    }

    private static void Click(StackPanel footer, string name)
    {
        footer.Children.OfType<Button>().Single(b => b.Name == name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
    }
}
