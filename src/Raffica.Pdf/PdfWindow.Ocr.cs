using Avalonia.Platform.Storage;
using Raffica.Core.Ocr;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Settings;

namespace Raffica.Pdf;

/// <summary>
/// The commands of the Edit tab that ask for text recognition: "Recognize text…" opens the options card (languages, pages, mode, resolution) and hands the
/// choice to <see cref="OcrRunStarter"/>; "Languages…" opens the language files card (the trust warning first, then the file picker) and copies the chosen
/// file into the folder of the user. Nothing is read from disk before one of the two is used: the tessdata folders are listed when a card opens, never at
/// startup or at the first sharp page. The run itself and its cancel command arrive with the next task.
/// </summary>
internal sealed partial class PdfWindow
{
    private Action<PdfTab, OcrOptions>? _ocrRunStarter;
    private Func<bool>? _ocrLibraryProbe;
    private bool? _ocrLibraryKnown;
    private bool _recognizeRunning;
    private bool _languagesRunning;

    /// <summary>
    /// What starts a run with the options of the card (the run flow of the next task). While it is null the Recognize command stays disabled, so the card
    /// is never offered for something that would not start.
    /// </summary>
    internal Action<PdfTab, OcrOptions>? OcrRunStarter
    {
        get => _ocrRunStarter;
        set
        {
            _ocrRunStarter = value;
            Ribbon?.RefreshCommandStates();
        }
    }

    /// <summary>Replaces the question whether the OCR library is next to the program (tests); null: <see cref="TesseractEngineFactory.IsLibraryAvailable"/>, asked once.</summary>
    internal Func<bool>? OcrLibraryProbe
    {
        get => _ocrLibraryProbe;
        set
        {
            _ocrLibraryProbe = value;
            _ocrLibraryKnown = null;
            Ribbon?.RefreshCommandStates();
        }
    }

    /// <summary>Replaces the folder of the language files that ship with the program (tests); null: the <c>tessdata</c> folder next to the program.</summary>
    internal string? ShippedTessdataDirectory { get; set; }

    /// <summary>Replaces the system file dialog of "Languages…" (tests): the chosen path, null when the user canceled. Null: the system dialog.</summary>
    internal Func<Task<string?>>? LanguageFilePicker { get; set; }

    /// <summary>Replaces the copy of a language file (tests, a slow one): path and whether an earlier file is replaced. Null: <see cref="TessdataStore.Import"/>.</summary>
    internal Func<string, bool, TessdataImportResult>? TessdataImporter { get; set; }

    // The library is looked for once (a file test), the first time a document is open and the rules ask.
    private bool OcrLibraryAvailable() => _ocrLibraryKnown ??= (_ocrLibraryProbe ?? (() => TesseractEngineFactory.IsLibraryAvailable))();

    private TessdataStore TessdataStoreOfWindow() => new(
        PdfSettingsFiles.TessdataDirectoryFor(_options.SettingsFile),
        ShippedTessdataDirectory ?? TessdataStore.ShippedDirectoryFor(AppContext.BaseDirectory));

    private void RunOcrCommand() => _ = GuardAsync(RecognizeAsync);

    private void ShowLanguageFiles() => _ = GuardAsync(LanguagesAsync);

    /// <summary>The Recognize command: the options card for the open document; a choice is stored (the languages) and handed to <see cref="OcrRunStarter"/>.</summary>
    internal async Task RecognizeAsync()
    {
        // A held key or two clicks must not pile up cards.
        if (_recognizeRunning || Workspace.Active is not { } tab || tab.Session.Document is not { } document || OcrRunStarter is not { } starter || !OcrLibraryAvailable())
        {
            return;
        }

        _recognizeRunning = true;
        try
        {
            var signed = SignaturePolicy.Evaluate(await document.GetSignatureInfoAsync()) == SignatureAdvice.WouldInvalidate;
            var store = TessdataStoreOfWindow();
            var installed = await Task.Run(store.List);
            var languages = new LanguageList(installed, PdfStrings.Culture.TwoLetterISOLanguageName, Settings.App.OcrLanguages);
            var name = tab.Title;
            var count = document.PageCount;
            var options = await Dialogs.ShowAsync<OcrOptions?>(done => RecognizeCard.Create(name, count, languages, signed, done));

            // The tab may be gone or the document replaced while the card was open: nothing starts for it then.
            if (options is null || !Workspace.Tabs.Contains(tab) || !ReferenceEquals(tab.Session.Document, document))
            {
                return;
            }

            Settings.Update(Settings.App with { OcrLanguages = options.Languages });
            starter(tab, options);
        }
        finally
        {
            _recognizeRunning = false;
        }
    }

    /// <summary>The Languages command: the trust warning, then the file picker, then the copy off the UI thread and its result.</summary>
    internal async Task LanguagesAsync()
    {
        if (_languagesRunning || Workspace.Active is null)
        {
            return;
        }

        _languagesRunning = true;
        try
        {
            var store = TessdataStoreOfWindow();
            var installed = await Task.Run(store.List);
            if (!await Dialogs.ShowAsync<bool>(done => LanguageFilesCard.Create(installed, done)))
            {
                return;
            }

            var path = LanguageFilePicker is { } picker ? await picker() : await PickLanguageFileAsync();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Func<string, bool, TessdataImportResult> import = TessdataImporter ?? store.Import;
            var result = await LanguageFilesCard.ImportAsync(import, path, replaceImported: false);
            if (result.Error == TessdataImportError.AlreadyImported && result.Language is { } language)
            {
                if (await Dialogs.ShowAsync<bool>(done => LanguageFilesCard.CreateReplace(language, done)))
                {
                    result = await LanguageFilesCard.ImportAsync(import, path, replaceImported: true);
                }
            }

            await Dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.OcrImportTitle, PdfStrings.OcrImportMessage(result.Error, result.Language), () => done(true)));
        }
        finally
        {
            _languagesRunning = false;
        }
    }

    private async Task<string?> PickLanguageFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = PdfStrings.OcrFilesPickTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(PdfStrings.OcrFilesFilter) { Patterns = ["*.traineddata"] }],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
