using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>
/// The facts of the formatting commands on the command-state path: nothing is read before a command that needs a fact is asked, a repeated question costs nothing while the
/// selection and the document stay as they are, a selection beyond <see cref="TextEditor.MaxFactBlocks"/> is not walked, and a refresh over every command of a select all stays small.
/// </summary>
public sealed class FormatFactsCacheTests
{
    // Time bounds, in milliseconds, for a select all of the thesis: the first refresh may read once (beyond the bound the read is only a lookup), the next ones must be free.
    private const int FirstRefreshBudget = 500;
    private const int RepeatedRefreshBudget = 200;

    private static readonly string[] s_factCommands = [WriteCommandIds.ClearDirect, WriteCommandIds.ParagraphStyle, WriteCommandIds.CharacterStyle, WriteCommandIds.UpdateStyle];

    private static async Task<WriteTab> OpenWholeAsync()
    {
        var attempt = DocumentOpener.Open(Path.GetFullPath(WindowFixture.Thesis), new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);
        Assert.NotNull(attempt.Document);
        var tab = new WriteTab(WindowFixture.Thesis, attempt.Document!) { RefreshesFields = false };
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted, 30000);
        Assert.True(tab.Runner.FullModelReady);
        return tab;
    }

    private static WriteCommandRules RulesOf(WriteTab tab) =>
        new(() => true, () => 1.0, () => true, () => EditCommandState.Of(tab), null, () => EditCommandState.FactsOf(tab));

    private static TextRange Blocks(WriteTab tab, int firstBlock, int count)
    {
        var paragraphs = tab.Docx.Model.Body.Blocks.OfType<Paragraph>().ToList();
        var first = paragraphs[firstBlock];
        var last = paragraphs[firstBlock + count - 1];
        return new TextRange(
            new TextPosition(StoryRef.Body, first, 0),
            new TextPosition(StoryRef.Body, last, ParagraphProjection.Build(last, ProjectionHost.Plain).Text.Length));
    }

    // The selection from the first paragraph among the first blocks to the last paragraph before block index <paramref name="blockCount"/>: exactly that many top-level blocks (tables included).
    private static TextRange FirstBlocks(WriteTab tab, int blockCount)
    {
        var blocks = tab.Docx.Model.Body.Blocks;
        var first = blocks.OfType<Paragraph>().First();
        var last = blocks.Take(blockCount).OfType<Paragraph>().Last();
        Assert.True(blocks.ToList().IndexOf(last) - blocks.ToList().IndexOf(first) + 1 <= TextEditor.MaxFactBlocks);
        return new TextRange(
            new TextPosition(StoryRef.Body, first, 0),
            new TextPosition(StoryRef.Body, last, ParagraphProjection.Build(last, ProjectionHost.Plain).Text.Length));
    }

    private static void Refresh(WriteCommandRules rules)
    {
        foreach (var command in WriteCommandCatalog.Commands)
        {
            _ = rules.Allows(command.Id);
            _ = rules.ReasonFor(command.Id);
        }
    }

    [AvaloniaFact]
    public async Task NothingIsReadBeforeACommandThatNeedsAFactIsAsked()
    {
        using var tab = await OpenWholeAsync();
        var rules = RulesOf(tab);

        // No session yet: the state and the facts read nothing and make no session.
        Refresh(rules);
        Assert.Null(tab.Editor);

        var session = tab.EnsureEditor();
        _ = session.Selection;
        foreach (var id in new[] { WriteCommandIds.Bold, WriteCommandIds.Undo, WriteCommandIds.Copy, WriteCommandIds.Paste, WriteCommandIds.AlignLeft, WriteCommandIds.Font, WriteCommandIds.FormattingPane })
        {
            _ = rules.Allows(id);
            _ = rules.ReasonFor(id);
        }

        Assert.Equal(0, session.FactReads);

        _ = rules.Allows(WriteCommandIds.ClearDirect);
        Assert.Equal(1, session.FactReads);
    }

    [AvaloniaFact]
    public async Task ARepeatedQuestionCostsNothingUntilTheSelectionOrTheDocumentChanges()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Editor;
        var rules = RulesOf(tab);
        session.SetSelection(Blocks(tab, 3, 4));

        for (var i = 0; i < 30; i++)
        {
            foreach (var id in s_factCommands)
            {
                _ = rules.Allows(id);
                _ = rules.ReasonFor(id);
            }
        }

        Assert.Equal(1, session.FactReads);

        // Another selection reads again, once.
        session.SetSelection(Blocks(tab, 5, 2));
        Refresh(rules);
        Refresh(rules);
        Assert.Equal(2, session.FactReads);

        // The same selection after the document changed (the revision rose) reads again, once.
        tab.Docx.Model.Edits.Raise();
        Refresh(rules);
        Refresh(rules);
        Assert.Equal(3, session.FactReads);

        // A real edit does too.
        var range = Blocks(tab, 5, 1);
        Assert.NotNull(session.Apply(e => e.ToggleCharacter(range, CharacterToggle.Bold), 0));
        Refresh(rules);
        Refresh(rules);
        Assert.Equal(4, session.FactReads);
    }

    [AvaloniaFact]
    public async Task ASelectAllOfTheThesisIsNotWalkedAndARefreshStaysSmall()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Editor;
        var rules = RulesOf(tab);
        var all = Blocks(tab, 0, tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Count());
        Assert.True(all.End.Paragraph != all.Start.Paragraph);
        session.SetSelection(all);

        var timer = Stopwatch.StartNew();
        Refresh(rules);
        var first = timer.ElapsedMilliseconds;
        timer.Restart();
        for (var i = 0; i < 20; i++)
        {
            Refresh(rules);
        }

        var repeated = timer.ElapsedMilliseconds;
        TestContext.Current.SendDiagnosticMessage($"select all of the thesis: first refresh {first} ms, 20 further refreshes {repeated} ms, fact reads {session.FactReads}");
        Assert.Equal(1, session.FactReads);
        Assert.True(first < FirstRefreshBudget, $"first refresh {first} ms");
        Assert.True(repeated < RepeatedRefreshBudget, $"20 refreshes {repeated} ms");

        // Beyond the bound the facts are the permissive ones: the commands stay usable and nothing is dimmed.
        Assert.True(rules.Allows(WriteCommandIds.ClearDirect));
        Assert.True(rules.Allows(WriteCommandIds.UpdateStyle));
        Assert.True(rules.Allows(WriteCommandIds.ParagraphStyle));
        Assert.Equal(FormatFacts.Unknown, session.ReadFormatFacts(all));
    }

    [AvaloniaFact]
    public async Task ASelectionAtTheBoundIsReadOnceAndTheReadIsTimed()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Editor;
        var rules = RulesOf(tab);
        var range = FirstBlocks(tab, TextEditor.MaxFactBlocks);
        session.SetSelection(range);

        var timer = Stopwatch.StartNew();
        Refresh(rules);
        var first = timer.ElapsedMilliseconds;
        timer.Restart();
        for (var i = 0; i < 20; i++)
        {
            Refresh(rules);
        }

        var repeated = timer.ElapsedMilliseconds;
        TestContext.Current.SendDiagnosticMessage($"{TextEditor.MaxFactBlocks} paragraphs of the thesis: first refresh {first} ms, 20 further refreshes {repeated} ms, fact reads {session.FactReads}");
        Assert.Equal(1, session.FactReads);
        Assert.True(first < FirstRefreshBudget, $"first refresh {first} ms");
        Assert.True(repeated < RepeatedRefreshBudget, $"20 refreshes {repeated} ms");
    }

    // The last blocks of the thesis: the story walk of the format must not grow with the position of the selection. The read counters are the guard; the bound is generous (the read took 510 ms on the UI thread of the reviewer's probe
    // for the last 200 blocks while every paragraph still walked the story from its start, and takes about 120 ms with the cell index, JIT included; 1273 ms before).
    [AvaloniaFact]
    public async Task TheLastBlocksOfTheThesisAreReadOnceAndTheReadIsTimed()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Editor;
        var rules = RulesOf(tab);
        var blocks = tab.Docx.Model.Body.Blocks;
        var tail = blocks.Skip(blocks.Count - TextEditor.MaxFactBlocks).ToList();
        var first = tail.OfType<Paragraph>().First();
        var last = tail.OfType<Paragraph>().Last();
        var range = new TextRange(
            new TextPosition(StoryRef.Body, first, 0),
            new TextPosition(StoryRef.Body, last, ParagraphProjection.Build(last, ProjectionHost.Plain).Text.Length));
        session.SetSelection(range);

        var timer = Stopwatch.StartNew();
        var shown = session.ReadSelectionFormat(range);
        Refresh(rules);
        var firstRead = timer.ElapsedMilliseconds;
        timer.Restart();
        for (var i = 0; i < 20; i++)
        {
            _ = session.ReadSelectionFormat(range);
            Refresh(rules);
        }

        var repeated = timer.ElapsedMilliseconds;
        TestContext.Current.SendDiagnosticMessage($"the last {TextEditor.MaxFactBlocks} blocks of the thesis: first read {firstRead} ms, 20 further reads {repeated} ms, format reads {session.FormatReads}, fact reads {session.FactReads}");
        Assert.False(shown.Truncated);
        Assert.Equal(1, session.FormatReads);
        Assert.Equal(1, session.FactReads);
        Assert.True(firstRead < 1000, $"first read {firstRead} ms");
        Assert.True(repeated < RepeatedRefreshBudget, $"20 reads {repeated} ms");
    }

    [AvaloniaFact]
    public async Task ASelectionThatDoesNotBelongToTheModelGivesPermissiveFactsAndNoDisplay()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Editor;

        // The debounced refresh and the command state read on a timer tick and nothing guards them: a stale selection answers "unknown", it never throws.
        var stranger = new Paragraph();
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, stranger, 0), new TextPosition(StoryRef.Body, stranger, 0)));

        Assert.Equal(FormatFacts.Unknown, EditCommandState.FactsOf(tab));
        Assert.Null(EditCommandState.FormatOf(tab));
    }
}
