using System.Reflection;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class ProjectGuardTests
{
    private static readonly Assembly s_model = typeof(OoxmlElement).Assembly;
    private static readonly Assembly s_docx = Assembly.Load("Raffica.Write.Docx");

    private static readonly string[] s_expectedDocxReferences = ["Raffica.Core", "Raffica.Write.Model"];

    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    [Fact]
    public void ModelReferencesNoRafficaAssembly()
    {
        Assert.DoesNotContain(References(s_model), n => n.StartsWith("Raffica.", StringComparison.Ordinal) || n.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void DocxReferencesOnlyModelAndCore()
    {
        var refs = References(s_docx);
        var raffica = refs.Where(n => n.StartsWith("Raffica.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(s_expectedDocxReferences, raffica);
        Assert.DoesNotContain(refs, n => n.StartsWith("Avalonia", StringComparison.Ordinal) || n == "Raffica.Shell" || n == "Raffica.Pdf" || n == "Raffica.Pdfium");
    }

    [Fact]
    public void NoNetworkAssemblyIsReferenced()
    {
        foreach (var assembly in new[] { s_model, s_docx })
        {
            Assert.DoesNotContain(References(assembly), n => n == "System.Net.Http" || n == "System.Net.Sockets");
        }
    }

    [Fact]
    public void BothLibrariesAreAotCompatible()
    {
        foreach (var assembly in new[] { s_model, s_docx })
        {
            Assert.Contains(assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), a => a.Key == "IsTrimmable" && a.Value == "True");
        }
    }
}
