using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>The commands of the Pages tab: the rules, the locks while a plan is open and the wiring in the window.</summary>
public sealed class OrganizeCommandTests
{
    private static readonly string[] EditIds = ["pages.rotateLeft", "pages.rotateRight", "pages.duplicate", "pages.moveUp", "pages.moveDown", "pages.delete"];
    private static readonly string[] OutputIds = ["pages.extract", "pages.split", "pages.exportImages"];
    private static readonly string[] ToolIds = ["annotate.mode", "annotate.highlight", "annotate.freehand", "annotate.textBox", "annotate.sign"];
    private static readonly string[] FormIds = ["forms.next", "forms.previous", "forms.nextRequired", "forms.highlight"];

    private static PdfCommandRules Rules(
        bool document = true,
        bool ready = true,
        bool active = false,
        bool selection = false,
        bool changed = false,
        bool canUndo = false,
        bool canRedo = false,
        bool marks = false) =>
        new(
            () => document, () => 1, () => new PdfPrintInfo(false, true),
            hasForm: () => true,
            pagesReady: () => ready,
            organizeActive: () => active,
            pageSelection: () => selection,
            planCanUndo: () => canUndo,
            planCanRedo: () => canRedo,
            planChanged: () => changed,
            marksUnsaved: () => marks);

    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<PdfTab> OpenReadyAsync(WindowFixture fixture, string file = "pages-marked.pdf")
    {
        var tab = await fixture.OpenAsync(file);
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        return tab;
    }

    [Fact]
    public void EditCommandsNeedTheModeAndASelection()
    {
        foreach (var id in EditIds)
        {
            Assert.False(Rules().Allows(id), id + " without the mode");
            Assert.False(Rules(selection: true).Allows(id), id + " with a selection but without the mode");
            Assert.False(Rules(active: true).Allows(id), id + " without a selection");
            Assert.True(Rules(active: true, selection: true).Allows(id), id);
        }

        // The blank page and the insertion need only the mode.
        foreach (var id in new[] { "pages.blank", "pages.insertFile" })
        {
            Assert.False(Rules().Allows(id), id);
            Assert.True(Rules(active: true).Allows(id), id);
        }

        // The mode itself needs a document with a first sharp page, and stays available to leave it.
        Assert.True(Rules().Allows("pages.organize"));
        Assert.False(Rules(ready: false).Allows("pages.organize"));
        Assert.True(Rules(ready: false, active: true).Allows("pages.organize"));
        Assert.False(Rules(document: false).Allows("pages.organize"));

        // Undo and redo of the plan follow its history.
        Assert.False(Rules(active: true).Allows("pages.undo"));
        Assert.True(Rules(active: true, canUndo: true).Allows("pages.undo"));
        Assert.False(Rules(canUndo: true).Allows("pages.undo"));
        Assert.True(Rules(active: true, canRedo: true).Allows("pages.redo"));
    }

    [Fact]
    public void ApplyNeedsANonIdentityPlan()
    {
        Assert.False(Rules().Allows("pages.apply"));
        Assert.False(Rules(active: true).Allows("pages.apply"));
        Assert.True(Rules(active: true, changed: true).Allows("pages.apply"));

        // Discard needs an open plan, changed or not (the mode is open; discarding an unchanged plan just leaves it).
        Assert.False(Rules().Allows("pages.discard"));
        Assert.True(Rules(active: true).Allows("pages.discard"));
    }

    [Fact]
    public void OutputCommandsAreDisabledWithAReasonWhileAPlanIsOpen()
    {
        foreach (var id in OutputIds)
        {
            Assert.True(Rules().Allows(id), id);
            Assert.Null(Rules().ReasonFor(id));

            var open = Rules(active: true);
            Assert.False(open.Allows(id), id);
            Assert.Equal("Apply or discard the page changes first", open.ReasonFor(id));

            var unsaved = Rules(marks: true);
            Assert.False(unsaved.Allows(id), id);
            Assert.Equal("Save the marks first", unsaved.ReasonFor(id));

            // Both: the plan is named first (the marks are locked and clean while it is open, so this is only the order of the questions).
            Assert.Equal("Apply or discard the page changes first", Rules(active: true, marks: true).ReasonFor(id));
            Assert.False(Rules(document: false).Allows(id));
        }

        Assert.Equal("Wenden Sie die Seitenänderungen zuerst an oder verwerfen Sie sie", PdfStrings.GetIn("PageChangesLockedReason", System.Globalization.CultureInfo.GetCultureInfo("de")));
        Assert.Equal("Speichern Sie zuerst die Markierungen", PdfStrings.GetIn("SaveMarksFirstReason", System.Globalization.CultureInfo.GetCultureInfo("de")));
    }

