using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Ocr;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Tests.Printing;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class OcrReportTests
{
    // The words the card of the options card guards against, plus the ones the run must not use for a result it cannot vouch for.
    private static readonly Regex Forbidden = new(
        @"\b(correct|correctly|complete|completely|incomplete|exact|exactly|safe|safely|secure|guaranteed|guarantee|all found|error-free|fully|valid|legally binding"
        + @"|korrekt|richtig|vollständig|vollständige|komplett|sicher|garantiert|fehlerfrei|fehlerfreie|gültig|rechtsverbindlich|exakt)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void InBothLanguages(Action<string> check)
    {
        var before = PdfStrings.Culture;
        try
        {
            foreach (var language in new[] { "en", "de" })
            {
                PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
                check(language);
            }
        }
        finally
        {
            PdfStrings.Culture = before;
        }
    }

    [AvaloniaFact]
    public void ReportListsEverySkippedPageWithAReason()
    {
        var reasons = Enum.GetValues<OcrSkipReason>();
        var skipped = reasons.Select((reason, i) => new OcrSkippedPage(i * 2, reason, reason == OcrSkipReason.ResolutionTooLow ? "40 dpi" : null)).ToList();
        var report = new OcrReport { PagesRecognized = 1, Words = 3, MeanConfidence = 88.4, Skipped = skipped };

        InBothLanguages(language =>
        {
            var english = new Dictionary<OcrSkipReason, string>();
            foreach (var reason in reasons)
            {
                // Every member has words of its own, in the language of the UI (a member without wording throws and fails here).
                var text = OcrWording.Text(reason, reason == OcrSkipReason.ResolutionTooLow ? "40 dpi" : null);
                Assert.False(string.IsNullOrWhiteSpace(text), reason + " " + language);
                Assert.DoesNotContain("OcrSkip", text, StringComparison.Ordinal);
                Assert.DoesNotMatch(Forbidden, text);
                english[reason] = text;
            }

            Assert.Equal(reasons.Length, english.Values.Distinct().Count());
            var text2 = OcrWording.Report(report, applied: null, canceled: false);
            // One line per skipped page, with the number from 1 and the reason.
            for (var i = 0; i < skipped.Count; i++)
            {
                Assert.Contains(text2.Lines, line => line == PdfStrings.Fill(PdfStrings.OcrReportSkippedLineFormat, (i * 2) + 1, OcrWording.Text(skipped[i].Reason, skipped[i].Detail)));
            }

            // The card shows the same lines.
            var card = OcrReportCard.Create(text2, () => { });
            var host = new Raffica.Shell.Controls.DialogHost { Content = new Avalonia.Controls.Border() };
            var window = new Avalonia.Controls.Window { Content = host };
            window.Show();
            host.Show(card);
            PrintHost.Pump();
            var shown = card.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Where(b => b.Name == "PART_Line").Select(b => b.Text).ToList();
            Assert.All(text2.Lines, line => Assert.Contains(line, shown));
            window.Close();
        });

        // German differs from English for every reason: Core's English sentences are never what the user reads.
        var en = reasons.ToDictionary(r => r, r => OcrWording.Text(r, null));
        var before = PdfStrings.Culture;
        try
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
            Assert.All(reasons, r => Assert.NotEqual(en[r], OcrWording.Text(r, null)));
        }
        finally
        {
            PdfStrings.Culture = before;
        }
    }

    [Fact]
    public void EveryFailureKindHasWordsInBothLanguages()
    {
        InBothLanguages(language =>
        {
            foreach (var kind in Enum.GetValues<OcrFailureKind>())
            {
                var text = OcrWording.Text(kind);
                Assert.False(string.IsNullOrWhiteSpace(text), kind + " " + language);
                Assert.DoesNotMatch(Forbidden, text);
            }

            Assert.Contains("deu", OcrWording.Text(OcrFailureKind.LanguageMissing, ["deu"]), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ResolutionDetailGivesOnlyItsNumber()
    {
        Assert.Equal("The resolution of 40 dpi is too low for recognition.", OcrWording.Text(OcrSkipReason.ResolutionTooLow, "40 dpi"));
        Assert.Equal("The resolution is too low for recognition.", OcrWording.Text(OcrSkipReason.ResolutionTooLow, "<script>"));
        Assert.Equal("The resolution is too low for recognition.", OcrWording.Text(OcrSkipReason.ResolutionTooLow, null));
    }

    [AvaloniaFact]
    public async Task ReportNeverContainsTheRecognizedText()
    {
        using var rig = await OcrRig.OpenAsync();
        var seen = new List<string>();
        rig.Engines!.HoldAt(1);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, new OcrOptions("eng", Jobs: 1));
        await rig.UntilPagesAsync(1);
        seen.Add(rig.Window.Status.Hint ?? string.Empty);
        seen.Add(rig.Window.OcrBadgeOf(rig.Tab)!.Message);
        rig.Engines.Release();
        var card = await rig.CardWithAsync("PART_Ok");
        seen.AddRange(OcrRig.TextsOf(card));
        seen.Add(rig.Window.OcrBadgeOf(rig.Tab)!.Message);
        OcrRig.Click(card, "PART_Ok");
        await run;

        // The word is in the document, and nowhere in what the run said about itself.
        Assert.Contains(FakeEngineFactory.Secret, await rig.PageTextAsync(0), StringComparison.Ordinal);
        Assert.All(seen, text => Assert.DoesNotContain(FakeEngineFactory.Secret, text, StringComparison.OrdinalIgnoreCase));
        Assert.All(seen, text => Assert.DoesNotMatch(Forbidden, text));
        Assert.Contains(seen, text => text.Contains("3 words", StringComparison.Ordinal));
    }

    [Fact]
    public void UndoableFalseIsSaidPlainly()
    {
        var report = new OcrReport { PagesRecognized = 3, Words = 30 };
        var applied = new OcrApplyResult([0, 1, 2], 30, 0, [], Undoable: false);

        InBothLanguages(language =>
        {
            var lines = OcrWording.Report(report, applied, canceled: false).Lines;
            Assert.Contains(PdfStrings.OcrReportNotUndoable, lines);
            Assert.DoesNotContain(PdfStrings.OcrReportUndoable, lines);
            Assert.DoesNotMatch(Forbidden, PdfStrings.OcrReportNotUndoable);
        });

        Assert.Equal("This step is too large for the undo history, so Undo cannot take it back. It counts as an unsaved change; close without saving to drop it.", PdfStrings.OcrReportNotUndoable);
        var undoable = OcrWording.Report(report, applied with { Undoable = true }, canceled: false).Lines;
        Assert.Contains(PdfStrings.OcrReportUndoable, undoable);
        Assert.DoesNotContain(PdfStrings.OcrReportNotUndoable, undoable);
    }

    [Fact]
    public void ReportMentionsWordsLeftOutAndPagesThatKeptTheirLayer()
    {
        var report = new OcrReport { PagesRecognized = 2, Words = 9, LowConfidencePages = [1], PagesWithoutWords = [2], LoweredPages = [0] };
        var lines = OcrWording.Report(report, new OcrApplyResult([0, 1], 9, 4, [5, 6]), canceled: true).Lines;
        Assert.Contains(PdfStrings.Fill(PdfStrings.OcrReportLeftOutFormat, 4), lines);
        Assert.Contains(PdfStrings.Fill(PdfStrings.OcrReportKeptLayerFormat, "6, 7"), lines);
        Assert.Contains(PdfStrings.Fill(PdfStrings.OcrReportLowFormat, "2"), lines);
        Assert.Contains(PdfStrings.Fill(PdfStrings.OcrReportEmptyFormat, "3"), lines);
        Assert.Contains(PdfStrings.Fill(PdfStrings.OcrReportLoweredFormat, "1"), lines);
    }

    [AvaloniaFact]
    public void HistoryFullCardOffersSaveFirstAndBack()
    {
        var answers = new List<bool>();
        var card = OcrKeepPagesCard.CreateHistoryFull("scan.pdf", answers.Add);
        var host = new Raffica.Shell.Controls.DialogHost { Content = new Avalonia.Controls.Border() };
        var window = new Avalonia.Controls.Window { Content = host };
        window.Show();
        host.Show(card);
        PrintHost.Pump();
        Assert.Equal(PdfStrings.OcrHistoryFullTitle, card.Title);
        Assert.Contains("scan.pdf", card.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(b => b.Name == "PART_Message").Text, StringComparison.Ordinal);
        OcrRig.Click(card, "PART_SaveFirst");
        OcrRig.Click(card, "PART_Back");
        // The first answer counts, a second click does nothing.
        Assert.Equal([true], answers);
        window.Close();
    }
}
