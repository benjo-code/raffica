using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class LanguageFilesTests
{
    private static string Source(string folder, string fileName, int length = 32)
    {
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }

    private static async Task<Dialog> CardWithAsync(WindowFixture f, string button)
    {
        await PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == button));
        return f.Window.Host.ActiveDialog!;
    }

    private static async Task PressAsync(WindowFixture f, string button)
    {
        var card = await CardWithAsync(f, button);
        PrintHost.Find<Button>(card, button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PrintHost.Pump();
    }

    private static async Task<WindowFixture> OpenedAsync()
    {
        var f = WindowFixture.Create();
        await f.OpenAsync("pages-marked.pdf");
        f.Window.ShippedTessdataDirectory = Path.Combine(f.Folder, "no-shipped");
        return f;
    }

    [AvaloniaFact]
    public async Task WarningComesBeforeThePicker()
    {
        foreach (var language in new[] { "en", "de" })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
            using var f = await OpenedAsync();
            var picked = 0;
            f.Window.LanguageFilePicker = () =>
            {
                picked++;
                return Task.FromResult<string?>(null);
            };

            var run = f.Window.LanguagesAsync();
            var card = await CardWithAsync(f, "PART_Choose");

            // The warning is on screen, in the language of the app, and no picker has been asked for.
            Assert.Equal(0, picked);
            Assert.Equal(PdfStrings.OcrFilesTrustWarning, PrintHost.Find<TextBlock>(card, "PART_Trust").Text);
            Assert.Equal(PdfStrings.OcrFilesCopyNote, PrintHost.Find<TextBlock>(card, "PART_CopyNote").Text);
            Assert.Equal(language == "de", PdfStrings.OcrFilesTrustWarning.Contains("Quelle", StringComparison.Ordinal));
            Assert.Equal(language == "en", PdfStrings.OcrFilesTrustWarning.Contains("source", StringComparison.Ordinal));

            await PressAsync(f, "PART_Choose");
            await PrintHost.UntilAsync(() => run.IsCompleted);
            Assert.Equal(1, picked);
        }
    }

    [AvaloniaFact]
    public async Task CloseAndEscapeNeverOpenThePicker()
    {
        using var f = await OpenedAsync();
        var picked = 0;
        f.Window.LanguageFilePicker = () =>
        {
            picked++;
            return Task.FromResult<string?>(null);
        };

        var run = f.Window.LanguagesAsync();
        await PressAsync(f, "PART_CloseCard");
        await PrintHost.UntilAsync(() => run.IsCompleted);
        Assert.Equal(0, picked);

        run = f.Window.LanguagesAsync();
        var card = await CardWithAsync(f, "PART_CloseCard");
        Assert.NotNull(card);
        f.Window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        await PrintHost.UntilAsync(() => run.IsCompleted);
        Assert.Equal(0, picked);
    }

    [AvaloniaFact]
    public async Task ImportRunsOffTheUiThread()
    {
        // The copy itself: a slow one does not hold the UI thread.
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var uiThread = Environment.CurrentManagedThreadId;
        var copyThread = -1;
        var import = new Func<string, bool, TessdataImportResult>((path, replace) =>
        {
            copyThread = Environment.CurrentManagedThreadId;
            started.Set();
            release.Wait(TimeSpan.FromSeconds(20));
            return new TessdataImportResult(TessdataImportError.None, "deu", path);
        });

        var task = LanguageFilesCard.ImportAsync(import, "deu.traineddata", replaceImported: false);
        Assert.True(await Task.Run(() => started.Wait(TimeSpan.FromSeconds(10))));
        var roundTrip = Dispatcher.UIThread.InvokeAsync(() => 42).GetTask();
        Assert.Same(roundTrip, await Task.WhenAny(roundTrip, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal(42, await roundTrip);
        Assert.False(task.IsCompleted, "the copy is still running while the UI answered");
        Assert.NotEqual(uiThread, copyThread);

        release.Set();
        Assert.Equal(TessdataImportError.None, (await task).Error);
    }

    [AvaloniaFact]
    public async Task ImportThroughTheWindowKeepsTheUiResponsive()
    {
        using var f = await OpenedAsync();
        var source = Source(f.Folder, "deu.traineddata");
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        f.Window.LanguageFilePicker = () => Task.FromResult<string?>(source);
        f.Window.TessdataImporter = (path, replace) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(20));
            return new TessdataImportResult(TessdataImportError.None, "deu", path);
        };

        var run = f.Window.LanguagesAsync();
        await PressAsync(f, "PART_Choose");
        await PrintHost.UntilAsync(() => started.IsSet);
        var roundTrip = Dispatcher.UIThread.InvokeAsync(() => 1).GetTask();
        Assert.Same(roundTrip, await Task.WhenAny(roundTrip, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.False(run.IsCompleted);

        release.Set();
        var card = await CardWithAsync(f, "PART_Ok");
        Assert.Equal(PdfStrings.OcrImportMessage(TessdataImportError.None, "deu"), PrintHost.Find<TextBlock>(card, "PART_Message").Text);
        await PressAsync(f, "PART_Ok");
        await PrintHost.UntilAsync(() => run.IsCompleted);
    }

    [AvaloniaFact]
    public async Task BadNamesAndOversizedFilesAreRefusedWithAReason()
    {
        using var f = await OpenedAsync();
        var folder = Directory.CreateTempSubdirectory("raffica-bad-").FullName;
        try
        {
            var big = Source(folder, "big.traineddata", 0);
            using (var stream = new FileStream(big, FileMode.Open))
            {
                stream.SetLength(TessdataStore.MaxFileLength + 1);
            }

            var cases = new (string Path, TessdataImportError Error)[]
            {
                (Source(folder, "Deu 1.traineddata"), TessdataImportError.InvalidName),
                (Source(folder, "notes.txt"), TessdataImportError.InvalidName),
                (Path.Combine(folder, "gone.traineddata"), TessdataImportError.NotAFile),
                (Source(folder, "empty.traineddata", 0), TessdataImportError.Empty),
                (big, TessdataImportError.TooLarge),
            };

            foreach (var (path, error) in cases)
            {
                f.Window.LanguageFilePicker = () => Task.FromResult<string?>(path);
                var run = f.Window.LanguagesAsync();
                await PressAsync(f, "PART_Choose");
                var card = await CardWithAsync(f, "PART_Ok");

                // The card says why, in the words of the app (never Core's sentence), and nothing was added.
                var text = PrintHost.Find<TextBlock>(card, "PART_Message").Text;
                Assert.Equal(PdfStrings.OcrImportMessage(error, null), text);
                Assert.Contains("Nothing was added", text, StringComparison.Ordinal);
                await PressAsync(f, "PART_Ok");
                await PrintHost.UntilAsync(() => run.IsCompleted);
            }

            var userFolder = Raffica.Pdf.Settings.PdfSettingsFiles.TessdataDirectoryFor(Path.Combine(f.Folder, "settings.json"));
            Assert.Equal(f.Folder, Path.GetDirectoryName(userFolder));
            Assert.Empty(Directory.Exists(userFolder) ? Directory.GetFiles(userFolder, "*.traineddata") : []);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task AnImporterThatThrowsIsAFailedImportWithWordingAndLeavesNothingBehind()
    {
        // The copy itself: an exception that is not out of memory becomes the Failed result, never an unhandled one.
        var direct = await LanguageFilesCard.ImportAsync((_, _) => throw new InvalidOperationException("English text of the exception"), "deu.traineddata", replaceImported: false);
        Assert.Equal(TessdataImportError.Failed, direct.Error);
        Assert.DoesNotContain("exception", PdfStrings.OcrImportMessage(direct.Error, direct.Language), StringComparison.OrdinalIgnoreCase);

        // Through the window: the card says it in the words of the app, and no file or temporary file is in the folder of the user.
        using var f = await OpenedAsync();
        var folder = Directory.CreateTempSubdirectory("raffica-throw-").FullName;
        try
        {
            var source = Source(folder, "deu.traineddata");
            f.Window.LanguageFilePicker = () => Task.FromResult<string?>(source);
            f.Window.TessdataImporter = (_, _) => throw new InvalidOperationException("boom");
            var run = f.Window.LanguagesAsync();
            await PressAsync(f, "PART_Choose");
            var card = await CardWithAsync(f, "PART_Ok");
            Assert.Equal(PdfStrings.OcrImportFailed, PrintHost.Find<TextBlock>(card, "PART_Message").Text);
            await PressAsync(f, "PART_Ok");
            await PrintHost.UntilAsync(() => run.IsCompleted);

            var user = Raffica.Pdf.Settings.PdfSettingsFiles.TessdataDirectoryFor(Path.Combine(f.Folder, "settings.json"));
            Assert.Empty(Directory.Exists(user) ? Directory.GetFileSystemEntries(user) : []);
            Assert.True(File.Exists(source));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task AFileThatWasAddedBeforeIsReplacedOnlyOnRequest()
    {
        using var f = await OpenedAsync();
        var folder = Directory.CreateTempSubdirectory("raffica-replace-").FullName;
        try
        {
            var user = Raffica.Pdf.Settings.PdfSettingsFiles.TessdataDirectoryFor(Path.Combine(f.Folder, "settings.json"));
            var chosen = Source(folder, "fra.traineddata", 40);
            f.Window.LanguageFilePicker = () => Task.FromResult<string?>(chosen);

            async Task RunOnceAsync(string answer)
            {
                var run = f.Window.LanguagesAsync();
                await PressAsync(f, "PART_Choose");
                if (answer != "none")
                {
                    var replace = await CardWithAsync(f, "PART_Replace");

                    // "Keep" is the default (the primary button); "Replace" is the plain one.
                    Assert.Contains("primary", (IEnumerable<string>)PrintHost.Find<Button>(replace, "PART_Keep").Classes);
                    Assert.DoesNotContain("primary", (IEnumerable<string>)PrintHost.Find<Button>(replace, "PART_Replace").Classes);
                    await PressAsync(f, answer);
                }

                await PressAsync(f, "PART_Ok");
                await PrintHost.UntilAsync(() => run.IsCompleted);
            }

            await RunOnceAsync("none");
            Assert.Equal(40, new FileInfo(Path.Combine(user, "fra.traineddata")).Length);

            File.WriteAllBytes(Path.Combine(folder, "fra.traineddata"), new byte[80]);
            await RunOnceAsync("PART_Keep");
            Assert.Equal(40, new FileInfo(Path.Combine(user, "fra.traineddata")).Length);

            await RunOnceAsync("PART_Replace");
            Assert.Equal(80, new FileInfo(Path.Combine(user, "fra.traineddata")).Length);
            Assert.True(File.Exists(Path.Combine(folder, "fra.traineddata")), "the original is copied, never moved");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void EveryImportErrorAndLanguageChoiceHasWordingInBothLanguages()
    {
        foreach (var language in new[] { "en", "de" })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
            var texts = Enum.GetValues<TessdataImportError>().Select(e => PdfStrings.OcrImportMessage(e, "deu")).ToList();
            Assert.All(texts, t =>
            {
                Assert.False(string.IsNullOrWhiteSpace(t), language);
                Assert.DoesNotMatch(@"\{\d\}", t);
            });
            Assert.Equal(texts.Count, texts.Distinct().Count());
            Assert.Contains("deu", texts[(int)TessdataImportError.None], StringComparison.Ordinal);

            // Nothing of Core's English sentences, and no member without wording.
            Assert.All(texts, t => Assert.DoesNotContain("language data", t, StringComparison.OrdinalIgnoreCase));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfStrings.OcrImportMessage((TessdataImportError)999, null));

            var choices = Enum.GetValues<Raffica.Pdf.Ocr.LanguageChoice>().Where(c => c != Raffica.Pdf.Ocr.LanguageChoice.Done).Select(PdfStrings.OcrLanguageRefusal).ToList();
            Assert.All(choices, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.Equal(choices.Count, choices.Distinct().Count());
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfStrings.OcrLanguageRefusal(Raffica.Pdf.Ocr.LanguageChoice.Done));
        }
    }
}
