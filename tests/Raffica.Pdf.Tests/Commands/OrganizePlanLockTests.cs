using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Forms;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>While the page plan is open the marks and the form values are left alone, so Undo and Redo belong to the plan alone (call 9).</summary>
public sealed class OrganizePlanLockTests
{
    private const string Reason = "Apply or discard the page changes first";

    private static void ToggleMode(PdfWindow window) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "pages.organize").Run!();

    [AvaloniaFact]
    public async Task ClickOnAFieldDoesNothingWhileThePlanIsOpen()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        var agree = f.Field("Agb");
        ToggleMode(f.Window);
        ViewFixture.Pump();
        Assert.True(f.Tab.Organize.IsActive);

        f.Click(name);
        f.Click(agree);
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.False(f.Tab.Annotations.IsDirty);
        Assert.False(f.Window.Host.IsOpen, "the reason is a hover text, not a card");
        Assert.NotEqual(true, (await f.FromDocumentAsync("Agb")).State.Checked);

        // The hover text names the reason, also for a document that allows filling.
        f.Window.MouseMove(f.PointOf(name));
        ViewFixture.Pump();
        Assert.Equal(Reason, ToolTip.GetTip(f.View));

        // Filling works again once the mode has ended.
        ToggleMode(f.Window);
        ViewFixture.Pump();
        Assert.False(f.Tab.Organize.IsActive);
        f.Click(name);
        Assert.True(f.Interaction.IsEditing);
    }

    [AvaloniaFact]
    public async Task FormKeysArePassiveWhileThePlanIsOpen()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();
        ToggleMode(f.Window);
        ViewFixture.Pump();
        Assert.True(f.Tab.Organize.IsActive);

        FormEditFixture.Press(f.View, Key.Tab);
        Assert.False(f.Parts.Keys.TookLastKey);
        Assert.Null(f.Parts.Focus.Current);

        // Back to normal after the mode: Tab enters the form.
        ToggleMode(f.Window);
        ViewFixture.Pump();
        f.View.Focus();
        FormEditFixture.Press(f.View, Key.Tab);
        Assert.True(f.Parts.Keys.TookLastKey);
    }

    [AvaloniaFact]
    public async Task OpenFieldEditIsEndedWhenTheModeStarts()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Erika");

        ToggleMode(f.Window);
        await ViewFixture.WaitAsync(() => f.Tab.Organize.IsActive);
        await f.SettleAsync();

        // The value was written first (one step of the marks), the editor is gone, and the plan owns Ctrl+Z from here.
        Assert.False(f.Interaction.IsEditing);
        Assert.Equal("Erika", (await f.FromDocumentAsync("Name")).State.Text);
        Assert.True(f.Tab.Annotations.CanUndo);
        f.Tab.Organize.Selection.Select(0);
        f.Window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        f.Window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        ViewFixture.Pump();
        Assert.True(f.Tab.Annotations.CanUndo, "the marks history is not touched while the plan is open");
        Assert.Equal("Erika", (await f.FromDocumentAsync("Name")).State.Text);
    }

    // The mode starts after an awaited flush; a tool chosen meanwhile (Ctrl+Shift+H) must be ended when the mode starts.
    [AvaloniaFact]
    public async Task ToolChosenDuringTheFlushDoesNotSurviveTheModeStart()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Erika");

        ToggleMode(f.Window);
        Assert.False(f.Tab.Organize.IsActive, "the mode waits for the flush");
        f.Window.Tools.Choose(AnnotateTool.Highlight);
        await ViewFixture.WaitAsync(() => f.Tab.Organize.IsActive);
        await f.SettleAsync();

        Assert.Equal(AnnotateTool.None, f.Window.Tools.Tool);
    }

    [AvaloniaFact]
    public async Task MarksPaneCommentIsLockedWhileThePlanIsOpen()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var tab = await windows.OpenAsync("text-rich.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(tab)!.FirstSharpPageShown);
        await tab.Annotations.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(90, 95, 310, 115)], new AnnotationColor(255, 255, 0), "first thought", null);
        window.ToggleMarksPane();
        ViewFixture.Pump();
        var pane = window.MarksOf(tab)!;
        await ViewFixture.WaitAsync(() => pane.RowCount > 0);
        pane.List.SelectedIndex = Enumerable.Range(0, pane.RowCount).First(i => pane.EntryAt(i) is not null);
        ViewFixture.Pump();
        Assert.True(pane.CommentEditor.IsVisible);
        Assert.False(pane.CommentEditor.IsReadOnly);

        ToggleMode(window);
        ViewFixture.Pump();
        Assert.True(tab.Organize.IsActive);
        Assert.True(pane.CommentEditor.IsReadOnly);

        // Typing is refused, a Ctrl+Enter applies nothing, and the history stays where it was.
        var undoBefore = tab.Annotations.CanUndo;
        pane.CommentEditor.Text = "second thought";
        await pane.ApplyCommentAsync();
        await pane.Completion;
        Assert.Equal("first thought", pane.EntryAt(pane.List.SelectedIndex)!.Annotation.Contents);
        Assert.Equal(undoBefore, tab.Annotations.CanUndo);

        // After the mode the comment is editable again.
        ToggleMode(window);
        ViewFixture.Pump();
        Assert.False(tab.Organize.IsActive);
        Assert.False(pane.CommentEditor.IsReadOnly);
    }

    [AvaloniaFact]
    public async Task ARefusedRibbonEditIsSaidInThePagesPaneEvenWhenTheOutlineIsShown()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var tab = await windows.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(tab)!.FirstSharpPageShown);
        ToggleMode(window);
        ViewFixture.Pump();

        // Delete every page but the last, then try to delete that one: the plan refuses it.
        var session = tab.Organize;
        session.Selection.Set(session.Plan.Slots.Count, Enumerable.Range(0, session.Plan.Slots.Count - 1));
        Command(window, "pages.delete").Run!();
        session.Selection.Select(0);
        window.ShowOutlinePane();
        ViewFixture.Pump();
        Assert.Equal(Raffica.Pdf.Panes.LeftPaneKind.Outline, window.LeftPane);

        Command(window, "pages.delete").Run!();
        ViewFixture.Pump();

        Assert.Equal(Raffica.Pdf.Panes.LeftPaneKind.Pages, window.LeftPane);
        Assert.Single(session.Plan.Slots);
    }

    private static Raffica.Shell.Frame.CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    [AvaloniaFact]
    public async Task ABlankPageIgnoresTheExtraRotationOfTheNeighbour()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var tab = await windows.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(tab)!.FirstSharpPageShown);
        ToggleMode(window);
        var session = tab.Organize;
        session.Selection.Select(2);
        Command(window, "pages.rotateRight").Run!();
        Command(window, "pages.blank").Run!();
        ViewFixture.Pump();

        var blank = session.Plan.Slots[3].Blank;
        var size = window.ViewOf(tab)!.PageSizeOf(2);
        Assert.NotNull(blank);
        Assert.Equal(size!.Value.Width, blank.Width, 3);
        Assert.Equal(size.Value.Height, blank.Height, 3);
    }
}
