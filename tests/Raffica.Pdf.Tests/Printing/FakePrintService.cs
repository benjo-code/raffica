using Raffica.Core.Printing;

namespace Raffica.Pdf.Tests.Printing;

/// <summary>
/// A print service for the tests: it records what it was asked, prints nothing and reports page by page from a pool thread (as the
/// Windows service does from its own thread). Like the real services it makes a temporary file for the job and deletes it on every path.
/// </summary>
internal sealed class FakePrintService : IPrintService
{
    private readonly object _lock = new();

    public bool IsAvailable { get; set; } = true;

    public bool HasSystemDialog { get; set; } = true;

    /// <summary>What <see cref="ShowDialogAsync"/> answers (null: the user cancelled).</summary>
    public PrintTicket? DialogResult { get; set; }

    public List<PrintRequest> DialogRequests { get; } = [];

    public List<PrinterInfo> Printers { get; } = [];

    public bool DuplexSupported { get; set; }

    /// <summary>Pages (0-based) that fail: they are skipped and reported in <see cref="PrintProgress.FailedPages"/>.</summary>
    public HashSet<int> FailPages { get; } = [];

    /// <summary>The position in the job (0-based) before which the job waits for <see cref="Gate"/>, or -1 for no wait.</summary>
    public int PauseBefore { get; set; } = -1;

    public TaskCompletionSource Gate { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>An exception to throw instead of printing.</summary>
    public Exception? Fail { get; set; }

    public List<PrintJobRequest> Jobs { get; } = [];

    /// <summary>The pages (0-based) the service printed, per job, in order.</summary>
    public List<List<int>> Printed { get; } = [];

    /// <summary>The temporary files the jobs made (all deleted when the job ends, whatever way).</summary>
    public List<string> TemporaryFiles { get; } = [];

    /// <summary>Raised when the job waits at <see cref="PauseBefore"/>.</summary>
    public TaskCompletionSource Paused { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<PrintTicket?> ShowDialogAsync(PrintRequest request, CancellationToken cancellationToken)
    {
        if (!HasSystemDialog)
        {
            throw new NotSupportedException();
        }

        lock (_lock)
        {
            DialogRequests.Add(request);
        }

        return Task.FromResult(DialogResult);
    }

    public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrinterInfo>>(Printers.ToArray());

    public Task<bool> SupportsDuplexAsync(string printerName, CancellationToken cancellationToken) => Task.FromResult(DuplexSupported);

    public Task PrintAsync(PrintJobRequest job, IProgress<PrintProgress> progress, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Jobs.Add(job);
        }

        return Task.Run(() => RunAsync(job, progress, cancellationToken), CancellationToken.None);
    }

    private async Task RunAsync(PrintJobRequest job, IProgress<PrintProgress> progress, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "raffica-fakeprint-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(temporary, [1, 2, 3], CancellationToken.None);
        var printed = new List<int>();
        lock (_lock)
        {
            TemporaryFiles.Add(temporary);
            Printed.Add(printed);
        }

        try
        {
            if (Fail is not null)
            {
                throw Fail;
            }

            var plan = PrintPlan.Create(job.Document.PageCount, job.CurrentPage, job.Ticket.Settings);
            var failed = new List<int>();
            for (var i = 0; i < plan.Sequence.Count; i++)
            {
                if (i == PauseBefore)
                {
                    Paused.TrySetResult();
                    await Gate.Task.WaitAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var page = plan.Sequence[i];
                if (FailPages.Contains(page))
                {
                    failed.Add(page);
                }
                else
                {
                    lock (_lock)
                    {
                        printed.Add(page);
                    }
                }

                progress.Report(new PrintProgress(i + 1, plan.Sequence.Count, failed.ToArray()));
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
