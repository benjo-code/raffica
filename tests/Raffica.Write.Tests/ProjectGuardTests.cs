using System.Reflection;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class ProjectGuardTests
{
    private static readonly Assembly s_model = typeof(OoxmlElement).Assembly;
    private static readonly Assembly s_docx = Assembly.Load("Raffica.Write.Docx");

    private static readonly Assembly s_layout = Assembly.Load("Raffica.Write.Layout");

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
    public void LayoutReferencesOnlyModelAmongRafficaAssemblies()
    {
        var refs = References(s_layout);
        Assert.All(refs.Where(n => n.StartsWith("Raffica.", StringComparison.Ordinal)), n => Assert.Equal("Raffica.Write.Model", n));
        Assert.DoesNotContain(refs, n => n.StartsWith("Avalonia", StringComparison.Ordinal) || n is "Raffica.Core" or "Raffica.Write.Docx");
    }

    [Fact]
    public void EditingReferencesOnlyModel()
    {
        var refs = References(typeof(Raffica.Write.Editing.TextEditor).Assembly);
        Assert.All(refs.Where(n => n.StartsWith("Raffica.", StringComparison.Ordinal)), n => Assert.Equal("Raffica.Write.Model", n));
        Assert.DoesNotContain(refs, n => n.StartsWith("Avalonia", StringComparison.Ordinal) || n is "Raffica.Core" or "Raffica.Write.Docx" or "Raffica.Write.Layout");
    }

    [Fact]
    public void LayoutStillReferencesNoAvalonia()
    {
        // Besides the framework, the layout library may use Model, SkiaSharp and HarfBuzzSharp and nothing else.
        var allowed = new[] { "Raffica.Write.Model", "SkiaSharp", "HarfBuzzSharp" };
        var others = References(s_layout).Where(n => !n.StartsWith("System", StringComparison.Ordinal) && !n.StartsWith("Microsoft", StringComparison.Ordinal) && n is not ("netstandard" or "mscorlib" or "WindowsBase"));
        Assert.All(others, n => Assert.Contains(n, allowed));
        Assert.DoesNotContain(References(s_layout), n => n.Contains("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void DocxDoesNotReferenceEditingOrLayout()
    {
        Assert.DoesNotContain(References(s_docx), n => n is "Raffica.Write.Editing" or "Raffica.Write.Layout");
    }

    [Fact]
    public void TheFragmentCodecInterfaceLivesInModelAndDocxImplementsIt()
    {
        // Editing may not reference Docx, so the seam between them is defined in Model (Task 13 of milestone 4.3a).
        var codec = typeof(IFragmentCodec);
        Assert.Same(s_model, codec.Assembly);
        Assert.True(codec.IsInterface);
        Assert.Contains(s_docx.GetTypes(), t => t.IsClass && codec.IsAssignableFrom(t));
        Assert.DoesNotContain(typeof(Raffica.Write.Editing.TextEditor).Assembly.GetTypes(), t => t.IsClass && !t.IsAbstract && codec.IsAssignableFrom(t));
    }

    [Fact]
    public void TheHistoryLimitsAreThoseOfTheDecision()
    {
        // "Raffica Write: the editing engine (M4.3a)" in Docs/DECISIONS.md: 1000 steps and 16 MiB of snapshot estimate.
        Assert.Equal(1000, Raffica.Write.Editing.HistoryLimits.Default.MaxSteps);
        Assert.Equal(16L * 1024 * 1024, Raffica.Write.Editing.HistoryLimits.Default.MaxBytes);
    }

    [Fact]
    public void NoNetworkAssemblyIsReferenced()
    {
        foreach (var assembly in new[] { s_model, s_docx, s_layout })
        {
            Assert.DoesNotContain(References(assembly), n => n == "System.Net.Http" || n == "System.Net.Sockets");
        }
    }

    [Fact]
    public void AllLibrariesAreAotCompatible()
    {
        foreach (var assembly in new[] { s_model, s_docx, s_layout })
        {
            Assert.Contains(assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), a => a.Key == "IsTrimmable" && a.Value == "True");
        }
    }
}
