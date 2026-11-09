using Avalonia.Platform.Storage;
using Raffica.Core.Ocr;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Settings;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf;

/// <summary>
/// The commands of the Edit tab that ask for text recognition: "Recognize text…" opens the options card (languages, pages, mode, resolution) and hands the
/// choice to <see cref="OcrRunStarter"/>; "Languages…" opens the language files card (the trust warning first, then the file picker) and copies the chosen
/// file into the folder of the user. Nothing is read from disk before one of the two is used: the tessdata folders are listed when a card opens, never at
/// startup or at the first sharp page. The run is <see cref="OcrRunFlow"/> (made on the first run, not before): it is not modal, its badge sits in the
/// <c>Badge</c> slot of the page view, its progress is the hint of the status bar, and Cancel (the badge, the ribbon command, the close question) stops it.
/// After the first sharp page of a document that looks like a scan the window offers <see cref="ScanHint"/>; the check is skipped in a measurement run and
/// without the OCR library, and the hint never starts a run itself.
/// </summary>
internal sealed partial class PdfWindow
{
    private Action<PdfTab, OcrOptions>? _ocrRunStarter;
    private Func<bool>? _ocrLibraryProbe;
    private bool? _ocrLibraryKnown;
    private bool _recognizeRunning;
    private bool _languagesRunning;
    private bool _ocrStarting;
    private OcrRunFlow? _ocrFlow;
    private readonly Dictionary<PdfTab, OcrParts> _ocrParts = [];
    private readonly HashSet<PdfTab> _scanHintChecked = [];
    private string? _ocrHintText;

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

    /// <summary>Replaces the engine factory of a run (tests: a fake engine); null: Tesseract over the language files of the window (<see cref="TessdataStoreOfWindow"/>). It runs on a pool thread.</summary>
    internal Func<string, IOcrEngineFactory>? OcrEngineFactory { get; set; }

    /// <summary>Replaces the question whether a page is a picture of text (tests: counts the calls); null: <see cref="ScanPageProbe.IsPictureAsync"/>.</summary>
    internal Func<PdfDocument, int, CancellationToken, Task<bool>>? ScanProbe { get; set; }

    /// <summary>Whether a text recognition runs in any tab (it holds the file: saving, printing and the other file jobs wait for it).</summary>
    private bool OcrBusy => _ocrFlow?.IsRunning == true;

    /// <summary>The run flow of the window, made on the first run (for tests).</summary>
    internal OcrRunFlow OcrFlow => _ocrFlow ??= new OcrRunFlow(
        Dialogs,
        languages => OcrEngineFactory is { } custom ? custom(languages) : DefaultEngineFactory(languages),
        tab => Workspace.Tabs.Contains(tab),
        RefreshCommandStates);

    /// <summary>Whether the run flow exists (it is made by the first run; for tests).</summary>
    internal bool OcrFlowCreated => _ocrFlow is not null;

    private TesseractEngineFactory DefaultEngineFactory(string languages) =>
        new TesseractEngineFactory(TessdataStoreOfWindow().ResolveFolder(OcrLanguages.Split(languages)));

    /// <summary>The badge of the recognition of a tab once it ran (for tests); null before.</summary>
    internal OcrRunBadge? OcrBadgeOf(PdfTab tab) => _ocrParts.GetValueOrDefault(tab)?.Badge;

