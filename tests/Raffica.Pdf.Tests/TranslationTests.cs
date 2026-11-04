using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class TranslationTests
{
    private static string[] Keys(string language) =>
        XDocument.Load(RepoPaths.Combine("src", "Raffica.Pdf", "Resources", $"Strings.{language}.resx"))
            .Root!.Elements("data")
            .Select(d => (string)d.Attribute("name")!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void BothLanguagesHaveTheSameKeys()
    {
        var english = Keys("en");
        Assert.NotEmpty(english);
        Assert.Equal(english, Keys("de"));
        Assert.Equal(english.Length, english.Distinct().Count());
    }

    [Fact]
    public void EveryKeyHasATypedPropertyThatResolves()
    {
        var properties = typeof(PdfStrings).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string)).ToList();
        Assert.Equal(Keys("en"), properties.Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(properties, p => Assert.False(string.IsNullOrWhiteSpace((string?)p.GetValue(null))));
    }

    [Fact]
    public void AMissingKeyThrows() => Assert.Throws<InvalidOperationException>(() => PdfStrings.Get("NoSuchKey"));
}
