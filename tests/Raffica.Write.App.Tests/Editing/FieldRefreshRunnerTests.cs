using Avalonia.Headless.XUnit;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>
/// The refresh of field results after an editing burst and before a save (milestone 4.5b, task 6): when it runs and when it waits, what it leaves alone (the revision, the dirty mark, the history), and how the selection, the bar and the
/// status line follow it. A manual clock stands for the timer, as the autosave tests do.
/// </summary>
public sealed class FieldRefreshRunnerTests
{
    private static string Caption(WriteTab tab, int block) => FieldTestDocument.TextOf(tab, block);

    [AvaloniaFact]
    public async Task AnEditBurstRefreshesOnce250MsAfterTheLastEdit()
    {
        using var h = await FieldHarness.OpenAsync();
        Assert.Equal("Figure 77", Caption(h.Tab, 1));

        h.Type("a");
        h.Type("b");
        var revision = h.Tab.Docx.Model.Edits.Revision;
        var runner = Assert.IsType<FieldRefreshRunner>(h.Tab.Fields);

        // Two edits: one timer, started over by the second, 250 ms long, and nothing has been refreshed yet.
        Assert.Equal(1, h.Clock.Created);
        Assert.Equal(2, h.Clock.Restarts);
        Assert.Equal(TimeSpan.FromMilliseconds(250), h.Clock.LastDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(250), FieldRefreshRunner.Delay);
        Assert.Equal(0, runner.Passes);

        h.Clock.Fire();

        Assert.Equal(1, runner.Passes);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
        Assert.Equal("Figure 2", Caption(h.Tab, 2));
        Assert.Equal("Index idx", Caption(h.Tab, 3));

        // A refresh is not an edit: the revision, the history and the timer are as they were.
        Assert.Equal(revision, h.Tab.Docx.Model.Edits.Revision);
        Assert.True(h.Tab.Docx.Model.Edits.DerivedVersion > 0);
        Assert.False(h.Clock.IsArmed);
        h.Clock.Fire();
        Assert.Equal(1, runner.Passes);
        Assert.Equal(0, runner.Failures);
    }

    [AvaloniaFact]
    public async Task NothingRunsInsideTheKeyHandler()
    {
        using var h = await FieldHarness.OpenAsync();
        var model = h.Tab.Docx.Model;
        var derived = model.Edits.DerivedVersion;

        h.Type("a");

        // The edit returned (the key handler is over): no refresh ran, the old results are on the screen and the timer is armed.
        var runner = h.Tab.Fields!;
        Assert.Equal(0, runner.Passes);
        Assert.Equal(derived, model.Edits.DerivedVersion);
        Assert.Equal("Figure 77", Caption(h.Tab, 1));
        Assert.True(h.Clock.IsArmed);
        Assert.True(runner.IsPending);
    }

    [AvaloniaFact]
    public async Task ASaveInProgressDelaysTheRefresh()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var session = h.Tab.Editor!;
        var runner = h.Tab.Fields!;

        var save = session.BeginSave();
        h.Clock.Fire();
        Assert.Equal(0, runner.Passes);
        Assert.True(h.Clock.IsArmed, "it looks again after the delay");
        Assert.Equal("Figure 77", Caption(h.Tab, 1));

        save.Dispose();
        h.Clock.Fire();

