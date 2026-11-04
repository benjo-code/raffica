using System.Xml.Linq;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class AppProjectTests
{
    [Fact]
    public void TheAppReferencesOnlyCoreAndShell()
    {
        var referenced = typeof(PdfOptions).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Raffica.Pdfium", referenced);
        Assert.DoesNotContain("Raffica.Ocr", referenced);

        var project = XDocument.Load(RepoPaths.Combine("src", "Raffica.Pdf", "Raffica.Pdf.csproj"));
        var references = project.Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include")).ToList();
        Assert.Equal(2, references.Count);
        Assert.Contains(references, r => r!.EndsWith("Raffica.Core.csproj", StringComparison.Ordinal));
        Assert.Contains(references, r => r!.EndsWith("Raffica.Shell.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void ProgramStartsWithTheAppContextWorkaround()
    {
        var lines = File.ReadAllLines(RepoPaths.Combine("src", "Raffica.Pdf", "Program.cs"));
        var main = Array.FindIndex(lines, l => l.Contains("static int Main(", StringComparison.Ordinal));
        Assert.True(main >= 0, "Program.Main not found");

        // The first statement is the first line after the opening brace that is not blank and not a comment.
        var first = lines.Skip(main + 1).Select(l => l.Trim()).First(l => l.Length > 0 && l != "{" && !l.StartsWith("//", StringComparison.Ordinal));
        Assert.Equal("_ = AppContext.GetData(\"System.GC.Server\");", first);
    }

    [Fact]
    public void PrintStringPrintsAnEnglishAndAGermanValue()
    {
        Assert.Equal("View", Print("en"));
        Assert.Equal("Ansicht", Print("de"));
    }

    [Fact]
    public void PrintStringFallsBackToTheShellStringsAndReportsAnUnknownKey()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, PrintStringCommand.Run(PdfOptions.Parse(["--print-string", "Cancel", "--lang", "de"]), output, error));
        Assert.Equal("Abbrechen", output.ToString().Trim());

        Assert.Equal(1, PrintStringCommand.Run(PdfOptions.Parse(["--print-string", "NoSuchKey"]), new StringWriter(), error));
        Assert.Contains("NoSuchKey", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheOptionsParseTheSwitchesAndCollectTheFiles()
    {
        var options = PdfOptions.Parse(["a.pdf", "--lang", "de", "--theme", "dark", "--high-contrast", "--settings-file", "s.json", "b.pdf"]);
        Assert.Equal(["a.pdf", "b.pdf"], options.Files);
        Assert.Equal("de", options.Lang);
        Assert.Equal(Raffica.Shell.ThemeMode.Dark, options.Theme);
        Assert.True(options.ThemeGiven);
        Assert.True(options.HighContrast);
        Assert.Equal("s.json", options.SettingsFile);
        Assert.Throws<ArgumentException>(() => PdfOptions.Parse(["--lang", "fr"]));
        Assert.Throws<ArgumentException>(() => PdfOptions.Parse(["--bogus"]));
    }

    private static string Print(string lang)
    {
        var output = new StringWriter();
        Assert.Equal(0, PrintStringCommand.Run(PdfOptions.Parse(["--print-string", "TabView", "--lang", lang]), output, new StringWriter()));
        return output.ToString().Trim();
    }
}
