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
        var reason = PdfStrings.FindNotMarkableReason(NotMarkableKind.NoArea);
        Assert.Contains(panel.RowTexts, t => t.Contains("cannot be marked", StringComparison.Ordinal) && t.Contains(reason, StringComparison.Ordinal));
        Assert.DoesNotContain(panel.RowTexts, t => t.Contains(stuck.NotMarkableReason!, StringComparison.Ordinal));
        Assert.DoesNotContain(panel.RowNames, n => n.Contains("example", StringComparison.OrdinalIgnoreCase));

        // The same row in German carries the German reason, never Core's English sentence.
        var german = System.Globalization.CultureInfo.GetCultureInfo("de");
        try
        {
            PdfStrings.Culture = german;
            using var gpane = new RedactPane(f.Session, f.View);
            gpane.ToggleFind();
            var gpanel = gpane.FindPanel!;
            gpanel.TermBox.Text = "example";
            await gpanel.SearchAsync();
            gpanel.Find.Append([stuck], true);
            ViewFixture.Pump();
            Assert.Contains(gpanel.RowTexts, t => t.Contains("nicht markierbar", StringComparison.Ordinal) && t.Contains(PdfStrings.FindNotMarkableReason(NotMarkableKind.NoArea, german), StringComparison.Ordinal));
            Assert.DoesNotContain(gpanel.RowTexts, t => t.Contains(stuck.NotMarkableReason!, StringComparison.Ordinal));
        }
        finally
        {
            PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("en");
        }

        Assert.True(find.MarkAll().IsDone);
        Assert.Equal(4, f.Session.Plan.Marks.Count);
        Assert.Equal(1, find.LastNotMarkable);
        Assert.Equal("4 marks added. 1 could not be marked.", panel.NoteText);
    }

    private static RedactionHit Hit(IReadOnlyList<PageRect> areas, string text) => new(RedactionHitSource.PageText, 0, areas, text, false) { Markable = false };

    [Fact]
    public void EveryNotMarkableClassIsClassifiedFromTheHitAndWordedInBothLanguages()
    {
        PageRect[] one = [new PageRect(0, 0, 10, 10)];
        var many = Enumerable.Repeat(new PageRect(0, 0, 10, 10), RedactionLimits.MaxAreasPerMark + 1).ToArray();
        var cases = new (RedactionHit Hit, NotMarkableKind Kind)[]
        {
            (Hit([], "word"), NotMarkableKind.NoArea),
            (Hit(many, "word"), NotMarkableKind.TooManyLines),
            (Hit(one, "   "), NotMarkableKind.WhiteSpace),
            (Hit(one, new string('x', RedactionLimits.MaxTermLength + 1)), NotMarkableKind.TooLong),
            (Hit(one, "word"), NotMarkableKind.NoBox),

            // The order decides: no area wins over everything else, too many lines over white space.
            (Hit([], "   "), NotMarkableKind.NoArea),
            (Hit(many, "   "), NotMarkableKind.TooManyLines),
            (Hit(one, new string(' ', RedactionLimits.MaxTermLength + 1)), NotMarkableKind.WhiteSpace),
        };
        foreach (var (hit, kind) in cases)
        {
            // Whatever English sentence Core attached is not looked at.
            Assert.Equal(kind, NotMarkableReasons.Classify(hit with { NotMarkableReason = "Some other sentence." }));
        }

        var english = System.Globalization.CultureInfo.GetCultureInfo("en");
        var german = System.Globalization.CultureInfo.GetCultureInfo("de");
        var kinds = Enum.GetValues<NotMarkableKind>();
        Assert.Equal(5, kinds.Length);
        foreach (var kind in kinds)
        {
            var en = PdfStrings.FindNotMarkableReason(kind, english);
            var de = PdfStrings.FindNotMarkableReason(kind, german);
            Assert.False(string.IsNullOrWhiteSpace(en), kind.ToString());
            Assert.False(string.IsNullOrWhiteSpace(de), kind.ToString());
            Assert.NotEqual(en, de);
            Assert.DoesNotContain("{", en, StringComparison.Ordinal);
            Assert.DoesNotContain("{", de, StringComparison.Ordinal);
        }

        Assert.Equal(kinds.Length, kinds.Select(k => PdfStrings.FindNotMarkableReason(k, english)).Distinct().Count());
        Assert.Equal(kinds.Length, kinds.Select(k => PdfStrings.FindNotMarkableReason(k, german)).Distinct().Count());
        Assert.Contains("500", PdfStrings.FindNotMarkableReason(NotMarkableKind.TooManyLines, english), StringComparison.Ordinal);
        Assert.Contains("1,000", PdfStrings.FindNotMarkableReason(NotMarkableKind.TooLong, english), StringComparison.Ordinal);
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
        // No command is wired here, so the row carries the instruction.
        Assert.Equal("1 page is a picture; text in pictures is not searched. " + PdfStrings.FindPicturesInstruction, panel2.PictureText);
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

        // A command that cannot run offers no dead button: the row says what to do instead.
        Assert.False(panel.OcrButton.IsVisible);
        Assert.Equal(PdfStrings.FindPictures(3) + ". " + PdfStrings.FindPicturesInstruction, panel.PictureText);
    }

    [AvaloniaFact]
    public async Task InTheRealWindowTheRowIsAnInstructionBecauseRedactAndOcrExcludeEachOther()
    {
        using var rig = await RedactUiRig.OpenAsync("ocr-scan.pdf", realSave: false);
        await rig.EnterModeAsync();
        var pane = rig.Window.RedactOf(rig.Tab)!.Pane;
        pane.ToggleFind();
        var panel = pane.FindPanel!;
        Assert.False(rig.Command("ocr.run").IsEnabled, "Recognize text cannot run inside the redact mode");

        panel.TermBox.Text = "Invoice";
        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();

        Assert.False(panel.OcrButton.IsVisible);
        Assert.Equal(
            "3 pages are pictures; text in pictures is not searched. Recognize text first: leave Redact mode (apply or discard the marks), recognize the text, then search again.",
            panel.PictureText);

        PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
        panel.TermBox.Text = "Invoice";
        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();
        Assert.False(panel.OcrButton.IsVisible);
        Assert.Contains("Erkennen Sie zuerst den Text", panel.PictureText, StringComparison.Ordinal);
        Assert.Contains("Bildern wird nicht durchsucht. Erkennen Sie zuerst den Text", panel.PictureText, StringComparison.Ordinal);
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
        Assert.Equal(PdfStrings.FindNotSearchedLines(), panel.NotSearchedLines);

        panel.NotSearchedToggle.IsChecked = false;
        Assert.False(panel.NotSearchedVisible);

        // In German the panel shows the German table, never Core's English lines.
        var german = System.Globalization.CultureInfo.GetCultureInfo("de");
        try
        {
            PdfStrings.Culture = german;
            using var gpane = new RedactPane(f.Session, f.View);
            gpane.ToggleFind();
            Assert.Equal(PdfStrings.FindNotSearchedLines(german), gpane.FindPanel!.NotSearchedLines);
            Assert.DoesNotContain(gpane.FindPanel.NotSearchedLines, line => RedactionSearch.NotSearched.Contains(line));
        }
        finally
        {
            PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("en");
        }
    }

    [Fact]
    public void TheNotSearchedTableHasOneLinePerCoreLineInBothLanguages()
    {
        var english = System.Globalization.CultureInfo.GetCultureInfo("en");
        var german = System.Globalization.CultureInfo.GetCultureInfo("de");
        var en = PdfStrings.FindNotSearchedLines(english);
        var de = PdfStrings.FindNotSearchedLines(german);

        // Fails when Core adds or drops a line: the wording must follow.
        Assert.Equal(RedactionSearch.NotSearched.Count, en.Count);
        Assert.Equal(RedactionSearch.NotSearched.Count, de.Count);
        for (var i = 0; i < en.Count; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(en[i]));
            Assert.False(string.IsNullOrWhiteSpace(de[i]));
            Assert.NotEqual(en[i], de[i]);

            // Same order as Core: the PDF names of a Core line appear in the same line of the table.
            foreach (var token in Regex.Matches(RedactionSearch.NotSearched[i], "/[A-Za-z]+").Select(m => m.Value))
            {
                Assert.Contains(token, en[i], StringComparison.Ordinal);
                Assert.Contains(token, de[i], StringComparison.Ordinal);
            }
        }
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
    public async Task ATermThatIsTooLongIsRefusedWithAReasonAndKeepsThePreviousResult()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "example";
        Assert.True(await panel.SearchAsync());
        Assert.Equal(4, panel.Find.Hits.Count);

        panel.TermBox.Text = new string('x', RedactionLimits.MaxTermLength + 1);
        Assert.False(await panel.SearchAsync());
        ViewFixture.Pump();

        Assert.Equal(FindRefusal.TermTooLong, panel.Find.Refusal);
        Assert.Equal(FindState.Done, panel.Find.State);
        Assert.Equal(4, panel.Find.Hits.Count);
        Assert.Equal("4 matches", panel.StatusText);
        Assert.Equal("The term is too long: at most 1,000 characters. The previous result is unchanged.", panel.NoteText);
        Assert.Equal(0, panel.TermBox.MaxLength);
        Assert.Equal(RedactionLimits.MaxTermLength + 1, panel.TermBox.Text!.Length);

        // The German note exists and differs.
        var german = System.Globalization.CultureInfo.GetCultureInfo("de");
        Assert.NotEqual(PdfStrings.FindTermTooLong(german), PdfStrings.FindTermTooLong());

        // A valid search clears the note.
        panel.TermBox.Text = "DE89";
        Assert.True(await panel.SearchAsync());
        ViewFixture.Pump();
        Assert.Equal(string.Empty, panel.NoteText);
        Assert.Equal(FindRefusal.None, panel.Find.Refusal);
    }

    [AvaloniaFact]
    public async Task MarkingSaysWhatWasAddedAndMarkingAgainSaysNothingWasAdded()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "DE89";
        await panel.SearchAsync();
        Assert.Equal(string.Empty, panel.NoteText);

        panel.MarkAllButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal("3 marks added.", panel.NoteText);
        Assert.Equal(3, f.Session.Plan.Marks.Count);

        panel.MarkAllButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal("These matches are marked already.", panel.NoteText);
        Assert.Equal(3, f.Session.Plan.Marks.Count);

        // One new mark reads in the singular.
        f.Session.Delete(new HashSet<Guid> { f.Session.Plan.Marks[0].Id });
        panel.MarkTickedButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal("1 mark added.", panel.NoteText);

        // A new search drops the old feedback.
        panel.TermBox.Text = "example";
        await panel.SearchAsync();
        Assert.Equal(string.Empty, panel.NoteText);
    }

    [AvaloniaFact]
    public async Task AMarkAllThatWouldOverflowThePlanChangesNothingAndSaysWhy()
    {
        using var f = await RedactFixture.OpenAsync("text-dense.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        Assert.True(f.Session.AddArea(0, new PageRect(50, 100, 150, 120)).IsDone);
        var versionBefore = f.Session.Version;
        var planBefore = f.Session.Plan;

        panel.TermBox.Text = "e";
        Assert.True(await panel.SearchAsync());
        Assert.Equal(RedactionLimits.MaxMarks, panel.Find.Hits.Count);
        panel.Find.Acknowledge();

        panel.MarkAllButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        ViewFixture.Pump();

        Assert.Same(planBefore, f.Session.Plan);
        Assert.Equal(versionBefore, f.Session.Version);
        Assert.Single(f.Session.Plan.Marks);
        Assert.Equal(RedactProblem.TooManyMarks, panel.Find.MarkAll().Problem);
        var reason = PdfStrings.RedactRefusal(RedactProblem.TooManyMarks);
        Assert.Contains("10,000", reason, StringComparison.Ordinal);
        Assert.Equal(reason, pane.NoteText);

        // The history holds the one area mark only: one undo empties the plan.
        f.Session.Undo();
        Assert.Empty(f.Session.Plan.Marks);
        Assert.False(f.Session.CanUndo);
    }

    private static void MarkIncomplete(FindSession find, int page) =>
        typeof(RedactionSearchReport).GetMethod("MarkIncomplete", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(find.Report, [page]);

    [AvaloniaFact]
    public async Task AnIncompleteSearchNamesTheUnreadPagesAndNeverLeadsWithABareNoMatches()
    {
        using var f = await RedactFixture.OpenAsync("redact-patterns.pdf");
        var (pane, panel) = OpenFind(f);
        using var _ = pane;
        panel.TermBox.Text = "zzzzqq";
        Assert.True(await panel.SearchAsync());
        Assert.Equal("No matches.", panel.StatusText);

        // A fake report with unread pages, shown through the Append seam (it raises Changed without adding a hit).
        MarkIncomplete(panel.Find, 2);
        MarkIncomplete(panel.Find, 4);
        panel.Find.Append([], true);
        ViewFixture.Pump();

        Assert.StartsWith("No matches in the pages searched.", panel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"^No matches\.", panel.StatusText);
        Assert.Contains("could not be read in full: 3, 5.", panel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("searched completely", panel.StatusText, StringComparison.Ordinal);
        Assert.True(panel.AcknowledgeButton.IsVisible);

        var german = System.Globalization.CultureInfo.GetCultureInfo("de");
        Assert.NotEqual(PdfStrings.FindNoMatchesIncomplete, PdfStrings.GetIn("FindNoMatchesIncomplete", german));
        Assert.DoesNotContain("vollständig durchsucht", PdfStrings.GetIn("FindIncompletePagesFormat", german), StringComparison.Ordinal);

        panel.Find.Acknowledge();
        ViewFixture.Pump();
        Assert.Contains("Accepted for this search.", panel.StatusText, StringComparison.Ordinal);
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
