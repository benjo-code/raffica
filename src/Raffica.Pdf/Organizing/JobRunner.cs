using Avalonia.Threading;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// The job of a file flow (apply, extract, split, later export): work on a pool thread behind a modal dialog with the job card (progress, Cancel).
/// The window is blocked meanwhile, so nothing the job reads changes. Canceling, the tab closing or <see cref="Cancel"/> (the window closing) cancel
/// the token. The exception of the work always reaches the caller; a job card that cannot be shown or made never replaces the result of the work
/// (a written file is no failure). One runner per flow, one job at a time.
/// </summary>
/// <param name="dialogs">Where the job card appears.</param>
internal sealed class JobRunner(DialogQueue dialogs)
{
    private CancellationTokenSource? _running;

    /// <summary>Replaces the dialog around the job card (tests make it throw); null: the normal dialog.</summary>
    internal Func<SaveJobCard, Dialog>? JobDialog { get; set; }

    /// <summary>Cancels the running job, if any.</summary>
    public void Cancel() => TryCancel(_running);

    /// <summary>Runs <paramref name="work"/> on a pool thread behind the job card.</summary>
    /// <param name="tab">The tab the job is about (its title is on the card; closing it cancels the job).</param>
    /// <param name="dialogTitle">The title of the dialog.</param>
    /// <param name="titleFormat">The first line of the card with {0} for the document name.</param>
    /// <param name="work">The work; it gets the progress (handed to the UI thread) and the token. Anything it needs from a UI-bound object is resolved before.</param>
    public async Task<T> RunAsync<T>(PdfTab tab, string dialogTitle, string titleFormat, Func<IProgress<double>, CancellationToken, Task<T>> work)
    {
        using var cancellation = new CancellationTokenSource();
        _running = cancellation;
        var card = new SaveJobCard(tab.Title, () => TryCancel(cancellation), titleFormat);
        Action<bool>? close = null;
        var finished = false;
        var shown = dialogs.ShowAsync<bool>(done =>
        {
            // A card that was waiting in the queue while the job already ended is not shown.
            if (finished)
            {
                throw new OperationCanceledException();
            }

            close = done;
            var dialog = JobDialog?.Invoke(card) ?? new Dialog { Title = dialogTitle, Content = card };
            dialog.CancelRequested += (_, _) => card.RequestCancel();
            return dialog;
        });

        void OnTabDisposing() => TryCancel(cancellation);
        tab.Disposing += OnTabDisposing;
        try
        {
            // Made here, on the UI thread: it captures the dispatcher of the thread it is made on.
            var progress = new UiProgress(card);
            return await Task.Run(() => work(progress, cancellation.Token));
        }
        finally
        {
            tab.Disposing -= OnTabDisposing;
            _running = null;
            finished = true;
            close?.Invoke(true);
            try
            {
                await shown;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The card never appeared or could not be made: that must not replace the result of the job.
            }
        }
    }

    /// <summary>Runs a step whose failure must not turn into a failed flow (a refresh of the window after the file is written).</summary>
    internal static void Guarded(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The work is done; a step after it that fails must not turn into the card "the file is unchanged".
        }
    }

    /// <summary>Whether two paths name the same file by their text (by the rule of PathRules).</summary>
    internal static bool SamePath(string a, string? b) =>
        b is not null && PathRules.AreEqual(Path.GetFullPath(a), Path.GetFullPath(b));

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job ended meanwhile.
        }
    }

    /// <summary>Takes the progress from the PDFium thread and hands it to the card on the UI thread.</summary>
    private sealed class UiProgress(SaveJobCard card) : IProgress<double>
    {
        // Reports arrive on the thread of the work: post to the dispatcher this was made on, not to the static one.
        private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

        private double _last;

        public void Report(double value)
        {
            // The bar never goes back, whatever order the steps of the work report in.
            double shown;
            lock (this)
            {
                shown = _last = Math.Max(_last, value);
            }

            _dispatcher.Post(() => card.Report(shown));
        }
    }
}
