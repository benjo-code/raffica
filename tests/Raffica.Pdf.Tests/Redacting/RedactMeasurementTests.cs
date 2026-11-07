using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using Raffica.Core.Text;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>
/// Measurement of M2.5 (<c>Docs/DECISIONS.md</c>, "Raffica PDF: redaction, OCR and text editing measurements (M2.5)"): the redaction of a 1000-page file
/// through the flow of the window (one area mark per page). Runs only with <c>RAFFICA_MEASURE_OUT=&lt;folder&gt;</c> (best in Release:
/// <c>dotnet test tests/Raffica.Pdf.Tests --filter-class "*RedactMeasurementTests"</c>) and writes <c>redact-measurements.txt</c>; information only,
/// no time is asserted. The target file lies in a temp folder.
/// </summary>
public sealed class RedactMeasurementTests
{
    private const string OutputVariable = "RAFFICA_MEASURE_OUT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task ApplyOnAThousandPageFile()
    {
        var output = Environment.GetEnvironmentVariable(OutputVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(output), $"Measurement only: set {OutputVariable}=<folder>.");
        output = Directory.CreateDirectory(output).FullName;

        using var rig = await RedactApplyRig.OpenAsync("thousand-pages.pdf");
        var input = new FileInfo(rig.Path).Length;
        var watch = Stopwatch.StartNew();
        for (var page = 0; page < 1000; page++)
        {
            rig.MarkArea(page, new PageRect(20, 20, 120, 60));
        }

        var marked = watch.Elapsed.TotalMilliseconds;
        Assert.Equal(1000, rig.Session.Plan.Marks.Count);
        var target = Path.Combine(rig.Folder, "redacted.pdf");
        rig.Picks.Enqueue(target);

        watch.Restart();
        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await CardAsync(rig, "PART_SaveAs");
        var analyzed = watch.Elapsed.TotalMilliseconds;
        rig.Time.Advance(Raffica.Pdf.Dialogs.ArmingLock.DelayMilliseconds + 100);
        watch.Restart();
        RedactApplyRig.Click(card, "PART_SaveAs");
        var result = await CardAsync(rig, "PART_Checked");
        var written = watch.Elapsed.TotalMilliseconds;
        RedactApplyRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromMinutes(5), Ct));
        var process = Process.GetCurrentProcess();
        process.Refresh();

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Raffica PDF redaction measurement (M2.5), {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        text.AppendLine(CultureInfo.InvariantCulture, $"OS {Environment.OSVersion}, {Environment.ProcessorCount} logical processors, {(IsRelease() ? "Release" : "Debug")} build of the tests, headless window; single run, hot file cache");
        text.AppendLine(CultureInfo.InvariantCulture, $"thousand-pages.pdf ({input / 1024.0:0} KiB), one area mark on each of the 1000 pages, applied to a new file");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"make the 1000 marks (session calls)",-48}: {marked,9:0.0} ms");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Apply to the analysis card",-48}: {analyzed,9:0.0} ms");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"card Apply to the result card (write and check)",-48}: {written,9:0.0} ms");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"output size",-48}: {new FileInfo(target).Length / 1024.0,9:0} KiB");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"peak working set of the test process",-48}: {process.PeakWorkingSet64 / (1024.0 * 1024.0),9:0} MiB");
        File.WriteAllText(Path.Combine(output, "redact-measurements.txt"), text.ToString());
    }

    // The cards of a 1000-page file take longer than the ten seconds of the usual wait (the card with the part, or the card that ended the run).
    private static async Task<Raffica.Shell.Controls.Dialog> CardAsync(RedactApplyRig rig, string part)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        while (!(rig.Host.ActiveDialog is { } d && RedactApplyRig.Has(d, part)))
        {
            Assert.True(DateTime.UtcNow < deadline, "no card with " + part);
            Raffica.Pdf.Tests.Printing.PrintHost.Pump();
            await Task.Delay(20, Ct);
        }

        return rig.Host.ActiveDialog!;
    }

    private static bool IsRelease() =>
#if DEBUG
        false;
#else
        true;
#endif
}
