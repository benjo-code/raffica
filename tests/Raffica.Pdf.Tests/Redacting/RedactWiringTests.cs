using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactWiringTests
{
    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<PdfTab> OpenReadyAsync(WindowFixture fixture, string file = "pages-marked.pdf")
    {
        var tab = await fixture.OpenAsync(file);
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        return tab;
    }

    private static bool Has(Dialog dialog, string name) => dialog.GetVisualDescendants().OfType<Button>().Any(b => b.Name == name);

    private static async Task<Dialog> CardWithAsync(WindowFixture fixture, string name)
    {
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is { } d && Has(d, name));
        return fixture.Window.Host.ActiveDialog!;
    }

    private static async Task PressAsync(WindowFixture fixture, string name)
    {
        var card = await CardWithAsync(fixture, name);
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PrintHost.Pump();
    }

    private static void Mark(RedactSession session, int page, double top) =>
        Assert.True(session.AddArea(page, new PageRect(50, top, 150, top + 20)).IsDone);

    [AvaloniaFact]
    public async Task ModeShowsPaneAndCounter()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        var window = f.Window;
        Assert.False(window.CommentsPane.IsOpen);

        Command(window, "redact.mode").Run!();
        ViewFixture.Pump();

        var pane = Assert.IsType<RedactPane>(window.CommentsPane.Content);
        Assert.True(window.CommentsPane.IsOpen);
        Assert.Equal("Redact", window.CommentsPane.Title);
        Assert.Equal("Nothing is removed until you apply the marks.", pane.HintText);
        Assert.Equal("No marks yet", pane.CounterText);
        Assert.False(pane.ApplyButton.IsVisible, "Apply… appears with the first mark");

        Mark(tab.Redact, 0, 100);
        Mark(tab.Redact, 0, 200);
        Mark(tab.Redact, 1, 100);
        Mark(tab.Redact, 1, 200);
        ViewFixture.Pump();

        Assert.Equal("4 marks on 2 pages", pane.CounterText);
        Assert.True(pane.ApplyButton.IsVisible);
        Assert.Equal("Apply…", pane.ApplyButton.Content);

        tab.Redact.Selection.Set(tab.Redact.Plan.Marks.Take(1).Select(m => m.Id));
        tab.Redact.Delete(tab.Redact.Selection.Ids);
        ViewFixture.Pump();
        Assert.Equal("3 marks on 2 pages", pane.CounterText);
    }

    [AvaloniaFact]
    public async Task CounterSaysOneMarkOnOnePage()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        Mark(tab.Redact, 0, 100);
        ViewFixture.Pump();

        Assert.Equal("1 mark on 1 page", Assert.IsType<RedactPane>(f.Window.CommentsPane.Content).CounterText);
    }

    [AvaloniaFact]
    public async Task ListIsGroupedByPageAndNamesNoTerm()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        Mark(tab.Redact, 2, 100);
        Mark(tab.Redact, 0, 100);
        ViewFixture.Pump();
        var pane = Assert.IsType<RedactPane>(f.Window.CommentsPane.Content);

        // Page 1 first, then its mark, then page 3 and its mark.
        Assert.Equal(["Page 1", "Mark for redaction, page 1", "Page 3", "Mark for redaction, page 3"], pane.RowNames);
    }

    [AvaloniaFact]
    public async Task ClickingARowSelectsTheMarkAndDeleteRemovesIt()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        Mark(tab.Redact, 0, 100);
        Mark(tab.Redact, 0, 200);
        tab.Redact.Selection.Clear();
        ViewFixture.Pump();
        var pane = Assert.IsType<RedactPane>(f.Window.CommentsPane.Content);
        var second = tab.Redact.Plan.Marks[1];

        pane.Choose(second.Id);
        Assert.Equal(second.Id, tab.Redact.Selection.Single);

        pane.DeleteSelected();
        ViewFixture.Pump();
        Assert.Single(tab.Redact.Plan.Marks);
        Assert.DoesNotContain(tab.Redact.Plan.Marks, m => m.Id == second.Id);
    }

    [AvaloniaFact]
    public async Task TheRibbonCommandsOfTheModeAreWired()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        Mark(tab.Redact, 0, 100);
        Mark(tab.Redact, 0, 200);
        ViewFixture.Pump();

        // Undo and Redo of the Home tab belong to the marks while the mode is on.
        Assert.True(Command(f.Window, "edit.undo").IsEnabled);
        Command(f.Window, "edit.undo").Run!();
        await ViewFixture.WaitAsync(() => tab.Redact.Plan.Marks.Count == 1);
        Assert.True(Command(f.Window, "edit.redo").IsEnabled);
        Command(f.Window, "edit.redo").Run!();
        await ViewFixture.WaitAsync(() => tab.Redact.Plan.Marks.Count == 2);

        // Delete marks removes the selected ones.
        tab.Redact.Selection.Set([tab.Redact.Plan.Marks[0].Id]);
        Command(f.Window, "redact.deleteMarks").Run!();
        ViewFixture.Pump();
        Assert.Single(tab.Redact.Plan.Marks);

        // Mark selection with no text selected says why in the pane; nothing is marked.
        Command(f.Window, "redact.markSelection").Run!();
        await ViewFixture.WaitAsync(() => !string.IsNullOrEmpty(Assert.IsType<RedactPane>(f.Window.CommentsPane.Content).NoteText));
        Assert.Equal("Select some text first.", Assert.IsType<RedactPane>(f.Window.CommentsPane.Content).NoteText);
        Assert.Single(tab.Redact.Plan.Marks);
    }

    [AvaloniaFact]
    public async Task LeavingWithMarksAsksAndDiscardDropsThemCancelKeepsThem()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        Mark(tab.Redact, 0, 100);
        ViewFixture.Pump();

        Command(f.Window, "redact.mode").Run!();
        var card = await CardWithAsync(f, "PART_Discard");
        Assert.Equal("Discard the redactions?", card.Title);
        await PressAsync(f, "PART_Cancel");
        Assert.True(tab.Redact.IsActive);
        Assert.Single(tab.Redact.Plan.Marks);

        Command(f.Window, "redact.discard").Run!();
        await PressAsync(f, "PART_Discard");
        await ViewFixture.WaitAsync(() => !tab.Redact.IsActive);
        Assert.True(tab.Redact.Plan.IsEmpty);
        Assert.False(f.Window.CommentsPane.IsOpen);
        Assert.False(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task ClosingTheTabWithMarksAsksAndCancelKeepsTheTab()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        Mark(tab.Redact, 0, 100);
        ViewFixture.Pump();

        var close = f.Window.CloseActiveTabAsync();
        await CardWithAsync(f, "PART_Discard");
        await PressAsync(f, "PART_Cancel");
        await close;
        Assert.Contains(tab, f.Window.Workspace.Tabs);
        Assert.True(tab.Redact.IsActive);

        close = f.Window.CloseActiveTabAsync();
        await CardWithAsync(f, "PART_Discard");
        await PressAsync(f, "PART_Discard");
        await close;
        Assert.DoesNotContain(tab, f.Window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task AnEmptyPlanLeavesAndClosesWithoutAsking()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.True(tab.Redact.IsActive);

        await f.Window.CloseActiveTabAsync();

        Assert.False(f.Window.Host.IsOpen);
        Assert.DoesNotContain(tab, f.Window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task EscFromThePageViewAsksToLeaveWhenNothingIsLeftToDrop()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        Mark(tab.Redact, 0, 100);
        ViewFixture.Pump();
        var view = f.Window.ViewOf(tab)!;
        view.Focus();

        void Esc() => view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = view });

        Esc();
        Assert.Equal(0, tab.Redact.Selection.Count);
        Assert.False(f.Window.Host.IsOpen);

        Esc();
        await CardWithAsync(f, "PART_Discard");
        await PressAsync(f, "PART_Cancel");
        Assert.True(tab.Redact.IsActive);
    }

    [AvaloniaFact]
    public async Task ModeBadgeNamesTheModeAndOffersBothTools()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Command(f.Window, "redact.mode").Run!();
        ViewFixture.Pump();

        var badge = f.Window.RedactBadgeOf(tab)!;
        Assert.True(badge.IsVisible);
        Assert.Equal("Redact – Esc to finish", badge.Message);
        Assert.Equal(RedactTool.Area, tab.Redact.Tool);
        badge.ChooseText();
        Assert.Equal(RedactTool.Text, tab.Redact.Tool);

        Command(f.Window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.Null(f.Window.RedactBadgeOf(tab));
        Assert.DoesNotContain(badge, f.Window.BadgeHostOf(tab)!.Children);
    }

    [AvaloniaFact]
    public async Task NoSessionIsMadeUntilTheModeStarts()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        Assert.Null(tab.RedactIfCreated);
        _ = Command(f.Window, "redact.mode").IsEnabled;
        Assert.Null(tab.RedactIfCreated);

        Command(f.Window, "redact.mode").Run!();
        Assert.NotNull(tab.RedactIfCreated);
    }
}
