using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Ocr;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

/// <summary>
/// The consolidated refusals of text recognition against hostile input: each ends in a reason in the words of the app and leaves the file, the
/// history and the language folder as they were. Already pinned where the feature is, and not repeated: nine languages
/// (<c>LanguageListTests.MoreThanEightIsRefused</c>, <c>OcrOptionsTests.RangeAndLanguagesGateTheRecognizeButton</c>), an import that is 200 MB and
/// one byte (<c>LanguageFilesTests.BadNamesAndOversizedFilesAreRefusedWithAReason</c>, the 200 MB limit is <c>TessdataStore.MaxFileLength</c>).
/// </summary>
public sealed class OcrHostileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEnumerable<string> LinesOf(Dialog card) =>
        card.GetVisualDescendants().OfType<TextBlock>().Where(b => b.Name == "PART_Line").Select(b => b.Text ?? string.Empty);

    [AvaloniaFact]
    public void BadPageRangesStopTheCardWithTheWordsOfTheApp()
    {
        var results = new List<OcrOptions?>();
        var dialog = RecognizeCard.Create("scan.pdf", 6, new LanguageList([new TessdataLanguage("eng", TessdataOrigin.Shipped, 16, "eng.traineddata")], "en", stored: null), false, results.Add);
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host };
        window.Show();
        host.Show(dialog);
        PrintHost.Pump();
        var ok = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Recognize");
        dialog.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "PART_PagesRange").IsChecked = true;
        var range = dialog.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "PART_Range");
        var hint = dialog.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_RangeHint");

        foreach (var bad in new[] { "0", "5-2", "999999999", "7", "1-", "-3", "1,,2" })
        {
            range.Text = bad;
            PrintHost.Pump();
            Assert.False(ok.IsEnabled, bad);
            Assert.True(hint.IsVisible, bad);
            Assert.Equal(RecognizeCardProblem(bad), hint.Text);
            Assert.False(string.IsNullOrWhiteSpace(hint.Text), bad);
            Assert.Empty(results);
        }

        window.Close();
    }

    private static string? RecognizeCardProblem(string text) => InsertFileCard.Problem(text, 6);

    [AvaloniaFact]
    public async Task ALanguageNamedLikeAPathIsRefusedAndNothingIsAdded()
    {
        using var rig = await OcrRig.OpenAsync();
        var folder = Directory.CreateTempSubdirectory("raffica-hostile-").FullName;
        try
        {
            foreach (var name in new[] { "..x.traineddata", "x y.traineddata", "..traineddata" })
            {
                var path = Path.Combine(folder, name);
                File.WriteAllBytes(path, new byte[32]);
                rig.Window.LanguageFilePicker = () => Task.FromResult<string?>(path);
                var run = rig.Window.LanguagesAsync();
                var choose = await rig.CardWithAsync("PART_Choose");
                OcrRig.Click(choose, "PART_Choose");
                var card = await rig.CardWithAsync("PART_Ok");
                Assert.Equal(PdfStrings.OcrImportMessage(TessdataImportError.InvalidName, null), card.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Message").Text);
                OcrRig.Click(card, "PART_Ok");
                await PrintHost.UntilAsync(() => run.IsCompleted);
            }

            var user = Raffica.Pdf.Settings.PdfSettingsFiles.TessdataDirectoryFor(Path.Combine(rig.Fixture.Folder, "settings.json"));
            Assert.Empty(Directory.Exists(user) ? Directory.GetFiles(user, "*.traineddata") : []);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ARunWithALanguageNamedLikeAPathEndsInACardAndChangesNothing()
    {
        using var rig = await OcrRig.OpenAsync();
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, new OcrOptions("../x", Jobs: 1));
        var card = await rig.CardWithAsync("PART_Ok");
        var texts = OcrRig.TextsOf(card);
        Assert.All(texts, t => Assert.False(t.Contains("Exception", StringComparison.Ordinal) || t.Contains("../x", StringComparison.Ordinal), t));
        Assert.Contains(texts, t => t.Length > 10);
        OcrRig.Click(card, "PART_Ok");
        await run;
        Assert.Null(rig.Card);
        Assert.False(rig.Tab.IsDirty);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task ACorruptPageIsSkippedWithItsReasonAndTheRestIsKept()
    {
        using var rig = await OcrRig.OpenAsync("ocr-scan.pdf", behavior: _ => throw new InvalidOperationException("damaged page"));
        var run = rig.Window.StartOcrRunAsync(rig.Tab, new OcrOptions("eng", Jobs: 1));
        var card = await rig.CardWithAsync("PART_Ok");
        var lines = LinesOf(card).ToList();
        Assert.Contains(lines, l => l.Contains(OcrWording.Text(OcrSkipReason.Failed, null), StringComparison.Ordinal));
        Assert.All(OcrRig.TextsOf(card), t => Assert.DoesNotContain("damaged page", t, StringComparison.Ordinal));
        OcrRig.Click(card, "PART_Ok");
        await run;
        Assert.False(rig.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task TheHostileFixtureEndsInAReportWithAReasonPerPage()
    {
        var words = new OcrRecognition([.. Enumerable.Range(0, 100_001).Select(static i => new OcrWord("w", 10 + (i % 1000), 10 + (i / 1000), 12 + (i % 1000), 12 + (i / 1000), 40))]);
        using var rig = await OcrRig.OpenAsync("ocr-hostile.pdf", behavior: _ => words);
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, new OcrOptions("eng", Jobs: 1));
        var card = await rig.CardWithAsync("PART_Ok");
        var lines = LinesOf(card).ToList();
        Assert.Equal(2, lines.Count(l => l.Contains(OcrWording.Text(OcrSkipReason.TooLarge, null), StringComparison.Ordinal)));
        OcrRig.Click(card, "PART_Ok");
        await run;
        Assert.False(rig.Tab.IsDirty);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Path, Ct));
    }
}
