using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The edit gate (the lease of the layout runner) and the editing session of a tab (Task 14 of milestone 4.3a).</summary>
public sealed class EditSessionTests
{
    // A gate that stands still fails the test with a TimeoutException instead of hanging the run (a hard timeout of the test itself cannot end a blocked UI thread).
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);

    private static WriteTab Open(string path)
    {
        var attempt = DocumentOpener.Open(Path.GetFullPath(path), new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);
        Assert.NotNull(attempt.Document);
        return new WriteTab(path, attempt.Document!);
    }

    private static async Task WaitAsync(Func<bool> condition, int milliseconds = 30000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 10)
        {
            await Task.Delay(10);
            WindowFixture.Pump();
        }

        Assert.True(condition(), "the condition was not met in time");
    }

    // A tab whose whole model is loaded (the background layout may still be running).
    private static async Task<WriteTab> OpenWholeAsync(string path)
    {
        var tab = Open(path);
        tab.Runner.LeaseWaitTimeout = GateTimeout;
        tab.Runner.Start();
        await WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        return tab;
    }

    private static Func<TextEditor, EditResult?> Type(EditSession session, string text) => e => e.InsertText(session.Selection!.Value, text);

    private static string Text(DocumentLayout layout, int page) => layout.Page(page).Text;

    private static DocumentLayout Fresh(WriteTab tab, LayoutMode mode)
    {
        var layout = DocumentLayout.Create(tab.Docx.Model, tab.Fonts, new LayoutOptions(mode, LayoutRunner.FlowTextWidth, tab.Images.Read));
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        return layout;
    }

    // Every published page of the layout reads as the page of a layout made from scratch over the same model.
    private static void AssertEqualToFresh(WriteTab tab, DocumentLayout layout, LayoutMode mode)
    {
        var fresh = Fresh(tab, mode);
        var count = layout.LaidOutPages;
        Assert.True(count > 0);
        Assert.True(count <= fresh.LaidOutPages);
        for (var i = 0; i < count; i++)
        {
            Assert.True(layout.TryGetPage(i, out var page), $"page {i}");
            Assert.Equal(fresh.Page(i).Text, page!.Text);
        }
    }

    [AvaloniaFact]
    public async Task NoPageIsLaidOutWhileLeased()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var runner = tab.Runner;
        var layout = runner.PageLayout;
        EditLease lease;
        int before;
        lease = runner.PauseForEdit();
        try
        {
            before = layout.LaidOutPages;
            Assert.False(layout.IsComplete, "the thesis was laid out before the lease: the test shows nothing");
            Thread.Sleep(400);
            Assert.Equal(before, layout.LaidOutPages);
        }
        finally
        {
            lease.Dispose();
        }

        await WaitAsync(() => layout.IsComplete);
        Assert.True(layout.LaidOutPages > before);
    }

    [AvaloniaFact]
    public async Task AFooterEditIsFinishedInTheBackgroundAndALeaseStopsThePassAtABatchBoundary()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var runner = tab.Runner;
        var layout = runner.PageLayout;
        await WaitAsync(() => layout.IsSettled);

        var model = tab.Docx.Model;
        var footer = model.HeadersFooters.Where(h => h.Value.Kind == HeaderFooterKind.Footer)
            .SelectMany(h => h.Value.Content.Blocks.OfType<Paragraph>().Select(p => (Id: h.Key, Paragraph: p)))
            .First(f => ParagraphProjection.Build(f.Paragraph, layout.ProjectionHost(new StoryRef(StoryKind.HeaderFooter, f.Id), f.Paragraph)).Text.Length > 0);
        var story = new StoryRef(StoryKind.HeaderFooter, footer.Id);
        var length = ParagraphProjection.Build(footer.Paragraph, layout.ProjectionHost(story, footer.Paragraph)).Text.Length;
        var session = tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(story, footer.Paragraph, length);
        session.SetSelection(new TextRange(at, at));

        Assert.NotNull(session.Apply(Type(session, "!"), 100));
        Assert.True(layout.Stats.ChromeOnly >= 1 || layout.Stats.Restarts >= 1);

        // The lease asked at once stops the background pass at a batch boundary: while it is held nothing is made again, and when it ends the pass goes on.
        using (var lease = runner.PauseForEdit())
        {
            var waiting = layout.StaleChromePages;
            Thread.Sleep(300);
            Assert.Equal(waiting, layout.StaleChromePages);
        }

        await WaitAsync(() => layout.IsSettled);
        Assert.False(layout.ChromeRefreshPending);
        AssertEqualToFresh(tab, layout, LayoutMode.Pages);
    }

    [AvaloniaFact]
    public async Task EditRefusedWhileOnlyAPrefixIsOpen()
    {
        using var gate = new ManualResetEventSlim();
        var tab = Open(WindowFixture.Thesis);
        try
        {
            tab.Runner.BeforeFullOpen = () => gate.Wait(TimeSpan.FromSeconds(30));
            tab.Runner.Start();
            var session = tab.EnsureEditor();
            Assert.False(session.CanEdit);
            Assert.Equal(EditBlockedReason.StillOpening, session.BlockedReason);
            var ran = false;
            Assert.Null(session.Apply(_ => { ran = true; return null; }, 0));
            Assert.False(ran);
            Assert.True(tab.Docx.IsPrefix);
            var changes = 0;
            session.Changed += () => changes++;
            Assert.Null(session.Apply(_ => throw new InvalidOperationException("must not run"), 0));
            Assert.Equal(0, changes);
        }
        finally
        {
            gate.Set();
            await WaitAsync(() => tab.Runner.FullModelReady);
            tab.Dispose();
        }

        Assert.True(tab.EnsureEditor().BlockedReason is EditBlockedReason.Faulted);
    }

    [AvaloniaFact]
    public async Task EditUpdatesTheActiveLayoutAndDropsTheOther()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var runner = tab.Runner;
        runner.EnsureFlow();
        await WaitAsync(() => runner.FlowLayout is not null);
        var session = tab.EnsureEditor();
        var changes = 0;
        session.Changed += () => changes++;
        var result = session.Apply(Type(session, "Q7z "), 0);
        Assert.NotNull(result);
        Assert.Equal(1, changes);
        Assert.Contains("Q7z ", Text(runner.PageLayout, 0), StringComparison.Ordinal);
        Assert.Null(runner.FlowLayout);
        AssertEqualToFresh(tab, runner.PageLayout, LayoutMode.Pages);

        runner.EnsureFlow();
        await WaitAsync(() => runner.FlowLayout is not null);
        Assert.Contains("Q7z ", Text(runner.FlowLayout!, 0), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task EditInTheWritingViewFollowsTheStripsAndMakesThePagesAgainOnTheSwitch()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var runner = tab.Runner;
        runner.EnsureFlow();
        await WaitAsync(() => runner.FlowLayout is not null);
        var session = tab.EnsureEditor();
        session.ActiveMode = LayoutMode.Flow;
        Assert.NotNull(session.Apply(Type(session, "Q7z "), 0));
        Assert.True(runner.PageLayoutIsStale);
        Assert.Contains("Q7z ", Text(runner.FlowLayout!, 0), StringComparison.Ordinal);
        AssertEqualToFresh(tab, runner.FlowLayout!, LayoutMode.Flow);

        session.ActiveMode = LayoutMode.Pages;
        Assert.False(runner.PageLayoutIsStale);
        Assert.Same(runner.PageLayout, tab.Layout);
        Assert.Contains("Q7z ", Text(runner.PageLayout, 0), StringComparison.Ordinal);
        await WaitAsync(() => runner.PageLayout.IsComplete);
        AssertEqualToFresh(tab, runner.PageLayout, LayoutMode.Pages);
    }

    [AvaloniaFact]
    public async Task InputDuringASaveIsQueuedAndReplayedInOrder()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var session = tab.EnsureEditor();
        var changes = 0;
        session.Changed += () => changes++;
        var save = session.BeginSave();
        Assert.False(session.CanEdit);
        Assert.Equal(EditBlockedReason.Saving, session.BlockedReason);
        foreach (var letter in new[] { "Q", "7", "z" })
        {
            Assert.Null(session.Apply(Type(session, letter), 0));
        }

        Assert.Equal(3, session.PendingCount);
        Assert.DoesNotContain("Q7z", Text(tab.Runner.PageLayout, 0), StringComparison.Ordinal);
        Assert.Equal(0, changes);
        save.Dispose();
        save.Dispose();
        Assert.Equal(0, session.PendingCount);
        Assert.True(session.CanEdit);
        var text = Text(tab.Runner.PageLayout, 0);
        Assert.Equal(1, text.Split("Q7z").Length - 1);
        Assert.Equal(1, changes);
        AssertEqualToFresh(tab, tab.Runner.PageLayout, LayoutMode.Pages);
    }

    [AvaloniaFact]
    public async Task InputQueuedByASaveThatEndsOnAFaultedSessionIsReportedAndCounted()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        var dropped = new List<EditBlockedReason>();
        session.InputDropped += dropped.Add;
        var save = session.BeginSave();
        Assert.Null(session.Apply(Type(session, "a"), 0));
        Assert.Null(session.Apply(Type(session, "b"), 0));
        tab.Runner.FailEdit(new InvalidOperationException("an edit failed during the save"));

        save.Dispose();

        // The session is no longer editable: nothing was replayed, the user is told once, and the two operations are counted.
        Assert.Equal([EditBlockedReason.Faulted], dropped);
        Assert.Equal(2, session.DroppedWhileSaving);
        Assert.Equal(0, session.PendingCount);
    }

    [AvaloniaFact]
    public async Task AReplayedOperationThatFailsFaultsTheSessionOnceAndTheRestOfTheBatchIsCounted()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        var dropped = new List<EditBlockedReason>();
        session.InputDropped += dropped.Add;
        var ran = new List<string>();
        var save = session.BeginSave();
        Assert.Null(session.Apply(ed => { ran.Add("first"); return Type(session, "a")(ed); }, 0));
        Assert.Null(session.Apply(_ => throw new InvalidOperationException("a bug in a queued operation"), 0));
        Assert.Null(session.Apply(ed => { ran.Add("third"); return Type(session, "c")(ed); }, 0));

        save.Dispose();

        Assert.Equal(["first"], ran);
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.NotNull(tab.Runner.Fault);
        Assert.Equal([EditBlockedReason.Faulted], dropped);
        Assert.Equal(2, session.DroppedWhileSaving);
        Assert.Equal(0, session.PendingCount);
    }

    [AvaloniaFact]
    public async Task AReplayedOperationThatFailsWithAnArgumentExceptionFaultsTheSession()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        var dropped = new List<EditBlockedReason>();
        session.InputDropped += dropped.Add;
        var save = session.BeginSave();
        Assert.Null(session.Apply(_ => throw new ArgumentOutOfRangeException(null, "after a change"), 0));
        Assert.Null(session.Apply(Type(session, "x"), 0));

        save.Dispose();

        // Only the editor's own stale-position refusal is a plain refusal: any other exception, argument exceptions included, may have come after a change.
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.Equal([EditBlockedReason.Faulted], dropped);
        Assert.Equal(2, session.DroppedWhileSaving);
    }

    [AvaloniaFact]
    public async Task AReplayedOperationThatOnlyRefusesAStalePositionIsSkippedAndTheBatchGoesOn()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        var dropped = new List<EditBlockedReason>();
        session.InputDropped += dropped.Add;
        var save = session.BeginSave();
        Assert.Null(session.Apply(_ => throw new StalePositionException("the paragraph is not in the story of the position"), 0));
        Assert.Null(session.Apply(Type(session, "Q7z "), 0));

        save.Dispose();

        Assert.Null(session.BlockedReason);
        Assert.Empty(dropped);
        Assert.Equal(1, session.DroppedWhileSaving);
        Assert.Contains("Q7z ", Text(tab.Runner.PageLayout, 0), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheQueueOfASaveIsBounded()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        using (session.BeginSave())
        {
            for (var i = 0; i < EditSession.MaxQueued + 5; i++)
            {
                Assert.Null(session.Apply(_ => null, 0));
            }

            Assert.Equal(EditSession.MaxQueued, session.PendingCount);
            Assert.Equal(5, session.DroppedWhileSaving);
        }

        Assert.Equal(0, session.PendingCount);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoEditSession()
    {
        var tab = Open(WindowFixture.Thesis);
        try
        {
            Assert.Null(tab.Editor);
            _ = tab.Runner;
            tab.Runner.Start();
            await WaitAsync(() => tab.Runner.FullModelReady);
            Assert.Null(tab.Editor);
            var session = tab.EnsureEditor();
            Assert.Same(session, tab.Editor);
            Assert.Same(session, tab.EnsureEditor());
            Assert.Equal(0, session.PendingCount);
        }
        finally
        {
            tab.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ASecondRequestOnTheSameThreadThrowsAndAnotherThreadWaits()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var runner = tab.Runner;
        var first = runner.PauseForEdit();
        var message = Assert.Throws<InvalidOperationException>(() => runner.PauseForEdit()).Message;
        Assert.Contains("lease", message, StringComparison.Ordinal);

        using var acquired = new ManualResetEventSlim();
        var other = Task.Run(() =>
        {
            using var second = runner.PauseForEdit();
            acquired.Set();
        });
        Assert.False(acquired.Wait(300));
        first.Dispose();
        first.Dispose();
        Assert.True(await Task.Run(() => acquired.Wait(10000)));
        await other;
        using (runner.PauseForEdit())
        {
        }
    }

    [AvaloniaFact]
    public async Task TheLayoutResumesAfterTheLease()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var runner = tab.Runner;
        var layout = runner.PageLayout;
        using (runner.PauseForEdit())
        {
            Thread.Sleep(50);
        }

        await WaitAsync(() => layout.IsComplete);
        await runner.PageTask;
        var pages = layout.LaidOutPages;
        Assert.True(pages > 100);

        // After the end an edit makes the layout incomplete again: the loop is started again by the release of the lease.
        var session = tab.EnsureEditor();
        Assert.NotNull(session.Apply(Type(session, "Q7z "), 0));
        await WaitAsync(() => layout.IsComplete);
        AssertEqualToFresh(tab, layout, LayoutMode.Pages);
        Assert.True(layout.LaidOutPages >= pages);
    }

    [AvaloniaFact]
    public async Task ARefusedOperationChangesNothingAndRaisesNothing()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        var changes = 0;
        session.Changed += () => changes++;
        var before = Text(tab.Runner.PageLayout, 0);
        Assert.Null(session.Apply(_ => null, 0));
        Assert.Null(session.Apply(e => e.InsertText(session.Selection!.Value, string.Empty), 0));
        Assert.Equal(0, changes);
        Assert.Equal(before, Text(tab.Runner.PageLayout, 0));
        Assert.False(tab.Runner.IsLeased);
    }

    [AvaloniaFact]
    public async Task ASelectedResultSetsTheSelectionAndChangesNoDocument()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        var start = session.Selection!.Value.Start;
        var range = new TextRange(start, start with { Offset = 2 });
        var changes = 0;
        session.Changed += () => changes++;
        var revision = tab.Docx.Model.Edits.Revision;
        var result = session.Apply(_ => new EditResult(range.End, [], range), 0);
        Assert.NotNull(result);
        Assert.Equal(range, session.Selection);
        Assert.Equal(range.End, session.Caret);
        Assert.Equal(1, changes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task AnEditOfSeveralBlocksKeepsTheLayoutRight()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var session = tab.EnsureEditor();
        var result = session.Apply(Type(session, "Q7z\nA8y\nB9x"), 1);
        Assert.NotNull(result);
        Assert.Contains(result.Changes, c => c.Added > c.Removed);
        var text = Text(tab.Runner.PageLayout, 0);
        Assert.Contains("Q7z", text, StringComparison.Ordinal);
        Assert.Contains("A8y", text, StringComparison.Ordinal);
        AssertEqualToFresh(tab, tab.Runner.PageLayout, LayoutMode.Pages);
        Assert.True(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task TheSessionRebindsWhenTheModelIsAnotherObject()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        Assert.NotNull(session.Apply(Type(session, "Q"), 0));
        var first = session.Editor;
        Assert.True(session.CanUndo);
        var model = tab.Docx.Model;
        Assert.Same(first, session.Editor);

        // A reload of the document: another model object.
        tab.CompleteOpen(Raffica.Write.Docx.DocxFile.Open(tab.Docx.Package));
        Assert.NotSame(model, tab.Docx.Model);
        var second = session.Editor;
        Assert.NotSame(first, second);
        Assert.False(session.CanUndo);
        Assert.False(first.History.CanUndo);
        Assert.Same(tab.Docx.Model.Body.Blocks[0], session.Selection!.Value.Start.Paragraph);
        Assert.Equal(0, session.Selection!.Value.Start.Offset);
    }

    [AvaloniaFact]
    public async Task AFailedOpenIsFaultedNotOpening()
    {
        var tab = Open(WindowFixture.Thesis);
        try
        {
            tab.Runner.BeforeFullOpen = () => throw new InvalidOperationException("broken");
            tab.Runner.Start();
            await WaitAsync(() => tab.Runner.IsFaulted);
            var session = tab.EnsureEditor();
            Assert.False(session.CanEdit);
            Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
            Assert.Null(session.Apply(_ => throw new InvalidOperationException("must not run"), 0));
        }
        finally
        {
            tab.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task TheSessionBelongsToTheUiThread()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => session.Apply(_ => null, 0)));
    }

    [AvaloniaFact]
    public async Task GateOnlyTwoHundredLeasesWithEditsWhileTheLayoutRunsNeitherDeadlockNorThrow()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var runner = tab.Runner;
        var layout = runner.PageLayout;
        var model = tab.Docx.Model;
        var editor = new TextEditor(model, layout.ProjectionHost);
        var paragraph = model.Body.Blocks.OfType<Paragraph>().First();
        var caret = new TextPosition(StoryRef.Body, paragraph, 0);
        var cycles = 0;

        // The gate alone (the editor and the layout are driven by hand, not through the session) on a thread of its own so that a deadlock fails the wait below instead of hanging the run (the gate does not need the UI thread).
        var worker = Task.Run(() =>
        {
            while (Volatile.Read(ref cycles) < 200)
            {
                using var lease = runner.PauseForEdit();
                var result = editor.InsertText(new TextRange(caret, caret), "x");
                foreach (var change in result.Changes)
                {
                    layout.ApplyEdit(change, 0);
                }

                caret = result.Caret;
                Interlocked.Increment(ref cycles);
            }
        });
        var finished = await Task.WhenAny(worker, Task.Delay(TimeSpan.FromSeconds(90)));
        Assert.True(ReferenceEquals(finished, worker), $"the gate stood still after {Volatile.Read(ref cycles)} cycles");
        await worker;
        Assert.False(runner.IsLeased);
        Assert.Null(runner.Fault);
        await WaitAsync(() => layout.IsComplete);
        AssertEqualToFresh(tab, layout, LayoutMode.Pages);
    }

    [AvaloniaFact]
    public async Task OneHundredAppliesOnTheUiThreadWhileTheLayoutRuns()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var session = tab.EnsureEditor();
        for (var i = 0; i < 100; i++)
        {
            Assert.NotNull(session.Apply(Type(session, "x"), 0));
            if (i % 10 == 0)
            {
                WindowFixture.Pump();
            }
        }

        await WaitAsync(() => tab.Runner.PageLayout.IsComplete);
        Assert.Null(tab.Runner.Fault);
        AssertEqualToFresh(tab, tab.Runner.PageLayout, LayoutMode.Pages);
        Assert.Equal(100, tab.Runner.PageLayout.Stats.Edits);
    }

    [AvaloniaFact]
    public async Task ASaveHandleDisposedOnAPoolThreadEndsTheSaveAndReplays()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var session = tab.EnsureEditor();
        var handle = session.BeginSave();
        foreach (var letter in new[] { "Q", "7", "z" })
        {
            Assert.Null(session.Apply(Type(session, letter), 0));
        }

        await Task.Run(() => handle.Dispose());
        await WaitAsync(() => session.CanEdit);
        Assert.Equal(0, session.PendingCount);
        Assert.Equal(1, Text(tab.Runner.PageLayout, 0).Split("Q7z").Length - 1);
        Assert.NotNull(session.Apply(Type(session, "k"), 0));
    }

    [AvaloniaFact]
    public async Task ADoubleDisposeFromTwoThreadsEndsTheSaveOnce()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var session = tab.EnsureEditor();
        var handle = session.BeginSave();
        var runs = 0;
        Assert.Null(session.Apply(_ => { runs++; return null; }, 0));
        using var barrier = new Barrier(2);
        await Task.WhenAll(
            Task.Run(() => { barrier.SignalAndWait(); handle.Dispose(); }),
            Task.Run(() => { barrier.SignalAndWait(); handle.Dispose(); }));
        await WaitAsync(() => session.CanEdit);
        WindowFixture.Pump();
        Assert.Equal(1, runs);

        // The count came back to 0 exactly: a second save starts and ends cleanly.
        using (session.BeginSave())
        {
            Assert.False(session.CanEdit);
        }

        Assert.True(session.CanEdit);
    }

    [AvaloniaFact]
    public async Task AFailedLayoutUpdateMakesTheSessionFaulted()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var runner = tab.Runner;
        var layout = runner.PageLayout;
        await WaitAsync(() => layout.IsComplete);
        runner.EnsureFlow();
        await WaitAsync(() => runner.FlowLayout is not null);
        var session = tab.EnsureEditor();
        var changes = 0;
        session.Changed += () => changes++;
        layout.PageFinished = _ => throw new InvalidOperationException("seam");
        Assert.Throws<InvalidOperationException>(() => session.Apply(Type(session, "Q"), 0));
        Assert.Equal(1, changes);
        Assert.False(session.CanEdit);
        Assert.Equal(EditBlockedReason.Faulted, session.BlockedReason);
        Assert.True(runner.IsFaulted);
        Assert.Null(runner.FlowLayout);
        Assert.False(runner.IsLeased);

        var revision = tab.Docx.Model.Edits.Revision;
        var ran = false;
        Assert.Null(session.Apply(e => { ran = true; return e.InsertText(session.Selection!.Value, "R"); }, 0));
        Assert.False(ran);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public async Task ALeaseThatIsNeverFreeTimesOutAndLeavesTheGateUsable()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Poi);
        var runner = tab.Runner;
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using var lease = runner.PauseForEdit();
            held.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        });
        Assert.True(await Task.Run(() => held.Wait(10000)));
        runner.LeaseWaitTimeout = TimeSpan.FromMilliseconds(300);
        Assert.Throws<TimeoutException>(() => runner.PauseForEdit());
        release.Set();
        await holder;
        runner.LeaseWaitTimeout = GateTimeout;
        using (runner.PauseForEdit())
        {
        }

        Assert.False(runner.IsLeased);
    }
}
