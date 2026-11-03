using System.Xml.Linq;
using Avalonia.Media;

namespace Raffica.Shell.Tests.Tokens;

/// <summary>Reads the colour tokens straight from the AXAML source, so the tests need no Avalonia application.</summary>
public static class TokenFile
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string ColorPrefix = "Raffica.Color.";
    private const string BrushPrefix = "Raffica.Brush.";

    public static string StylesPath(string fileName) => RepoPaths.Combine("src", "Raffica.Shell", "Styles", fileName);

    /// <summary>The colours of one theme ("Light", "Dark" from Tokens.axaml, "HighContrast" from Tokens.HighContrast.axaml), keyed by token name such as <c>Surface.Page</c>.</summary>
    public static IReadOnlyDictionary<string, Color> Load(string theme) =>
        Elements(theme, "Color", ColorPrefix).ToDictionary(e => e.Name, e => Color.Parse(e.Value));

    /// <summary>The brushes of one theme, keyed by token name; the value is the brush colour.</summary>
    public static IReadOnlyDictionary<string, Color> LoadBrushes(string theme) =>
        Elements(theme, "SolidColorBrush", BrushPrefix).ToDictionary(e => e.Name, e => Color.Parse(e.Value));

    /// <summary>The raw text of a non-colour resource of one theme, such as the page shadow.</summary>
    public static string LoadText(string theme, string key) =>
        Root(theme).Elements().Single(e => (string?)e.Attribute(X + "Key") == key).Value.Trim();

    private static IEnumerable<(string Name, string Value)> Elements(string theme, string element, string prefix)
    {
        foreach (var e in Root(theme).Elements().Where(e => e.Name.LocalName == element))
        {
            var key = (string?)e.Attribute(X + "Key");
            if (key is null || !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var value = element == "Color" ? e.Value.Trim() : (string?)e.Attribute("Color");
            yield return (key[prefix.Length..], value ?? throw new InvalidOperationException(key + " has no colour."));
        }
    }

    private static XElement Root(string theme)
    {
        if (theme == "HighContrast")
        {
            return XDocument.Load(StylesPath("Tokens.HighContrast.axaml")).Root!;
        }

        return XDocument.Load(StylesPath("Tokens.axaml")).Root!
            .Descendants()
            .Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(X + "Key") == theme);
    }
}
