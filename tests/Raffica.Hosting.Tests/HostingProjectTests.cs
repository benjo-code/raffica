using System.Xml.Linq;
using Xunit;

namespace Raffica.Hosting.Tests;

public sealed class HostingProjectTests
{
    [Fact]
    public void HostingReferencesOnlyCoreAndShell()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot(), "src", "Raffica.Hosting", "Raffica.Hosting.csproj"));
        var references = project.Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include")).ToList();
        Assert.Equal(2, references.Count);
        Assert.Contains(references, r => r!.EndsWith("Raffica.Core.csproj", StringComparison.Ordinal));
        Assert.Contains(references, r => r!.EndsWith("Raffica.Shell.csproj", StringComparison.Ordinal));

        var referenced = typeof(AppFiles).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("RafficaPdf", referenced);
        Assert.DoesNotContain("RafficaWrite", referenced);
        Assert.DoesNotContain("Raffica.Pdfium", referenced);
        Assert.DoesNotContain("Raffica.Ocr", referenced);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Raffica.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Raffica.slnx not found above the test output.");
    }
}
