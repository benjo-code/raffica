using Avalonia.Headless.XUnit;
using Raffica.Core.Viewing;
using Raffica.Ocr;
using Raffica.Pdf.Tests.Ocr;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class StartupCreatesNoRedactOcrOrEditWorkTests
{
    [AvaloniaFact]
    public async Task StartupCreatesNoRedactOcrOrEditWork()
    {
        using var f = WindowFixture.Create();
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => tab.Session.State == ViewerState.Ready);
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)!.FirstSharpPageShown);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();

        foreach (var open in f.Window.Workspace.Tabs)
        {
            Assert.Null(open.RedactIfCreated);
            Assert.Null(open.OcrIfCreated);
            Assert.Null(open.TextEditIfCreated);
        }

        // The apply flow is made by the first Apply, not by opening a file or entering the mode.
        Assert.False(f.Window.RedactFlowCreated);
        f.Window.ToggleRedact();
        await ViewFixture.WaitAsync(() => tab.RedactIfCreated is { IsActive: true });
        Assert.False(f.Window.RedactFlowCreated);
    }

    // The same with the real types of the OCR side present: a scan, the library "present" and the scan hint armed. Nothing of the recognition runs
    // at the start (no engine, no run flow, no session, no badge, no language folder made); the one thing that is allowed is the probe of the scan
    // hint after the first sharp page, once, and only when the library is there. No text run is read and no mark, find or look row exists.
    [AvaloniaFact]
    public async Task OnlyTheScanHintProbeRunsAfterTheFirstSharpPageAndOnlyWithTheLibrary()
    {
        foreach (var libraryThere in new[] { true, false })
        {
            using var f = WindowFixture.Create();
            var probes = 0;
            var engines = 0;
            var libraryAsks = 0;
            f.Window.OcrLibraryProbe = () =>
            {
                libraryAsks++;
                return libraryThere;
            };
            f.Window.OcrRunStarter = (_, _) => { };
            f.Window.OcrEngineFactory = _ =>
            {
                engines++;
                return new FakeEngineFactory();
            };
            f.Window.ShippedTessdataDirectory = Path.Combine(f.Folder, "no-shipped");
            f.Window.ScanProbe = (_, _, _) =>
            {
                probes++;
                return Task.FromResult(false);
            };

            var tab = await f.OpenAsync("ocr-scan.pdf");
            await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)!.FirstSharpPageShown);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            ViewFixture.Pump();

            Assert.Equal(libraryThere ? 1 : 0, probes);
            Assert.Equal(0, engines);
            Assert.True(libraryAsks <= 1, "the library is looked for once");
            Assert.Null(tab.OcrIfCreated);
            Assert.Null(tab.RedactIfCreated);
            Assert.Null(tab.TextEditIfCreated);
            Assert.False(f.Window.OcrFlowCreated);
            Assert.False(f.Window.RedactFlowCreated);
            Assert.Null(f.Window.OcrBadgeOf(tab));
            Assert.Null(f.Window.TextEditOf(tab));
            Assert.False(Directory.Exists(Raffica.Pdf.Settings.PdfSettingsFiles.TessdataDirectoryFor(Path.Combine(f.Folder, "settings.json"))), "no language folder is made at the start");
        }
    }
}
