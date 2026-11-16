using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Decides when two neighbouring runs are one run again, so formatting does not fragment a paragraph. Runs merge only when both hold
/// nothing but text, tabs and breaks and their properties, attributes (<c>w:rsid*</c> ignored) and kept children are equal.
/// </summary>
internal static class RunMerger
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static bool CanMerge(Run a, Run b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return Plain(a) && Plain(b) && SameAttributes(a.UnknownAttributes, b.UnknownAttributes) && SameProperties(a.Properties, b.Properties);
    }

    /// <summary>Appends the content of <paramref name="b"/> to <paramref name="a"/> (neighbouring text joins into one text).</summary>
    public static void Merge(Run a, Run b)
    {
        foreach (var content in b.Content)
        {
            if (a.Content.Count > 0 && a.Content[^1] is TextContent head && content is TextContent tail && head.UnknownAttributes.Count == 0 && tail.UnknownAttributes.Count == 0)
            {
                head.Text += tail.Text;
            }
            else
            {
                a.Content.Add(content);
            }
        }

        b.Content.Clear();
    }

    private static bool Plain(Run run)
    {
        foreach (var content in run.Content)
        {
            if (content is not (TextContent or TabContent or BreakContent))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameProperties(PropertyBag a, PropertyBag b)
    {
        if (a.Children.Count != b.Children.Count || !SameAttributes(a.UnknownAttributes, b.UnknownAttributes))
        {
            return false;
        }

        for (var i = 0; i < a.Children.Count; i++)
        {
            if (!XNode.DeepEquals(a.Children[i], b.Children[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameAttributes(IList<XAttribute> a, IList<XAttribute> b)
    {
        var left = Relevant(a);
        var right = Relevant(b);
        return left.Count == right.Count && left.Zip(right).All(pair => pair.First.Name == pair.Second.Name && pair.First.Value == pair.Second.Value);
    }

    private static List<XAttribute> Relevant(IList<XAttribute> attributes) =>
        [.. attributes.Where(a => !(a.Name.Namespace == W && a.Name.LocalName.StartsWith("rsid", StringComparison.Ordinal))).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)];
}
