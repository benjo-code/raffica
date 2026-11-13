using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Layout.Common;
using SkiaSharp;

namespace LayoutBench;

/// <summary>The measurements of spike 2 (spec section 5): first page, full layout, keystroke, hit test, memory.</summary>
public static class Bench
{
    const int FirstPageBlocks = 60; // the cover and the contents; more than page 1 needs

    sealed record Result(string Metric, double Median, double Min, double Max, string Unit);

    public static int Run(Options o)
    {
        var results = new List<Result>();
        Console.WriteLine($"engine={o.Engine} doc={o.Doc} runs={o.Runs} warmup={o.Warmup}");

        // Cold-process metrics: the child reports "done" on stdout; the parent times from spawn to that line.
        results.Add(Cold("first-page", o));
        results.Add(Cold("full-layout", o));
        results.Add(ColdMemory(o));

        // In-process metrics.
        var root = FontSet.FindRepoRoot();
        using var fonts = FontSet.LoadSpikeFonts(root);
        using var layouter = Engines.Create(o.Engine, fonts);
        results.Add(Repeat("keystroke", "ms", o, () => Keystroke(layouter, o.Doc)));
        results.Add(Repeat("hit-test", "us", o, () => HitTest(layouter, o.Doc)));

        foreach (var r in results)
            Console.WriteLine($"{r.Metric,-12} median {r.Median,10:F3} {r.Unit}   min {r.Min:F3}   max {r.Max:F3}");
        if (o.Out.Length > 0) WriteCsv(o, results);
        return 0;
    }

    static Result Repeat(string metric, string unit, Options o, Func<double> once)
    {
        for (int i = 0; i < o.Warmup; i++) once();
        var v = new List<double>();
        for (int i = 0; i < o.Runs; i++) v.Add(once());
        v.Sort();
        return new Result(metric, v[v.Count / 2], v[0], v[^1], unit);
    }

    static Result Cold(string metric, Options o)
    {
        double Once()
        {
            var sw = Stopwatch.StartNew();
            var psi = ChildInfo(metric, o);
            using var p = Process.Start(psi)!;
            string? line;
            bool done = false;
            while ((line = p.StandardOutput.ReadLine()) is not null)
                if (line.StartsWith("done", StringComparison.Ordinal)) { done = true; sw.Stop(); break; }
            p.WaitForExit();
            if (!done) throw new InvalidOperationException($"child {metric} did not report: {p.StandardError.ReadToEnd()}");
            return sw.Elapsed.TotalMilliseconds;
        }
        return Repeat(metric, "ms", o, Once);
    }

    static Result ColdMemory(Options o)
    {
        double Once()
        {
            using var p = Process.Start(ChildInfo("memory", o))!;
            double mb = double.NaN;
            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
                if (line.StartsWith("done", StringComparison.Ordinal))
                    mb = double.Parse(line.Split("memory_mb=")[1], CultureInfo.InvariantCulture);
            p.WaitForExit();
            return mb;
        }
        return Repeat("memory", "MB", o, Once);
    }

