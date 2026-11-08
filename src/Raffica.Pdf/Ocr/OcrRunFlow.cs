using Avalonia.Threading;
using Raffica.Core.Annotations;
using Raffica.Core.Ocr;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Ocr;

/// <summary>
/// One text recognition from the options to the report. It is not modal: the page view stays readable while the run goes (the session of the tab is the mode that
/// locks the commands, its badge shows the progress and holds Cancel, the status bar says "3 of 12 pages"). The engine factory is made on a pool thread, the
/// run reports on other threads and every report is posted to the UI thread first, the result is written with <see cref="AnnotationEditor.ApplyOcrAsync"/> as one step
/// of the undo history (an unsaved change; the save is the incremental one, so the original bytes stay the beginning of the file), and the report card names every page that was
/// not recognized with its reason. A cancel asks what to do with the pages that were finished (Discard is the default; nothing is written without a question), a cancel by the close of
/// the tab or the window drops them. Every refusal and failure ends in a card with its reason. At most one run at a time; the mode stays on until the last card of the run is closed.
/// </summary>
internal sealed class OcrRunFlow
{
    private readonly DialogQueue _dialogs;
    private readonly Func<string, IOcrEngineFactory> _factoryFor;
    private readonly Func<PdfTab, bool> _isOpen;
    private readonly Action _refresh;
    private Run? _current;
    private bool _abandoned;

