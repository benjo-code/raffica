using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Text;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Dialogs;

/// <summary>The Apply card and the Save-first card: what they say, the default button and the locked destructive choice.</summary>
public sealed class ApplyCardTests
{
    private static readonly PlanSummary Summary = new(12, 10, 3, 1, 0, 0);

    private sealed class Shown(Window window, Dialog card, ManualTime time) : IDisposable
    {
        public Dialog Card { get; } = card;

        public ManualTime Time { get; } = time;

        public IReadOnlyList<string?> Texts => Card.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToList();

        public IReadOnlyList<string> ButtonNames =>
            Card.GetVisualDescendants().OfType<Button>().Select(b => b.Name!).Where(n => n.StartsWith("PART_", StringComparison.Ordinal) && n != "PART_Close").ToList();

        public Button Button(string name) => Card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

        public void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        public void Escape()
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
            }
        }

        public void Dispose() => window.Close();
    }

    private static Shown Show(Func<ManualTime, Dialog> make)
    {
        var time = new ManualTime();
        var card = make(time);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 700, Height = 500 };
        window.Show();
        host.Show(card);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        return new Shown(window, card, time);
    }

    private static PageEditReport Report(params PageLoss[] losses) => new(losses);

    private static Shown ShowApply(PageEditReport report, Action<ApplyChoice>? done = null, ApplyTarget target = ApplyTarget.Choose, PlanSummary? summary = null) =>
        Show(time => ApplyCard.Create("a.pdf", summary ?? Summary, report, id => id == 1 ? "other.pdf" : null, target, done ?? (_ => { }), time));

    [AvaloniaFact]
    public void ApplyCardShowsSummaryLossesAndTheWarning()
    {
        using var s = ShowApply(Report(
            new PageLoss(PageLossKind.SignatureInvalidated, PageLossDetail.DocMdp(1), null),
            new PageLoss(PageLossKind.ForeignOutlineDropped, PageLossDetail.Source(1), null)));

        Assert.Equal(PdfStrings.ApplyTitle, s.Card.Title);
        Assert.Contains("12 → 10 pages, 3 turned, 1 file inserted", s.Texts);
        Assert.Contains("a.pdf", s.Texts);
        Assert.Equal(2, s.Texts.Count(t => t is not null && t.StartsWith('•')));
        Assert.Contains(s.Texts, t => t is not null && t.Contains("digitally signed", StringComparison.Ordinal) && t.Contains("broken", StringComparison.Ordinal));
        Assert.Contains(s.Texts, t => t is not null && t.Contains("\"other.pdf\"", StringComparison.Ordinal));
        Assert.Contains(PdfStrings.ApplyWarning, s.Texts);
        Assert.Contains("cannot be undone", PdfStrings.ApplyWarning, StringComparison.Ordinal);
        Assert.Contains("no backup copy", PdfStrings.ApplyWarning, StringComparison.Ordinal);
        Assert.Equal(["PART_SaveAs", "PART_Original", "PART_Back"], s.ButtonNames);
    }

    [AvaloniaFact]
    public void NoLossesSaysSo()
    {
        using var s = ShowApply(Report());

        Assert.Contains(PdfStrings.ApplyNoLosses, s.Texts);
        Assert.DoesNotContain(PdfStrings.ApplyLossesHeading, s.Texts);
    }

    [AvaloniaFact]
    public void SummaryNamesEveryKindOfChange()
    {
        Assert.Equal("6 → 6 pages", ApplyCard.SummaryText(new PlanSummary(6, 6, 0, 0, 0, 0)));
        Assert.Equal("6 → 9 pages, 1 turned, 2 files inserted, 1 blank page, 2 duplicated", ApplyCard.SummaryText(new PlanSummary(6, 9, 1, 2, 1, 2)));
        Assert.Equal("6 → 8 pages, 2 blank pages", ApplyCard.SummaryText(new PlanSummary(6, 8, 0, 0, 2, 0)));
    }

    [AvaloniaFact]
    public void SaveAsIsTheDefaultButtonAndHasTheFocus()
    {
        ApplyChoice? result = null;
        using var s = ShowApply(Report(new PageLoss(PageLossKind.PageLabelsShifted, PageLossDetail.Shifted, null)), c => result = c);

        Assert.True(s.Button("PART_SaveAs").IsFocused);
        Assert.False(s.Button("PART_Original").IsFocused);
        Assert.Contains("primary", s.Button("PART_SaveAs").Classes);
        Assert.DoesNotContain("primary", s.Button("PART_Original").Classes);

        s.Click("PART_SaveAs");
        Assert.Equal(ApplyChoice.SaveAs, result);
    }

    [AvaloniaFact]
    public void OriginalFileButtonIsArmedLikeTheOtherDestructiveCards()
    {
        ApplyChoice? result = null;
        using var s = ShowApply(Report(), c => result = c);

        Assert.False(s.Button("PART_Original").IsEnabled);
        s.Click("PART_Original");
        Assert.Null(result);

        s.Time.Advance(ArmingLock.DelayMilliseconds - 1);
        s.Click("PART_Original");
        Assert.Null(result);

        s.Time.Advance(1);
        s.Click("PART_Original");
        Assert.Equal(ApplyChoice.Original, result);
    }

    [AvaloniaFact]
    public void BackAndEscapeLeaveEverythingAsItIs()
    {
        var results = new List<ApplyChoice>();
        using (var s = ShowApply(Report(), results.Add))
        {
            s.Click("PART_Back");
        }

        using (var s = ShowApply(Report(), results.Add))
        {
            s.Escape();
        }

        Assert.Equal([ApplyChoice.Back, ApplyChoice.Back], results);
    }

    [AvaloniaFact]
    public void SaveAsTargetOffersNoOriginalFileButton()
    {
        using var s = ShowApply(Report(), target: ApplyTarget.SaveAs);

        Assert.Equal(["PART_SaveAs", "PART_Back"], s.ButtonNames);
    }

    [AvaloniaFact]
    public void RefusedReportShowsOnlyTheReasonAndBack()
    {
        ApplyChoice? result = null;
        using var s = ShowApply(Report(new PageLoss(PageLossKind.EncryptionNotPreserved, PageLossDetail.Source(1), null, Blocks: true)), c => result = c);

        Assert.Equal(PdfStrings.ApplyRefusedTitle, s.Card.Title);
        Assert.Equal(["PART_Back"], s.ButtonNames);
        Assert.True(s.Button("PART_Back").IsFocused);
        Assert.Contains(PdfStrings.ApplyRefusedIntro, s.Texts);
        Assert.Contains(s.Texts, t => t is not null && t.Contains("\"other.pdf\" is encrypted", StringComparison.Ordinal));
        Assert.DoesNotContain(PdfStrings.ApplyWarning, s.Texts);

        s.Click("PART_Back");
        Assert.Equal(ApplyChoice.Back, result);
    }

    [AvaloniaFact]
    public void LossesAreWordedPerKind()
    {
        var losses = Enum.GetValues<PageLossKind>().Select(kind => new PageLoss(kind, kind == PageLossKind.ForeignOutlineDropped ? PageLossDetail.Source(1) : null, null)).ToArray();
        using var s = ShowApply(Report(losses));

        // One line per kind, each its own sentence.
        var lines = s.Texts.Where(t => t is not null && t.StartsWith('•')).ToList();
        Assert.Equal(Enum.GetValues<PageLossKind>().Length, lines.Count);
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [AvaloniaFact]
    public void SaveFirstCardSavesOnAgreementAndBackKeepsEverything()
    {
        bool? answer = null;
        using (var s = Show(_ => SaveFirstCard.Create("a.pdf", a => answer = a)))
        {
            Assert.Equal(PdfStrings.SaveFirstTitle, s.Card.Title);
            Assert.Equal(["PART_SaveFirst", "PART_Back"], s.ButtonNames);
            Assert.True(s.Button("PART_SaveFirst").IsFocused);
            Assert.Contains(s.Texts, t => t is not null && t.Contains("a.pdf", StringComparison.Ordinal));
            s.Click("PART_SaveFirst");
        }

        Assert.True(answer);

        using (var s = Show(_ => SaveFirstCard.Create("a.pdf", a => answer = a)))
        {
            s.Click("PART_Back");
        }

        Assert.False(answer);

        using (var s = Show(_ => SaveFirstCard.Create("a.pdf", a => answer = a)))
        {
            answer = null;
            s.Escape();
        }

        Assert.False(answer);
    }
}