    static ProcessStartInfo ChildInfo(string metric, Options o)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "child", metric, "--engine", o.Engine, "--doc", o.Doc }) psi.ArgumentList.Add(a);
        return psi;
    }

    // ---- the child process --------------------------------------------------------------------------------------------

    /// <summary>Where the time of the first page goes: process start to Main, fonts, engine, DOCX read, layout, drawing (a child process, as the metric).</summary>
    static int Phases(Options o)
    {
        double sinceStart = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        var sw = Stopwatch.StartNew();
        double Lap() { double t = sw.Elapsed.TotalMilliseconds; sw.Restart(); return t; }
        var root = FontSet.FindRepoRoot();
        using var fonts = FontSet.LoadSpikeFonts(root);
        double tFonts = Lap();
        using var layouter = Engines.Create(o.Engine, fonts);
        double tEngine = Lap();
        var doc = DocxReader.Read(o.Doc, FirstPageBlocks);
        double tRead = Lap();
        using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
        pb.LayoutUntil(0);
        double tLayout = Lap();
        DrawPage(pb, 0);
        double tDraw = Lap();
        Console.WriteLine($"done start-to-Main {sinceStart:F1} ms, fonts {tFonts:F1}, engine {tEngine:F1}, DOCX read {tRead:F1}, layout of page 1 {tLayout:F1}, drawing {tDraw:F1}");
        return 0;
    }

    public static int Child(string metric, Options o)
    {
        if (metric == "first-page-phases") return Phases(o);
        var root = FontSet.FindRepoRoot();
        using var fonts = FontSet.LoadSpikeFonts(root);
        using var layouter = Engines.Create(o.Engine, fonts);
        switch (metric)
        {
            case "first-page":
                {
                    var doc = DocxReader.Read(o.Doc, FirstPageBlocks);
                    using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
                    pb.LayoutUntil(0);
                    DrawPage(pb, 0);
                    Console.WriteLine("done pages=" + pb.PageCount);
                    return 0;
                }
            case "full-layout":
                {
                    var doc = DocxReader.Read(o.Doc);
                    using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
                    int pages = pb.LayoutAll();
                    Console.WriteLine("done pages=" + pages);
                    return 0;
                }
            case "memory":
                {
                    var doc = DocxReader.Read(o.Doc);
                    using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
                    pb.LayoutAll();
                    GC.Collect();
                    double mb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
                    Console.WriteLine("done memory_mb=" + mb.ToString("F1", CultureInfo.InvariantCulture));
                    return 0;
                }
            default:
                Console.Error.WriteLine("unknown metric " + metric);
                return 2;
        }
    }

    /// <summary>Line count per paragraph for both engines and the number of paragraphs that differ (a sanity check, recorded, not a failure).</summary>
    public static int CompareLines(Options o)
    {
        var root = FontSet.FindRepoRoot();
        using var fonts = FontSet.LoadSpikeFonts(root);
        using var a = Engines.Create("csharp", fonts);
        using var b = Engines.Create("rust", fonts);
        var doc = DocxReader.Read(o.Doc);
        int total = 0, differ = 0, shown = 0;
        long linesA = 0, linesB = 0;
        foreach (var block in doc.Blocks)
        {
            if (block is not Paragraph p) continue;
            float w = PageSetup.A4.TextWidth - p.Format.LeftIndent;
            using var la = a.Layout(p, w);
            using var lb = b.Layout(p, w);
            total++;
            linesA += la.Lines.Count; linesB += lb.Lines.Count;
            if (la.Lines.Count != lb.Lines.Count)
            {
                differ++;
                if (shown++ < 5) Console.WriteLine($"  differs: C# {la.Lines.Count} lines, Rust {lb.Lines.Count} lines: \"{p.Text[..Math.Min(50, p.Text.Length)]}\"");
            }
        }
        Console.WriteLine($"paragraphs {total}, line count differs in {differ} ({100.0 * differ / Math.Max(1, total):F2} %), total lines C# {linesA}, Rust {linesB}");
        return 0;
    }

    /// <summary>Writes one page as a PNG (a visual check of the layout) and prints the page count.</summary>
    public static int Render(Options o, List<string> positional)
    {
        var root = FontSet.FindRepoRoot();
        using var fonts = FontSet.LoadSpikeFonts(root);
        using var layouter = Engines.Create(o.Engine, fonts);
        var doc = DocxReader.Read(o.Doc);
        using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
        int pages = pb.LayoutAll();
        Console.WriteLine("pages=" + pages);
        if (positional.Count < 2) return 0;
        int page = int.Parse(positional[0]) - 1;
        var info = new SKImageInfo(794, 1123, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Scale(794f / pb.Setup.Width);
        PageRenderer.Draw(pb, page, surface.Canvas);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(positional[1], data.ToArray());
        Console.WriteLine($"page {page + 1}: footer \"{pb.FooterText(page)}\", items {pb.GetPage(page).Items.Count}, footnotes {pb.GetPage(page).Footnotes.Count}");
        return 0;
    }

    static void DrawPage(PageBreaker pb, int page)
    {
        var info = new SKImageInfo(794, 1123, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Scale(794f / pb.Setup.Width);
        PageRenderer.Draw(pb, page, canvas);
        canvas.Flush();
    }

    // ---- in-process metrics ---------------------------------------------------------------------------------------------

    /// <summary>Median of 200 single-character inserts on page 150, laid out incrementally, in milliseconds.</summary>
    static double Keystroke(IParagraphLayouter layouter, string docPath)
    {
        var doc = DocxReader.Read(docPath);
        using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
        pb.LayoutAll();
        var page = pb.GetPage(Math.Min(149, pb.PageCount - 1));
        var targets = page.Items.Where(it => pb.Blocks[it.Block] is Paragraph p && p.Text.Length > 20).Select(it => it.Block).ToList();
        var rng = new Random(4242);
        var times = new double[200];
        for (int i = 0; i < times.Length; i++)
        {
            int block = targets[i % targets.Count];
            int offset = rng.Next(((Paragraph)pb.Blocks[block]).Text.Length + 1);
            long t0 = Stopwatch.GetTimestamp();
            pb.InsertText(block, offset, "e");
            times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        Array.Sort(times);
        return times[times.Length / 2];
    }

    /// <summary>Microseconds per HitTest + Caret pair, over 10,000 pairs.</summary>
    static double HitTest(IParagraphLayouter layouter, string docPath)
    {
        var doc = DocxReader.Read(docPath);
        using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
        pb.LayoutAll();
        var rng = new Random(4242);
        var points = new List<(int Page, float X, float Y)>();
        while (points.Count < 10_000)
        {
            int page = rng.Next(pb.PageCount);
            var info = pb.GetPage(page);
            var paras = info.Items.Where(it => pb.Blocks[it.Block] is Paragraph).ToList();
            if (paras.Count == 0) continue;
            var it = paras[rng.Next(paras.Count)];
            var tops = pb.LineTopsOf(it.Block);
            int line = it.First + rng.Next(it.Count);
            var m = pb.ParagraphLayoutOf(it.Block).Lines[line];
            float x = pb.Setup.MarginLeft + (float)(rng.NextDouble() * Math.Max(1, m.Width));
            float y = it.Y + (tops[line] - tops[it.First]) + (tops[line + 1] - tops[line]) / 2;
            points.Add((page, x, y));
        }
        long sink = 0;
        long t0 = Stopwatch.GetTimestamp();
        foreach (var (page, x, y) in points)
        {
            var pos = pb.HitTest(page, x, y);
            var caret = pb.Caret(pos);
            sink += pos.Offset + (long)caret.X;
        }
        double us = Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000.0 / points.Count;
        if (sink == long.MinValue) Console.WriteLine(sink);
        return us;
    }

    static void WriteCsv(Options o, List<Result> results)
    {
        bool header = !File.Exists(o.Out);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.Out))!);
        using var w = new StreamWriter(o.Out, append: true);
        if (header) w.WriteLine("date,machine,os,engine,metric,median,min,max,unit,commit");
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        foreach (var r in results)
            w.WriteLine(string.Join(',', date, o.Machine, RuntimeInformation.OSDescription.Replace(',', ' '), o.Engine, r.Metric,
                r.Median.ToString("F3", CultureInfo.InvariantCulture), r.Min.ToString("F3", CultureInfo.InvariantCulture),
                r.Max.ToString("F3", CultureInfo.InvariantCulture), r.Unit, o.Commit));
    }
}
