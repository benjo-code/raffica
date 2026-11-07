using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class ScanHintTests
{
    private static async Task<(WindowFixture Fixture, Raffica.Pdf.Documents.PdfTab Tab)> OpenAsync(string file, Func<PdfWindow, Task<bool>>? probe = null, bool library = true)
    {
        var f = WindowFixture.Create();
        f.Window.OcrLibraryProbe = () => library;
        if (probe is not null)
        {
            f.Window.ScanProbe = (_, _, _) => probe(f.Window);
        }

        var tab = await f.OpenAsync(file);
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        return (f, tab);
    }

    private static ScanHint? HintOf(PdfWindow window, Raffica.Pdf.Documents.PdfTab tab) => window.BadgeHostOf(tab)?.Hint;

    [AvaloniaFact]
    public async Task AsksOnceAndNeverStartsARun()
    {
        var calls = 0;
        var (f, tab) = await OpenAsync("ocr-scan.pdf", _ =>
        {
            calls++;
            return Task.FromResult(true);
        });
        using var _ = f;
        var started = new List<OcrOptions>();
        f.Window.OcrRunStarter = (_, options) => started.Add(options);
        await PrintHost.UntilAsync(() => HintOf(f.Window, tab) is not null);

        var hint = HintOf(f.Window, tab)!;
        Assert.True(f.Window.BadgeHostOf(tab)!.IsVisible);
        Assert.True(hint.IsVisible);
        Assert.Equal("This looks like a scan. Make it searchable?", hint.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Text").Text);
        Assert.Equal("Recognize…", (string)hint.RecognizeButton.Content!);
        Assert.Equal("Not now", (string)hint.NotNowButton.Content!);
        Assert.Equal("Don't ask again for this file", (string)hint.NeverButton.Content!);
        Assert.Equal(1, calls);

        // Not now: gone, and asked no more for this document (not at a later sharp page, not at another ask).
        hint.NotNowButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(HintOf(f.Window, tab));
        await f.Window.OfferScanHintAsync(tab);
        Assert.Null(HintOf(f.Window, tab));
        Assert.Equal(1, calls);

        // It started nothing, and made no run: the tab has a session only to remember the answer, and it is not active.
        Assert.Empty(started);
        Assert.False(tab.OcrIfCreated!.IsActive);
        Assert.False(f.Window.OcrFlowCreated);
        Assert.False(tab.IsDirty);

        // Another document: Don't ask again ends it the same way; Recognize… opens the options card and still starts nothing.
        var second = await f.OpenAsync("ocr-scan-rotated.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(second)?.FirstSharpPageShown == true);
        await PrintHost.UntilAsync(() => HintOf(f.Window, second) is not null);
        Assert.Equal(2, calls);
        var again = HintOf(f.Window, second)!;
        again.NeverButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(HintOf(f.Window, second));
        await f.Window.OfferScanHintAsync(second);
        Assert.Null(HintOf(f.Window, second));
        Assert.Equal(2, calls);
        Assert.Empty(started);

        // Going back to the first tab shows no hint either.
        f.Window.Workspace.Select(tab);
        ViewFixture.Pump();
        Assert.Null(HintOf(f.Window, tab));
        Assert.Equal(2, calls);

        var third = await f.OpenAsync("ocr-mixed.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(third)?.FirstSharpPageShown == true);
        await PrintHost.UntilAsync(() => HintOf(f.Window, third) is not null);
        HintOf(f.Window, third)!.RecognizeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(HintOf(f.Window, third));
        await PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Recognize"));
        Assert.Empty(started);
        Assert.False(third.OcrIfCreated!.IsActive);
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await PrintHost.UntilAsync(() => !f.Window.Host.IsOpen);
        Assert.Empty(started);
    }

    [AvaloniaFact]
    public async Task NotShownForTextPagesOrWithoutLibrary()
    {
        // A page with text of its own: the real probe says no, and nothing is made for the tab.
        var (text, textTab) = await OpenAsync("pages-marked.pdf");
        using (text)
        {
            await Task.Delay(100);
            ViewFixture.Pump();
            await text.Window.OfferScanHintAsync(textTab);
            Assert.Null(HintOf(text.Window, textTab));
            Assert.Null(textTab.OcrIfCreated);
        }

        // The library is absent: the probe is not even asked.
        var calls = 0;
        var (absent, absentTab) = await OpenAsync("ocr-scan.pdf", _ =>
        {
            calls++;
            return Task.FromResult(true);
        }, library: false);
        using (absent)
        {
            await Task.Delay(100);
            await absent.Window.OfferScanHintAsync(absentTab);
            Assert.Equal(0, calls);
            Assert.Null(HintOf(absent.Window, absentTab));
            Assert.Null(absentTab.OcrIfCreated);
        }

        // A real scan with the real probe does give the hint (the probe itself is what separates the two cases above).
        var (scan, scanTab) = await OpenAsync("ocr-scan.pdf");
        using (scan)
        {
            await PrintHost.UntilAsync(() => HintOf(scan.Window, scanTab) is not null);
        }
    }

    [AvaloniaFact]
    public async Task NeverInAMeasurementRun()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-measure-").FullName;
        try
        {
            var path = Raffica.TestSupport.TestData.Pdf("ocr-scan.pdf");
            var options = PdfOptions.Parse(["--measure-startup", "--settings-file", Path.Combine(folder, "settings.json"), path]);
            Assert.True(options.IsMeasuring);
            var window = new PdfWindow(options, new ShellSettings(), null, null, null, new Raffica.Pdf.Tests.Printing.FakePrintService { DialogResult = null }, null, _ => Task.FromResult<string?>(null)) { Width = 900, Height = 600 };
            var calls = 0;
            window.OcrLibraryProbe = () => true;
            window.ScanProbe = (_, _, _) =>
            {
                calls++;
                return Task.FromResult(true);
            };
            window.Show();
            var tab = await window.Flow.OpenAsync(path);
            await ViewFixture.WaitAsync(() => window.ViewOf(tab!)?.FirstSharpPageShown == true);
            await Task.Delay(200);
            ViewFixture.Pump();
            await window.OfferScanHintAsync(tab!);
            Assert.Equal(0, calls);
            Assert.Null(window.BadgeHostOf(tab!)?.Hint);
            Assert.Null(tab!.OcrIfCreated);
            window.Close();
            foreach (var open in window.Workspace.Tabs.ToList())
            {
                open.Dispose();
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
