using System.Reflection;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class FontCatalogTests
{
    private sealed class NoSystemFonts : ISystemFonts
    {
        public FontFace? Match(string family, bool bold, bool italic) => null;
    }

    private sealed class FakeSystemFonts(FontFace face) : ISystemFonts
    {
        public List<string> Asked { get; } = [];

        public FontFace? Match(string family, bool bold, bool italic)
        {
            Asked.Add(family);
            return string.Equals(family, "Segoe UI", StringComparison.OrdinalIgnoreCase) ? face : null;
        }
    }

    private static FontCatalog Catalog(ISystemFonts? system = null) => FontCatalog.CreateShipped(system ?? new NoSystemFonts());

    // The bullets of the blank document are private-use code points of Symbol and Wingdings (U+F0B7 and U+F0A7), which the layout draws as U+2022 in the paragraph's own font (the Wingdings square, U+25AA, is
    // not in Caladea and would be an empty box there, so it is drawn as the round bullet too), and a plain "o" in Courier New (a font that falls back to a shipped face). A glyph a shipped face lacks is drawn
    // from some other font on one system and as an empty box on another.
    [Fact]
    public void EveryBulletGlyphTheLayoutDrawsExistsInEveryShippedFace()
    {
        var catalog = Catalog();
        foreach (var family in new[] { "Carlito", "Caladea" })
        {
            foreach (var bold in new[] { false, true })
            {
                foreach (var italic in new[] { false, true })
                {
                    var face = catalog.Resolve(family, bold, italic);
                    foreach (var glyph in new[] { '\u2022', 'o' })
                    {
                        Assert.True(face.Typeface.ContainsGlyph(glyph), $"{family} bold {bold} italic {italic} lacks U+{(int)glyph:X4}");
                    }
                }
            }
        }
    }

    [Fact]
    public void ShippedFacesLoad()
    {
        var catalog = Catalog();
        foreach (var family in new[] { "Carlito", "Caladea" })
        {
            foreach (var bold in new[] { false, true })
            {
                foreach (var italic in new[] { false, true })
                {
                    var face = catalog.Resolve(family, bold, italic);
                    Assert.Equal(family, face.Family);
                    Assert.Equal(bold, face.Bold);
                    Assert.Equal(italic, face.Italic);
                    Assert.NotEqual(0, face.Typeface.GetGlyph('A'));
                    Assert.NotEqual(0, face.Typeface.GetGlyph('ä'));
                    Assert.True(face.Ascent > 0);
                    Assert.True(face.Descent > 0);
                    Assert.True(face.LineGap >= 0);
                    Assert.NotNull(face.Shaper);
                }
            }
        }

        Assert.Equal(8, catalog.LoadedFaces);
    }

    [Theory]
    [InlineData("Calibri", "Carlito")]
    [InlineData("calibri", "Carlito")]
    [InlineData("CALIBRI LIGHT", "Carlito")]
    [InlineData("Calibri Light", "Carlito")]
    [InlineData("Cambria", "Caladea")]
    [InlineData("cambria", "Caladea")]
    public void MetricAliasesResolve(string requested, string shipped)
    {
        Assert.Equal(shipped, Catalog().Resolve(requested, false, false).Family);
    }

    [Fact]
    public void ServesInstalledFontsBeforeTheDefault()
    {
        var shipped = Catalog().Resolve("Carlito", false, false);
        var fake = new FakeSystemFonts(shipped);
        var catalog = Catalog(fake);

        Assert.Same(shipped, catalog.Resolve("Segoe UI", false, false));
        Assert.Contains("Segoe UI", fake.Asked);

        _ = catalog.Resolve("Calibri", false, false);
        _ = catalog.Resolve("Cambria", true, false);
        Assert.DoesNotContain("Calibri", fake.Asked);
        Assert.DoesNotContain("Cambria", fake.Asked);
        Assert.DoesNotContain("Carlito", fake.Asked);

        Assert.Equal("Carlito", Catalog().Resolve("Segoe UI", false, false).Family);
    }

    [Theory]
    [InlineData("Times New Roman", "Caladea")]
    [InlineData("Georgia", "Caladea")]
    [InlineData("Garamond", "Caladea")]
    [InlineData("Arial", "Carlito")]
    [InlineData("Comic Sans MS", "Carlito")]
    [InlineData("", "Carlito")]
    [InlineData("  ", "Carlito")]
    public void UnknownFontNeverThrows(string requested, string expected)
    {
        var face = Catalog().Resolve(requested, true, true);
        Assert.Equal(expected, face.Family);
        Assert.True(face.Bold);
        Assert.True(face.Italic);
    }

    [Fact]
    public void FacesLoadOnFirstUse()
    {
        var catalog = Catalog();
        Assert.Equal(0, catalog.LoadedFaces);
        _ = catalog.Resolve("Calibri", false, false);
        Assert.Equal(1, catalog.LoadedFaces);
        _ = catalog.Resolve("Calibri Light", false, false);
        Assert.Equal(1, catalog.LoadedFaces);
        Assert.Same(catalog.Resolve("Carlito", false, false), catalog.Resolve("calibri", false, false));
    }

    [Fact]
    public void ConcurrentResolveLoadsAFaceOnce()
    {
        var catalog = Catalog();
        var faces = new FontFace[16];
        Parallel.For(0, faces.Length, i => faces[i] = catalog.Resolve("Cambria", false, true));
        Assert.All(faces, f => Assert.Same(faces[0], f));
        Assert.Equal(1, catalog.LoadedFaces);
    }

    [Fact]
    public void LayoutGuard()
    {
        var assembly = typeof(FontCatalog).Assembly;
        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();
        var raffica = refs.Where(n => n.StartsWith("Raffica.", StringComparison.Ordinal)).ToArray();
        // The compiler drops a reference the code does not use, so Model may be absent until the layout reads the model.
        Assert.All(raffica, n => Assert.Equal("Raffica.Write.Model", n));
        Assert.DoesNotContain(refs, n => n is "Raffica.Core" or "Raffica.Write.Docx" or "Raffica.Shell" or "Raffica.Pdf");
        Assert.DoesNotContain(refs, n => n.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.Contains(assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), a => a.Key == "IsTrimmable" && a.Value == "True");
    }
}
