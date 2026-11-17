using System.Diagnostics;
using System.Globalization;
using System.Text;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace WriteLayoutBench;

/// <summary>
/// <c>edit --snapshot</c>: what one recovery snapshot of the document costs, through the UI-free editor (the app's <c>--measure-typing-snapshot</c> measures the typing beside it). Each round types
/// one letter at the first typing case (so the model differs from the copy before) and writes the document to a temporary file with <see cref="DocxFile.SaveSnapshotAsync"/>
/// (the call a recovery snapshot makes, which reuses the blocks that did not change), then once more with the plain <see cref="DocxFile.SaveAsync(string, bool, CancellationToken)"/> for comparison. The lease is the span between <see cref="DocxFile.BeforeModelRead"/> and <see cref="DocxFile.AfterModelRead"/> (the time the app holds the edit lease; no contention
/// here), the total is the whole call. Recorded, never judged: the 16 ms limit is judged on the typing beside a snapshot, in the app.
/// </summary>
internal static partial class EditCommand
{
    private const int DefaultSnapshots = 10;

    /// <summary>The snapshots of <see cref="MeasureSnapshot"/>: the lease and the total in milliseconds per snapshot, and the size of the last copy.</summary>
    internal sealed record SnapshotResult(double[] LeaseMillis, double[] TotalMillis, double[] FullLeaseMillis, double[] FullTotalMillis, long Bytes, double CopyAllocatedMiB, double FullAllocatedMiB, int Gen2);

    internal static SnapshotResult MeasureSnapshot(string file, int snapshots)
    {
        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var model = docx.Model;
        var fonts = FontCatalog.CreateShipped();
        var images = new PackageImages(bytes);
        var layout = LayOut(model, fonts, images, LayoutMode.Pages);
        var place = EditCases.Pick(model, layout).FirstOrDefault()
            ?? throw new InvalidOperationException("the document has no place to type at");
        var editor = new TextEditor(model, (story, paragraph) => layout.ProjectionHost(story, paragraph));
        var position = new TextPosition(place.Story, place.Paragraph, place.Offset);
        var random = new Random(Seed);

        var target = Path.Combine(Path.GetTempPath(), $"write-layout-bench-snapshot-{Environment.ProcessId}-{Guid.NewGuid():N}.docx");
        var lease = new double[snapshots];
        var total = new double[snapshots];
        var fullLease = new double[snapshots];
        var fullTotal = new double[snapshots];
        long size = 0;
        long copyAllocated = 0;
        long fullAllocated = 0;
        var gen2 = GC.CollectionCount(2);
        var held = new Stopwatch();
        docx.BeforeModelRead = held.Restart;
        docx.AfterModelRead = held.Stop;
        try
        {
            // One copy before the rounds: the first write makes the folder entry and warms the code, as the first copy of a session does (it is not a round).
            docx.SaveSnapshotAsync(target, CancellationToken.None).GetAwaiter().GetResult();
            for (var i = 0; i < snapshots; i++)
            {
                var result = editor.InsertText(new TextRange(position, position), EditCases.KeyText(random, i));
                position = result.Caret;
                var allocatedBefore = GC.GetTotalAllocatedBytes(false);
                var start = Stopwatch.GetTimestamp();
                docx.SaveSnapshotAsync(target, CancellationToken.None).GetAwaiter().GetResult();
                copyAllocated += GC.GetTotalAllocatedBytes(false) - allocatedBefore;
                total[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                lease[i] = held.Elapsed.TotalMilliseconds;

                // The same model through the plain save, for the comparison: it writes every block.
                allocatedBefore = GC.GetTotalAllocatedBytes(false);
                start = Stopwatch.GetTimestamp();
                docx.SaveAsync(target, overwriteChangedOriginal: false, CancellationToken.None).GetAwaiter().GetResult();
                fullAllocated += GC.GetTotalAllocatedBytes(false) - allocatedBefore;
                fullTotal[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                fullLease[i] = held.Elapsed.TotalMilliseconds;
            }

            size = new FileInfo(target).Length;
        }
        finally
        {
            docx.BeforeModelRead = null;
            docx.AfterModelRead = null;
            File.Delete(target);
        }

        return new SnapshotResult(lease, total, fullLease, fullTotal, size, copyAllocated / 1048576.0 / snapshots, fullAllocated / 1048576.0 / snapshots, GC.CollectionCount(2) - gen2);
    }

    internal static string SnapshotTable(SnapshotResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"snapshot",-20}{"n",5}{"median ms",11}{"p95 ms",9}{"max ms",9}"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{"lease held (copy)",-20}{result.LeaseMillis.Length,5}{EditRow.Percentile(result.LeaseMillis, 0.5),11:0.000}{EditRow.Percentile(result.LeaseMillis, 0.95),9:0.000}{result.LeaseMillis.Max(),9:0.000}"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{"total (copy)",-20}{result.TotalMillis.Length,5}{EditRow.Percentile(result.TotalMillis, 0.5),11:0.000}{EditRow.Percentile(result.TotalMillis, 0.95),9:0.000}{result.TotalMillis.Max(),9:0.000}"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{"lease held (full)",-20}{result.FullLeaseMillis.Length,5}{EditRow.Percentile(result.FullLeaseMillis, 0.5),11:0.000}{EditRow.Percentile(result.FullLeaseMillis, 0.95),9:0.000}{result.FullLeaseMillis.Max(),9:0.000}"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{"total (full)",-20}{result.FullTotalMillis.Length,5}{EditRow.Percentile(result.FullTotalMillis, 0.5),11:0.000}{EditRow.Percentile(result.FullTotalMillis, 0.95),9:0.000}{result.FullTotalMillis.Max(),9:0.000}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"copy size {result.Bytes / 1024.0 / 1024.0:0.00} MiB; allocated per copy {result.CopyAllocatedMiB:0.0} MiB (plain save {result.FullAllocatedMiB:0.0} MiB), gen2 collections {result.Gen2}; no limit: the model is held for the lease span only; typing beside a snapshot is judged in the app (--measure-typing-snapshot)."));
        return text.ToString();
    }
}
