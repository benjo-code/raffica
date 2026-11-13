using System.Xml.Linq;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// Equivalence of two XML trees for round-trip tests. Equal means: the same element names, the same attribute set
/// regardless of order, namespace declarations ignored, the same child order, text compared exactly for elements
/// without element children, and whitespace-only text between elements ignored.
/// </summary>
internal static class XmlEquivalence
{
    public static bool AreEqual(XElement a, XElement b) => Explain(a, b) is null;

    /// <summary>The first difference with the path of the element where it was found, or null when equivalent.</summary>
    public static string? Explain(XElement a, XElement b) => Compare(a, b, "/" + Label(a, 1));

    private static string? Compare(XElement a, XElement b, string path)
    {
        if (a.Name != b.Name)
        {
            return $"{path}: element name {a.Name} differs from {b.Name}";
        }

        var attributesA = a.Attributes().Where(x => !x.IsNamespaceDeclaration).ToDictionary(x => x.Name, x => x.Value);
        var attributesB = b.Attributes().Where(x => !x.IsNamespaceDeclaration).ToDictionary(x => x.Name, x => x.Value);
        foreach (var (name, value) in attributesA)
        {
            if (!attributesB.TryGetValue(name, out var other))
            {
                return $"{path}: attribute {name} is missing on the right";
            }

            if (value != other)
            {
                return $"{path}: attribute {name} is \"{value}\" on the left and \"{other}\" on the right";
            }
        }

        foreach (var name in attributesB.Keys)
        {
            if (!attributesA.ContainsKey(name))
            {
                return $"{path}: attribute {name} is missing on the left";
            }
        }

        var childrenA = a.Elements().ToList();
        var childrenB = b.Elements().ToList();
        if (childrenA.Count == 0 && childrenB.Count == 0)
        {
            return a.Value == b.Value ? null : $"{path}: text \"{a.Value}\" differs from \"{b.Value}\"";
        }

        if (!HasOnlyWhitespaceText(a) || !HasOnlyWhitespaceText(b))
        {
            return $"{path}: text mixed with child elements differs";
        }

        for (var i = 0; i < Math.Min(childrenA.Count, childrenB.Count); i++)
        {
            var childPath = path + "/" + Label(childrenA[i], Position(childrenA, i));
            var difference = Compare(childrenA[i], childrenB[i], childPath);
            if (difference is not null)
            {
                return difference;
            }
        }

        return childrenA.Count == childrenB.Count
            ? null
            : $"{path}: {childrenA.Count} child elements on the left, {childrenB.Count} on the right";
    }

    private static bool HasOnlyWhitespaceText(XElement element) =>
        element.Nodes().OfType<XText>().All(t => string.IsNullOrWhiteSpace(t.Value));

    private static int Position(List<XElement> siblings, int index) =>
        siblings.Take(index + 1).Count(s => s.Name == siblings[index].Name);

    private static string Label(XElement element, int position)
    {
        var prefix = element.GetPrefixOfNamespace(element.Name.Namespace);
        var name = string.IsNullOrEmpty(prefix) ? element.Name.LocalName : prefix + ":" + element.Name.LocalName;
        return $"{name}[{position}]";
    }
}
