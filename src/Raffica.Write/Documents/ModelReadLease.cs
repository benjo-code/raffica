using Raffica.Write.Docx;

namespace Raffica.Write.Documents;

/// <summary>
/// The hold on the document model that a write of the file takes around the read of the model into memory: <see cref="DocxFile.BeforeModelRead"/> takes it and
/// <see cref="DocxFile.AfterModelRead"/> gives it back, both on the saving (pool) thread, never on the UI thread. Save and the recovery snapshot share it.
/// </summary>
internal static class ModelReadLease
{
    /// <summary>
    /// Sets the two hooks of <paramref name="docx"/>. The lease is taken on the saving thread right before the model is read (then <paramref name="afterLeaseTaken"/>, then the hook that
    /// was set before) and released in <see cref="DocxFile.AfterModelRead"/> (before the hook that was set before), or at once when a hook after the take throws, so it is released on
    /// every failure. Disposing the result puts the previous hooks back.
    /// </summary>
    /// <param name="docx">The file that is about to be written.</param>
    /// <param name="takeLease">Takes the lease on the calling thread.</param>
    /// <param name="afterLeaseTaken">Runs on the saving thread right after the lease was taken, before the model is read (a test seam, and where a snapshot reads its revision).</param>
    public static IDisposable Attach(DocxFile docx, Func<IDisposable> takeLease, Action? afterLeaseTaken = null)
    {
        ArgumentNullException.ThrowIfNull(docx);
        ArgumentNullException.ThrowIfNull(takeLease);
        var previousBefore = docx.BeforeModelRead;
        var previousAfter = docx.AfterModelRead;
        IDisposable? lease = null;
        docx.BeforeModelRead = () =>
        {
            lease = takeLease();
            try
            {
                afterLeaseTaken?.Invoke();
                previousBefore?.Invoke();
            }
            catch
            {
                Interlocked.Exchange(ref lease, null)?.Dispose();
                throw;
            }
        };
        docx.AfterModelRead = () =>
        {
            Interlocked.Exchange(ref lease, null)?.Dispose();
            previousAfter?.Invoke();
        };
        return new Restore(docx, previousBefore, previousAfter);
    }

    private sealed class Restore(DocxFile docx, Action? before, Action? after) : IDisposable
    {
        public void Dispose()
        {
            docx.BeforeModelRead = before;
            docx.AfterModelRead = after;
        }
    }
}