    [Fact]
    public void AnnotateToolsSignAndFormsAreLockedWhileAPlanIsOpen()
    {
        foreach (var id in ToolIds.Concat(FormIds))
        {
            Assert.True(Rules().Allows(id) || id == "forms.nextRequired", id);
            var open = Rules(active: true);
            Assert.False(open.Allows(id), id);
            Assert.Equal("Apply or discard the page changes first", open.ReasonFor(id));
        }

        // The other commands keep their rules.
        Assert.True(Rules(active: true).Allows("zoom.in"));
        Assert.Null(Rules(active: true).ReasonFor("zoom.in"));
    }

    [AvaloniaFact]
    public async Task EnteringTheModeOpensThePagesPane()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var window = fixture.Window;
        window.ShowOutlinePane();
        Assert.Equal(LeftPaneKind.Outline, window.LeftPane);

        var organize = Command(window, "pages.organize");
        Assert.True(organize.IsEnabled);
        organize.Run!();
        ViewFixture.Pump();

        Assert.True(tab.Organize.IsActive);
        Assert.Equal(LeftPaneKind.Pages, window.LeftPane);

        // An unchanged plan is left by the same command; the pane stays open.
        organize.Run!();
        ViewFixture.Pump();
        Assert.False(tab.Organize.IsActive);
        Assert.Equal(LeftPaneKind.Pages, window.LeftPane);
    }

    [AvaloniaFact]
    public async Task ALeaveWithAChangedPlanKeepsTheMode()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var window = fixture.Window;
        Command(window, "pages.organize").Run!();
        tab.Organize.Selection.Select(1);
        Command(window, "pages.rotateRight").Run!();
        ViewFixture.Pump();
        Assert.False(tab.Organize.Plan.IsIdentity);

        // Leaving asks (the answers are tested in OrganizeCloseTests); Cancel keeps the mode.
        Command(window, "pages.organize").Run!();
        await PrintHost.UntilAsync(() => window.Host.ActiveDialog is not null);
        PrintHost.Find<Button>(window.Host.ActiveDialog!, "PART_Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.True(tab.Organize.IsActive);
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task NoOrganizeSessionIsMadeBeforeAPagesCommandRuns()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var window = fixture.Window;

        // Opening, showing the pages pane and refreshing the ribbon make no session.
        window.ShowPagesPane();
        ViewFixture.Pump();
        _ = Command(window, "pages.organize").IsEnabled;
        Assert.Null(tab.OrganizeIfCreated);

        Command(window, "pages.organize").Run!();
        Assert.NotNull(tab.OrganizeIfCreated);
    }

    [AvaloniaFact]
    public async Task TheEditCommandsActOnTheSelectionAndRefreshThroughTheSession()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var window = fixture.Window;
        Command(window, "pages.organize").Run!();
        ViewFixture.Pump();
        var session = tab.Organize;

        // No selection: the edits are disabled; selecting enables them (the session's Changed refreshes the ribbon).
        Assert.False(Command(window, "pages.rotateRight").IsEnabled);
        Assert.True(Command(window, "pages.blank").IsEnabled);
        Assert.False(Command(window, "pages.apply").IsEnabled);
        session.Selection.Select(2);
        Assert.True(Command(window, "pages.rotateRight").IsEnabled);

        Command(window, "pages.rotateRight").Run!();
        Assert.Equal(90, session.Plan.Slots[2].ExtraRotation);
        Command(window, "pages.rotateLeft").Run!();
        Assert.Equal(0, session.Plan.Slots[2].ExtraRotation);

        Command(window, "pages.moveUp").Run!();
        Assert.Equal([0, 2, 1, 3, 4, 5], session.Plan.Slots.Select(s => s.SourceIndex).ToArray());
        Command(window, "pages.moveDown").Run!();
        Assert.Equal([0, 1, 2, 3, 4, 5], session.Plan.Slots.Select(s => s.SourceIndex).ToArray());

        Command(window, "pages.duplicate").Run!();
        Assert.Equal(7, session.Plan.Slots.Count);
        Assert.True(Command(window, "pages.apply").IsEnabled, "a plan that changes something can be applied");

        Command(window, "pages.delete").Run!();
        Assert.Equal(6, session.Plan.Slots.Count);

        Command(window, "pages.undo").Run!();
        Assert.Equal(7, session.Plan.Slots.Count);
        Command(window, "pages.redo").Run!();
        Assert.Equal(6, session.Plan.Slots.Count);
    }

    [AvaloniaFact]
    public async Task ABlankPageTakesTheSizeOfThePageBeforeItAndIsSelected()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var window = fixture.Window;
        Command(window, "pages.organize").Run!();
        var session = tab.Organize;
        session.Selection.Select(2); // pages-marked.pdf: the third page is landscape

        Command(window, "pages.blank").Run!();
        ViewFixture.Pump();

        var blank = session.Plan.Slots[3].Blank;
        Assert.NotNull(blank);
        var expected = window.ViewOf(tab)!.PageSizeOf(2);
        Assert.NotNull(expected);
        Assert.True(blank.Width > blank.Height, "landscape like the page before");
        Assert.Equal(expected.Value.Width, blank.Width, 3);
        Assert.Equal([3], session.Selection.Positions.ToArray());
    }

    [AvaloniaFact]
    public async Task AnnotateToolsSignAndFormsAreRefusedInTheWindowWhileAPlanIsOpen()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture, "forms-basic.pdf");
        var window = fixture.Window;
        await ViewFixture.WaitAsync(() => window.AnnotateAllowedOf(tab) is not null);
        Assert.True(Command(window, "annotate.highlight").IsEnabled);

        Command(window, "pages.organize").Run!();
        ViewFixture.Pump();

        foreach (var id in ToolIds.Concat(FormIds))
        {
            var command = Command(window, id);
            Assert.False(command.IsEnabled, id);
            Assert.Equal("Apply or discard the page changes first", command.CurrentDisabledReason);
        }

        // A tool chosen by its shortcut (Ctrl+Shift+H) is refused, and the mode badge does not appear.
        window.KeyPress(Key.H, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.H, null);
        window.KeyRelease(Key.H, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.H, null);
        ViewFixture.Pump();
        Assert.Equal(Raffica.Pdf.Annotating.AnnotateTool.None, window.Tools.Tool);
        Assert.False(window.ModeIndicatorOf(tab)!.IsVisible);

        // A path around the commands ends the tool again.
        window.Tools.Choose(Raffica.Pdf.Annotating.AnnotateTool.Highlight);
        ViewFixture.Pump();
        Assert.Equal(Raffica.Pdf.Annotating.AnnotateTool.None, window.Tools.Tool);

        // Leaving the mode gives the tools back.
        Command(window, "pages.organize").Run!();
        ViewFixture.Pump();
        Assert.True(Command(window, "annotate.highlight").IsEnabled);
    }

    [AvaloniaFact]
    public async Task EnteringTheModeEndsAnActiveTool()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var window = fixture.Window;
        await ViewFixture.WaitAsync(() => window.AnnotateAllowedOf(tab) is not null);
        window.Tools.Choose(Raffica.Pdf.Annotating.AnnotateTool.Highlight);

        Command(window, "pages.organize").Run!();
        ViewFixture.Pump();

        Assert.Equal(Raffica.Pdf.Annotating.AnnotateTool.None, window.Tools.Tool);
        Assert.True(tab.Organize.IsActive);
    }

    [AvaloniaFact]
    public async Task CtrlZGoesToThePlanWhileTheModeIsActiveAndToTheMarksOtherwise()
    {
        using var fixture = WindowFixture.Create();
        var tab = await OpenReadyAsync(fixture);
        var window = fixture.Window;
        await ViewFixture.WaitAsync(() => window.AnnotateAllowedOf(tab) is not null);

        // A mark first: Ctrl+Z undoes it while the mode is off.
        await tab.Annotations.Editor.ApplyMarkupAsync(0, Raffica.Core.Annotations.AnnotationKind.Highlight, [new Raffica.Core.Text.PageRect(10, 10, 100, 30)], new Raffica.Core.Annotations.AnnotationColor(255, 255, 0), null, null);
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.True(tab.Annotations.CanUndo);
        Assert.False(Command(window, "pages.undo").IsEnabled);

        // Leave the marks as they are: undo them first so the plan owns the history in the mode.
        await tab.Annotations.UndoAsync();
        await tab.Annotations.RedoAsync();
        ViewFixture.Pump();
        Assert.True(tab.Annotations.CanUndo);

        Command(window, "pages.organize").Run!();
        tab.Organize.Selection.Select(1);
        Command(window, "pages.rotateRight").Run!();
        ViewFixture.Pump();
        Assert.Equal(90, tab.Organize.Plan.Slots[1].ExtraRotation);

        Ctrl(window, Key.Z, PhysicalKey.Z);
        Assert.Equal(0, tab.Organize.Plan.Slots[1].ExtraRotation);
        Assert.True(tab.Annotations.CanUndo, "the marks history was not touched");

        Ctrl(window, Key.Y, PhysicalKey.Y);
        Assert.Equal(90, tab.Organize.Plan.Slots[1].ExtraRotation);
        Assert.True(tab.Annotations.CanUndo);

        // The Home tab's own commands go to the plan as well.
        Assert.True(Command(window, "edit.undo").IsEnabled);
        Command(window, "edit.undo").Run!();
        await Task.Yield();
        Assert.Equal(0, tab.Organize.Plan.Slots[1].ExtraRotation);
        Assert.True(Command(window, "edit.redo").IsEnabled);

        // After leaving the mode Ctrl+Z is the marks' again.
        Command(window, "pages.organize").Run!();
        ViewFixture.Pump();
        Assert.False(tab.Organize.IsActive);
        Ctrl(window, Key.Z, PhysicalKey.Z);
        await ViewFixture.WaitAsync(() => !tab.Annotations.CanUndo);
    }

    private static void Ctrl(PdfWindow window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.Control, physical, null);
        window.KeyRelease(key, RawInputModifiers.Control, physical, null);
        ViewFixture.Pump();
    }
}
