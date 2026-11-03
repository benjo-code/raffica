using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Raffica.Shell.Tests.Tokens;

/// <summary>Component styles and the gallery take their colours from the tokens, never from a literal.</summary>
public sealed partial class NoHexInComponentStylesTests
{
    private static readonly string[] ColorAttributes =
        ["Color", "Background", "Foreground", "Fill", "Stroke", "BorderBrush", "CaretBrush", "SelectionBrush", "SelectionForegroundBrush", "TextFill"];

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexLiteral();

    [Fact]
    public void NoComponentStyleContainsAColourLiteral()
    {
        var files = ComponentFiles().ToList();

        Assert.Empty(files.SelectMany(f => Literals(f)));
    }

    [Fact]
    public void ALiteralInAComponentStyleIsFound()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raffica-nohex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var hex = Path.Combine(folder, "Hex.axaml");
            File.WriteAllText(hex, "<Style xmlns=\"https://github.com/avaloniaui\"><Setter Property=\"Background\" Value=\"#FF0000\" /></Style>");
            var named = Path.Combine(folder, "Named.axaml");
            File.WriteAllText(named, "<Border xmlns=\"https://github.com/avaloniaui\" Background=\"Red\" />");
            var token = Path.Combine(folder, "Token.axaml");
            File.WriteAllText(token, "<Border xmlns=\"https://github.com/avaloniaui\" Background=\"{DynamicResource Raffica.Brush.Surface.Page}\" BorderBrush=\"Transparent\" />");

            Assert.Single(Literals(hex));
            Assert.Single(Literals(named));
            Assert.Empty(Literals(token));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static IEnumerable<string> ComponentFiles()
    {
        var controls = RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Controls");
        if (Directory.Exists(controls))
        {
            foreach (var file in Directory.EnumerateFiles(controls, "*.axaml", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        foreach (var file in Directory.EnumerateFiles(RepoPaths.Combine("src", "Raffica.Gallery"), "*.axaml", SearchOption.AllDirectories))
        {
            if (!file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> Literals(string file)
    {
        foreach (var attribute in XDocument.Load(file).Descendants().SelectMany(e => e.Attributes()))
        {
            var value = attribute.Value.Trim();
            if (HexLiteral().IsMatch(value))
            {
                yield return $"{Path.GetFileName(file)}: {attribute.Name.LocalName}=\"{value}\"";
            }
            else if (ColorAttributes.Contains(attribute.Name.LocalName) &&
                     value.Length > 0 && !value.StartsWith('{') && !value.Equals("Transparent", StringComparison.Ordinal))
            {
                yield return $"{Path.GetFileName(file)}: {attribute.Name.LocalName}=\"{value}\"";
            }
        }
    }
}
