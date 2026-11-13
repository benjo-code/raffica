using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace TestDoc;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            switch (args.FirstOrDefault())
            {
                case "verify" when args.Length == 2:
                    {
                        var error = Verifier.Verify(args[1]);
                        if (error is null) { Console.WriteLine("verify: ok"); return 0; }
                        Console.Error.WriteLine("verify: " + error);
                        return 1;
                    }
                case "generate":
                    {
                        string? outPath = null; int words = Generator.DefaultWords;
                        for (int i = 1; i < args.Length; i++)
                        {
                            if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
                            else if (args[i] == "--words" && i + 1 < args.Length) words = int.Parse(args[++i]);
                            else return Usage();
                        }
                        if (outPath is null) return Usage();
                        Generator.Generate(outPath, words);
                        Console.WriteLine("generate: wrote " + outPath);
                        return 0;
                    }
                case "selftest": return SelfTest();
                default: return Usage();
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 2;
        }
    }

    static int Usage()
    {
        Console.Error.WriteLine("usage: testdoc generate --out <path> [--words <n>] | verify <path> | selftest");
        return 2;
    }

    /// <summary>The empty DOCX must fail verify with the section count message (exit 1 = the check works).</summary>
    static int SelfTest()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "testdoc-empty-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            using (var d = WordprocessingDocument.Create(tmp, WordprocessingDocumentType.Document))
            {
                var m = d.AddMainDocumentPart();
                m.Document = new Document(new Body(new Paragraph(new Run(new Text("empty")))));
            }
            var error = Verifier.Verify(tmp);
            Console.WriteLine("selftest: empty document -> " + (error ?? "ok"));
            return error is not null && error.StartsWith("section count", StringComparison.Ordinal) ? 1 : 3;
        }
        finally { File.Delete(tmp); }
    }
}
