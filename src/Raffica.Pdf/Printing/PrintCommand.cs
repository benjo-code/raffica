using Avalonia.Threading;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Settings;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Printing;

/// <summary>
/// The Print command: refuses a document that forbids printing, asks for the choices (the dialog of the system where there is one, the
/// <see cref="PrintCard"/> of the app elsewhere), then runs the job in the background with a <see cref="PrintJobCard"/> (progress, Cancel)
/// and reports the pages that failed when it ends. One job at a time. The command must be called on the UI thread: the Windows dialog is
/// shown on the calling thread. It never waits for PDFium on it, because the services print on their own thread and the progress is
/// handed back to the UI thread.
/// </summary>
internal sealed class PrintCommand
{
    private const int MaxListedPages = 10;

    private readonly Func<IPrintService> _service;
    private readonly Func<PdfTab?> _active;
    private readonly PrintPresenter _presenter;
    private readonly Func<AppSettings> _settings;
    private readonly Action<string?> _rememberPrinter;
    private readonly Func<int>? _currentPage;
    private readonly Func<nint>? _owner;
    private readonly Func<bool>? _saveRunning;
    private CancellationTokenSource? _running;

    /// <param name="service">The print service of the system; asked lazily, so nothing is probed before the first print.</param>
    /// <param name="active">The tab to print.</param>
    /// <param name="presenter">Where the dialog, the job card and the messages appear.</param>
    /// <param name="settings">The viewer settings in force (the printer used last, the scaling).</param>
    /// <param name="rememberPrinter">Stores the printer a job was sent to.</param>
    /// <param name="currentPage">The page being looked at, 0-based (null: the first).</param>
    /// <param name="owner">The native handle of the window that owns the system dialog (null: none).</param>
    /// <param name="saveRunning">Whether a save runs; printing is then refused (null: never).</param>
    public PrintCommand(
        Func<IPrintService> service,
        Func<PdfTab?> active,
        PrintPresenter presenter,
        Func<AppSettings> settings,
        Action<string?> rememberPrinter,
        Func<int>? currentPage = null,
        Func<nint>? owner = null,
        Func<bool>? saveRunning = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(rememberPrinter);
        _service = service;
        _active = active;
        _presenter = presenter;
        _settings = settings;
        _rememberPrinter = rememberPrinter;
        _currentPage = currentPage;
        _owner = owner;
        _saveRunning = saveRunning;
    }

    /// <summary>Gets whether a job is running (a second one is refused).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Cancels the running job, if any (the window is closing).</summary>
    public void Cancel()
    {
        try
        {
            _running?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job ended meanwhile.
        }
    }

    /// <summary>Gets whether the system can print at all (on Linux: CUPS is installed); this is what the command rules ask.</summary>
    public bool IsAvailable => _service().IsAvailable;

