using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Panes;

/// <summary>The outline follows the edits of the document (task of the final review of milestone 4.3a): it is read where the model is edited, re-read after a pause in the edits, and a click lands on the heading's current block.</summary>
public sealed class OutlineFollowsEditsTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(60);

    private static string Document(WindowFixture fixture) => OutlinePaneTests.WriteDocx(
        fixture.Folder,
        "outline.docx",
        OutlinePaneTests.P("Intro text")
        + OutlinePaneTests.P("Alpha", "Chapter")
        + OutlinePaneTests.P("Body of alpha")
        + OutlinePaneTests.P("Beta", "Chapter"));

    private static async Task<(WriteWindow Window, WriteTab Tab, OutlinePane Pane, EditSession Session)> OpenAsync(WindowFixture fixture, Func<WordDocument, IReadOnlyList<OutlineHeading>>? reader = null)
    {
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(Document(fixture))).Tab;
        WindowFixture.Pump();
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady, 30000);
        var session = tab.EnsureEditor();
        window.RunCommand(WriteCommandIds.Outline);
        var pane = window.OutlineOf(tab)!;
        pane.RefreshDelay = Delay;
        if (reader is not null)
        {
            pane.Reader = reader;
        }

        await ViewHarness.WaitAsync(() => pane.LoadTask.IsCompleted, 30000);
        return (window, tab, pane, session);
    }

    private static Paragraph P(WriteTab tab, int index) => (Paragraph)tab.Docx.Model.Body.Blocks[index];

    private static TextRange Caret(Paragraph paragraph, int offset) => new(new TextPosition(StoryRef.Body, paragraph, offset), new TextPosition(StoryRef.Body, paragraph, offset));

    private static void Enter(EditSession session, Paragraph paragraph, int offset) =>
        Assert.NotNull(session.Apply(e => e.InsertText(Caret(paragraph, offset), "\n"), 0));

    private static async Task SettleAsync(OutlinePane pane, int reads)
    {
        await ViewHarness.WaitAsync(() => pane.ReadCount >= reads, 10000);
        ViewHarness.Pump();
    }

    [AvaloniaFact]
    public async Task TheHeadingListGainsAHeadingWhenEnterSplitsOneAndLosesOneWhenItIsEmptied()
    {
        using var fixture = new WindowFixture();
        var (_, tab, pane, session) = await OpenAsync(fixture);
        Assert.Equal(["Alpha", "Beta"], pane.Entries.Select(e => e.Text));
        Assert.Equal(1, pane.ReadCount);

        // Enter in the middle of "Alpha": both halves keep the heading style, so the list has three headings.
        Enter(session, P(tab, 1), 2);
        await SettleAsync(pane, 2);
        Assert.Equal(["Al", "pha", "Beta"], pane.Entries.Select(e => e.Text));
        Assert.Equal([1, 2, 4], pane.Entries.Select(e => e.BodyBlockIndex));

        // The text of "Beta" removed: an empty heading is no heading.
        var beta = P(tab, 4);
        Assert.NotNull(session.Apply(e => e.Delete(new TextRange(new TextPosition(StoryRef.Body, beta, 0), new TextPosition(StoryRef.Body, beta, 4))), 0));
        await SettleAsync(pane, 3);
        Assert.Equal(["Al", "pha"], pane.Entries.Select(e => e.Text));
        Assert.Equal(2, pane.List.ItemsSource!.Cast<object>().Count());
    }

    [AvaloniaFact]
    public async Task AClickAfterAnEditGoesToTheCurrentBlockOfTheHeadingEvenBeforeTheListWasRead()
    {
        using var fixture = new WindowFixture();
        var (_, tab, pane, session) = await OpenAsync(fixture);
        var chosen = new List<int>();
        pane.Navigate += chosen.Add;
        Assert.Equal([1, 3], pane.Entries.Select(e => e.BodyBlockIndex));

        // Two paragraphs are added above the headings; the pane has not re-read yet, so its entries still say 1 and 3.
        Enter(session, P(tab, 0), 3);
        Enter(session, P(tab, 1), 0);
        Assert.Equal(1, pane.ReadCount);
        pane.Choose(1);

        // "Beta" is now block 5 of the body.
        Assert.Equal([5], chosen);
        Assert.Equal("Beta", Text(tab, 5));

        await SettleAsync(pane, 2);
        pane.Choose(0);
        Assert.Equal([5, 3], chosen);
        Assert.Equal("Alpha", Text(tab, 3));
    }

    private static string Text(WriteTab tab, int index) =>
        ParagraphProjection.Build(P(tab, index), ProjectionHost.Plain).Text;

    [AvaloniaFact]
    public async Task AHeadingThatNoLongerExistsRaisesNoNavigation()
    {
        using var fixture = new WindowFixture();
        var (_, tab, pane, session) = await OpenAsync(fixture);
        var chosen = new List<int>();
        pane.Navigate += chosen.Add;
        var beta = P(tab, 3);
        Assert.NotNull(session.Apply(e => e.Delete(new TextRange(new TextPosition(StoryRef.Body, P(tab, 2), 5), new TextPosition(StoryRef.Body, beta, 4))), 0));

        pane.Choose(1);

        Assert.Empty(chosen);
    }

    [AvaloniaFact]
    public async Task TheReadRunsOnTheUiThreadNotOnAPoolThread()
    {
        using var fixture = new WindowFixture();
        var onUiThread = new List<bool>();
        var (_, tab, pane, session) = await OpenAsync(
            fixture,
            document =>
            {
                onUiThread.Add(Dispatcher.UIThread.CheckAccess());
                return OutlineSource.ReadHeadings(document);
            });

        // The first read ran before the reader seam was set (OpenAsync sets it after the pane exists); edit and read again through the seam.
        Enter(session, P(tab, 0), 3);
        await SettleAsync(pane, 2);

        Assert.NotEmpty(onUiThread);
        Assert.All(onUiThread, Assert.True);
    }

    [AvaloniaFact]
    public async Task ManyEditsInABurstAreOneRead()
    {
        using var fixture = new WindowFixture();
        var (_, tab, pane, session) = await OpenAsync(fixture);
        Assert.False(pane.RefreshTimerCreated);
        pane.RefreshDelay = TimeSpan.FromMilliseconds(250);

        for (var i = 0; i < 20; i++)
        {
            Assert.NotNull(session.Apply(e => e.InsertText(Caret(P(tab, 0), 0), "x"), 0));
        }

        Assert.True(pane.RefreshTimerCreated);
        Assert.Equal(1, pane.ReadCount);
        await SettleAsync(pane, 2);
        await Task.Delay(400);
        ViewHarness.Pump();
        Assert.Equal(2, pane.ReadCount);
    }

    [AvaloniaFact]
    public async Task NoTimerExistsBeforeTheFirstEditAndTypingInBodyTextLeavesTheListAlone()
    {
        using var fixture = new WindowFixture();
        var (_, tab, pane, session) = await OpenAsync(fixture);
        Assert.False(pane.RefreshTimerCreated);
        var rows = pane.List.ItemsSource;

        Assert.NotNull(session.Apply(e => e.InsertText(Caret(P(tab, 0), 0), "x"), 0));
        await SettleAsync(pane, 2);

        // The headings did not change (the paragraphs are the same objects), so the list is not rebuilt.
        Assert.Same(rows, pane.List.ItemsSource);
    }

    [AvaloniaFact]
    public async Task TheSelectedHeadingStaysSelectedWhenTheListIsRebuilt()
    {
        using var fixture = new WindowFixture();
        var (_, tab, pane, session) = await OpenAsync(fixture);
        pane.List.SelectedIndex = 1;
        Assert.Equal("Beta", pane.Entries[1].Text);

        Enter(session, P(tab, 1), 2);
        await SettleAsync(pane, 2);

        Assert.Equal(["Al", "pha", "Beta"], pane.Entries.Select(e => e.Text));
        Assert.Equal(2, pane.List.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task AnEditWithoutAPaneCreatesNoTimerAndNoRead()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(Document(fixture))).Tab;
        WindowFixture.Pump();
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady, 30000);
        var session = tab.EnsureEditor();

        Enter(session, P(tab, 0), 3);

        Assert.Null(window.OutlineOf(tab));
        Assert.Null(window.OutlineHost);
    }
}
