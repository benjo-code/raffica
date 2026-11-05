using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// An <see cref="ImageExportFlow"/> in a headless window with a dialog host: a temp copy of a test file as the tab, a scripted folder picker and
/// helpers that wait for a card, fill it and press Export. Nothing is written outside the temp folder.
/// </summary>
internal sealed class ExportRig : IDisposable
{
    private readonly PrintHost _host = new();
    private readonly TempCopy _copy;

    private ExportRig(TempCopy copy, PdfTab tab)
    {
        _copy = copy;
        Tab = tab;
        Flow = new ImageExportFlow(
            new DialogQueue(_host.Host),
            _ =>
            {
                FolderAsked++;
                return Task.FromResult(FolderPicks.Count > 0 ? FolderPicks.Dequeue() : null);
            });
    }

    public PdfTab Tab { get; }

    public ImageExportFlow Flow { get; }

    public Queue<string?> FolderPicks { get; } = new();

    public int FolderAsked { get; private set; }

    public string Path => _copy.Path;

    public string Folder => _copy.Folder;

    public Raffica.Shell.Controls.DialogHost Host => _host.Host;

    public static async Task<ExportRig> OpenAsync(string file = "pages-marked.pdf")
    {
        var copy = new TempCopy(file);
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(copy.Path, null);
        return new ExportRig(copy, new PdfTab(session, copy.Path));
    }

