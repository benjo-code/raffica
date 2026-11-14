using System.Xml.Linq;
using Raffica.Hosting;
using Xunit;

namespace Raffica.WriteApp.Tests;

public sealed class WriteProjectTests
{
    [Fact]
    public void TheAppReferencesOnlyTheProjectsItMay()
    {
        var referenced = typeof(WriteOptions).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Raffica.Pdf", referenced);
        Assert.DoesNotContain("RafficaPdf", referenced);
        Assert.DoesNotContain("Raffica.Pdfium", referenced);
        Assert.DoesNotContain("Raffica.Ocr", referenced);

        var project = XDocument.Load(RepoPaths.Combine("src", "Raffica.Write", "Raffica.Write.csproj"));
        var references = project.Descendants("ProjectReference").Select(r => Path.GetFileNameWithoutExtension(((string)r.Attribute("Include")!).Replace('\\', '/'))).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["Raffica.Core", "Raffica.Hosting", "Raffica.Shell", "Raffica.Write.Docx", "Raffica.Write.Layout", "Raffica.Write.Model"], references);
    }

    [Fact]
    public void TheProjectIsTheWriteAppOfTheSolution()
    {
        var project = XDocument.Load(RepoPaths.Combine("src", "Raffica.Write", "Raffica.Write.csproj"));
        string Property(string name) => (string)project.Descendants(name).Single();
        Assert.Equal("RafficaWrite", Property("AssemblyName"));
        Assert.Equal("Raffica Write", Property("Product"));
        Assert.Equal("true", Property("PublishAot"));
        Assert.Equal("WinExe", Property("OutputType"));
        Assert.True(File.Exists(RepoPaths.Combine("src", "Raffica.Write", Property("ApplicationIcon"))));
        var solution = File.ReadAllText(RepoPaths.Combine("Raffica.slnx"));
        Assert.Contains("Raffica.Write.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("Raffica.Write.App.Tests.csproj", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramStartsWithTheAppContextWorkaround()
    {
        var lines = File.ReadAllLines(RepoPaths.Combine("src", "Raffica.Write", "Program.cs"));
        var main = Array.FindIndex(lines, l => l.Contains("static int Main(", StringComparison.Ordinal));
        Assert.True(main >= 0, "Program.Main not found");

        // The first statement is the first line after the opening brace that is not blank and not a comment.
        var first = lines.Skip(main + 1).Select(l => l.Trim()).First(l => l.Length > 0 && l != "{" && !l.StartsWith("//", StringComparison.Ordinal));
        Assert.Equal("_ = AppContext.GetData(\"System.GC.Server\");", first);
    }

    [Fact]
    public void SecondInstanceNameDiffersFromPdf()
    {
        Assert.NotEqual(AppFiles.Pdf.InstanceName(null), AppFiles.Write.InstanceName(null));
        Assert.NotEqual(AppFiles.Pdf.InstanceName("s.json"), AppFiles.Write.InstanceName("s.json"));
        Assert.Equal(AppFiles.Write.InstanceName("s.json"), AppFiles.Write.InstanceName("s.json"));
        Assert.NotEqual(AppFiles.Write.InstanceName("a.json"), AppFiles.Write.InstanceName("b.json"));
    }

    [Fact]
    public void WriteKeepsItsFilesApartFromPdf()
    {
        Assert.NotEqual(AppFiles.Pdf.ShellPathFor(null), AppFiles.Write.ShellPathFor(null));
        Assert.NotEqual(AppFiles.Pdf.RecentPathFor(null), AppFiles.Write.RecentPathFor(null));
        Assert.NotEqual(AppFiles.Pdf.AppSettingsPathFor(null), AppFiles.Write.AppSettingsPathFor(null));
    }

    [Fact]
    public void PrintStringPrintsAnEnglishAndAGermanValue()
    {
        Assert.Equal("Page view", Print("en"));
        Assert.Equal("Seitenansicht", Print("de"));
    }

    [Fact]
    public void PrintStringFallsBackToTheShellStringsAndReportsAnUnknownKey()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, PrintStringCommand.Run(WriteOptions.Parse(["--print-string", "Cancel", "--lang", "de"]), output, error));
        Assert.Equal("Abbrechen", output.ToString().Trim());

        output = new StringWriter();
        Assert.Equal(0, PrintStringCommand.Run(WriteOptions.Parse(["--print-string", "StartOpenDocument", "--lang", "en"]), output, error));
        Assert.Equal("Open document", output.ToString().Trim());

        Assert.Equal(1, PrintStringCommand.Run(WriteOptions.Parse(["--print-string", "NoSuchKey"]), new StringWriter(), error));
        Assert.Contains("NoSuchKey", error.ToString(), StringComparison.Ordinal);
    }

    private static string Print(string lang)
    {
        var output = new StringWriter();
        Assert.Equal(0, PrintStringCommand.Run(WriteOptions.Parse(["--print-string", "ViewPage", "--lang", lang]), output, new StringWriter()));
        return output.ToString().Trim();
    }
}