    /// <param name="dialogs">Where the cards appear.</param>
    /// <param name="factoryFor">Makes the engine factory for the normalized languages (it runs on a pool thread; it throws <see cref="TessdataMissingException"/> for a language without a file).</param>
    /// <param name="isOpen">Whether a tab is still open.</param>
    /// <param name="refresh">Asks the commands again (the mode or the run changed).</param>
    public OcrRunFlow(DialogQueue dialogs, Func<string, IOcrEngineFactory> factoryFor, Func<PdfTab, bool> isOpen, Action refresh)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(factoryFor);
        ArgumentNullException.ThrowIfNull(isOpen);
        ArgumentNullException.ThrowIfNull(refresh);
        _dialogs = dialogs;
        _factoryFor = factoryFor;
        _isOpen = isOpen;
        _refresh = refresh;
    }

    /// <summary>Tests: called on the UI thread right after the runner returned a result, before the flow looks at a cancel that arrived meanwhile.</summary>
    internal Action? RunReturned { get; set; }

    /// <summary>Whether a run is on (from its start to the close of its last card).</summary>
    public bool IsRunning => _current is not null;

    /// <summary>The tab the run belongs to, or null.</summary>
    public PdfTab? RunningTab => _current?.Tab;

    /// <summary>The user's Cancel: the run stops, then the finished pages are asked about.</summary>
    public void Cancel() => _current?.Session.Cancel();

    /// <summary>Stops the run and drops everything it did (the window is closing): no card is shown afterwards.</summary>
    public void Abandon()
    {
        _abandoned = true;
        if (_current is { } run)
        {
            run.Discard = true;
            run.Session.Cancel();
        }
    }

    /// <summary>Stops the run of a tab without asking about its finished pages (the close of the tab) and returns when it has ended.</summary>
    public async Task StopAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_current is { } run && ReferenceEquals(run.Tab, tab))
        {
            run.Discard = true;
            run.Session.Cancel();
            await run.Ended.Task.ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Runs the recognition of the open document of <paramref name="tab"/> with the chosen options. Nothing happens when a run is on or the tab has no document;
    /// everything else ends in a card. The caller has ended open edits first.
    /// </summary>
    public async Task RunAsync(PdfTab tab, OcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(options);
        if (_current is not null || tab.Session.Document is not { } document)
        {
            return;
        }

        var session = tab.Ocr;
        var run = new Run(tab, session);
        _current = run;
        if (session.Enter(PdfStrings.OcrAnnounceStart) is not { } token)
        {
            _current = null;
            return;
        }

        _refresh();
        try
        {
            await RunCoreAsync(run, document, options, token).ConfigureAwait(true);
        }
        catch (ObjectDisposedException)
        {
            // The tab or the document went while the run was on: there is nobody to tell.
        }
        finally
        {
            session.Leave();
            _current = null;
            run.Ended.TrySetResult();
            _refresh();
        }
    }

    private async Task RunCoreAsync(Run run, Raffica.Core.Pdf.PdfDocument document, OcrOptions options, CancellationToken token)
    {
        var tab = run.Tab;
        var session = run.Session;
        var languages = OcrLanguages.Normalize(options.Languages);
        if (languages is null)
        {
            await FailAsync(run, PdfStrings.OcrFailLanguageInvalid).ConfigureAwait(true);
            return;
        }

        IOcrEngineFactory factory;
        try
        {
            factory = await Task.Run(() => _factoryFor(languages), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A missing library or language file is named; anything else is the plain failure.
            await FailAsync(run, OcrFailure.From(ex) is { } failure ? OcrWording.Text(failure) : PdfStrings.OcrFailFailed).ConfigureAwait(true);
            return;
        }

        OcrRunResult? result = null;
        OcrRunResult? partial = null;
        try
        {
            result = await OcrRunner.RunAsync(document, options, factory, new UiProgress(this, run), token).ConfigureAwait(true);
        }
        catch (OcrRunCanceledException canceled)
        {
            partial = canceled.Partial;
        }
        catch (OperationCanceledException)
        {
            partial = null;
        }
        catch (Exception ex) when (OcrFailure.From(ex) is { } failure)
        {
            await FailAsync(run, OcrWording.Text(failure)).ConfigureAwait(true);
            return;
        }
        catch (ArgumentException)
        {
            // The options were checked by the card; a page that is not there any more is the same case.
            await FailAsync(run, PdfStrings.OcrFailFailed).ConfigureAwait(true);
            return;
        }

        if (_abandoned || !_isOpen(tab))
        {
            return;
        }

        RunReturned?.Invoke();

        // A Cancel that arrived after the last page but before the run returned is a cancel: the finished pages are asked about, never written silently.
        var canceledRun = result is null || (session.CancelRequested && !run.Discard);
        var toWrite = result ?? partial;
        if (canceledRun)
        {
            // Nothing finished, or the close of the tab or window asks for nothing to be kept: the file stays as it is.
            if (run.Discard || toWrite is null || toWrite.Pages.Count == 0)
            {
                session.Say(PdfStrings.OcrAnnounceCanceled);
                return;
            }

            var keep = await _dialogs.ShowAsync<bool>(done => OcrKeepPagesCard.CreateKeep(toWrite.Pages.Count, tab.Title, done)).ConfigureAwait(true);
            if (!keep || _abandoned || run.Discard || !_isOpen(tab) || !ReferenceEquals(tab.Session.Document, document))
            {
                session.Say(PdfStrings.OcrAnnounceCanceled);
                return;
            }
        }
        else if (run.Discard)
        {
            // The close arrived after the last page: nothing is written for a tab that is going.
            return;
        }

        var applied = await ApplyAsync(run, toWrite!, options.Mode == OcrMode.Redo).ConfigureAwait(true);
        if (applied.Failed || _abandoned || !_isOpen(tab))
        {
            return;
        }

        var report = OcrWording.Report(toWrite!.Report, applied.Result, canceledRun);
        session.Say(canceledRun
            ? PdfStrings.Fill(PdfStrings.OcrAnnounceKeptFormat, toWrite.Pages.Count)
            : PdfStrings.Fill(PdfStrings.OcrAnnounceDoneFormat, toWrite.Report.PagesRecognized));
        await _dialogs.ShowAsync<bool>(done => OcrReportCard.Create(report, tab.Title, () => done(true))).ConfigureAwait(true);
    }

    // Writes the result as one step. A run too large for the undo history is written all the same (the report says it cannot be undone); a refusal is a card.
    private async Task<Applied> ApplyAsync(Run run, OcrRunResult result, bool replace)
    {
        var tab = run.Tab;
        try
        {
            // The handle keeps the tab from closing under the write and is disposed on every path.
            using (tab.Annotations.BeginPending())
            {
                return new Applied(await tab.Annotations.Editor.ApplyOcrAsync(result, replace).ConfigureAwait(true), Failed: false);
            }
        }
        catch (InvalidOperationException)
        {
            // PDFium refused: nothing is left of the run.
            await FailAsync(run, PdfStrings.OcrFailApply).ConfigureAwait(true);
            return new Applied(null, Failed: true);
        }
    }

    private async Task FailAsync(Run run, string reason)
    {
        run.Session.Say(PdfStrings.OcrAnnounceFailed);
        if (_abandoned || !_isOpen(run.Tab))
        {
            return;
        }

        await _dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.OcrFailTitle, reason, () => done(true))).ConfigureAwait(true);
    }

    // Reports arrive on the threads of the run: every one is posted to the UI thread, where it is checked against the run that is on.
    private void OnProgress(Run run, OcrProgress progress)
    {
        if (!ReferenceEquals(_current, run) || !run.Session.IsActive)
        {
            return;
        }

        run.Session.Report(progress, StepMessage(progress));
    }

    // The sentence changes at the start of a page count that is a tenth of the run (or every page of a short run), so a screen reader is told the steps, not every page.
    private static string? StepMessage(OcrProgress progress)
    {
        var step = Math.Max(1, (int)Math.Ceiling(progress.PagesTotal / 10.0));
        return progress.PagesDone == progress.PagesTotal || progress.PagesDone % step == 0
            ? PdfStrings.Fill(PdfStrings.OcrBadgeProgressFormat, progress.PagesDone, progress.PagesTotal)
            : null;
    }

    private sealed class UiProgress(OcrRunFlow flow, Run run) : IProgress<OcrProgress>
    {
        // Reports arrive on the thread of the work: post to the dispatcher this was made on, not to the static one.
        private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

        public void Report(OcrProgress value) => _dispatcher.Post(() => flow.OnProgress(run, value));
    }

    private sealed record Applied(OcrApplyResult? Result, bool Failed);

    private sealed class Run(PdfTab tab, OcrSession session)
    {
        public PdfTab Tab { get; } = tab;

        public OcrSession Session { get; } = session;

        public bool Discard { get; set; }

        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