    /// <summary>The Cancel recognition command (the ribbon, the badge): the run of the active tab stops, then its finished pages are asked about.</summary>
    internal void CancelOcr()
    {
        if (_ocrFlow is { RunningTab: { } running } flow && ReferenceEquals(running, Workspace.Active))
        {
            flow.Cancel();
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
        if (_recognizeRunning || _ocrStarting || OcrBusy || Workspace.Active is not { } tab || tab.Session.Document is not { } document || OcrRunStarter is not { } starter || !OcrLibraryAvailable())
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
            // The page that is shown now; the card keeps it, whatever the view does while the card is open.
            int? shown = _views.TryGetValue(tab, out var view) ? view.CurrentPage : null;
            var options = await Dialogs.ShowAsync<OcrOptions?>(done => RecognizeCard.Create(name, count, languages, signed, done, shown));

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
        // The files are read when an engine is made: no import while any run goes, in whatever tab (the command is locked too).
        if (_languagesRunning || Workspace.Active is null || OcrBusy)
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

    /// <summary>
    /// Starts the run for the options of the card. An open field edit and a typed comment are written first (the tools that held them are locked from here on);
    /// a value that is refused stays on screen with its reason and the run does not start. Whatever else keeps the run from starting after the card was confirmed
    /// (a running job, another mode, a permission, a document that changed meanwhile) ends in a note with its reason, never in nothing.
    /// </summary>
    internal async Task StartOcrRunAsync(PdfTab tab, OcrOptions options)
    {
        if (_ocrStarting || !Workspace.Tabs.Contains(tab) || !_views.TryGetValue(tab, out var view))
        {
            return;
        }

        // The guard covers the whole start: the Recognize card cannot open again during the flush (a held key), and ends where the mode takes over.
        _ocrStarting = true;
        string? refusal;
        try
        {
            var modes = new ModeExclusion(mode => ModeIsOnFor(tab, mode));
            refusal = OcrStartRefusal(tab, view, modes);
            if (refusal is null)
            {
                var editing = FormsOf(tab)?.Interaction.IsEditing == true;
                var typed = MarksOf(tab)?.HasUnappliedComment == true;
                if (editing && !FormsOf(tab)!.Interaction.EndOrReveal())
                {
                    // The refused value stays on screen with its own reason.
                    return;
                }

                if (editing || typed)
                {
                    await tab.Annotations.FlushAsync();
                    if (!Workspace.Tabs.Contains(tab))
                    {
                        return;
                    }

                    refusal = OcrStartRefusal(tab, view, modes)
                        ?? (FormsOf(tab)?.Interaction.IsEditing == true || MarksOf(tab)?.HasUnappliedComment == true ? PdfStrings.OcrNotStartedReason : null);
                }
            }

            if (refusal is null)
            {
                // The badge and the hint of the status bar follow the session from here; the scan hint has served its purpose. The run enters its mode before
                // its first await, so there is no gap in which the guard is off and the mode is not on.
                EnsureOcrParts(tab);
                if (_hosts.TryGetValue(tab, out var host))
                {
                    host.Hint?.End();
                }

                _ocrStarting = false;
                await OcrFlow.RunAsync(tab, options);
                UpdateOcrStatus();
                return;
            }
        }
        finally
        {
            _ocrStarting = false;
        }

        await Dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.OcrFailTitle, refusal, () => done(true)));
    }

    // Why a run cannot start now, in the UI language, or null when it can.
    private string? OcrStartRefusal(PdfTab tab, PageView view, ModeExclusion modes)
    {
        if (OcrBusy)
        {
            return PdfStrings.ModeOcrReason;
        }

        if (AnnotateAllowedOf(tab) == false)
        {
            return PdfStrings.AnnotateLockedReason;
        }

        if (BusyWithFileJob())
        {
            return PdfStrings.SaveBusy;
        }

        if (!view.FirstSharpPageShown)
        {
            return PdfStrings.OcrNotStartedReason;
        }

        return modes.CanEnter(PdfMode.Ocr, out var inTheWay) ? null : inTheWay;
    }

    private bool BusyWithFileJob() =>
        _saves.IsSaving || _print.IsRunning || _applyFlow?.IsRunning == true || _redactFlow?.IsRunning == true || _outputFlow?.IsRunning == true || _exportFlow?.IsRunning == true;