    /// <summary>
    /// The text for a page list: the pages counted from 1, ascending, the first ten, then "and 40 more".
    /// </summary>
    /// <param name="failedPages">The pages that failed, 0-based.</param>
    internal static string FailedPagesText(IReadOnlyList<int> failedPages)
    {
        var pages = failedPages.Distinct().Order().ToList();
        var listed = string.Join(", ", pages.Take(MaxListedPages).Select(p => (p + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return pages.Count > MaxListedPages ? PdfStrings.Fill(PdfStrings.PrintFailedMoreFormat, listed, pages.Count - MaxListedPages) : listed;
    }

    /// <summary>
    /// Prints the active tab. Returns when the job has ended (or when there was nothing to print); the UI stays free meanwhile, and the
    /// messages it shows do not hold it back.
    /// </summary>
    public async Task RunAsync()
    {
        if (IsRunning)
        {
            _presenter.Message(PdfStrings.PrintBusy);
            return;
        }

        if (_saveRunning?.Invoke() == true)
        {
            _presenter.Message(PdfStrings.SaveBusy);
            return;
        }

        if (_active() is not { } tab || tab.Session.Document is not { } document)
        {
            return;
        }

        IsRunning = true;
        try
        {
            await PrintAsync(tab, document);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever the services do not foresee ends as a plain message, never as an exception on the UI thread.
            _presenter.Message(ErrorText(ex));
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>The text for the user: our own messages as they are, everything else (it may name paths) as the general one.</summary>
    internal static string ErrorText(Exception exception)
    {
        if (exception is PrintException)
        {
            string[] known =
            [
                PdfStrings.PrintLockedReason,
                PdfStrings.PrintErrorStart,
                PdfStrings.PrintErrorPage,
                PdfStrings.PrintErrorDialog,
                PdfStrings.PrintNoService,
                PdfStrings.PrintNoPrinter,
            ];
            if (Array.IndexOf(known, exception.Message) >= 0)
            {
                return exception.Message;
            }
        }

        return PdfStrings.PrintErrorStart;
    }

    private async Task PrintAsync(PdfTab tab, PdfDocument document)
    {
        var service = _service();
        if (!service.IsAvailable)
        {
            _presenter.Message(PdfStrings.PrintNoPrinter);
            return;
        }

        // An open field edit is committed first (the same flush the save path uses), so the pages carry the value the user just typed; a draft
        // the rules refuse stays where it is and the pages carry the last accepted value. A tab that never had an annotation session has
        // nothing to flush, and none is made for it.
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
        }

        var info = await document.GetPrintInfoAsync();
        if (!info.PrintAllowed)
        {
            _presenter.Message(PdfStrings.PrintLockedReason);
            return;
        }

        var app = _settings();
        var current = Math.Clamp(_currentPage?.Invoke() ?? 0, 0, Math.Max(0, document.PageCount - 1));
        var defaults = new PrintSettings { Printer = app.LastPrinter ?? string.Empty, Scaling = app.PrintScaling };
        PrintTicket? ticket;
        try
        {
            ticket = await AskAsync(service, tab, document, current, defaults);
        }
        catch (PrintException ex)
        {
            _presenter.Message(ErrorText(ex));
            return;
        }

        if (ticket is null)
        {
            return;
        }

        // The ticket belongs to this method from here on: it is disposed whether the job ran, failed or was cancelled.
        using (ticket)
        {
            if (ticket.Settings.Printer.Length > 0)
            {
                _rememberPrinter(ticket.Settings.Printer);
            }

            await RunJobAsync(service, tab, document, current, ticket);
        }
    }

    private async Task<PrintTicket?> AskAsync(IPrintService service, PdfTab tab, PdfDocument document, int current, PrintSettings defaults)
    {
        if (service.HasSystemDialog)
        {
            // Called here, on the UI thread: the Windows dialog needs the thread of the caller and an STA.
            return await service.ShowDialogAsync(new PrintRequest(document.PageCount, current, tab.Title, _owner?.Invoke() ?? 0, defaults), CancellationToken.None);
        }

        var printers = await service.GetPrintersAsync(CancellationToken.None);
        if (printers.Count == 0)
        {
            _presenter.Message(PdfStrings.PrintNoPrinter);
            return null;
        }

        var chosen = await _presenter.ChooseAsync(new PrintCardInput(
            document.PageCount,
            current,
            tab.Title,
            printers,
            defaults,
            name => service.SupportsDuplexAsync(name, CancellationToken.None)));
        return chosen is null ? null : new PrintTicket(chosen);
    }

    private async Task RunJobAsync(IPrintService service, PdfTab tab, PdfDocument document, int current, PrintTicket ticket)
    {
        using var cancellation = new CancellationTokenSource();
        _running = cancellation;
        var card = _presenter.ShowJob(tab.Title, () =>
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The job ended while the click was on its way.
            }
        });
        var progress = new JobProgress(card);

        // The tab that is closing takes its session with it: the job is cancelled before the session goes, and what the next page then
        // throws is no error to report.
        var tabClosing = false;
        void OnTabDisposing()
        {
            tabClosing = true;
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The job ended meanwhile.
            }
        }

        tab.Disposing += OnTabDisposing;
        try
        {
            // A file with unsaved changes has no file to print: the service then writes a copy from the document in memory.
            var path = tab.IsDirty ? null : tab.Path;
            await service.PrintAsync(new PrintJobRequest(document, path, tab.Title, current, ticket), progress, cancellation.Token);
            if (progress.FailedPages.Count > 0)
            {
                _presenter.Message(PdfStrings.Fill(PdfStrings.PrintFailedPagesFormat, FailedPagesText(progress.FailedPages)));
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled job ends quietly.
        }
        catch (Exception ex) when (ex is ObjectDisposedException || (tabClosing && ex is not OutOfMemoryException))
        {
            // The document went away under the job (its tab was closed): the same as a cancel.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _presenter.Message(ErrorText(ex));
        }
        finally
        {
            tab.Disposing -= OnTabDisposing;
            _running = null;
            _presenter.HideJob(card);
        }
    }

    /// <summary>
    /// Takes the progress from the thread of the service: the failed pages are kept at once (so the report is complete when the job returns),
    /// the card is updated on the UI thread.
    /// </summary>
    private sealed class JobProgress(PrintJobCard card) : IProgress<PrintProgress>
    {
        private IReadOnlyList<int> _failed = [];

        public IReadOnlyList<int> FailedPages => Volatile.Read(ref _failed);

        public void Report(PrintProgress value)
        {
            Volatile.Write(ref _failed, value.FailedPages);
            Dispatcher.UIThread.Post(() => card.Report(value));
        }
    }
}
