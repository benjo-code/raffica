using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The facts of the structure commands are read lazily per command and bounded (fix round of Task 11, milestone 4.4a).</summary>
public sealed class StructureFactsTests
{
    private const string Heading = "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>";

    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture, params string[] paragraphs)
    {
        var path = StyledDocx.WriteWithNumbering(fixture.Folder, "doc.docx", StyledDocx.StylesWithHeading2, StyledDocx.BulletNumbering, paragraphs);
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

    private static void Caret(EditSession session, WriteTab tab, int paragraph, int offset)
    {
        var at = new TextPosition(StoryRef.Body, (Paragraph)tab.Docx.Model.Body.Blocks[paragraph], offset);
        session.SetSelection(new TextRange(at, at));
    }

    [AvaloniaFact]
    public async Task ListStatesReadNoHeadingsAndOutlineStatesReadThemOnceAndNoListFacts()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(
            fixture,
            Heading + "<w:r><w:t>A</w:t></w:r>",
            "<w:r><w:t>under a</w:t></w:r>",
            Heading + "<w:r><w:t>B</w:t></w:r>");
        Caret(session, tab, 1, 0);
        Assert.Equal(0, window.StructureHeadingReads);

        // The list commands and the numbering switch read list facts (or nothing) and never the headings.
        foreach (var id in new[] { WriteCommandIds.Bullets, WriteCommandIds.Numbers, WriteCommandIds.RestartNumbering, WriteCommandIds.ContinueNumbering, WriteCommandIds.NumberHeadings })
        {
            _ = Command(window, id).EnabledWhen!();
            _ = Command(window, id).IsChecked;
        }

        Assert.Equal(0, window.StructureHeadingReads);
        Assert.Equal(1, session.ListReads);

        // The outline commands read the headings once per revision and never the list facts.
        Caret(session, tab, 2, 1);
        var lists = session.ListReads;
        foreach (var id in new[] { WriteCommandIds.MoveUp, WriteCommandIds.MoveDown, WriteCommandIds.Promote, WriteCommandIds.Demote })
        {
            _ = Command(window, id).EnabledWhen!();
            _ = Command(window, id).DisabledReason!();
        }

        Assert.Equal(1, window.StructureHeadingReads);
        Assert.Equal(lists, session.ListReads);

        // Another caret in the same revision reuses the headings; an edit makes the next ask read them again.
        Caret(session, tab, 0, 0);
        _ = Command(window, WriteCommandIds.MoveDown).EnabledWhen!();
        Assert.Equal(1, window.StructureHeadingReads);
        Command(window, WriteCommandIds.Bullets).Run!();
        WindowFixture.Pump();
        _ = Command(window, WriteCommandIds.MoveDown).EnabledWhen!();
        Assert.Equal(2, window.StructureHeadingReads);
    }

    [AvaloniaFact]
    public async Task APauseInTypingAsksNoOutlineFactsUntilAnOutlineStateIsAsked()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Heading + "<w:r><w:t>A</w:t></w:r>", "<w:r><w:t>text</w:t></w:r>");
        Caret(session, tab, 1, 2);
        session.Apply(ed => ed.InsertText(session.Selection!.Value, "x"), 0, typing: true);
        window.FormatRefresh?.Flush();
        window.ViewStatesRefresh?.Flush();
        WindowFixture.Pump();

        // Whatever the refresh asked, the headings are read at most once for the revision, and asking one outline state more costs nothing.
        var reads = window.StructureHeadingReads;
        Assert.True(reads <= 1);
        _ = Command(window, WriteCommandIds.Demote).EnabledWhen!();
        Assert.Equal(Math.Max(reads, 1), window.StructureHeadingReads);
    }

    [AvaloniaFact]
    public async Task ABodyAboveTheBoundIsAnsweredPermissivelyAndTheOperationDecides()
    {
        using var fixture = new WindowFixture();
        var paragraphs = new List<string> { "<w:r><w:t>intro</w:t></w:r>" };
        for (var i = 0; i < TextEditor.MaxFactBlocks + 50; i++)
        {
            paragraphs.Add(i % 50 == 0 ? Heading + $"<w:r><w:t>H{i}</w:t></w:r>" : $"<w:r><w:t>p{i}</w:t></w:r>");
        }

        var (window, tab, session) = await OpenAsync(fixture, [.. paragraphs]);
        Assert.True(tab.Docx.Model.Body.Blocks.Count > TextEditor.MaxFactBlocks);

        // Before the first heading nothing can move, but the bounded read does not look: every outline command is offered and no heading was read.
        Caret(session, tab, 0, 0);
        foreach (var id in new[] { WriteCommandIds.MoveUp, WriteCommandIds.MoveDown, WriteCommandIds.Promote, WriteCommandIds.Demote })
        {
            Assert.True(Command(window, id).EnabledWhen!(), id);
        }

        Assert.Equal(0, window.StructureHeadingReads);

        // The operation reads the live model and refuses correctly, with a visible note.
        var revision = tab.Docx.Model.Edits.Revision;
        Command(window, WriteCommandIds.MoveDown).Run!();
        WindowFixture.Pump();
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(WriteStrings.ReasonNoHeadingAtCaret, window.Status.Hint);

        // At a heading the same command does the move (the second heading goes above the first: block 1 is H0 and block 51 is H50).
        var second = (Paragraph)tab.Docx.Model.Body.Blocks[51];
        Caret(session, tab, 51, 0);
        Command(window, WriteCommandIds.MoveUp).Run!();
        WindowFixture.Pump();
        Assert.Same(second, tab.Docx.Model.Body.Blocks[1]);
        Assert.Equal(1, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task ADisabledStructureKeyShowsItsReason()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>intro</w:t></w:r>", Heading + "<w:r><w:t>A</w:t></w:r>");
        Caret(session, tab, 0, 0);
        window.FormatRefresh?.Flush();
        var view = window.ViewOf(tab)!;

        view.StructureKeyHandler!(WriteCommandIds.Demote);
        Assert.Equal(WriteStrings.ReasonNoHeadingAtCaret, window.Status.Hint);
    }
}