    // The badge of a tab is made with its first run and kept (hidden between runs) until the tab closes or is rebuilt.
    private void EnsureOcrParts(PdfTab tab)
    {
        if (_ocrParts.ContainsKey(tab))
        {
            return;
        }

        var session = tab.Ocr;
        var badge = new OcrRunBadge(session, CancelOcr);
        var wasActive = session.IsActive;

        // The commands are asked again when the mode starts or ends, not at every page: a run of a thousand pages reports a thousand times.
        void OnChanged()
        {
            if (session.IsActive != wasActive)
            {
                wasActive = session.IsActive;
                RefreshCommandStates();
            }

            UpdateOcrStatus();
        }

        session.Changed += OnChanged;
        _ocrParts[tab] = new OcrParts(session, badge, OnChanged);
        if (_hosts.TryGetValue(tab, out var host))
        {
            host.Ocr = badge;
        }
    }

    private void DisposeOcrParts(PdfTab tab)
    {
        if (!_ocrParts.Remove(tab, out var parts))
        {
            return;
        }

        parts.Session.Changed -= parts.Changed;
        if (_hosts.TryGetValue(tab, out var host) && ReferenceEquals(host.Ocr, parts.Badge))
        {
            host.Ocr = null;
        }

        parts.Badge.Dispose();
    }

    // The status bar says how far the run of the active tab is ("3 of 12 pages") and gives the cell back when it is over. A hint of another owner (the field
    // codes) is never overwritten or cleared: the cell is ours only while it holds the text this feature wrote.
    private void UpdateOcrStatus()
    {
        if (Workspace.Active?.OcrIfCreated is { IsActive: true, Progress: { } progress })
        {
            var text = PdfStrings.Fill(PdfStrings.OcrHintFormat, progress.PagesDone, progress.PagesTotal);
            if (string.IsNullOrEmpty(Status.Hint) || Status.Hint == _ocrHintText)
            {
                Status.Hint = text;
                _ocrHintText = text;
            }
        }
        else if (_ocrHintText is not null)
        {
            if (Status.Hint == _ocrHintText)
            {
                Status.Hint = null;
            }

            _ocrHintText = null;
        }
    }

    /// <summary>"Cancel the text recognition?" for a tab whose run still goes: Stop recognition is DontSave, Keep going is Cancel. A card that is already open answers Cancel.</summary>
    internal Task<CloseChoice> AskOcrAsync(PdfTab tab) => AskAsync(tab, UnsavedKind.Ocr);

    // The close of a tab or of the window stops the run and drops its pages.
    private Task StopOcrForCloseAsync(PdfTab tab) => _ocrFlow?.StopAsync(tab) ?? Task.CompletedTask;

    /// <summary>
    /// After the first sharp page of a tab: when the page shown is a picture of text and the library is there, the hint offers to make the document searchable.
    /// Asked once per tab and document (memory only); nothing is read in a measurement run or without the library, and nothing is made before the page is sharp.
    /// </summary>
    internal async Task OfferScanHintAsync(PdfTab tab)
    {
        if (_options.IsMeasuring || tab.OcrIfCreated is { ScanHintHandled: true } || _scanHintChecked.Contains(tab) || OcrRunStarter is null
            || !_views.TryGetValue(tab, out var view) || !view.FirstSharpPageShown || tab.Session.Document is not { } document || !OcrLibraryAvailable())
        {
            return;
        }

        _scanHintChecked.Add(tab);
        bool picture;
        try
        {
            picture = await (ScanProbe ?? ScanPageProbe.IsPictureAsync)(document, view.CurrentPage, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A page that cannot be read gives no hint.
            return;
        }

        if (!picture || !Workspace.Tabs.Contains(tab) || !ReferenceEquals(tab.Session.Document, document) || !_hosts.TryGetValue(tab, out var host) || ModeIsOnFor(tab, PdfMode.Ocr) || OcrBusy)
        {
            return;
        }

        tab.Ocr.ScanHintHandled = true;
        var hint = new ScanHint(() => _ = GuardAsync(RecognizeAsync));
        hint.Ended += () =>
        {
            if (ReferenceEquals(host.Hint, hint))
            {
                host.Hint = null;
            }
        };
        host.Hint = hint;
    }

    /// <summary>The pieces of the recognition of one tab: the session it follows, its badge and the handler that keeps the commands and the status bar current.</summary>
    private sealed record OcrParts(OcrSession Session, OcrRunBadge Badge, Action Changed);

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
