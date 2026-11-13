using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OpenXmlAot;

public static class Docx
{
    /// <summary>
    /// Copies <paramref name="input"/> to <paramref name="output"/>, changes the text of the first body paragraph of chapter 1 and the size of
    /// style Heading2 (15 pt), saves. Touched parts: /word/document.xml and /word/styles.xml.
    /// </summary>
    public static void Edit(string input, string output)
    {
        File.Copy(input, output, true);
        using var doc = WordprocessingDocument.Open(output, true);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        var firstHeading = body.Elements<Paragraph>().First(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading1");
        var target = firstHeading.ElementsAfter().OfType<Paragraph>().First(p =>
        {
            var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            return style is null || !style.StartsWith("Heading", StringComparison.Ordinal);
        });
        foreach (var run in target.Elements<Run>().ToList()) run.Remove();
        target.Append(new Run(new Text("Edited by the spike.")));

        var heading2 = main.StyleDefinitionsPart!.Styles!.Elements<Style>().First(s => s.StyleId?.Value == "Heading2");
        heading2.StyleRunProperties!.FontSize!.Val = "30";
        heading2.StyleRunProperties.FontSizeComplexScript!.Val = "30";
    }

    /// <summary>Runs <see cref="Edit"/> <paramref name="runs"/> times after two warm-up runs; returns the median in milliseconds.</summary>
    public static double Bench(string input, string output, int runs)
    {
        for (int i = 0; i < 2; i++) Edit(input, output);
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            Edit(input, output);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return times[times.Count / 2];
    }
}
