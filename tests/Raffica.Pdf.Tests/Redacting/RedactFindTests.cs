using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactFindTests
{
    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            ViewFixture.Pump();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "the condition did not become true");
    }

    private static (RedactPane Pane, RedactFindPanel Panel) OpenFind(RedactFixture f)
    {
        var pane = new RedactPane(f.Session, f.View);
        pane.ToggleFind();
        return (pane, pane.FindPanel!);
    }

    private static void Enter(RedactFindPanel panel) =>
        panel.TermBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = panel.TermBox });

    [AvaloniaFact]
    public async Task SearchRunsOnEnterNeverWhileTyping()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;

        foreach (var typed in new[] { "e", "ex", "exam", "example" })
        {
            panel.TermBox.Text = typed;
            ViewFixture.Pump();
        }

        panel.MatchCaseBox.IsChecked = true;
        panel.WholeWordsBox.IsChecked = true;
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.Equal(FindState.Idle, panel.Find.State);
        Assert.Empty(panel.Find.Hits);

        panel.MatchCaseBox.IsChecked = false;
        panel.WholeWordsBox.IsChecked = false;
        Enter(panel);
        await UntilAsync(() => panel.Find.State == FindState.Done);

        Assert.Equal("example", panel.Find.Term);
        Assert.Equal(4, panel.Find.Hits.Count);
        Assert.Equal("4 matches", panel.StatusText);
    }

    [AvaloniaFact]
    public async Task NothingIsMadeBeforeFindIsOpened()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var pane = new RedactPane(f.Session, f.View);

        Assert.False(pane.IsFindOpen);
        Assert.Null(pane.FindPanel);

        pane.ToggleFind();
        var find = pane.FindPanel!.Find;
        Assert.True(pane.IsFindOpen);
        Assert.Equal(FindState.Idle, find.State);

        pane.ToggleFind();
        Assert.False(pane.IsFindOpen);
        Assert.Null(pane.FindPanel);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => find.RunAsync("example", default));
    }

    [AvaloniaFact]
    public async Task CanceledSearchSaysSoAndShowsNoResult()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "example";

        var run = panel.SearchAsync();
        Assert.Equal(FindState.Running, panel.Find.State);
        Assert.Equal("Searching…", panel.StatusText);
        panel.Find.Cancel();

        Assert.False(await run);
        ViewFixture.Pump();
        Assert.True(panel.Find.IsCanceled);
        Assert.Empty(panel.Find.Hits);
        Assert.Equal("The search was canceled. No result is shown.", panel.StatusText);
        Assert.DoesNotContain("No matches", panel.StatusText, StringComparison.Ordinal);
        Assert.Empty(panel.RowTexts);
        Assert.Equal(RedactProblem.NothingToMark, panel.Find.MarkAll().Problem);
        Assert.Empty(f.Session.Plan.Marks);
    }

    [AvaloniaFact]
    public async Task ANewSearchAndLeavingTheModeCancelTheRunningOne()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var find = new FindSession(f.Session);

        var first = find.RunAsync("example", default);
        var second = find.RunAsync("DE89", default);
        Assert.False(await first);
        Assert.True(await second);
        Assert.Equal("DE89", find.Term);
        Assert.Equal(3, find.Hits.Count);

        var third = find.RunAsync("example", default);
        f.Session.End();
        Assert.False(await third);
        Assert.Equal(FindState.Idle, find.State);
        Assert.Empty(find.Hits);
    }

    [AvaloniaFact]
    public async Task IncompleteSearchBlocksMarkingUntilAcknowledged()
    {
        // text-dense.pdf has more matches than one search yields: the search stops at the limit and says more may exist.
        using var f = await RedactFixture.OpenAsync("text-dense.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "e";

        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();
        var find = panel.Find;
        Assert.True(find.IsIncomplete);
        Assert.Equal(RedactionSearch.MaxHits, find.Hits.Count);
        Assert.True(panel.AcknowledgeButton.IsVisible);
        Assert.Contains("more may exist", panel.StatusText, StringComparison.Ordinal);

        Assert.Equal(RedactProblem.SearchIncomplete, find.MarkAll().Problem);
        Assert.Equal(RedactProblem.SearchIncomplete, find.MarkTicked().Problem);
        Assert.Empty(f.Session.Plan.Marks);

        panel.AcknowledgeButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        ViewFixture.Pump();
        Assert.True(find.AcknowledgedIncomplete);
        Assert.False(panel.AcknowledgeButton.IsVisible);
        Assert.True(find.MarkTicked().IsDone);
        Assert.NotEmpty(f.Session.Plan.Marks);

        // The acknowledgement covers one search only.
        Assert.True(await find.RunAsync("e", default));
        Assert.False(find.AcknowledgedIncomplete);
        Assert.Equal(RedactProblem.SearchIncomplete, find.MarkAll().Problem);
    }

    [AvaloniaFact]
    public async Task HitsArriveInBatchesOfTwoHundred()
    {
        using var f = await RedactFixture.OpenAsync("text-dense.pdf");
        using var find = new FindSession(f.Session);
        var sizes = new List<int>();
        var last = 0;
        find.Changed += () =>
        {
            if (find.Hits.Count != last)
            {
                sizes.Add(find.Hits.Count - last);
                last = find.Hits.Count;
            }
        };

        await find.RunAsync("e", default);

        Assert.Equal(RedactionSearch.MaxHits, find.Hits.Count);
        Assert.All(sizes, size => Assert.True(size <= FindSession.BatchSize && size > 0));
        Assert.True(sizes.Count > 1);
    }

    [AvaloniaFact]
    public async Task AllMarksEveryMarkableHitWithEverywhereUnlessWholeWords()
    {
        using (var f = await RedactFixture.OpenAsync("redact-patterns.pdf"))
        using (var find = new FindSession(f.Session))
        {
            await find.RunAsync("example", default);
            find.SetTicked(0, false);

            Assert.True(find.MarkAll().IsDone, "Mark all ignores the ticks");

            Assert.Equal(4, f.Session.Plan.Marks.Count);
            Assert.All(f.Session.Plan.Marks, m =>
            {
                Assert.Equal(RedactionKind.Hit, m.Kind);
                Assert.True(m.Everywhere);
                Assert.Null(m.Pattern);
            });
            Assert.Equal(4, f.Session.Selection.Count);

            // Marking the same hits again adds nothing.
            Assert.True(find.MarkAll().IsDone);
            Assert.Equal(4, f.Session.Plan.Marks.Count);
            Assert.Equal(0, find.LastAdded);

            // One history step.
            f.Session.Undo();
            Assert.Empty(f.Session.Plan.Marks);
        }

        using (var f = await RedactFixture.OpenAsync("redact-patterns.pdf"))
        using (var find = new FindSession(f.Session))
        {
            await find.RunAsync("example", new SearchOptions(MatchCase: false, WholeWord: true));
            Assert.True(find.Hits.Count > 0);
            Assert.True(find.MarkAll().IsDone);

            Assert.Equal(find.Hits.Count, f.Session.Plan.Marks.Count);
            Assert.All(f.Session.Plan.Marks, m => Assert.False(m.Everywhere));
        }
    }

    [AvaloniaFact]
    public async Task TickedHitsAreMarkedAndUntickedAreNot()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        using var find = new FindSession(f.Session);
        await find.RunAsync("example", default);
        Assert.Equal(4, find.TickedCount);

        find.SetTicked(1, false);
        find.SetTicked(2, false);
        Assert.True(find.MarkTicked().IsDone);

        Assert.Equal(2, f.Session.Plan.Marks.Count);
        Assert.Equal(2, find.LastAdded);
    }

    [AvaloniaFact]
    public async Task NonMarkableHitsStayListedWithAReasonAndCannotBeTicked()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "example";
        await panel.SearchAsync();
        var find = panel.Find;
        var stuck = new RedactionHit(RedactionHitSource.PageText, 0, [], "example", false)
        {
            Markable = false,
            NotMarkableReason = "No character of the hit has a box with an area, so there is nothing to mark.",
        };
        find.Append([stuck], true);
        ViewFixture.Pump();

        var index = find.IndexOf(stuck);
        Assert.Equal(4, index);
        Assert.False(find.IsTicked(index));
        Assert.False(find.SetTicked(index, true));
        Assert.False(find.IsTicked(index));
        Assert.Contains(panel.RowTexts, t => t.Contains("cannot be marked", StringComparison.Ordinal) && t.Contains("nothing to mark", StringComparison.Ordinal));
        Assert.DoesNotContain(panel.RowNames, n => n.Contains("example", StringComparison.OrdinalIgnoreCase));

        Assert.True(find.MarkAll().IsDone);
        Assert.Equal(4, f.Session.Plan.Marks.Count);
    }

    [AvaloniaFact]
    public async Task InvisibleTextIsFlagged()
    {
        // redact-hidden.pdf holds its term only in places no page shows; the file with text of the invisible render mode is redact-everywhere.pdf.
        using var f = await RedactFixture.OpenAsync("redact-everywhere.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "e";
        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();

        var hidden = panel.Find.Hits.Where(h => h.Invisible).ToList();
        Assert.NotEmpty(hidden);
        Assert.Equal(hidden.Count, panel.RowTexts.Count(t => t.Contains("(hidden text)", StringComparison.Ordinal)));
        Assert.Contains(panel.Find.Hits, h => !h.Invisible);

        using var g = await RedactFixture.OpenAsync("redact-hidden.pdf");
        using var find = new FindSession(g.Session);
        Assert.True(await find.RunAsync("Zephyr", default));
        Assert.Equal(FindState.Done, find.State);
        Assert.Empty(find.Hits);
        Assert.True(find.Report!.IsComplete);
    }

    [AvaloniaFact]
    public async Task PicturePagesAreNamedWithTheOcrOffer()
    {
        using var f = await RedactFixture.OpenAsync("ocr-scan.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        var ran = 0;
        pane.OcrCommand = () => new CommandDefinition("ocr.run", "Recognize text…", "i", "0", Run: () => ran++);
        panel.TermBox.Text = "Invoice";

        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();

        Assert.Empty(panel.Find.Hits);
        Assert.Equal("No matches.", panel.StatusText);
        Assert.Equal("3 pages are pictures; text in pictures is not searched", panel.PictureText);
        Assert.True(panel.OcrButton.IsVisible);
        Assert.Equal("Recognize text…", panel.OcrButton.Content);
        Assert.True(panel.OcrButton.IsEnabled);
        Assert.Equal(0, ran);

        panel.OcrButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Equal(1, ran);

        // One picture page reads in the singular.
        using var g = await RedactFixture.OpenAsync("ocr-mixed.pdf");
        var (pane2, panel2) = OpenFind(g);
        using var __ = pane2;
        panel2.TermBox.Text = "Zzyzx";
        Assert.True(await panel2.SearchAsync());
        ViewFixture.Pump();
        Assert.Equal("1 page is a picture; text in pictures is not searched", panel2.PictureText);
    }

    [AvaloniaFact]
    public async Task TheOcrOfferIsDisabledWithoutAWiredCommandAndStartsNothing()
    {
        using var f = await RedactFixture.OpenAsync("ocr-scan.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        pane.OcrCommand = () => new CommandDefinition("ocr.run", "Recognize text…", "i", "0", Run: null, EnabledWhen: () => false, DisabledReason: () => "Leave the redact mode first.");
        panel.TermBox.Text = "Invoice";

        await panel.SearchAsync();
        ViewFixture.Pump();

        Assert.True(panel.OcrButton.IsVisible);
        Assert.False(panel.OcrButton.IsEnabled);
        Assert.Equal("Leave the redact mode first.", Avalonia.Controls.ToolTip.GetTip(panel.OcrButton));
    }

    [AvaloniaFact]
    public async Task NotSearchedListIsShownOnDemand()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;

        Assert.False(panel.NotSearchedVisible);
        panel.NotSearchedToggle.IsChecked = true;
        ViewFixture.Pump();

        Assert.True(panel.NotSearchedVisible);
        Assert.Equal(RedactionSearch.NotSearched, panel.NotSearchedLines);

        panel.NotSearchedToggle.IsChecked = false;
        Assert.False(panel.NotSearchedVisible);
    }

    [AvaloniaFact]
    public async Task EmptyTermDoesNotSearchAndSaysWhatToDo()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "   ";

        Assert.False(await panel.SearchAsync());

        Assert.Equal(FindState.Idle, panel.Find.State);
        Assert.Equal("Type the text to find, then press Enter.", panel.StatusText);
    }

    [AvaloniaFact]
    public async Task WordingNeverClaimsEverythingWasFound()
    {
        using var f = await RedactFixture.OpenAsync("text-dense.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        var forbidden = new Regex(@"\b(all found|error-free|safe|complete)\b", RegexOptions.IgnoreCase);
        var seen = new List<string>();

        panel.TermBox.Text = "e";
        await panel.SearchAsync();
        seen.Add(panel.StatusText);
        panel.TermBox.Text = "zzzzqq";
        await panel.SearchAsync();
        seen.Add(panel.StatusText);
        panel.PatternsToggle.IsChecked = true;
        await panel.SearchAsync();
        seen.Add(panel.StatusText);
        seen.Add(panel.CautionText);
        seen.AddRange(panel.NotSearchedLines.Where(l => !l.StartsWith("Patterns", StringComparison.Ordinal)));
        ViewFixture.Pump();

        Assert.All(seen, text => Assert.DoesNotMatch(forbidden, text));
    }

    [AvaloniaFact]
    public async Task TheFindCommandOpensAndClosesThePanelAndLeavingTheModeDropsIt()
    {
        using var f = WindowFixture.Create();
        var tab = await f.OpenAsync("redact-patterns.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        var commands = f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
        var find = commands.Single(c => c.Id == "redact.find");
        var mode = commands.Single(c => c.Id == "redact.mode");

        Assert.NotNull(find.Run);
        Assert.False(find.IsEnabled, "Find works inside the mode only");

        mode.Run!();
        ViewFixture.Pump();
        var pane = Assert.IsType<RedactPane>(f.Window.CommentsPane.Content);
        Assert.True(find.IsEnabled);
        Assert.False(pane.IsFindOpen, "nothing exists before the explicit toggle");

        find.Run!();
        ViewFixture.Pump();
        Assert.True(pane.IsFindOpen);
        var session = pane.FindPanel!.Find;

        var running = session.RunAsync("example", default);
        mode.Run!();
        ViewFixture.Pump();
        Assert.False(await running);
        Assert.False(tab.Redact.IsActive);
        Assert.False(pane.IsFindOpen);
    }
}
