using System.Runtime.CompilerServices;

namespace LayoutBench;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            string cmd = args.FirstOrDefault() ?? "";
            var opts = Options.Parse(args.Skip(1).ToArray(), out var positional);
            switch (cmd)
            {
                case "check":
                    return Checks.Run(opts.Engine, opts.Doc);
                case "run":
                    if (RuntimeFeature.IsDynamicCodeSupported)
                    {
                        Console.Error.WriteLine("refusing to run: this is not a Native AOT publish (RuntimeFeature.IsDynamicCodeSupported is true). Use dotnet publish -c Release -r <rid>.");
                        return 2;
                    }
                    return Bench.Run(opts);
                case "compare-lines":
                    return Bench.CompareLines(opts);
                case "render":
                    return Bench.Render(opts, positional);
                case "child":
                    return Bench.Child(positional.FirstOrDefault() ?? "", opts);
                default:
                    Console.Error.WriteLine("usage: layout-bench check --engine <csharp|rust> [--doc <path>] | run --engine <e> [--doc <path>] --runs 10 --warmup 2 --machine <label> --out <csv> | compare-lines [--doc <path>] | render <page-number> <out.png> --engine <e> | child <metric> --engine <e> --doc <path>");
                    return 2;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 2;
        }
    }
}

public sealed class Options
{
    public string Engine = "csharp";
    public string Doc = "";
    public int Runs = 10, Warmup = 2;
    public string Out = "";
    public string Machine = "unknown";
    public string Commit = "unknown";

    public static Options Parse(string[] args, out List<string> positional)
    {
        var o = new Options();
        positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value after " + a);
            switch (a)
            {
                case "--engine": o.Engine = Next(); break;
                case "--doc": o.Doc = Next(); break;
                case "--runs": o.Runs = int.Parse(Next()); break;
                case "--warmup": o.Warmup = int.Parse(Next()); break;
                case "--out": o.Out = Next(); break;
                case "--machine": o.Machine = Next(); break;
                case "--commit": o.Commit = Next(); break;
                default: positional.Add(a); break;
            }
        }
        if (o.Doc.Length == 0)
            o.Doc = Path.Combine(Layout.Common.FontSet.FindRepoRoot(), "spikes", "write", "testdoc", "thesis.docx");
        return o;
    }
}