        Assert.Equal(1, runner.Passes);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
    }

    [AvaloniaFact]
    public async Task AnImeCompositionDelaysTheRefresh()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var session = h.Tab.Editor!;
        var runner = h.Tab.Fields!;
        var at = session.Selection!.Value.Start;

        Assert.True(session.SetPreedit(at, "ni", 0));
        h.Clock.Fire();
        Assert.Equal(0, runner.Passes);
        Assert.True(h.Clock.IsArmed);

        Assert.True(session.SetPreedit(at, null, 0));
        h.Clock.Fire();

        Assert.Equal(1, runner.Passes);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
    }

    [AvaloniaFact]
    public async Task PrefixFaultedAndStaleLayoutSkipWithoutATrace()
    {
        // A prefix document: the thesis, before the whole file was read.
        {
            var attempt = DocumentOpener.Open(WindowFixture.Thesis, new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);
            Assert.NotNull(attempt.Document);
            using var prefix = new WriteTab(WindowFixture.Thesis, attempt.Document!);
            var clock = new ManualFieldClock();
            prefix.FieldClock = clock;
            Assert.True(prefix.Docx.IsPrefix);
            var runner = prefix.EnsureFields();
            runner.Notify(null);

            clock.Fire();

            Assert.Equal(0, runner.Passes);
            Assert.Equal(0, runner.Failures);
            Assert.Equal(0, prefix.Docx.Model.Edits.DerivedVersion);
            Assert.True(clock.IsArmed, "a prefix document is looked at again");
        }

        // A faulted layout: nothing follows any more, and the runner stops asking.
        using (var h = await FieldHarness.OpenAsync())
        {
            h.Type("a");
            var runner = h.Tab.Fields!;
            h.Tab.Runner.FailEdit(new InvalidOperationException("test"));

            h.Clock.Fire();

            Assert.Equal(0, runner.Passes);
            Assert.Equal(0, runner.Failures);
            Assert.False(runner.IsPending);
            Assert.False(h.Clock.IsArmed);
            Assert.Equal("Figure 77", Caption(h.Tab, 1));
        }

        // A layout that is not the one being edited (the writing view is active and its strips are not made yet): the refresh waits.
        using (var h = await FieldHarness.OpenAsync())
        {
            var session = h.Tab.EnsureEditor();
            session.ActiveMode = LayoutMode.Flow;
            h.Type("a");
            var runner = h.Tab.Fields!;

            h.Clock.Fire();

            Assert.Equal(0, runner.Passes);
            Assert.Equal(0, runner.Failures);
            Assert.True(runner.IsPending);
            Assert.True(h.Clock.IsArmed);
        }
    }

    [AvaloniaFact]
    public async Task AFailureIsCountedAndRetried()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var runner = h.Tab.Fields!;
        var calls = 0;
        runner.Refresher = (editor, host, scope, touched) =>
        {
            if (++calls == 1)
            {
                throw new InvalidOperationException("test");
            }

            return editor.RefreshFields(host, scope, touched);
        };

        h.Clock.Fire();

        Assert.Equal(1, runner.Failures);
        Assert.Equal(0, runner.Passes);
        Assert.True(runner.IsPending);
        Assert.True(h.Clock.IsArmed);
        Assert.False(h.Tab.Docx.Model.Edits.DerivedVersion > 0);
        Assert.False(h.Tab.Runner.IsFaulted, "a failed refresh shows nothing and breaks nothing");
        Assert.True(h.Tab.Editor!.CanEdit);

        h.Clock.Fire();

        Assert.Equal(1, runner.Failures);
        Assert.Equal(1, runner.Passes);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
    }

    [AvaloniaFact]
    public async Task AnEditDuringThePassesStartsOver()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var runner = h.Tab.Fields!;
        var seen = new List<IReadOnlyList<BlockChange>?>();
        BlockChange late = new(StoryRef.Body, h.Tab.Docx.Model.Body.Blocks, 2, 1, 1, []);
        runner.Refresher = (editor, host, scope, touched) =>
        {
            seen.Add(touched);
            if (seen.Count == 1)
            {
                // The edit arrives in the middle of the passes, and the result says that a page result may still change.
                runner.Notify([late]);
                return new FieldRefreshResult([], null, 0, 0, false);
            }

            return editor.RefreshFields(host, scope, touched);
        };

        h.Clock.Fire();

        Assert.Equal(1, runner.Passes);
        Assert.Equal(0, runner.Unsettled);
        Assert.True(runner.IsPending);
        Assert.True(h.Clock.IsArmed);

        h.Clock.Fire();

        Assert.Equal(2, runner.Passes);
        Assert.Equal([late], seen[1]);
        Assert.False(runner.IsPending);
    }

    [AvaloniaFact]
    public async Task ThreePassesThatStayUnsettledAreCounted()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var runner = h.Tab.Fields!;
        runner.Refresher = (_, _, _, _) => new FieldRefreshResult([], null, 0, 0, false);

        h.Clock.Fire();

        Assert.Equal(FieldRefreshRunner.MaxPasses, runner.Passes);
        Assert.Equal(1, runner.Unsettled);
        Assert.False(runner.IsPending);
    }

    [AvaloniaFact]
    public async Task SettleAsyncRefreshesSynchronouslyBeforeASave()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var runner = h.Tab.Fields!;
        Assert.Equal("Figure 77", Caption(h.Tab, 1));

        var task = runner.SettleAsync(h.Tab.Token);

        Assert.True(task.IsCompletedSuccessfully, "the settle is done when the call returns");
        Assert.Equal(1, runner.Passes);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
        Assert.Equal("Figure 2", Caption(h.Tab, 2));
        Assert.False(h.Clock.IsArmed, "the timer's refresh is not needed any more");
        Assert.False(runner.IsPending);

        // Nothing pending: nothing to do.
        await runner.SettleAsync(h.Tab.Token);
        Assert.Equal(1, runner.Passes);
    }

    [AvaloniaFact]
    public async Task TheFirstSettleRunsOnlyAfterTheLayoutIsComplete()
    {
        using var h = await FieldHarness.OpenAsync(layoutComplete: false);
        Assert.False(h.Tab.Runner.PageLayout.IsComplete);
        var runner = h.Tab.EnsureFields();
        runner.Notify(null);

        h.Clock.Fire();

        Assert.Equal(0, runner.Passes);
        Assert.True(h.Clock.IsArmed, "it looks again");
        Assert.Equal("Figure 77", Caption(h.Tab, 1));

        await h.CompleteLayoutAsync();
        h.Clock.Fire();

        Assert.Equal(1, runner.Passes);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
    }

    [AvaloniaFact]
    public async Task TheCaretMovesToTheStartOfAReplacedField()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var session = h.Tab.Editor!;
        var model = h.Tab.Docx.Model;
        var first = (Paragraph)model.Body.Blocks[1];
        var second = (Paragraph)model.Body.Blocks[2];

        // "Figure 77": the field result is the two characters from offset 7. A caret inside the result, and a selection end behind it.
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, first, 8), new TextPosition(StoryRef.Body, second, 9)));
        var selections = 0;
        session.SelectionChanged += () => selections++;
        h.Tab.Fields!.Refresher = null;

        h.Clock.Fire();

        var selection = session.Selection!.Value;
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
        Assert.Same(first, selection.Start.Paragraph);
        Assert.Equal(7, selection.Start.Offset);
        Assert.Same(second, selection.End.Paragraph);
        Assert.Equal(8, selection.End.Offset);
        Assert.Equal(1, selections);
    }

    [AvaloniaFact]
    public async Task ACaretBeforeAReplacedFieldKeepsItsOffset()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var session = h.Tab.Editor!;
        var first = (Paragraph)h.Tab.Docx.Model.Body.Blocks[1];
        var caret = new TextPosition(StoryRef.Body, first, 3);
        session.SetSelection(new TextRange(caret, caret));

        h.Clock.Fire();

        Assert.Equal(3, session.Selection!.Value.End.Offset);
        Assert.Same(first, session.Selection!.Value.End.Paragraph);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowMidSettleCancelsWithoutAnException()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        var runner = h.Tab.Fields!;
        using var source = new CancellationTokenSource();
        runner.Refresher = (_, _, _, _) =>
        {
            // The window closes while the passes run.
            source.Cancel();
            return new FieldRefreshResult([], null, 0, 0, false);
        };

        var task = runner.SettleAsync(source.Token);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(1, runner.Passes);
        Assert.Equal(0, runner.Failures);

        // After the tab is gone nothing runs and nothing throws.
        h.Tab.Dispose();
        FieldHarness.Pump();
        await h.Tab.SettleFieldsAsync(h.Tab.Token);
        await runner.SettleAsync(h.Tab.Token);
        h.Clock.Fire();
        Assert.Equal(1, runner.Passes);
    }

    [AvaloniaFact]
    public async Task AStructuralResultWaitsForAPendingRedoAndTheDoubleSeesTheOtherOutcomes()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        h.Type("b");
        var session = h.Tab.Editor!;
        var runner = h.Tab.Fields!;
        var model = h.Tab.Docx.Model;
        h.Clock.Fire();
        Assert.Equal(1, runner.Passes);

        // A redo is pending: an undo, then a structural result from the (test double of the) refresh must wait.
        Assert.NotNull(session.Apply(e => e.History.Undo(), 0));
        Assert.True(session.CanRedo);
        var record = new DerivedRecord([new BlockChange(StoryRef.Body, model.Body.Blocks, 1, 1, 1, [model.Body.Blocks[1].Clone()])]);
        var structural = true;
        runner.Refresher = (_, _, _, _) => structural ? new FieldRefreshResult([], record, 0, 0, true) : new FieldRefreshResult([], null, 0, 0, true);
        runner.Notify([]);

        h.Clock.Fire();

        Assert.Equal(1, runner.StructuralWaits);
        Assert.True(runner.IsPending);

        // While the redo stack is not empty the runner does not even try again.
        var passes = runner.Passes;
        h.Clock.Fire();
        Assert.Equal(passes, runner.Passes);

        // The redo is used: the next look finds nothing structural and is done; with an empty history nothing is attached.
        structural = false;
        Assert.NotNull(session.Apply(e => e.History.Redo(), 0));

        // The look at the whole body waits for a complete layout: the edits above lay out in the background, so the tick that matters is the one after that has finished. The pass that rewrites the list leaves pages to lay out, so one more tick follows it (the timer path never lays out on the UI thread).
        await ViewHarness.WaitAsync(() => h.Tab.Runner.PageLayout.IsComplete, 30000);
        h.Clock.Fire();
        Assert.True(runner.Passes > passes);
        Assert.False(runner.IsPending);
        Assert.Equal(0, runner.StructuralAttached);
    }

    [AvaloniaFact]
    public async Task ADocumentWithoutAnyFieldMakesNoPassesAndNoHostWork()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.PlainBytes());
        h.Type("a");
        var runner = h.Tab.Fields!;

        h.Clock.Fire();

        Assert.Equal(0, runner.Passes);
        Assert.Equal(0, runner.Host.LabelObjects);
        Assert.False(runner.IsPending);
        Assert.Equal(0, h.Tab.Docx.Model.Edits.DerivedVersion);
    }

    [AvaloniaFact]
    public async Task AnUndoOfTheTypedStepIsFollowedByARefreshAgain()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        h.Clock.Fire();
        var runner = h.Tab.Fields!;
        Assert.Equal(1, runner.Passes);

        // Undo and redo make the model what an older snapshot says, so the refresh looks again after each.
        Assert.NotNull(h.Tab.Editor!.Apply(e => e.History.Undo(), 0));
        Assert.True(h.Clock.IsArmed);
        h.Clock.Fire();

        Assert.Equal(2, runner.Passes);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
    }

    [AvaloniaFact]
    public async Task TheLayoutEqualsAFullLayoutAfterARefreshAndAfterUndoAndRedo()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        h.Clock.Fire();
        AssertLayoutEqualsFull(h.Tab, "after the refresh");

        Assert.NotNull(h.Tab.Editor!.Apply(e => e.History.Undo(), 0));
        h.Clock.Fire();
        AssertLayoutEqualsFull(h.Tab, "after the undo");

        Assert.NotNull(h.Tab.Editor!.Apply(e => e.History.Redo(), 0));
        h.Clock.Fire();
        AssertLayoutEqualsFull(h.Tab, "after the redo");
    }

    /// <summary>The pages of the tab's layout, once it has laid out the rest, say what the pages of a fresh layout of the same model say.</summary>
    internal static void AssertLayoutEqualsFull(WriteTab tab, string label)
    {
        var layout = tab.Runner.PageLayout;
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        var full = DocumentLayout.Create(tab.Docx.Model, tab.Fonts, new LayoutOptions());
        full.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(layout.IsComplete, label);
        Assert.Equal(full.LaidOutPages, layout.LaidOutPages);
        for (var i = 0; i < full.LaidOutPages; i++)
        {
            Assert.Equal(full.Page(i).Text, layout.Page(i).Text);
        }
    }

    [AvaloniaFact]
    public async Task ARefusedUndoThatRolledADerivedRecordBackStartsTheLayoutOver()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.Bytes(headings: true));
        var session = h.Tab.EnsureEditor();
        h.Type("a");
        var model = h.Tab.Docx.Model;
        var editor = session.Editor;

        // A record attached by hand to the newest step; then the step is made unable to undo (the style sheet it swapped is gone), so the undo reverts the record, cannot go on and puts it back.
        Assert.NotNull(session.Apply(e => e.SetHeadingNumbering(true, new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[0], 0)), 0));
        var copy = model.Body.Blocks[1].Clone();
        model.Body.Blocks.Insert(2, copy);
        var change = new BlockChange(StoryRef.Body, model.Body.Blocks, 2, 0, 1, []);
        model.Edits.RaiseDerived();
        model.Edits.AttributeDerived([change]);
        h.Tab.Runner.PageLayout.ApplyEdit(change, 2);
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(new DerivedRecord([change])));
        var restarts = h.Tab.Runner.PageLayout.Stats.Restarts;
        model.Styles = null;
        var derived = model.Edits.DerivedVersion;

        var result = session.Apply(e => e.History.Undo(), 0);

        // The undo was refused and the derived version rose with no result to explain it: the session started the layout over from the model.
        Assert.Null(result);
        Assert.True(model.Edits.DerivedVersion > derived);
        Assert.True(h.Tab.Runner.PageLayout.Stats.Restarts > restarts);
        AssertLayoutEqualsFull(h.Tab, "after the refused undo");
    }

    [AvaloniaFact]
    public async Task TheBarShowsTheNotUpdatedCountOnceAndTheStatusLineSaysItInsideSuchAField()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Write("fields.docx", FieldTestDocument.Bytes());
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        var clock = new ManualFieldClock();
        tab.FieldClock = clock;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Fields is not null);

        // The first look is made after the layout is complete: the one INDEX field is the only field Write leaves alone.
        Assert.Null(window.ShownFieldsBar);
        clock.Fire();
        WindowFixture.Pump();
        var bar = Assert.IsType<FieldsBar>(window.ShownFieldsBar);
        Assert.Equal(1, bar.Count);
        Assert.Equal(WriteStrings.FieldsBarOne, bar.Text);
        Assert.Equal("Figure 1", Caption(tab, 1));

        // Once per document session: another whole-body look says nothing new; dismissing hides the bar and it does not come back.
        tab.Fields!.Notify(null);
        clock.Fire();
        Assert.Same(bar, window.ShownFieldsBar);
        bar.Dismiss();
        tab.Fields.Notify(null);
        clock.Fire();
        WindowFixture.Pump();
        Assert.False(bar.IsShown);
        Assert.Equal(1, bar.Count);

        // The status line: a caret at the edge of the INDEX field result says so, a caret in the caption does not.
        var session = tab.EnsureEditor();
        var view = window.ViewOf(tab)!;
        var index = (Paragraph)tab.Docx.Model.Body.Blocks[3];
        var caption = (Paragraph)tab.Docx.Model.Body.Blocks[1];
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, index, 6), new TextPosition(StoryRef.Body, index, 6)));
        Assert.Equal(WriteStrings.StatusFieldNotUpdated, window.StatusCountText(view));
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, index, 2), new TextPosition(StoryRef.Body, index, 2)));
        Assert.Null(window.StatusCountText(view));
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, caption, 7), new TextPosition(StoryRef.Body, caption, 8)));
        Assert.Null(window.StatusCountText(view));
    }

    [AvaloniaFact]
    public async Task ACleanDocumentStaysCleanAndSaveStaysDisabledAfterARefresh()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Write("fields.docx", FieldTestDocument.Bytes());
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        var clock = new ManualFieldClock();
        tab.FieldClock = clock;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Fields is not null);

        clock.Fire();
        WindowFixture.Pump();

        Assert.Equal("Figure 1", Caption(tab, 1));
        Assert.Equal(0, tab.Docx.Model.Edits.Revision);
        Assert.False(tab.IsDirty);
        Assert.False(tab.Tab.IsDirty);
        Assert.NotNull(SaveCommandState.Of(tab, false).Refusal(saveAs: false));
        Assert.Null(SaveCommandState.Of(tab, false).Refusal(saveAs: true));
        Assert.Null(window.Autosave);
        Assert.Null(tab.Editor);
    }

    private sealed class ThrowingHost(IFieldHost inner) : IFieldHost
    {
        public bool IsComplete => inner.IsComplete;

        public string? NumberLabel(Paragraph paragraph) => throw new InvalidOperationException("test");

        public string? ChapterNumber(Paragraph paragraph) => throw new InvalidOperationException("test");

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => inner.PageOf(position);

        public string? NoteMarkText(int noteId, NoteType type) => inner.NoteMarkText(noteId, type);
    }

    [AvaloniaFact]
    public async Task AThrowingSubscriberNeverEscapesTheTick()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Type("a");
        h.Tab.ModelEdited += () => throw new InvalidOperationException("test");
        var runner = h.Tab.Fields!;

        h.Clock.Fire();

        Assert.Equal("Figure 1", Caption(h.Tab, 1));
        Assert.True(runner.Failures >= 1, "the failure is counted");
        Assert.False(runner.IsPending, "the results were written: nothing to retry");
        Assert.True(h.Tab.Editor!.CanEdit);
        Assert.False(h.Tab.Runner.IsFaulted);
    }

    [AvaloniaFact]
    public async Task AThrowingScanNeverEscapesTheTick()
    {
        using var h = await FieldHarness.OpenAsync();
        h.Tab.FieldScan = _ => throw new InvalidOperationException("test");
        h.Type("a");
        var runner = h.Tab.Fields!;

        h.Clock.Fire();

        Assert.True(runner.Failures >= 1);
        Assert.Equal("Figure 77", Caption(h.Tab, 1));
        Assert.True(h.Tab.Editor!.CanEdit);
    }

    [AvaloniaFact]
    public async Task AFailureHalfwayThroughTheWalkStillTellsTheLayoutAndTheRetryLeavesNothingUnreported()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.Bytes(headings: true, styleRef: true));
        h.Type("a");
        var runner = h.Tab.Fields!;
        var calls = 0;
        runner.Refresher = (editor, host, scope, touched) => editor.RefreshFields(++calls == 1 ? new ThrowingHost(host) : host, scope, touched);
        var model = h.Tab.Docx.Model;

        // The walk writes the two captions and then fails at the STYLEREF: the model is changed, so the layout, the derived version and the snapshot tracker must know it.
        h.Clock.Fire();

        Assert.Equal(1, runner.Failures);
        Assert.Equal("Figure 1", Caption(h.Tab, 1));
        Assert.True(model.Edits.DerivedVersion > 0);
        AssertLayoutEqualsFull(h.Tab, "after the failed walk");
        Assert.True(runner.IsPending);

        // The retry finds the captions as they are (nothing is left unreported) and ends.
        h.Clock.Fire();

        Assert.Equal(1, runner.Passes);
        Assert.Equal(1, runner.Failures);
        AssertLayoutEqualsFull(h.Tab, "after the retry");
        Assert.False(runner.IsPending);
    }

    [AvaloniaFact]
    public async Task AFieldThatAppearsInTheWindowOfAWholeBodyChangeIsNotLost()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.PlainBytes());
        var scans = 0;
        h.Tab.FieldScan = _ => ++scans > 1;
        h.Type("a");
        var runner = h.Tab.Fields!;
        h.Clock.Fire();
        Assert.Equal(0, runner.Passes);

        // A style or section change (everything) arrives together with an edit that brought the first field: the second scan finds it.
        runner.Notify(null);
        h.Clock.Fire();

        Assert.Equal(1, runner.Passes);
    }

    [AvaloniaFact]
    public async Task ARefusedUndoThatSwappedTheNumberedHeadingStillGivesTheRightChapterLabelAndTheEditorFormatResolvesTheClone()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.Bytes(headings: true, styleRef: true, numbered: true));
        h.Type("a");
        var runner = h.Tab.Fields!;
        h.Clock.Fire();
        Assert.Equal("Chapter 1", Caption(h.Tab, 3));
        var session = h.Tab.Editor!;
        var model = h.Tab.Docx.Model;

        // A step whose undo will be refused, a record on the heading block, and then the undo: the roll-back puts equal clones in the document, the heading is another object now.
        Assert.NotNull(session.Apply(e => e.MoveBlocks(5, 1, 4), 0));
        var heading = model.Body.Blocks[0];
        var change = new BlockChange(StoryRef.Body, model.Body.Blocks, 0, 1, 1, [heading.Clone()]);
        Assert.Equal(AttachOutcome.Attached, session.Editor.History.AttachDerived(new DerivedRecord([change])));
        model.Body.Blocks.RemoveAt(model.Body.Blocks.Count - 1);
        var derived = model.Edits.DerivedVersion;
        Assert.Null(session.Apply(e => e.History.Undo(), 0));
        Assert.True(model.Edits.DerivedVersion > derived);
        Assert.NotSame(heading, model.Body.Blocks[0]);

        // The editor's own format still resolves the numbering of the clone, and a refresh that needs the label of the swapped heading gets it.
        Assert.True(session.ResolveParagraph(StoryRef.Body, (Paragraph)model.Body.Blocks[0]).NumberingId > 0);
        var styleRef = (Paragraph)model.Body.Blocks[3];
        var field = FieldView.InParagraph(styleRef).Single();
        Assert.True(FieldWriter.SetResult(styleRef, field, "9"));
        model.Edits.RaiseDerived();
        runner.Notify(null);

        // The refresh of the whole body waits for a complete layout (the timer starts over until it is): the edits above lay out in the background, so the tick that matters is the one after that has finished.
        await ViewHarness.WaitAsync(() => h.Tab.Runner.PageLayout.IsComplete, 30000);
        h.Clock.Fire();

        Assert.Equal("Chapter 1", Caption(h.Tab, 3));
        Assert.Equal(0, runner.NotUpdated);
        Assert.Equal(0, runner.Failures);
    }

    private static TocOptions TocDefaults => new(1, 3, true, true, TocLeader.Dots, null);

    private static int TocParagraphs(WriteTab tab) =>
        TocRefresh.Find(tab.Docx.Model.Body.Blocks) is [var site, ..] ? site.Last - site.First + 1 : 0;

    private static TextRange HeadingRange(WriteTab tab, int block)
    {
        var paragraph = (Paragraph)tab.Docx.Model.Body.Blocks[block];
        return new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, FieldTestDocument.TextOf(tab, block).Length));
    }

    [AvaloniaFact]
    public async Task ARealStructuralTocRefreshIsAttachedByTheRunnerAndUndoTakesItWithTheStep()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.Bytes(headings: true, onePage: true));
        var session = h.Tab.EnsureEditor();
        var host = h.Tab.EnsureFields().Host;
        var runner = h.Tab.Fields!;
        Assert.NotNull(session.Apply(e => e.InsertToc(session.Selection!.Value.Start, TocDefaults, host), 0));
        await h.FireAsync();
        var inserted = TocParagraphs(h.Tab);
        Assert.Equal(2, inserted);

        // A paragraph becomes a heading: the list needs one more entry. The refresh runs 250 ms after the edit, not in it, and the newest step carries the structural change.
        var before = h.Tab.Docx.Model.Body.Blocks.Count;
        var endBlock = before - 1;
        Assert.NotNull(session.Apply(e => e.ApplyParagraphStyle(HeadingRange(h.Tab, endBlock), "Heading1"), 0));
        Assert.Equal(inserted, TocParagraphs(h.Tab));
        var revision = h.Tab.Docx.Model.Edits.Revision;

        await h.FireAsync();

        Assert.Equal(inserted + 1, TocParagraphs(h.Tab));
        Assert.Equal(1, runner.StructuralAttached);
        Assert.Equal(0, runner.StructuralWaits);
        Assert.Equal(0, runner.Failures);
        Assert.Equal(revision, h.Tab.Docx.Model.Edits.Revision);

        // Undo takes the step and the paragraph of the list together; the refresh that follows finds nothing structural to do.
        Assert.NotNull(session.Apply(e => e.History.Undo(), 0));
        Assert.Equal(inserted, TocParagraphs(h.Tab));
        Assert.Equal(before, h.Tab.Docx.Model.Body.Blocks.Count);
        await h.FireAsync();
        Assert.Equal(inserted, TocParagraphs(h.Tab));
        Assert.Equal(1, runner.StructuralAttached);
        Assert.NotNull(session.Apply(e => e.History.Redo(), 0));
        Assert.Equal(inserted + 1, TocParagraphs(h.Tab));
        await h.FireAsync();
        Assert.Equal(inserted + 1, TocParagraphs(h.Tab));
        Assert.Equal(1, runner.StructuralAttached);
        Assert.Equal(0, runner.Failures);
    }

    [AvaloniaFact]
    public async Task ARealStructuralTocRefreshLeavesTheListAloneWhileARedoIsPendingAndRunsAfterIt()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.Bytes(headings: true, onePage: true));
        var session = h.Tab.EnsureEditor();
        var host = h.Tab.EnsureFields().Host;
        var runner = h.Tab.Fields!;
        Assert.NotNull(session.Apply(e => e.InsertToc(session.Selection!.Value.Start, TocDefaults, host), 0));
        await h.FireAsync();
        var inserted = TocParagraphs(h.Tab);
        var endBlock = h.Tab.Docx.Model.Body.Blocks.Count - 1;
        Assert.NotNull(session.Apply(e => e.ApplyParagraphStyle(HeadingRange(h.Tab, endBlock), "Heading1"), 0));

        // The caret rests at the end of the list (an edge of the field takes no typing): the typing goes into the paragraph before the last one.
        var typed = new TextPosition(StoryRef.Body, (Paragraph)h.Tab.Docx.Model.Body.Blocks[endBlock - 1], 0);
        session.SetSelection(new TextRange(typed, typed));
        Assert.NotNull(session.Apply(e => e.InsertText(new TextRange(typed, typed), "q"), 0));

        // The undo of the typing leaves a redo pending while the list still lacks the new heading: the refresh does not touch the list and keeps asking.
        Assert.NotNull(session.Apply(e => e.History.Undo(), 0));
        Assert.True(session.CanRedo);
        var bytes = ModelBytes(h.Tab);
        await h.FireAsync();

        Assert.Equal(1, runner.StructuralWaits);
        Assert.Equal(0, runner.StructuralAttached);
        Assert.Equal(inserted, TocParagraphs(h.Tab));
        Assert.Equal(bytes, ModelBytes(h.Tab));
        Assert.True(runner.IsPending);
        var passes = runner.Passes;
        await h.FireAsync();
        Assert.Equal(passes, runner.Passes);

        // The redo is done: the list is brought up to date and the step that was redone carries the change.
        Assert.NotNull(session.Apply(e => e.History.Redo(), 0));

        // The look at the whole body waits for a complete layout: the edits above lay out in the background, so the tick that matters is the one after that has finished. The pass that rewrites the list leaves pages to lay out, so one more tick follows it (the timer path never lays out on the UI thread).
        await ViewHarness.WaitAsync(() => h.Tab.Runner.PageLayout.IsComplete, 30000);
        await h.FireUntilIdleAsync();
        Assert.True(runner.Passes > passes);
        Assert.Equal(inserted + 1, TocParagraphs(h.Tab));
        Assert.Equal(1, runner.StructuralAttached);
        Assert.False(runner.IsPending);
        Assert.Equal(0, runner.Failures);
    }

    // What the list says: the stored text of every paragraph of the first list (enough to tell that nothing was rewritten).
    private static string ModelBytes(WriteTab tab) =>
        TocRefresh.Find(tab.Docx.Model.Body.Blocks) is [var site, ..]
            ? string.Join("|", Enumerable.Range(site.First, site.Last - site.First + 1).Select(i => TocBuilder.Signature((Paragraph)site.Container[i])))
            : string.Empty;
}
