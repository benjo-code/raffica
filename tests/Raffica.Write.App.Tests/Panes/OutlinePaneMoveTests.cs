using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.WriteApp.Tests.Commands;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Panes;

/// <summary>The outline pane moves, promotes and demotes chapters: the header buttons, the pointer drag with its drop line and the Alt and Shift keys typed in the list (Task 12 of milestone 4.4a).</summary>
public sealed class OutlinePaneMoveTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(40);

    private const string H1 = "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>";
    private const string H2 = "<w:pPr><w:pStyle w:val=\"Heading2\"/></w:pPr>";

    // A, A1 (under A), B, C: block indices 0 to 7, rows 0 to 3.
    private static readonly string[] Standard =
    [
        H1 + "<w:r><w:t>A</w:t></w:r>",
        "<w:r><w:t>under a</w:t></w:r>",
        H2 + "<w:r><w:t>A1</w:t></w:r>",
        "<w:r><w:t>under a1</w:t></w:r>",
        H1 + "<w:r><w:t>B</w:t></w:r>",
        "<w:r><w:t>under b</w:t></w:r>",
        H1 + "<w:r><w:t>C</w:t></w:r>",
        "<w:r><w:t>under c</w:t></w:r>",
    ];

    private sealed record Setup(WriteWindow Window, WriteTab Tab, OutlinePane Pane, EditSession Session);

    private static async Task<Setup> OpenAsync(WindowFixture fixture, string path)
    {
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady, 30000);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        Assert.True(window.RunCommand(WriteCommandIds.Outline));
        var pane = window.OutlineOf(tab)!;
        pane.RefreshDelay = Delay;
        await ViewHarness.WaitAsync(() => pane.LoadTask.IsCompleted, 30000);
        WindowFixture.Pump();
        pane.List.UpdateLayout();
        return new Setup(window, tab, pane, session);
    }

    private static Task<Setup> OpenStandardAsync(WindowFixture fixture) =>
        OpenAsync(fixture, StyledDocx.Write(fixture.Folder, "outline.docx", StyledDocx.StylesWithHeading2, Standard));

    private static Paragraph P(WriteTab tab, int index) => (Paragraph)tab.Docx.Model.Body.Blocks[index];

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static string[] Order(WriteTab tab) => [.. tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(Text)];

    private static async Task SettleAsync(Setup s, int reads)
    {
        await ViewHarness.WaitAsync(() => s.Pane.ReadCount >= reads, 10000);
        WindowFixture.Pump();
        s.Pane.List.UpdateLayout();
    }

    // The point of a row in window coordinates; fraction 0 is its top edge, 1 its bottom edge.
    private static Point RowPoint(Setup s, int row, double fraction)
    {
        var container = s.Pane.List.ContainerFromIndex(row) ?? throw new InvalidOperationException("The row is not realized.");
        return container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height * fraction), s.Window)!.Value;
    }

    private static void Press(Setup s, int row)
    {
        s.Window.MouseDown(RowPoint(s, row, 0.5), MouseButton.Left);
        // Past the drag threshold, still inside the row.
        s.Window.MouseMove(RowPoint(s, row, 0.5) + new Point(0, 7), RawInputModifiers.LeftMouseButton);
        WindowFixture.Pump();
    }

    private static void MoveTo(Setup s, int row, double fraction)
    {
        s.Window.MouseMove(RowPoint(s, row, fraction), RawInputModifiers.LeftMouseButton);
        WindowFixture.Pump();
    }

    private static void Release(Setup s, int row, double fraction)
    {
        s.Window.MouseUp(RowPoint(s, row, fraction), MouseButton.Left);
        WindowFixture.Pump();
    }

    private static void FocusRow(Setup s, int row)
    {
        s.Pane.List.SelectedIndex = row;
        s.Pane.FocusContent();
        WindowFixture.Pump();
        Assert.True(s.Pane.Engaged);
        Assert.Equal(row, s.Pane.List.SelectedIndex);
    }

    private static void AltShift(Setup s, Key key, PhysicalKey physical, RawInputModifiers extra = RawInputModifiers.None)
    {
        s.Window.KeyPressQwerty(physical, RawInputModifiers.Alt | RawInputModifiers.Shift | extra);
        s.Window.KeyReleaseQwerty(physical, RawInputModifiers.Alt | RawInputModifiers.Shift | extra);
        _ = key;
        WindowFixture.Pump();
    }

    // Which platform's keys the window, the pane and the page view match (the Alt and Shift keys of the outline commands exist on Windows and Linux only).
    private static void UseKeysOf(Setup s, bool macOs)
    {
        s.Window.MacOsKeys = macOs;
        s.Pane.MacOsKeys = macOs;
        s.Window.ViewOf(s.Tab)!.MacOsKeys = macOs;
    }

    private static void Settle(Setup s)
    {
        s.Window.FormatRefresh?.Flush();
        s.Window.ViewStatesRefresh?.Flush();
        WindowFixture.Pump();
        s.Pane.RefreshButtons();
        WindowFixture.Pump();
    }

    private static void ClickButton(OutlinePane pane, string id) => pane.ButtonOf(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task DraggingARowBelowAnotherMovesTheHeadingWithItsContent()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        var heading = P(s.Tab, 0);

        // A (with its subsection A1 and both bodies) goes below B.
        Press(s, 0);
        Assert.True(s.Pane.IsDragging);
        MoveTo(s, 2, 0.8);
        Assert.True(s.Pane.DropLineVisible);
        Assert.Equal(WriteStrings.Fill(WriteStrings.OutlineDropBelowFormat, "A", "B"), s.Pane.DropName);
        Release(s, 2, 0.8);

        Assert.False(s.Pane.IsDragging);
        Assert.Equal(["B", "under b", "A", "under a", "A1", "under a1", "C", "under c"], Order(s.Tab));
        Assert.Equal(1, s.Session.Editor.History.UndoCount);

        // The caret is at the start of the moved heading and its row stays selected once the pane has read again.
        Assert.Same(heading, s.Session.Caret!.Value.Paragraph);
        Assert.Equal(0, s.Session.Caret!.Value.Offset);
        await SettleAsync(s, 2);
        Assert.Equal(["B", "A", "A1", "C"], s.Pane.Entries.Select(e => e.Text));
        Assert.Same(heading, s.Pane.SelectedParagraph);
        Assert.Equal(1, s.Pane.List.SelectedIndex);
        Assert.True(s.Pane.List.IsKeyboardFocusWithin);

        // Undo puts the order back and the pane reads again.
        Assert.True(s.Window.RunCommand(WriteCommandIds.Undo));
        await SettleAsync(s, 3);
        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal(["A", "A1", "B", "C"], s.Pane.Entries.Select(e => e.Text));
    }

    [AvaloniaFact]
    public async Task TheDropLineShowsOnlyOnLegalPlaces()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);

        // B dragged: above A, between A and A1 and below C are places; its own two edges are not.
        Press(s, 2);
        Assert.True(s.Pane.IsDragging);
        MoveTo(s, 0, 0.2);
        Assert.True(s.Pane.DropLineVisible);
        MoveTo(s, 1, 0.2);
        Assert.True(s.Pane.DropLineVisible);
        MoveTo(s, 2, 0.2);
        Assert.False(s.Pane.DropLineVisible);
        MoveTo(s, 2, 0.8);
        Assert.False(s.Pane.DropLineVisible);
        Assert.Null(s.Pane.DropName);
        MoveTo(s, 3, 0.8);
        Assert.True(s.Pane.DropLineVisible);
        Assert.Equal(WriteStrings.Fill(WriteStrings.OutlineDropBelowFormat, "B", "C"), s.Pane.DropName);
        MoveTo(s, 3, 0.2);
        Assert.False(s.Pane.DropLineVisible);

        // The line's name is the accessible name and never longer than the entry text it quotes.
        MoveTo(s, 0, 0.2);
        Assert.Equal(WriteStrings.Fill(WriteStrings.OutlineDropAboveFormat, "B", "A"), s.Pane.DropName);
        Assert.Equal(s.Pane.DropName, AutomationProperties.GetName(s.Pane.DropLine!));
        Release(s, 0, 0.2);
        Assert.Equal(["B", "under b", "A", "under a", "A1", "under a1", "C", "under c"], Order(s.Tab));
    }

    [AvaloniaFact]
    public async Task ADropInsideTheOwnSubtreeDoesNothing()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        var revision = s.Tab.Docx.Model.Edits.Revision;

        Press(s, 0);
        MoveTo(s, 1, 0.8);
        Assert.False(s.Pane.DropLineVisible);
        Release(s, 1, 0.8);

        Assert.False(s.Pane.IsDragging);
        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal(revision, s.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, s.Session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task EscCancelsADragAndALeftOverDropDoesNothing()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        Press(s, 0);
        MoveTo(s, 3, 0.8);
        Assert.True(s.Pane.DropLineVisible);

        s.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        s.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.False(s.Pane.IsDragging);
        Assert.False(s.Pane.DropLineVisible);

        // The button is still down in the user's hand: letting go changes nothing.
        Release(s, 3, 0.8);
        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal(0, s.Session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task AltShiftDownMovesTheSubtreeByOneRow()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        var heading = P(s.Tab, 0);
        UseKeysOf(s, macOs: false);
        FocusRow(s, 0);

        // Typed in the list itself: the list's own Down would only move the selection.
        AltShift(s, Key.Down, PhysicalKey.ArrowDown);

        Assert.Equal(["B", "under b", "A", "under a", "A1", "under a1", "C", "under c"], Order(s.Tab));
        Assert.Equal(1, s.Session.Editor.History.UndoCount);
        Assert.Same(heading, s.Session.Caret!.Value.Paragraph);
        await SettleAsync(s, 2);
        Assert.Same(heading, s.Pane.SelectedParagraph);
        Assert.Equal(1, s.Pane.List.SelectedIndex);
        Assert.True(s.Pane.List.IsKeyboardFocusWithin);
        Assert.True(s.Pane.Engaged);

        // Up again, from the rebuilt list: back where it was.
        AltShift(s, Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal(2, s.Session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task AltShiftLeftAndRightPromoteAndDemoteTheRowFromTheList()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        UseKeysOf(s, macOs: false);
        FocusRow(s, 1);

        AltShift(s, Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal("Heading1", P(s.Tab, 2).Properties.StyleId);
        await SettleAsync(s, 2);
        Assert.Equal([1, 1, 1, 1], s.Pane.Entries.Select(e => e.Level));
        Assert.Equal(1, s.Pane.List.SelectedIndex);

        AltShift(s, Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal("Heading2", P(s.Tab, 2).Properties.StyleId);
        await SettleAsync(s, 3);
        Assert.Equal([1, 2, 1, 1], s.Pane.Entries.Select(e => e.Level));
        Assert.Equal(1, s.Pane.List.SelectedIndex);
        Assert.Equal(2, s.Session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task OnTheMacAltShiftArrowsTypedInTheListRunNoOutlineCommand()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        UseKeysOf(s, macOs: true);
        FocusRow(s, 1);
        var revision = s.Tab.Docx.Model.Edits.Revision;

        // The chord has no outline command on the Mac: the list keeps it, the document is not touched.
        AltShift(s, Key.Down, PhysicalKey.ArrowDown);
        AltShift(s, Key.Up, PhysicalKey.ArrowUp);
        AltShift(s, Key.Left, PhysicalKey.ArrowLeft);
        AltShift(s, Key.Right, PhysicalKey.ArrowRight);

        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal("Heading2", P(s.Tab, 2).Properties.StyleId);
        Assert.Equal(revision, s.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, s.Session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task AltGrWithShiftAndAnArrowDoesNothing()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        FocusRow(s, 0);

        AltShift(s, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Control);
        AltShift(s, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Control);

        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal("Heading2", P(s.Tab, 2).Properties.StyleId);
        Assert.Equal(0, s.Session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task ATypedKeyInTheListStaysTheListsOnly()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        FocusRow(s, 0);
        var scroll = (s.Window.ViewOf(s.Tab)!).OffsetY;

        // Down and End are the list's: the selection moves, the document is not touched and the page does not scroll.
        s.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        s.Window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal(1, s.Pane.List.SelectedIndex);
        s.Window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        s.Window.KeyReleaseQwerty(PhysicalKey.End, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Equal(3, s.Pane.List.SelectedIndex);
        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal(0, s.Session.Editor.History.UndoCount);
        Assert.Equal(scroll, s.Window.ViewOf(s.Tab)!.OffsetY);
        Assert.True(s.Pane.List.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task PromoteAndDemoteChangeTheRowLevelAfterTheRefresh()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        s.Pane.List.SelectedIndex = 1;
        Settle(s);

        // The row A1 (level 2): promote is offered; demote would need a Heading 3 style the document does not have, and says so.
        Assert.True(s.Pane.ButtonOf(WriteCommandIds.Promote).IsEnabled);
        Assert.False(s.Pane.ButtonOf(WriteCommandIds.Demote).IsEnabled);
        Assert.Equal(WriteStrings.ReasonShiftNotHeadingStyle, AutomationProperties.GetName(s.Pane.ButtonOf(WriteCommandIds.Demote)));

        ClickButton(s.Pane, WriteCommandIds.Promote);
        WindowFixture.Pump();
        Assert.Equal("Heading1", P(s.Tab, 2).Properties.StyleId);
        // The list reads again by the rule of the pause; the row keeps its place and its selection.
        await SettleAsync(s, 2);
        Assert.Equal([1, 1, 1, 1], s.Pane.Entries.Select(e => e.Level));
        Assert.Same(P(s.Tab, 2), s.Pane.SelectedParagraph);

        // Now a level-1 row that is the first: no promote (its reason is the name and the tooltip), demote works.
        s.Pane.List.SelectedIndex = 0;
        Settle(s);
        var promote = s.Pane.ButtonOf(WriteCommandIds.Promote);
        Assert.False(promote.IsEnabled);
        Assert.Equal(WriteStrings.ReasonPromoteOutOfRange, AutomationProperties.GetName(promote));
        Assert.Equal(WriteStrings.ReasonPromoteOutOfRange, ToolTip.GetTip(promote));

        ClickButton(s.Pane, WriteCommandIds.Demote);
        WindowFixture.Pump();
        Assert.Equal("Heading2", P(s.Tab, 0).Properties.StyleId);
        await SettleAsync(s, 3);
        Assert.Equal(2, s.Pane.Entries[0].Level);
        Assert.Same(P(s.Tab, 0), s.Pane.SelectedParagraph);
    }

    [AvaloniaFact]
    public async Task TheButtonsMoveTheSelectedRowEvenWhenThePageHasTheFocus()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        // The caret sits in the first chapter; the selected row is the last one.
        var at = new TextPosition(StoryRef.Body, P(s.Tab, 0), 0);
        s.Session.SetSelection(new TextRange(at, at));
        s.Window.ViewOf(s.Tab)!.Focus();
        WindowFixture.Pump();
        s.Pane.List.SelectedIndex = 3;
        Settle(s);
        Assert.False(s.Pane.Engaged);
        Assert.True(s.Pane.ButtonOf(WriteCommandIds.MoveUp).IsEnabled);
        Assert.False(s.Pane.ButtonOf(WriteCommandIds.MoveDown).IsEnabled);
        Assert.Equal(WriteStrings.ReasonMoveAtEnd, AutomationProperties.GetName(s.Pane.ButtonOf(WriteCommandIds.MoveDown)));

        ClickButton(s.Pane, WriteCommandIds.MoveUp);
        WindowFixture.Pump();

        Assert.Equal(["A", "under a", "A1", "under a1", "C", "under c", "B", "under b"], Order(s.Tab));
    }

    [AvaloniaFact]
    public async Task ABlockedRowShowsItsReason()
    {
        using var fixture = new WindowFixture();
        // The body of A ends a section: A and everything under it cannot travel.
        var path = StyledDocx.Write(
            fixture.Folder,
            "blocked.docx",
            StyledDocx.StylesWithHeading2,
            H1 + "<w:r><w:t>A</w:t></w:r>",
            "<w:pPr><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:pPr><w:r><w:t>end of the section</w:t></w:r>",
            H1 + "<w:r><w:t>B</w:t></w:r>",
            "<w:r><w:t>under b</w:t></w:r>");
        var s = await OpenAsync(fixture, path);
        s.Pane.List.SelectedIndex = 0;
        Settle(s);

        foreach (var id in new[] { WriteCommandIds.MoveUp, WriteCommandIds.MoveDown })
        {
            var button = s.Pane.ButtonOf(id);
            Assert.False(button.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button) as string));
            Assert.Equal(ToolTip.GetTip(button), AutomationProperties.GetName(button));
        }

        Assert.Equal(WriteStrings.ReasonMoveSectionBreak, AutomationProperties.GetName(s.Pane.ButtonOf(WriteCommandIds.MoveDown)));

        // The drag of that row does not start, and says why.
        Press(s, 0);
        Assert.False(s.Pane.IsDragging);
        Assert.Equal(WriteStrings.ReasonMoveSectionBreak, s.Window.Status.Hint);
        Release(s, 0, 0.5);
        Assert.Equal(["A", "end of the section", "B", "under b"], Order(s.Tab));
    }

    [AvaloniaFact]
    public async Task ADropBelowASubtreeThatEndsInATableAtTheEndIsNotOffered()
    {
        using var fixture = new WindowFixture();
        const string Table = "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var path = OutlinePaneTests.WriteDocx(
            fixture.Folder,
            "table.docx",
            OutlinePaneTests.P("A", "Chapter") + OutlinePaneTests.P("under a") + Table + OutlinePaneTests.P("B", "Chapter") + OutlinePaneTests.P("under b"));
        var s = await OpenAsync(fixture, path);

        // A ends in a table: below B it would end the body.
        Press(s, 0);
        Assert.True(s.Pane.IsDragging);
        MoveTo(s, 1, 0.8);
        Assert.False(s.Pane.DropLineVisible);
        Release(s, 1, 0.8);
        Assert.Equal(0, s.Session.Editor.History.UndoCount);
        Assert.Equal(["Paragraph", "Paragraph", "Table", "Paragraph", "Paragraph"], s.Tab.Docx.Model.Body.Blocks.Select(b => b.GetType().Name).ToArray());
        Assert.Equal(["A", "under a", "B", "under b"], Order(s.Tab));
    }

    [AvaloniaFact]
    public async Task AMoveDuringASaveIsQueuedAndReplayed()
    {
        using var fixture = new WindowFixture();
        var s = await OpenStandardAsync(fixture);
        s.Pane.List.SelectedIndex = 0;
        Settle(s);
        var revision = s.Tab.Docx.Model.Edits.Revision;

        var save = s.Session.BeginSave();

        // The buttons are the commands, and a command waits for the save with its reason (the rules of the structure commands); a drop names its rows itself, is planned from the model when it runs, and is queued.
        s.Pane.RefreshButtons();
        Assert.False(s.Pane.ButtonOf(WriteCommandIds.MoveDown).IsEnabled);
        Assert.Equal(WriteStrings.ReasonEditSaving, AutomationProperties.GetName(s.Pane.ButtonOf(WriteCommandIds.MoveDown)));
        Press(s, 0);
        Assert.True(s.Pane.IsDragging);
        MoveTo(s, 2, 0.8);
        Assert.True(s.Pane.DropLineVisible);
        Release(s, 2, 0.8);

        // Nothing happened yet; the operation waits in the queue of the save.
        Assert.Equal(revision, s.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(["A", "under a", "A1", "under a1", "B", "under b", "C", "under c"], Order(s.Tab));
        Assert.Equal(1, s.Session.PendingCount);

        save.Dispose();
        WindowFixture.Pump();

        Assert.Equal(0, s.Session.DroppedWhileSaving);
        Assert.Equal(["B", "under b", "A", "under a", "A1", "under a1", "C", "under c"], Order(s.Tab));
        Assert.Equal(1, s.Session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task NothingIsReadBeforeThePaneIsShown()
    {
        using var fixture = new WindowFixture();
        var path = StyledDocx.Write(fixture.Folder, "outline.docx", StyledDocx.StylesWithHeading2, Standard);
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady, 30000);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();

        // No pane, so no tree, no buttons, no drop line and no read of the headings.
        Assert.Null(window.OutlineOf(tab));
        Assert.Null(window.OutlineHost);
        var reads = window.StructureHeadingReads;

        // The pane exists once asked for; a drag was never started, so no line was made and no tree was read for it.
        Assert.True(window.RunCommand(WriteCommandIds.Outline));
        var pane = window.OutlineOf(tab)!;
        await ViewHarness.WaitAsync(() => pane.LoadTask.IsCompleted, 30000);
        Assert.False(pane.IsDragging);
        Assert.Null(pane.DropLine);
        Assert.Equal(0, pane.DragReads);

        // A pane that is hidden again keeps no drag and reads nothing more.
        Assert.True(window.RunCommand(WriteCommandIds.Outline));
        WindowFixture.Pump();
        Assert.Equal(1, pane.ReadCount);
        Assert.Equal(reads, window.StructureHeadingReads);
        Assert.Equal(0, pane.DragReads);
    }
}
