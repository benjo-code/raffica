using System.Xml.Linq;

namespace Raffica.Shell.Tests.Localization;

/// <summary>Compares an English and a German <c>.resx</c> document.</summary>
public static class TranslationCheck
{
    public static IReadOnlyList<string> FindProblems(XDocument english, XDocument german)
    {
        var en = Read(english);
        var de = Read(german);
        var problems = new List<string>();
        foreach (var key in en.Keys)
        {
            if (!de.TryGetValue(key, out var value))
            {
                problems.Add($"missing in German: {key}");
            }
            else if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"empty in German: {key}");
            }
        }

        problems.AddRange(de.Keys.Where(key => !en.ContainsKey(key)).Select(key => $"missing in English: {key}"));
        return problems;
    }

    private static Dictionary<string, string> Read(XDocument document) =>
        document.Root!.Elements("data").ToDictionary(
            e => (string)e.Attribute("name")!,
            e => (string?)e.Element("value") ?? string.Empty);
}
