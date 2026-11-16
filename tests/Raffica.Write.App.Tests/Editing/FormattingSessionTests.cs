using System.Xml.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The session side of milestone 4.3.2: style steps restyle the active layout, the selection raises an event, the pending format is the session's only (Task 7).</summary>
public sealed class FormattingSessionTests
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly PropertyChange BoldOn = new(W + "b", new XElement(W + "b"));

    private static async Task<WriteTab> OpenWholeAsync()
    {
        var attempt = DocumentOpener.Open(Path.GetFullPath(WindowFixture.Thesis), new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);
        Assert.NotNull(attempt.Document);
        var tab = new WriteTab(WindowFixture.Thesis, attempt.Document!);
        tab.Runner.LeaseWaitTimeout = GateTimeout;
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted, 30000);
        Assert.True(tab.Runner.FullModelReady);
        return tab;
    }

    // A paragraph whose first run has text beyond the length and is not bold by its style (a toggle then switches bold on).
    private static bool HasText(WriteTab tab, Paragraph p, int length) =>
        p.Inlines.OfType<Run>().FirstOrDefault() is { } first && first.Content.OfType<TextContent>().Any(t => t.Text.Length > length)
        && !new LayoutEffectiveFormat(tab.Docx.Model).ForRun(StoryRef.Body, p, first).Bold;

    // The first body paragraph with text, as a range.
    private static TextRange FirstTextRange(WriteTab tab, int from, int to)
    {
        foreach (var block in tab.Docx.Model.Body.Blocks)
        {
            if (block is Paragraph p && HasText(tab, p, to))
            {
                return new TextRange(new TextPosition(StoryRef.Body, p, from), new TextPosition(StoryRef.Body, p, to));
            }
        }

        throw new InvalidOperationException("no paragraph with text");
    }

    private static int PlainIndex(WriteTab tab) => tab.Docx.Model.Body.Blocks.ToList().FindIndex(b => b is Paragraph p && HasText(tab, p, 1));

    private static Paragraph ParagraphAt(WriteTab tab, int index) => (Paragraph)tab.Docx.Model.Body.Blocks[index];

    private static TextRange WholeOf(Paragraph paragraph)
    {
        var length = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length;
        return new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, length));
    }

    // The style step: the whole first paragraph bold, then "Update style to match" (the bold moves into the style).
    private static int StyleStep(WriteTab tab, EditSession session)
    {
        var index = PlainIndex(tab);
        var range = WholeOf(ParagraphAt(tab, index));
        Assert.NotNull(session.Apply(e => e.ToggleCharacter(range, CharacterToggle.Bold), 0));
        var again = WholeOf(ParagraphAt(tab, index));
        var result = session.Apply(e => e.UpdateStyleToMatch(again), 0);
        Assert.NotNull(result?.Style);
        return index;
    }

    private static string Text(DocumentLayout layout, int page) => layout.Page(page).Text;

    private static bool BoldRunWith(Paragraph paragraph, string text) =>
        paragraph.Inlines.OfType<Run>().Any(r => r.Properties.Bold && r.Content.OfType<TextContent>().Any(t => t.Text.Contains(text, StringComparison.Ordinal)));

    [AvaloniaFact]
    public async Task AStyleResultRestylesTheActiveLayoutAndDropsTheOther()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Selection;
        tab.Runner.EnsureFlow();
        await ViewHarness.WaitAsync(() => tab.Runner.FlowLayout is not null, 30000);
        var layout = tab.Runner.PageLayout;
        var restarts = layout.Stats.Restarts;
        var index = StyleStep(tab, session);
        Assert.True(layout.Stats.Restarts > restarts);
        Assert.Same(layout, tab.Runner.PageLayout);
        Assert.Null(tab.Runner.FlowLayout);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        var fresh = DocumentLayout.Create(tab.Docx.Model, tab.Fonts, new LayoutOptions(LayoutMode.Pages, LayoutRunner.FlowTextWidth, tab.Images.Read));
        await fresh.LayOutRestAsync(null, CancellationToken.None);
        for (var i = 0; i < Math.Min(layout.LaidOutPages, 3); i++)
        {
            Assert.Equal(Text(fresh, i), Text(layout, i));
        }
    }

    [AvaloniaFact]
    public async Task SelectionChangedFiresOnlyForARealChange()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        var range = FirstTextRange(tab, 1, 3);
        var fired = 0;
        session.SelectionChanged += () => fired++;
        session.SetSelection(range);
        Assert.Equal(1, fired);
        session.SetSelection(range);
        Assert.Equal(1, fired);
        session.SetSelection(new TextRange(range.Start, range.Start));
        Assert.Equal(2, fired);

        // A replayed batch raises it once.
        var save = session.BeginSave();
        var a = new TextRange(range.Start with { Offset = 4 }, range.Start with { Offset = 4 });
        var b = new TextRange(range.Start with { Offset = 5 }, range.Start with { Offset = 5 });
        session.Apply(_ => new EditResult(a.End, [], a), 0);
        session.Apply(_ => new EditResult(b.End, [], b), 0);
        Assert.Equal(2, fired);
        save.Dispose();
        Assert.Equal(3, fired);
    }

    [AvaloniaFact]
    public async Task ThePendingFormatAppliesToTheNextTypedTextOnly()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        var at = FirstTextRange(tab, 0, 0).Start;
        session.SetSelection(new TextRange(at, at));
        session.Pending.Set([BoldOn]);
        Assert.NotNull(session.Apply(e => e.InsertText(session.Selection!.Value, "QZ", session.Pending.Changes), 0, typing: true));
        Assert.Single(session.Pending.Changes);
        Assert.True(BoldRunWith(session.Caret!.Value.Paragraph, "QZ"));

        // Another kind of edit ends it.
        Assert.NotNull(session.Apply(e => e.InsertText(session.Selection!.Value, "plain"), 0));
        Assert.Empty(session.Pending.Changes);
    }

    [AvaloniaFact]
    public async Task ACaretMoveClearsThePendingFormat()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        var range = FirstTextRange(tab, 1, 1);
        session.SetSelection(range);
        session.Pending.Set([BoldOn]);
        session.SetSelection(range);
        Assert.Single(session.Pending.Changes);
        session.SetSelection(new TextRange(range.Start with { Offset = 2 }, range.Start with { Offset = 2 }));
        Assert.Empty(session.Pending.Changes);
    }

    [AvaloniaFact]
    public async Task AnImeCommitDoesNotTakeAStaleFormat()
    {
        using var h = new ViewHarness(WindowFixture.Thesis, 900, 700);
        h.View.Runner.LeaseWaitTimeout = GateTimeout;
        h.View.Runner.Start();
        await ViewHarness.WaitAsync(() => h.View.Runner.FullModelReady, 30000);
        var session = h.View.Tab.EnsureEditor();
        var at = FirstTextRange(h.View.Tab, 2, 2).Start;
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        h.View.RaiseEvent(args);
        args.Client!.SetPreeditText("kan");
        session.Pending.Set([BoldOn]);
        h.Window.KeyTextInput("QXQ");
        ViewHarness.Pump();
        Assert.Empty(session.Pending.Changes);
        Assert.False(BoldRunWith(session.Caret!.Value.Paragraph, "QXQ"));

        // Plain typing afterwards, with a pending format, does take it.
        session.Pending.Set([BoldOn]);
        h.Window.KeyTextInput("WYW");
        ViewHarness.Pump();
        Assert.True(BoldRunWith(session.Caret!.Value.Paragraph, "WYW"));
    }

    [AvaloniaFact]
    public async Task PendingFormatNeverReachesTheModelHistoryOrRevision()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Editor;
        var model = tab.Docx.Model;
        var revision = model.Edits.Revision;
        var undo = session.Editor.History.UndoCount;
        session.Pending.Set([BoldOn]);
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(undo, session.Editor.History.UndoCount);
        Assert.False(model.Edits.IsDirty);
        session.Pending.Clear();
        Assert.Empty(session.Pending.Changes);
    }

    [AvaloniaFact]
    public async Task ToggleAfterARestyleSeesTheNewStyle()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Selection;
        var index = StyleStep(tab, session);

        // The style now carries the bold: the paragraph is effectively bold, so the toggle must switch it off (a stale resolver would say "not bold" and set it on again).
        var paragraph = ParagraphAt(tab, index);
        var first = paragraph.Inlines.OfType<Run>().First(r => r.Content.OfType<TextContent>().Any());
        Assert.True(new LayoutEffectiveFormat(tab.Docx.Model).ForRun(StoryRef.Body, paragraph, first).Bold);
        var range = WholeOf(paragraph);
        Assert.NotNull(session.Apply(e => e.ToggleCharacter(range, CharacterToggle.Bold), 0));
        var after = ParagraphAt(tab, index);
        var run = after.Inlines.OfType<Run>().First(r => r.Content.OfType<TextContent>().Any());
        Assert.False(new LayoutEffectiveFormat(tab.Docx.Model).ForRun(StoryRef.Body, after, run).Bold);
    }

    [AvaloniaFact]
    public async Task FormattingWhileSavingIsQueuedLikeTyping()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        var range = FirstTextRange(tab, 0, 3);
        _ = session.Editor;
        session.SetSelection(range);
        var save = session.BeginSave();
        Assert.Null(session.Apply(e => e.ToggleCharacter(session.Selection!.Value, CharacterToggle.Bold), 0));
        Assert.Equal(1, session.PendingCount);
        Assert.DoesNotContain(range.Start.Paragraph.Inlines.OfType<Run>(), r => r.Properties.Bold);
        save.Dispose();
        Assert.Equal(0, session.DroppedWhileSaving);
        Assert.Equal(0, session.PendingCount);
        Assert.Contains(tab.Docx.Model.Body.Blocks.OfType<Paragraph>(), p => p.Inlines.OfType<Run>().Any(r => r.Properties.Bold));
    }

    [AvaloniaFact]
    public async Task AFaultedSessionRefusesFormatting()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        var range = FirstTextRange(tab, 0, 3);
        session.SetSelection(range);
        tab.Runner.FailEdit(new InvalidOperationException("test"));
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.Null(session.Apply(e => e.ToggleCharacter(range, CharacterToggle.Bold), 0));
        Assert.DoesNotContain(range.Start.Paragraph.Inlines.OfType<Run>(), r => r.Properties.Bold);
    }
}
