using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Layout.Common;
using Layout.CSharp;
using PrintSpike.Interop;

namespace PrintSpike;

/// <summary>Everything the print spike needs: the document, its layout and the fonts.</summary>
public sealed class Job : IDisposable
{
    public FontSet Fonts { get; }
    public HarfBuzzParagraphLayouter Layouter { get; }
    public PageBreaker Layout { get; }

    public Job()
    {
        string root = FontSet.FindRepoRoot();
        Fonts = FontSet.LoadSpikeFonts(root);
        Layouter = new HarfBuzzParagraphLayouter(Fonts);
        var doc = DocxReader.Read(Path.Combine(root, "spikes", "write", "testdoc", "thesis.docx"));
        Layout = new PageBreaker(doc, Layouter, PageSetup.A4);
        Layout.LayoutAll();
    }

    public void Dispose() { Layout.Dispose(); Layouter.Dispose(); Fonts.Dispose(); }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Automated modes need no window:
        //   --auto a|b <printer> <out.pdf>   Windows: print the test document to a named printer, into a file ("Microsoft Print to PDF")
        //   --pdf <out.pdf>                  every system: write the PDF of route b / CUPS to a file and report its size
        if (args.Length >= 1 && args[0] == "--pdf" && args.Length == 2) return PdfOnly(args[1]);
        if (args.Length == 3 && args[0] == "--text") return TextOf(args[1], int.Parse(args[2]));
        if (args.Length >= 1 && args[0] == "--cups" && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return Cups(args.Length > 1 ? args[1] : null);
        if (args.Length == 4 && args[0] == "--auto" && OperatingSystem.IsWindows()) return Auto(args[1], args[2], args[3]);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    /// <summary>The text PDFium finds on one page of a PDF (1-based): proves the text is real text, not a picture.</summary>
    static int TextOf(string path, int page)
    {
        var document = new Raffica.Core.Pdf.PdfEngine().OpenAsync(path).GetAwaiter().GetResult();
        string text = document.GetTextAsync(page - 1).GetAwaiter().GetResult();
        string flat = text.ReplaceLineEndings(" ");
        Console.WriteLine($"pages {document.PageCount}; page {page} text, {text.Length} chars: {flat[..Math.Min(160, flat.Length)]}");
        return 0;
    }

    /// <summary>Linux and macOS without a window: list the CUPS printers and hand the test document to one as a job (the first one when none is named).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    static int Cups(string? printer)
    {
        var printers = CupsJob.Printers();
        Console.WriteLine("CUPS printers: " + (printers.Count == 0 ? "(none)" : string.Join(", ", printers)));
        printer ??= printers.FirstOrDefault();
        if (printer is null) { Console.Error.WriteLine("no printer"); return 2; }
        using var job = new Job();
        var sw = Stopwatch.StartNew();
        using var pdf = PdfRoute.Render(job.Layout);
        double renderMs = sw.Elapsed.TotalMilliseconds;
        int id = CupsJob.Print(printer, pdf, "Write spike");
        Console.WriteLine($"CUPS job {id} on {printer}: PDF {pdf.Length} bytes in memory ({renderMs:F0} ms), handed over in {sw.Elapsed.TotalMilliseconds:F0} ms in total");
        return 0;
    }

    static int PdfOnly(string path)
    {
        using var job = new Job();
        var sw = Stopwatch.StartNew();
        using var pdf = PdfRoute.Render(job.Layout);
        File.WriteAllBytes(path, pdf.ToArray());
        Console.WriteLine($"pages {job.Layout.PageCount}, PDF {pdf.Length} bytes, {sw.Elapsed.TotalMilliseconds:F0} ms");
        return 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static int Auto(string route, string printer, string output)
    {
        using var job = new Job();
        nint dc = Gdi.CreateDC("WINSPOOL", printer, null, 0);
        if (dc == 0) { Console.Error.WriteLine("CreateDC failed for " + printer); return 2; }
        var sw = Stopwatch.StartNew();
        try
        {
            if (route == "a")
            {
                using var printerA = new GdiPagePrinter(job.Layout, dc, job.Fonts);
                var r = printerA.Print("Write spike route a", output, 1, job.Layout.PageCount);
                Console.WriteLine($"route a: {r.Pages} pages handed to the spooler in {r.Milliseconds:F0} ms");
            }
            else
            {
                var render = Stopwatch.StartNew();
                using var pdf = PdfRoute.Render(job.Layout);
                double renderMs = render.Elapsed.TotalMilliseconds;
                var print = Stopwatch.StartNew();
                int pages = PdfRoute.PrintToDeviceAsync(pdf, dc, "Write spike route b", output).GetAwaiter().GetResult();
                Console.WriteLine($"route b: PDF in memory {pdf.Length} bytes in {renderMs:F0} ms, {pages} pages printed through PDFium in {print.Elapsed.TotalMilliseconds:F0} ms (total {sw.Elapsed.TotalMilliseconds:F0} ms)");
            }
        }
        finally { Gdi.DeleteDC(dc); }
        // The output file appears when the spooler is done.
        for (int i = 0; i < 120 && !File.Exists(output); i++) Thread.Sleep(500);
        long last = -1;
        for (int i = 0; i < 120; i++) { Thread.Sleep(500); long len = File.Exists(output) ? new FileInfo(output).Length : -1; if (len == last && len > 0) break; last = len; }
        Console.WriteLine($"output {output}: {(File.Exists(output) ? new FileInfo(output).Length : -1)} bytes, {sw.Elapsed.TotalSeconds:F1} s from start of the job");
        return 0;
    }
}

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>A small window with a Print button; on Linux and macOS a list of the CUPS printers stands in for Write's own print dialog.</summary>
public sealed class MainWindow : Window
{
    readonly TextBlock _status = new() { Text = "Loading the test document...", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly ComboBox _printers = new() { MinWidth = 300 };
    Job? _job;

    public MainWindow()
    {
        Title = "Write print spike";
        Width = 520; Height = 300;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Prints the 300-page test document (spike 3). Throwaway code.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (OperatingSystem.IsWindows())
        {
            var a = new Button { Content = "Print (route a, drawn directly onto the printer)" };
            a.Click += async (_, _) => await RunWindows(routeA: true);
            var b = new Button { Content = "Print (route b, PDF in memory through PDFium)" };
            b.Click += async (_, _) => await RunWindows(routeA: false);
            panel.Children.Add(a);
            panel.Children.Add(b);
        }
        else
        {
            panel.Children.Add(_printers);
            var print = new Button { Content = "Print (PDF in memory to CUPS)" };
            print.Click += (_, _) => RunCups();
            panel.Children.Add(print);
            foreach (var p in CupsPrinters()) _printers.Items.Add(p);
            if (_printers.ItemCount > 0) _printers.SelectedIndex = 0;
        }
        panel.Children.Add(_status);
        Content = panel;
        Opened += async (_, _) =>
        {
            _job = await Task.Run(() => new Job());
            _status.Text = $"Ready: {_job.Layout.PageCount} pages laid out.";
        };
    }

    static IReadOnlyList<string> CupsPrinters() =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? CupsJob.Printers() : Array.Empty<string>();

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    async Task RunWindows(bool routeA)
    {
        if (_job is null) return;
        nint owner = TryGetPlatformHandle()?.Handle ?? 0;
        var chosen = PrintDialogNative.Show(owner, _job.Layout.PageCount);
        if (chosen is null) { _status.Text = "Cancelled."; return; }
        _status.Text = "Printing...";
        var job = _job;
        string text = await Task.Run(async () =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                if (routeA)
                {
                    using var printer = new GdiPagePrinter(job.Layout, chosen.DeviceContext, job.Fonts);
                    var r = printer.Print("Write spike route a", null, chosen.FromPage, chosen.ToPage);
                    return $"Route a: {r.Pages} pages handed to the spooler in {r.Milliseconds:F0} ms.";
                }
                using var pdf = PdfRoute.Render(job.Layout);
                int pages = await PdfRoute.PrintToDeviceAsync(pdf, chosen.DeviceContext, "Write spike route b", null);
                return $"Route b: {pages} pages (PDF {pdf.Length / 1024} KiB) handed to the spooler in {sw.Elapsed.TotalMilliseconds:F0} ms.";
            }
            catch (Exception e) { return "Failed: " + e.Message; }
            finally { Gdi.DeleteDC(chosen.DeviceContext); }
        });
        _status.Text = text;
    }

    void RunCups()
    {
        if (_job is null || _printers.SelectedItem is not string printer) { _status.Text = "No printer."; return; }
        try
        {
            var sw = Stopwatch.StartNew();
            using var pdf = PdfRoute.Render(_job.Layout);
            double renderMs = sw.Elapsed.TotalMilliseconds;
            int id = CupsJob.Print(printer, pdf, "Write spike");
            _status.Text = $"CUPS job {id}: PDF {pdf.Length / 1024} KiB in memory ({renderMs:F0} ms), handed over in {sw.Elapsed.TotalMilliseconds:F0} ms in total.";
        }
        catch (Exception e) { _status.Text = "Failed: " + e.Message; }
    }
}
