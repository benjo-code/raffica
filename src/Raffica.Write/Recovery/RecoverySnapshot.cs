using System.Diagnostics;
using Raffica.Write.Documents;

namespace Raffica.Write.Recovery;

/// <summary>What one snapshot did.</summary>
internal abstract record SnapshotResult
{
    private SnapshotResult()
    {
    }

    /// <summary>A copy was written.</summary>
    /// <param name="Revision">The revision of the model that was written (read inside the lease).</param>
    /// <param name="LeaseHeld">How long the model-read lease was held.</param>
    /// <param name="Total">How long the whole snapshot took.</param>
    public sealed record Written(int Revision, TimeSpan LeaseHeld, TimeSpan Total) : SnapshotResult;

    /// <summary>Nothing was written, for the reason.</summary>
    public sealed record Skipped(SnapshotSkip Reason) : SnapshotResult;

    /// <summary>The copy could not be written; the previous copy of the id is untouched. The exception is kept for the caller and never shown.</summary>
    public sealed record Failed(Exception Error) : SnapshotResult;
}

/// <summary>
/// Writes the recovery copy of one tab. The DOCX is written to <see cref="RecoveryStore.DocxPathOf"/> by <c>DocxFile.SaveAsync</c> (the save to another path of 4.1: atomic replace,
/// and neither the origin, the stamp nor the snapshot of the file change; nothing is marked saved) under the shared <see cref="ModelReadLease"/>, so the lease is taken and released
/// by this (pool) thread around the read of the model only. The revision is read inside the lease, so the revision recorded is the one written.
/// </summary>
/// <remarks>
/// <b>Write order.</b> The DOCX first, the sidecar second. The sidecar is the commit marker of a copy (the listing ignores a DOCX without one), so a first copy that dies between the two
/// writes is not offered and not claimed; a later copy of the same id replaces the DOCX atomically and the old sidecar then still lists a whole, openable DOCX (only its size and time
/// are older). A failure of the DOCX write leaves the previous DOCX and sidecar as they were and the sidecar is not touched; a failure of the sidecar write leaves a whole new DOCX
/// beside the previous sidecar. In no case does a sidecar name a file that is missing or half written. The failure is returned, never thrown, never shown.
/// </remarks>
internal static class RecoverySnapshot
{
    private static readonly Lazy<(int Id, DateTimeOffset Started)> s_self = new(static () =>
    {
        using var process = Process.GetCurrentProcess();
        return (Environment.ProcessId, new DateTimeOffset(process.StartTime));
    });

    public static async Task<SnapshotResult> WriteAsync(IRecoverySubject subject, RecoveryStore store, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(store);
        if (subject.CannotSnapshotNow is { } skip)
        {
            return new SnapshotResult.Skipped(skip);
        }

        var total = Stopwatch.StartNew();
        var docx = subject.Docx;
        var originalAfter = docx.AfterModelRead;
        try
        {
            store.EnsureFolder();
            var revision = -1;
            var held = new Stopwatch();
            var heldTime = TimeSpan.Zero;
            var path = store.DocxPathOf(subject.Id);

            // Set before Attach, so it runs after the lease is released: the time between the take and here is the time the model was held.
            docx.AfterModelRead = () => heldTime = held.Elapsed;
            using (ModelReadLease.Attach(docx, subject.TakeModelLease, () =>
            {
                held.Restart();
                revision = subject.Revision;
            }))
            {
                await docx.SaveAsync(path, overwriteChangedOriginal: false, token).ConfigureAwait(false);
            }

            var (pid, started) = s_self.Value;
            var entry = new RecoveryEntry(subject.Id, subject.Title, subject.OriginalPath, DateTimeOffset.Now, new FileInfo(path).Length, pid, started);
            await store.WriteSidecarAsync(entry).ConfigureAwait(false);
            return new SnapshotResult.Written(revision, heldTime, total.Elapsed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new SnapshotResult.Failed(ex);
        }
        finally
        {
            docx.AfterModelRead = originalAfter;
        }
    }
}