    public string NewFolder(string name = "out")
    {
        var folder = System.IO.Path.Combine(Folder, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public async Task<Dialog> CardWithAsync(string name)
    {
        await PrintHost.UntilAsync(() => _host.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name));
        return _host.Host.ActiveDialog!;
    }

    public static void Click(Dialog card, string name) =>
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    public static IReadOnlyList<string?> TextsOf(Dialog card) => card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    /// <summary>Opens the card, fills it, picks the folder and presses Export; returns the run.</summary>
    public async Task<Task<bool>> StartAsync(string folder, string range = "all", string dpi = "PART_Dpi72", string? custom = null, bool marks = true)
    {
        FolderPicks.Enqueue(folder);
        var run = Flow.ExportAsync(Tab);
        var card = await CardWithAsync("PART_Range");
        PrintHost.Find<TextBox>(card, "PART_Range").Text = range;
        PrintHost.Find<RadioButton>(card, dpi).IsChecked = true;
        if (custom is not null)
        {
            PrintHost.Find<TextBox>(card, "PART_DpiField").Text = custom;
        }

        PrintHost.Find<CheckBox>(card, "PART_Marks").IsChecked = marks;
        Click(card, "PART_ChooseFolder");
        var export = PrintHost.Find<Button>(card, "PART_Export");
        await PrintHost.UntilAsync(() => export.IsEnabled);
        Click(card, "PART_Export");
        return run;
    }

    public void Dispose()
    {
        _host.Dispose();
        Tab.Dispose();
        _copy.Dispose();
    }
}

/// <summary>Image export: the card, the files that result (read back), the check against the limit, and every way a run can end.</summary>
public sealed class ImageExportFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Pngs(string folder) => Directory.GetFiles(folder, "*.png").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    // The pixel size from the IHDR chunk of a PNG file.
    private static (int Width, int Height) SizeOfPng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[..4]);
        return (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    private static async Task<IReadOnlyList<PdfPageSize>> SizesAsync(ExportRig rig) =>
        await rig.Tab.Session.Document!.GetPageSizesAsync(0, rig.Tab.Session.Document.PageCount, PdfPriority.Interactive, Ct);

    private static async Task FinishAsync(ExportRig rig, Task<bool> run, bool expected)
    {
        var result = await rig.CardWithAsync("PART_Ok");
        ExportRig.Click(result, "PART_Ok");
        Assert.Equal(expected, await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));
    }

    [AvaloniaFact]
    public async Task ExportWritesOnePngPerPageNamedLikeTheCommandLine()
    {
        using var rig = await ExportRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var count = rig.Tab.Session.Document!.PageCount;
        var folder = rig.NewFolder();

        var run = await rig.StartAsync(folder);
        var result = await rig.CardWithAsync("PART_Ok");

        // The names are the ones of `raffica export` (the same call of Core: <name>-p<NNN>.png, three digits at least).
        var expected = Enumerable.Range(1, count).Select(i => $"pages-marked-p{i.ToString("D3", CultureInfo.InvariantCulture)}.png").ToArray();
        Assert.Equal(expected, Pngs(folder));
        Assert.Contains(ExportRig.TextsOf(result), t => t == $"{count} files were written.");
        ExportRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task ResolutionSetsThePixelSize()
    {
        using var rig = await ExportRig.OpenAsync();
        var sizes = await SizesAsync(rig);

        foreach (var (radio, dpi) in new[] { ("PART_Dpi72", 72), ("PART_Dpi150", 150) })
        {
            var folder = rig.NewFolder("out" + dpi);
            var run = await rig.StartAsync(folder, "2", radio);
            await FinishAsync(rig, run, true);
            var (width, height) = SizeOfPng(Path.Combine(folder, "pages-marked-p002.png"));
            var scale = dpi / 72.0;
            Assert.Equal((int)Math.Ceiling(sizes[1].Width * scale), width);
            Assert.Equal((int)Math.Ceiling(sizes[1].Height * scale), height);
        }

        // A custom value is the scale dpi / 72, too.
        var custom = rig.NewFolder("custom");
        var customRun = await rig.StartAsync(custom, "1", "PART_DpiCustom", "100");
        await FinishAsync(rig, customRun, true);
        Assert.Equal((int)Math.Ceiling(sizes[0].Width * 100 / 72.0), SizeOfPng(Path.Combine(custom, "pages-marked-p001.png")).Width);
    }

    [AvaloniaFact]
    public async Task MarksSwitchChangesThePixels()
    {
        using var rig = await ExportRig.OpenAsync("annotations.pdf");
        var withMarks = rig.NewFolder("with");
        var run = await rig.StartAsync(withMarks, "1", "PART_Dpi72", marks: true);
        await FinishAsync(rig, run, true);
        var without = rig.NewFolder("without");
        var second = await rig.StartAsync(without, "1", "PART_Dpi72", marks: false);
        await FinishAsync(rig, second, true);

        var name = Pngs(withMarks).Single();
        Assert.Equal(name, Pngs(without).Single());
        Assert.NotEqual(await File.ReadAllBytesAsync(Path.Combine(withMarks, name), Ct), await File.ReadAllBytesAsync(Path.Combine(without, name), Ct));
    }

    [AvaloniaFact]
    public async Task PageOverTheLimitIsNamedAndNothingIsWritten()
    {
        using var rig = await ExportRig.OpenAsync();
        var count = rig.Tab.Session.Document!.PageCount;
        var folder = rig.NewFolder();

        // Page 3 is a hostile size; the others are ordinary.
        rig.Flow.SizeReader = (_, _) => Task.FromResult<IReadOnlyList<PdfPageSize>>(
            Enumerable.Range(0, count).Select(i => i == 2 ? new PdfPageSize(20000, 20000) : new PdfPageSize(595, 842)).ToArray());
        rig.FolderPicks.Enqueue(folder);
        var run = rig.Flow.ExportAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Range");
        ExportRig.Click(card, "PART_ChooseFolder");
        var export = PrintHost.Find<Button>(card, "PART_Export");
        PrintHost.Find<RadioButton>(card, "PART_Dpi150").IsChecked = true;
        await PrintHost.UntilAsync(() => !string.IsNullOrEmpty(PrintHost.Find<TextBlock>(card, "PART_Folder").Text) && PrintHost.Find<TextBlock>(card, "PART_Folder").Text == folder);

        // Export is not possible, and the reason names the page with its number.
        Assert.False(export.IsEnabled);
        var hint = PrintHost.Find<TextBlock>(card, "PART_SizeHint");
        Assert.True(hint.IsVisible);
        Assert.Contains("Page 3 ", hint.Text, StringComparison.Ordinal);

        // Leaving the page out makes it possible again; choosing it again, not.
        PrintHost.Find<TextBox>(card, "PART_Range").Text = "1-2,4";
        Assert.True(export.IsEnabled);
        PrintHost.Find<TextBox>(card, "PART_Range").Text = "3";
        Assert.False(export.IsEnabled);

        ExportRig.Click(card, "PART_Cancel");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(Pngs(folder));
    }

    [AvaloniaFact]
    public async Task TheCheckOfTheAppAgreesWithTheRefusalOfCoreAtTheBoundary()
    {
        using var rig = await ExportRig.OpenAsync();
        var size = (await SizesAsync(rig))[0];
        var folder = rig.NewFolder();
        var document = rig.Tab.Session.Document!;

        foreach (var dpi in new[] { 72, 150, 300 })
        {
            var scale = ImageExportPlan.ScaleOf(dpi);
            var pixels = ImageExportPlan.PixelsOf(size, scale);

            // Exactly the limit is allowed, one pixel less is not, in the app and in Core.
            Assert.False(ImageExportPlan.Exceeds(size, scale, pixels));
            Assert.True(ImageExportPlan.Exceeds(size, scale, pixels - 1));
            var written = new List<string>();
            await PageImageExporter.ExportAsync(document, "boundary", [0], scale, true, folder, overwrite: true, null, Ct, written, rig.Path, pixels);
            Assert.Single(written);
            var refusal = await Assert.ThrowsAsync<PageExportException>(() =>
                PageImageExporter.ExportAsync(document, "boundary", [0], scale, true, folder, overwrite: true, null, Ct, null, rig.Path, pixels - 1));
            Assert.Equal(PageExportError.TooLarge, refusal.Error);
            Assert.Equal(0, refusal.Page);
        }

        // The card's verdict is the same check, also for a page that is over the limit (A4 at 1200 dpi).
        var scale1200 = ImageExportPlan.ScaleOf(1200);
        Assert.Equal(ImageExportPlan.Exceeds(size, scale1200), ImageExportPlan.Evaluate("all", "1200", [size]).OverPage == 0);
        var a4 = new PdfPageSize(595, 842);
        Assert.True(ImageExportPlan.Exceeds(a4, scale1200));
        var over = ImageExportPlan.Evaluate("all", "1200", [a4]);
        Assert.Equal(0, over.OverPage);
        Assert.False(over.IsOk);
    }

    [AvaloniaFact]
    public async Task ExistingFilesAreNotReplacedAndAreNamed()
    {
        using var rig = await ExportRig.OpenAsync();
        var folder = rig.NewFolder();
        var existing = Path.Combine(folder, "pages-marked-p002.png");
        byte[] sentinel = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(existing, sentinel, Ct);

        var run = await rig.StartAsync(folder);
        var result = await rig.CardWithAsync("PART_Ok");

        // Core refuses before the first file: nothing is written, the file in the way is named and is as it was.
        var texts = ExportRig.TextsOf(result);
        Assert.Contains(texts, t => t == PdfStrings.OutputNothingWritten);
        Assert.Contains(texts, t => t is not null && t.Contains("pages-marked-p002.png", StringComparison.Ordinal));
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(existing, Ct));
        Assert.Equal(["pages-marked-p002.png"], Pngs(folder));
        ExportRig.Click(result, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task CancelKeepsTheFinishedFilesAndListsThem()
    {
        using var rig = await ExportRig.OpenAsync();
        var folder = rig.NewFolder();

        // Cancel right after the first page is reported (deterministic: Core checks the token before each page).
        rig.Flow.Exporter = (document, stem, pages, scale, annotations, directory, progress, written, sourcePath, token) =>
            PageImageExporter.ExportAsync(document, stem, pages, scale, annotations, directory, false, new CancelAfterFirst(progress, rig.Flow), token, written, sourcePath);

        var run = await rig.StartAsync(folder);
        var result = await rig.CardWithAsync("PART_Ok");
        var texts = ExportRig.TextsOf(result);
        Assert.Contains(texts, t => t == PdfStrings.OutputCanceled);
        Assert.Contains(texts, t => t == PdfStrings.OutputWrittenBefore);
        var files = Pngs(folder);
        Assert.NotEmpty(files);
        Assert.True(files.Length < rig.Tab.Session.Document!.PageCount);

        // The finished files stay on disk, are complete PNG files and are the ones the card lists.
        foreach (var file in files)
        {
            Assert.True(SizeOfPng(Path.Combine(folder, file)).Width > 0);
        }

        Assert.Equal(files, result.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "PART_File").Select(t => t.Text).ToArray());
        ExportRig.Click(result, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(Directory.GetFiles(folder, ".pw-*"));
    }

    private sealed class CancelAfterFirst(IProgress<double>? inner, ImageExportFlow flow) : IProgress<double>
    {
        public void Report(double value)
        {
            inner?.Report(value);
            flow.Cancel();
        }
    }

    [AvaloniaFact]
    public async Task HundredPagesExportOffTheUiThread()
    {
        using var rig = await ExportRig.OpenAsync("thousand-pages.pdf");
        var folder = rig.NewFolder();
        var onUiThread = new List<bool>();
        var uiRanWhileExporting = false;
        rig.Flow.Exporter = async (document, stem, pages, scale, annotations, directory, progress, written, sourcePath, token) =>
        {
            onUiThread.Add(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());

            // The UI thread is free while the exporter runs: an action posted from the job runs before the export goes on.
            uiRanWhileExporting = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => true);
            return await PageImageExporter.ExportAsync(document, stem, pages, scale, annotations, directory, false, progress, token, written, sourcePath);
        };

        var run = await rig.StartAsync(folder, "1-100");

        // While the job runs the dispatcher keeps turning: the progress reaches the card.
        var ticks = 0;
        while (rig.Host.ActiveDialog is { } active && !active.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Ok") && ticks < 100_000)
        {
            ticks++;
            PrintHost.Pump();
            await Task.Delay(5, Ct);
        }

        var result = await rig.CardWithAsync("PART_Ok");
        Assert.Equal(100, Pngs(folder).Length);
        Assert.Equal(10, result.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "PART_File"));
        Assert.Contains(ExportRig.TextsOf(result), t => t == "and 90 more");
        Assert.Contains(ExportRig.TextsOf(result), t => t == "100 files were written.");
        Assert.Equal([false], onUiThread);
        Assert.True(uiRanWhileExporting);
        ExportRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));
    }

    [AvaloniaFact]
    public async Task EvaluateAllowsExactlyTheLimitAndRefusesOnePixelMore()
    {
        // 8000 x 8000 pt at 72 dpi is exactly 64,000,000 pixels: allowed. One point more on a side is over, and the page is named.
        var exact = ImageExportPlan.Evaluate("all", "72", [new PdfPageSize(8000, 8000)]);
        Assert.Equal(PageImageExporter.MaxPixels, (long)(exact.LargestWidth * exact.LargestHeight));
        Assert.Equal(-1, exact.OverPage);
        Assert.True(exact.IsOk);
        var over = ImageExportPlan.Evaluate("all", "72", [new PdfPageSize(595, 842), new PdfPageSize(8000, 8001)]);
        Assert.Equal(1, over.OverPage);
        Assert.Equal(8000L * 8001L, over.OverPixels);
        Assert.False(over.IsOk);

        // And the verdict is Core's, for a real page at the resolutions of the card.
        using var rig = await ExportRig.OpenAsync();
        var size = (await SizesAsync(rig))[0];
        var folder = rig.NewFolder();
        foreach (var dpi in new[] { 72, 150, 300 })
        {
            var view = ImageExportPlan.Evaluate("1", dpi.ToString(CultureInfo.InvariantCulture), [size]);
            var pixels = ImageExportPlan.PixelsOf(size, ImageExportPlan.ScaleOf(dpi));
            Assert.Equal(pixels > PageImageExporter.MaxPixels, view.OverPage == 0);
            await PageImageExporter.ExportAsync(rig.Tab.Session.Document!, "agree", [0], ImageExportPlan.ScaleOf(dpi), false, folder, true, null, Ct, null, rig.Path, view.OverPage == 0 ? pixels - 1 : pixels);
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheTabDuringTheSizeReadEndsTheFlowQuietly()
    {
        using var rig = await ExportRig.OpenAsync();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Flow.SizeReader = async (_, token) =>
        {
            reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return [];
        };
        var run = rig.Flow.ExportAsync(rig.Tab);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);

        rig.Tab.Dispose();

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(20), Ct));
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        Assert.False(rig.Flow.IsRunning);
    }

    [AvaloniaTheory]
    [InlineData("TooLarge")]
    [InlineData("DirectoryMissing")]
    [InlineData("TargetIsSource")]
    [InlineData("WriteFailed")]
    [InlineData("Render")]
    [InlineData("Generic")]
    public async Task EveryFailureEndsInACardWithItsOwnReason(string kind)
    {
        using var rig = await ExportRig.OpenAsync();
        var folder = rig.NewFolder();
        var (error, expected) = kind switch
        {
            "TooLarge" => (new PageExportException(1, "too large"), PdfStrings.Fill(PdfStrings.ExportFailTooLargeFormat, 2)),
            "DirectoryMissing" => (new PageExportException(PageExportError.DirectoryMissing, "m", -1, [], null), PdfStrings.OutputFailDirectoryMissing),
            "TargetIsSource" => (new PageExportException(PageExportError.TargetIsSource, "s", -1, [], null), PdfStrings.OutputFailTargetIsSource),
            "WriteFailed" => (new PageExportException(PageExportError.WriteFailed, "w", 1, ["x.png"], new IOException("disk")), PdfStrings.Fill(PdfStrings.ExportFailWriteFormat, 2)),
            "Render" => ((Exception)new PdfRenderException("r"), PdfStrings.ExportFailRender),
            _ => (new InvalidOperationException("boom"), PdfStrings.OutputFailOther),
        };
        rig.Flow.Exporter = (_, _, _, _, _, _, _, _, _, _) => throw error;

        var run = await rig.StartAsync(folder, "1");
        var result = await rig.CardWithAsync("PART_Ok");

        // Nothing escapes: a card names the reason, nothing was written, and the run ends as a failure.
        Assert.Equal(expected, PrintHost.Find<TextBlock>(result, "PART_Reason").Text);
        Assert.Contains(ExportRig.TextsOf(result), t => t == PdfStrings.OutputNothingWritten);
        ExportRig.Click(result, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(Pngs(folder));
    }

    [AvaloniaFact]
    public async Task RefusedWithAnOpenPlanOrUnsavedMarksAndWhileAnotherJobRuns()
    {
        using var rig = await ExportRig.OpenAsync();
        rig.Tab.Organize.Enter();
        var locked = rig.Flow.ExportAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Ok");
        Assert.Contains(ExportRig.TextsOf(card), t => t == PdfStrings.PageChangesLockedReason);
        ExportRig.Click(card, "PART_Ok");
        Assert.False(await locked.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(0, rig.FolderAsked);

        rig.Tab.Organize.End();
        using var pending = rig.Tab.Annotations.BeginPending();
        var marks = rig.Flow.ExportAsync(rig.Tab);
        var notice = await rig.CardWithAsync("PART_Ok");
        Assert.Contains(ExportRig.TextsOf(notice), t => t == PdfStrings.SaveMarksFirstReason);
        ExportRig.Click(notice, "PART_Ok");
        Assert.False(await marks.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task EveryNewTextIsFreeOfValidAndLegallyBindingInBothLanguages()
    {
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var keys = typeof(PdfStrings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(static p => p.Name)
            .Where(static n => n.StartsWith("Export", StringComparison.Ordinal))
            .ToList();
        Assert.True(keys.Count >= 18);
        foreach (var culture in new[] { "en", "de" })
        {
            var info = CultureInfo.GetCultureInfo(culture);
            Assert.All(keys, key => Assert.False(banned.IsMatch(PdfStrings.GetIn(key, info)), key + " " + culture));
        }

        // The texts the cards show (the result card included) in the language of the run.
        using var rig = await ExportRig.OpenAsync();
        var folder = rig.NewFolder();
        var run = await rig.StartAsync(folder, "1");
        var result = await rig.CardWithAsync("PART_Ok");
        Assert.All(ExportRig.TextsOf(result), t => Assert.False(t is not null && banned.IsMatch(t), t));
        ExportRig.Click(result, "PART_Ok");
        await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }
}
