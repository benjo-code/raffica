using System.Xml.Linq;
using Xunit;

namespace Raffica.Shell.Tests.Localization;

public sealed class TranslationTests
{
    private static List<string> EnglishFiles()
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(RepoPaths.Combine("src"), "Strings.en.resx", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{sep}obj{sep}") && !p.Contains($"{sep}bin{sep}"))
            .ToList();
    }

    [Fact]
    public void EveryEnglishKeyHasAGermanValue()
    {
        var files = EnglishFiles();
        Assert.NotEmpty(files);
        foreach (var english in files)
        {
            var german = Path.Combine(Path.GetDirectoryName(english)!, "Strings.de.resx");
            Assert.True(File.Exists(german), $"no German file next to {english}");
            Assert.Empty(TranslationCheck.FindProblems(XDocument.Load(english), XDocument.Load(german)));
        }
    }

    [Fact]
    public void TheCheckFailsOnARemovedGermanString()
    {
        var dir = RepoPaths.Combine("src", "Raffica.Shell", "Resources");
        var english = XDocument.Load(Path.Combine(dir, "Strings.en.resx"));

        var removed = XDocument.Load(Path.Combine(dir, "Strings.de.resx"));
        removed.Root!.Elements("data").First(e => (string)e.Attribute("name")! == "Cancel").Remove();
        Assert.Contains("missing in German: Cancel", TranslationCheck.FindProblems(english, removed));

        var emptied = XDocument.Load(Path.Combine(dir, "Strings.de.resx"));
        emptied.Root!.Elements("data").First(e => (string)e.Attribute("name")! == "Close").Element("value")!.Value = "";
        Assert.Contains("empty in German: Close", TranslationCheck.FindProblems(english, emptied));

        var extra = XDocument.Load(Path.Combine(dir, "Strings.de.resx"));
        extra.Root!.Add(new XElement("data", new XAttribute("name", "Extra"), new XElement("value", "x")));
        Assert.Contains("missing in English: Extra", TranslationCheck.FindProblems(english, extra));
    }
}
