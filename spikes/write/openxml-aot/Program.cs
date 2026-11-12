using System.IO.Compression;
using System.Text;

namespace OpenXmlAot;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            switch (args.FirstOrDefault())
            {
                case "compare" when args.Length >= 3:
                    {
                        var touched = new List<string>();
                        for (int i = 3; i < args.Length; i++)
                            if (args[i] == "--touched" && i + 1 < args.Length)
                                touched.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries));
                        return Compare.Run(args[1], args[2], touched);
                    }
                case "docx" when args.Length >= 3:
                    {
                        if (args.Length >= 5 && args[3] == "--runs")
                        {
                            double median = Docx.Bench(args[1], args[2], int.Parse(args[4]));
                            Console.WriteLine($"docx: median of {args[4]} runs after 2 warm-ups: {median:F1} ms");
                        }
                        else Docx.Edit(args[1], args[2]);
                        return 0;
                    }
                case "xlsx-make" when args.Length == 3:
                    Xlsx.Make(args[1], args[2]);
                    return 0;
                case "xlsx" when args.Length is 3 or 4:
                    Xlsx.Edit(args[1], args[2], args.Length == 4 && args[3] == "--first-part");
                    return 0;
                case "selftest": return SelfTest(args.Length > 1 ? args[1] : FindThesis());
                default:
                    Console.Error.WriteLine("usage: openxml-aot docx <in> <out> [--runs N] | xlsx-make <out-plain> <out-formulas> | xlsx <in> <out> [--first-part] | compare <before> <after> [--touched <part>,...] | selftest [docx]");
                    return 2;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 2;
        }
    }

    static string FindThesis()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Raffica.slnx"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? ".", "spikes", "write", "testdoc", "thesis.docx");
    }

    /// <summary>compare must see no difference for a package against itself, and exactly the part changed through System.IO.Compression.</summary>
    static int SelfTest(string docx)
    {
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failures++; }
        var changed = Path.Combine(Path.GetTempPath(), "oxa-changed-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            Check(Compare.Run(docx, docx, Array.Empty<string>()) == 0, "same file: exit 0");
            var (a, r, c) = Compare.Diff(docx, docx);
            Check(a.Count == 0 && r.Count == 0 && c.Count == 0, "same file: three empty lists");

            File.Copy(docx, changed, true);
            using (var zip = ZipFile.Open(changed, ZipArchiveMode.Update))
            {
                var e = zip.GetEntry("word/styles.xml")!;
                string xml;
                using (var s = e.Open()) xml = new StreamReader(s, Encoding.UTF8).ReadToEnd();
                e.Delete();
                var n = zip.CreateEntry("word/styles.xml");
                using var w = n.Open();
                w.Write(Encoding.UTF8.GetBytes(xml.Replace("</w:styles>", "<!--changed--></w:styles>")));
            }
            Check(Compare.Run(docx, changed, Array.Empty<string>()) == 1, "styles.xml changed, nothing touched: exit 1");
            Check(Compare.Run(docx, changed, new[] { "/word/styles.xml" }) == 0, "styles.xml changed and touched: exit 0");
            var (a2, r2, c2) = Compare.Diff(docx, changed);
            Check(a2.Count == 0 && r2.Count == 0 && c2.SequenceEqual(new[] { "/word/styles.xml" }), "only /word/styles.xml is listed as changed");
        }
        finally { if (File.Exists(changed)) File.Delete(changed); }
        return failures == 0 ? 0 : 1;
    }
}
