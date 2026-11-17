using System.Diagnostics;
using System.Runtime.CompilerServices;
using Raffica.Write.Documents;
using Raffica.Write.Docx;

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

    /// <summary>The copy could not be written; a previous copy of the id is untouched. The exception is kept for the caller and never shown.</summary>
    public sealed record Failed(Exception Error) : SnapshotResult;
}

/// <summary>
/// Writes the recovery copy of one tab. The DOCX is written to <see cref="RecoveryStore.DocxPathOf"/> by <c>DocxFile.SaveAsync</c> (the save to another path of 4.1: atomic replace,
/// and neither the origin, the stamp nor the snapshot of the file change; nothing is marked saved) under the shared <see cref="ModelReadLease"/>, so the lease is taken and released
/// by this (pool) thread around the read of the model only. The revision is read inside the lease, so the revision recorded is the one written.
/// </summary>
/// <remarks>
/// <b>One writer per file.</b> A snapshot and a user save share the hooks of one <see cref="DocxFile"/> and the runner's lease, and the file has no gate of its own. So
/// <see cref="WriteAsync"/> must be called on the UI thread, where the start of a save and the start of a snapshot are serialized: it marks the file "snapshot in flight"
/// synchronously, before its first await, and clears the mark in <c>finally</c>. A second snapshot of the same file meanwhile is <see cref="SnapshotSkip.Busy"/>; a save
/// awaits <see cref="WhenIdleAsync"/> (and re-checks <see cref="IsRunning"/> with no await in between) before it attaches its hooks, so the hooks of the two never chain. The
/// mark is a <see cref="ConditionalWeakTable{TKey, TValue}"/> keyed by the file, so it needs nothing from the subject and dies with the file.
/// <para>
/// <b>Write order and failure.</b> The DOCX first, the sidecar second; the sidecar is the commit marker (the listing ignores a DOCX without one, because a live instance may be
/// between the two writes). Later copies: the DOCX is replaced atomically and the old sidecar beside it stays listable and openable (only its size and time are older); a failed
/// DOCX write touches neither file. First copy: when the sidecar write fails after the DOCX was written, this call deletes the DOCX it created (best effort, silent), so no orphan
/// stays in a running process. A crash in the milliseconds between the two writes of a first copy loses only that first copy and leaves an orphan DOCX; that is a known limit.
/// The failure is returned, never thrown.
/// </para>
/// </remarks>
internal static class RecoverySnapshot
{
    private static readonly ConditionalWeakTable<DocxFile, InFlight> s_inFlight = [];
    private static readonly Lock s_gate = new();
    private static (int Id, DateTimeOffset Started)? s_self;

    private static Func<(int Id, DateTimeOffset Started)> s_readSelf = DefaultReadSelf;

    /// <summary>Test seam: reads the identity of this process (id and start time); setting it forgets what was cached.</summary>
    internal static Func<(int Id, DateTimeOffset Started)> ReadSelf
    {
        get => s_readSelf;
        set
        {
            lock (s_gate)
            {
                s_readSelf = value;
                s_self = null;
            }
        }
    }

    /// <summary>Whether a snapshot of the file is in flight.</summary>
    public static bool IsRunning(DocxFile docx)
    {
        ArgumentNullException.ThrowIfNull(docx);
        lock (s_gate)
        {
            return s_inFlight.TryGetValue(docx, out _);
        }
    }

    /// <summary>A task that completes when no snapshot of the file is in flight (at once when none is). Await it, never wait on it.</summary>
    public static Task WhenIdleAsync(DocxFile docx)
    {
        ArgumentNullException.ThrowIfNull(docx);
        lock (s_gate)
        {
            return s_inFlight.TryGetValue(docx, out var running) ? running.Done.Task : Task.CompletedTask;
        }
    }

    /// <summary>Writes the copy. Call it on the UI thread (see the remarks of the type); the file and the lease are used on a pool thread.</summary>
    public static async Task<SnapshotResult> WriteAsync(IRecoverySubject subject, RecoveryStore store, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(store);
        if (subject.CannotSnapshotNow is { } skip)
        {
            return new SnapshotResult.Skipped(skip);
        }

        var docx = subject.Docx;
        var mark = new InFlight();
        lock (s_gate)
        {
            if (s_inFlight.TryGetValue(docx, out _))
            {
                return new SnapshotResult.Skipped(SnapshotSkip.Busy);
            }

            s_inFlight.Add(docx, mark);
        }

        try
        {
            return await WriteMarkedAsync(subject, docx, store, token).ConfigureAwait(false);
        }
        finally
        {
            lock (s_gate)
            {
                s_inFlight.Remove(docx);
            }

            mark.Done.TrySetResult();
        }
    }

    private static async Task<SnapshotResult> WriteMarkedAsync(IRecoverySubject subject, DocxFile docx, RecoveryStore store, CancellationToken token)
    {
        var total = Stopwatch.StartNew();
        var originalAfter = docx.AfterModelRead;
        var docxWritten = false;
        var hadSidecar = false;
        var id = subject.Id;
        try
        {
            // Everything that can fail without touching the disk comes first.
            var (pid, started) = Self();
            store.EnsureFolder();
            var path = store.DocxPathOf(id);
            hadSidecar = store.SidecarExists(id);
            var revision = -1;
            var held = new Stopwatch();
            var heldTime = TimeSpan.Zero;

            // Set before Attach, so it runs after the lease is released: the time between the take and here is the time the model was held.
            docx.AfterModelRead = () => heldTime = held.Elapsed;
            using (ModelReadLease.Attach(docx, subject.TakeModelLease, () =>
            {
                held.Restart();

                // The state may have changed between the check at the start and the lease (a fault, a save that started): checked again where nothing can change it.
                if (subject.CannotSnapshotNow is { } late)
                {
                    throw new SkipSignal(late);
                }

                revision = subject.Revision;
            }))
            {
                await docx.SaveAsync(path, overwriteChangedOriginal: false, token).ConfigureAwait(false);
                docxWritten = true;
            }

            var entry = new RecoveryEntry(id, subject.Title, subject.OriginalPath, DateTimeOffset.Now, new FileInfo(path).Length, pid, started);
            await store.WriteSidecarAsync(entry).ConfigureAwait(false);
            return new SnapshotResult.Written(revision, heldTime, total.Elapsed);
        }
        catch (SkipSignal signal)
        {
            return new SnapshotResult.Skipped(signal.Reason);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (docxWritten && !hadSidecar)
            {
                // This call created the DOCX and no sidecar commits it: take it away again (silent; a later copy of the id starts clean).
                store.Delete(id);
            }

            return new SnapshotResult.Failed(ex);
        }
        finally
        {
            docx.AfterModelRead = originalAfter;
        }
    }

    // The identity of this process, read at the first snapshot (never at start) and cached only when the read worked, so a read that threw is tried again next time.
    private static (int Id, DateTimeOffset Started) Self()
    {
        lock (s_gate)
        {
            return s_self ??= s_readSelf();
        }
    }

    private static (int Id, DateTimeOffset Started) DefaultReadSelf()
    {
        using var process = Process.GetCurrentProcess();
        return (Environment.ProcessId, new DateTimeOffset(process.StartTime));
    }

    private sealed class InFlight
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SkipSignal(SnapshotSkip reason) : Exception("The snapshot was skipped.")
    {
        public SnapshotSkip Reason { get; } = reason;
    }
}
