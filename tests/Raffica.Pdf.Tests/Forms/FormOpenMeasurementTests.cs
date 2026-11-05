using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

/// <summary>
/// Measurements of M2.3 (<c>Docs/DECISIONS.md</c>, "Raffica PDF: forms and signing measurements (M2.3)"): the time from opening a form with many
/// fields to its first sharp page in the window, and from leaving a field to the written value (the editor closed, the document and the
/// field cache current) and to the next full render of its page. Runs only with <c>RAFFICA_MEASURE_OUT=&lt;folder&gt;</c> (best in Release:
/// <c>dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*FormOpenMeasurementTests"</c>) and writes <c>form-open-measurements.txt</c>
/// into that folder; measures, never asserts a time. The headless window renders without a display, so the numbers are those of the code
/// path, not of a graphics device.
/// </summary>
public sealed class FormOpenMeasurementTests
{
    private const string OutputVariable = "RAFFICA_MEASURE_OUT";
    private const int Fields = 400;
    private const int Runs = 9;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task OpenAndLeaveAFieldAreMeasured()
    {
        var output = Environment.GetEnvironmentVariable(OutputVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(output), $"Measurement only: set {OutputVariable}=<folder>.");
        output = Directory.CreateDirectory(output).FullName;
        var bytes = ManyFieldsPdf(Fields);
        var open = new List<double>();
        var leave = new List<double>();
        var render = new List<double>();
        for (var run = 0; run < Runs + 1; run++)
        {
            // The fixture opens a temp copy of forms-basic.pdf that the callback has overwritten with the many-field form; its open
            // returns after the first sharp page is shown and the widgets of page 0 are read.
            var watch = Stopwatch.StartNew();
            using var f = await FormEditFixture.OpenAsync(prepare: path => File.WriteAllBytes(path, bytes));
            var opened = watch.Elapsed.TotalMilliseconds;
            var tab = f.Tab;

            var box = (TextBox)f.Open(f.Field("F0_7"));
            FormEditFixture.SetText(box, "Erika");
            watch.Restart();
            f.Leave();
            await f.SettleAsync();
            var left = watch.Elapsed.TotalMilliseconds;
            Assert.Equal("Erika", (await f.FromDocumentAsync("F0_7")).State.Text);

            watch.Restart();
            _ = await tab.Session.Document!.RenderPageAsync(0, 1.0, cancellationToken: Cancel);
            var rendered = watch.Elapsed.TotalMilliseconds;
            if (run > 0)
            {
                open.Add(opened);
                leave.Add(left);
                render.Add(rendered);
            }
        }

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Raffica PDF form measurements (M2.3), {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        report.AppendLine(CultureInfo.InvariantCulture, $"OS {Environment.OSVersion}, {Environment.ProcessorCount} logical processors, {(IsRelease() ? "Release" : "Debug")} build of the tests, headless window");
        report.AppendLine(CultureInfo.InvariantCulture, $"One page with {Fields} text fields; median / min / max in ms over {Runs} runs after one warm-up run, each in a fresh window");
        report.AppendLine(CultureInfo.InvariantCulture, $"open -> first sharp page shown and page-0 widgets read : {Stats(open)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"leave a field -> value written, state current : {Stats(leave)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"full render of the page afterwards (scale 1.0)  : {Stats(render)}");
        File.WriteAllText(Path.Combine(output, "form-open-measurements.txt"), report.ToString());
    }

    private static bool IsRelease() =>
#if DEBUG
        false;
#else
        true;
#endif

    private static string Stats(List<double> values)
    {
        var sorted = values.Order().ToList();
        return string.Create(CultureInfo.InvariantCulture, $"{sorted[sorted.Count / 2],8:0.0} / {sorted[0],8:0.0} / {sorted[^1],8:0.0}");
    }

    /// <summary>One page with <paramref name="count"/> text fields (<c>F0_n</c>, four per row) as a complete PDF with a cross-reference table.</summary>
    private static byte[] ManyFieldsPdf(int count)
    {
        var objects = new List<string> { string.Empty, string.Empty, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>", string.Empty };
        var annots = new List<int>();
        for (var n = 0; n < count; n++)
        {
            var x = 20 + ((n % 4) * 145);
            var y = 770 - ((n / 4) * 7);
            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Annot /Subtype /Widget /Rect [{x} {y - 6} {x + 140} {y}] /P 4 0 R /F 4 /FT /Tx /T (F0_{n}) /DA (/Helv 6 Tf 0 g) >>"));
            annots.Add(objects.Count);
        }

        var refs = string.Join(' ', annots.Select(a => a.ToString(CultureInfo.InvariantCulture) + " 0 R"));
        objects[0] = $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [{refs}] /DA (/Helv 6 Tf 0 g) /DR << /Font << /Helv 3 0 R >> >> >> >>";
        objects[1] = "<< /Type /Pages /Kids [4 0 R] /Count 1 >>";
        objects[3] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [{refs}] >>";

        var body = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(body.Length);
            body.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = body.Length;
        body.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            body.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        body.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(body.ToString());
    }
}
