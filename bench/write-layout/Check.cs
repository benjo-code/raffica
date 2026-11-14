using System.Diagnostics;
using System.Globalization;
using Raffica.Write.Docx;
using Raffica.Write.Layout;

namespace WriteLayoutBench;

/// <summary><c>check</c>: lays the document out twice and asserts the engine is deterministic and numbers the thesis right.</summary>
internal static class Check
{
    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        var file = options.FileOrThesis();
        if (!File.Exists(file))
        {
            return Program.Fail(error, "the file does not exist");
        }

        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var fonts = FontCatalog.CreateShipped();
        var images = new PackageImages(bytes);

        var first = LayOutAll(docx, fonts, images, out var firstMs);
        var second = LayOutAll(docx, fonts, images, out var secondMs);

        var findings = new List<string>();
        if (first.Count != second.Count)
        {
            findings.Add($"page count differs between the runs ({first.Count} and {second.Count})");
        }
        else
        {
            for (var i = 0; i < first.Count; i++)
            {
                if (first[i].Text != second[i].Text || first[i].Number != second[i].Number)
                {
                    findings.Add($"page {i + 1} differs between the runs");
                    break;
                }
            }
        }

        if (first.Count < 3 || first[2].Number != "1")
        {
            findings.Add($"physical page 3 should have the number 1, it has '{(first.Count < 3 ? "none" : first[2].Number)}'");
        }

        // The "A-" of the appendix is literal footer text in front of the PAGE field, so the page's display number is the digit and
        // the footer is the last line of the page text.
        var appendix = first.FindIndex(p => LastLine(p.Text).StartsWith("A-", StringComparison.Ordinal));
        if (appendix < 0)
        {
            findings.Add("no appendix page (footer A-...) was found");
        }
        else if (LastLine(first[appendix].Text) != "A-1")
        {
            findings.Add($"the first appendix page has the footer '{LastLine(first[appendix].Text)}', not A-1");
        }

        if (first.Any(p => p.IsProvisional))
        {
            findings.Add("a page is still provisional after the layout is complete");
        }

        // Optional: the spec's limit for the full layout of the thesis (3 s on the Linux laptop). Not asserted unless asked for.
        if (options.Value("--budget-ms") is not null && secondMs > options.Int("--budget-ms", 0))
        {
            findings.Add(string.Create(CultureInfo.InvariantCulture, $"the full layout took {secondMs:0} ms, more than the budget of {options.Int("--budget-ms", 0)} ms"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"pages={first.Count} run1 ms={firstMs:0} run2 ms={secondMs:0}"));
        foreach (var finding in findings)
        {
            error.WriteLine("write-layout-bench: " + finding);
        }

        return findings.Count == 0 ? 0 : 1;
    }

    private static string LastLine(string text)
    {
        var end = text.TrimEnd('\n');
        return end[(end.LastIndexOf('\n') + 1)..];
    }

    private static List<LaidOutPage> LayOutAll(DocxFile docx, FontCatalog fonts, PackageImages images, out double milliseconds)
    {
        var clock = Stopwatch.StartNew();
        var layout = DocumentLayout.Create(docx.Model, fonts, new LayoutOptions(LayoutMode.Pages, ImageBytes: images.Read));
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        if (!layout.IsComplete)
        {
            throw new InvalidOperationException("the layout did not complete");
        }

        // The pages are read after IsComplete: the provisional NUMPAGES pages have been replaced by then.
        var pages = new List<LaidOutPage>(layout.LaidOutPages);
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            pages.Add(layout.Page(i));
        }

        milliseconds = clock.Elapsed.TotalMilliseconds;
        return pages;
    }
}
