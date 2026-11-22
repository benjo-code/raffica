using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests;

/// <summary>The list, numbering and outline commands in the window (Task 11 of milestone 4.4a).</summary>
public sealed class WriteWindowStructureTests
{
    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture, string styles, string? numbering, params string[] paragraphs)
    {
        var path = numbering is null
            ? StyledDocx.Write(fixture.Folder, "doc.docx", styles, paragraphs)
            : StyledDocx.WriteWithNumbering(fixture.Folder, "doc.docx", styles, numbering, paragraphs);
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        return (window, tab, session);
    }

    private static Paragraph P(WriteTab tab, int index) => (Paragraph)tab.Docx.Model.Body.Blocks[index];

    private static void Caret(EditSession session, WriteTab tab, int paragraph, int offset)
    {
        var at = new TextPosition(StoryRef.Body, P(tab, paragraph), offset);
        session.SetSelection(new TextRange(at, at));
    }

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static void Settle(WriteWindow window)
    {
        window.FormatRefresh?.Flush();
        window.ViewStatesRefresh?.Flush();
        WindowFixture.Pump();
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();
    }

    [AvaloniaFact]
    public async Task ClickingBulletsTogglesTheListAndUndoRestores()
    {
        using var fixture = new WindowFixture();
        // A document with no numbering part at all: the first list makes one, and the editor must know it afterwards (the second click removes the list again).
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, null, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        Assert.Null(tab.Docx.Model.Numbering);
        Caret(session, tab, 0, 1);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.Bullets).IsChecked);

        Command(window, WriteCommandIds.Bullets).Run!();
        WindowFixture.Pump();

        Assert.True(P(tab, 0).Properties.NumberingId > 0);
        Assert.NotNull(tab.Docx.Model.Numbering);
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.True(session.ResolveParagraph(StoryRef.Body, P(tab, 0)).NumberingId > 0);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.Bullets).IsChecked);
        Assert.False(Command(window, WriteCommandIds.Numbers).IsChecked);

        // The second click is the other way: the list goes (a stale editor that never saw the numbering part would add a list again).
        Command(window, WriteCommandIds.Bullets).Run!();
        WindowFixture.Pump();
        Assert.Null(P(tab, 0).Properties.NumberingId);
        Assert.Equal(2, session.Editor.History.UndoCount);

        // Undo and redo follow the history.
        session.Editor.History.Undo();
        Assert.True(P(tab, 0).Properties.NumberingId > 0);
        session.Editor.History.Undo();
        Assert.Null(P(tab, 0).Properties.NumberingId);
        Assert.Equal("one", Text(P(tab, 0)));
    }

    [AvaloniaFact]
    public async Task TheListCommandsClearThePendingFormatAndKeepTheSelection()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, StyledDocx.BulletNumbering, "<w:r><w:t>one two</w:t></w:r>");
        var start = new TextPosition(StoryRef.Body, P(tab, 0), 1);
        var end = start with { Offset = 5 };
        session.SetSelection(new TextRange(start, end));
        Settle(window);

        Command(window, WriteCommandIds.Numbers).Run!();
        WindowFixture.Pump();

        Assert.True(P(tab, 0).Properties.NumberingId > 0);
        Assert.Equal(1, session.Selection!.Value.Start.Offset);
        Assert.Equal(5, session.Selection!.Value.End.Offset);
        Assert.Empty(session.Pending.Changes);
    }

    [AvaloniaFact]
    public async Task TheMoveIsOneUndoStepAndTheCaretIsAtTheMovedHeading()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(
            fixture,
            StyledDocx.StylesWithHeading2,
            null,
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>A</w:t></w:r>",
            "<w:r><w:t>under a</w:t></w:r>",
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>B</w:t></w:r>",
            "<w:r><w:t>under b</w:t></w:r>");
        string[] Order() => tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(Text).ToArray();
        var heading = P(tab, 2);
        Caret(session, tab, 2, 1);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.MoveUp).EnabledWhen!());
        Assert.False(Command(window, WriteCommandIds.MoveDown).EnabledWhen!());
        Assert.Equal(WriteStrings.ReasonMoveAtEnd, Command(window, WriteCommandIds.MoveDown).DisabledReason!());

        Command(window, WriteCommandIds.MoveUp).Run!();
        WindowFixture.Pump();

        Assert.Equal(["B", "under b", "A", "under a"], Order());
        Assert.Equal(1, session.Editor.History.UndoCount);
        Assert.Same(heading, session.Caret!.Value.Paragraph);
        Assert.Equal(0, session.Caret!.Value.Offset);

        // Now first: Move up is disabled with its reason; down still goes.
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.MoveUp).EnabledWhen!());
        Assert.False(string.IsNullOrWhiteSpace(Command(window, WriteCommandIds.MoveUp).DisabledReason!()));
        Assert.True(Command(window, WriteCommandIds.MoveDown).EnabledWhen!());

        session.Editor.History.Undo();
        Assert.Equal(["A", "under a", "B", "under b"], Order());
    }

    [AvaloniaFact]
    public async Task PromoteAndDemoteActOnTheHeadingOfTheCaretAndShowTheirReason()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(
            fixture,
            StyledDocx.StylesWithHeading2,
            null,
            "<w:r><w:t>intro</w:t></w:r>",
            "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>A</w:t></w:r>");

        // Before the first heading there is nothing to act on, and the reason says so.
        Caret(session, tab, 0, 0);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.Demote).EnabledWhen!());
        Assert.Equal(WriteStrings.ReasonNoHeadingAtCaret, Command(window, WriteCommandIds.Demote).DisabledReason!());

        Caret(session, tab, 1, 0);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.Demote).EnabledWhen!());
        // Heading 1 cannot go any higher.
        Assert.False(Command(window, WriteCommandIds.Promote).EnabledWhen!());
        Assert.False(string.IsNullOrWhiteSpace(Command(window, WriteCommandIds.Promote).DisabledReason!()));

        Command(window, WriteCommandIds.Demote).Run!();
        WindowFixture.Pump();
        Assert.Equal("Heading2", P(tab, 1).Properties.StyleId);
    }

    [AvaloniaFact]
    public async Task RestartAndContinueNeedAListAndSomethingBeforeIt()
    {
        using var fixture = new WindowFixture();
        var item = "<w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>";
        var (window, tab, session) = await OpenAsync(fixture, StyledDocx.Styles, StyledDocx.BulletNumbering, "<w:r><w:t>plain</w:t></w:r>", item + "<w:r><w:t>a</w:t></w:r>", item + "<w:r><w:t>b</w:t></w:r>");

        Caret(session, tab, 0, 0);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.RestartNumbering).EnabledWhen!());
        Assert.Equal(WriteStrings.ReasonNotInAList, Command(window, WriteCommandIds.RestartNumbering).DisabledReason!());
        Assert.Equal(WriteStrings.ReasonNotInAList, Command(window, WriteCommandIds.ContinueNumbering).DisabledReason!());

        Caret(session, tab, 2, 0);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.RestartNumbering).EnabledWhen!());
        Assert.False(Command(window, WriteCommandIds.ContinueNumbering).EnabledWhen!());

        Command(window, WriteCommandIds.RestartNumbering).Run!();
        WindowFixture.Pump();
        Assert.NotEqual(P(tab, 1).Properties.NumberingId, P(tab, 2).Properties.NumberingId);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.ContinueNumbering).EnabledWhen!());

        Command(window, WriteCommandIds.ContinueNumbering).Run!();
        WindowFixture.Pump();
        Assert.Equal(P(tab, 1).Properties.NumberingId, P(tab, 2).Properties.NumberingId);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoStructureWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();

        // The ribbon has the commands, but nothing was read for them: no session (so no editor, list catalog or move), no outline pane (so no tree), no facts read, no note.
        var ids = window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Select(c => c.Id).ToList();
        Assert.Contains(WriteCommandIds.Bullets, ids);
        Assert.Contains(WriteCommandIds.MoveUp, ids);
        Assert.Null(tab.Editor);
        Assert.Null(window.OutlineOf(tab));
        Assert.Equal(0, window.StructureHeadingReads);
        Assert.Null(window.Status.Hint);

        // With a session a ribbon refresh of the compact start band asks the list states (one read of the list facts, memoized for the second refresh); the thesis body is above the bound of 200 blocks, so the outline states are answered without walking the headings.
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        window.Ribbon.RefreshCommandStates();
        window.Ribbon.RefreshCommandStates();
        Assert.Equal(1, session.ListReads);
        Assert.Equal(0, window.StructureHeadingReads);
        Assert.Null(window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task NumberingTheHeadingsOfABlankDocumentIsAToggleThatFollowsTheStyles()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = (await window.NewDocumentAsync())!;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        Settle(window);
        var command = Command(window, WriteCommandIds.NumberHeadings);
        Assert.True(command.EnabledWhen!());
        Assert.False(command.IsChecked);

        command.Run!();
        WindowFixture.Pump();
        Settle(window);
        Assert.True(command.IsChecked);
        Assert.Equal(HeadingNumberingState.On, HeadingNumbering.StateOf(tab.Docx.Model));

        session.Editor.History.Undo();
        Settle(window);
        Assert.False(command.IsChecked);
    }
}
