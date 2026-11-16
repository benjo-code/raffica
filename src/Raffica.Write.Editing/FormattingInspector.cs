using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>The pure read behind the formatting inspector: nothing in the document changes.</summary>
public static class FormattingInspector
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // The children that are not overrides: structure, not formatting. The same sets decide what Clear direct formatting keeps.
    private static readonly HashSet<XName> s_paragraphKept = [W + "pStyle", W + "numPr", W + "sectPr", W + "rPr", W + "pPrChange"];
    private static readonly HashSet<XName> s_runKept = [W + "rStyle", W + "rPrChange"];
    private static readonly HashSet<XName> s_markKept = [W + "rStyle", W + "rPrChange", W + "ins", W + "del", W + "moveFrom", W + "moveTo"];

    /// <summary>
    /// Reads the selection with the plain projection (the visible text without the field results of a layout host); an editor with its own host uses
    /// <see cref="TextEditor.Inspect"/>. Paragraph rows and the paragraph style cover every paragraph the selection touches, run rows and the
    /// character style every run that has covered content (a collapsed selection covers none), mark rows the touched paragraphs without text.
    /// </summary>
    public static InspectorView Read(WordDocument document, TextRange selection) => Read(document, selection, (_, _) => ProjectionHost.Plain);

    /// <summary>Reads the selection with the given projection hosts (the offsets of a selection are offsets of that projection).</summary>
    public static InspectorView Read(WordDocument document, TextRange selection, Func<StoryRef, Paragraph, IProjectionHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(hosts);
        return new TextEditor(document, hosts).Inspect(selection);
    }

    /// <summary>Whether the element is an override of this scope: a direct child that is not one of the structural elements.</summary>
    internal static bool IsOverride(OverrideScope scope, XName name) => !Kept(scope).Contains(name);

    internal static HashSet<XName> Kept(OverrideScope scope) => scope switch
    {
        OverrideScope.Paragraph => s_paragraphKept,
        OverrideScope.Run => s_runKept,
        _ => s_markKept,
    };

    /// <summary>The override elements among the children of a property element of the scope.</summary>
    internal static IEnumerable<XElement> OverridesOf(OverrideScope scope, IEnumerable<XElement> children) => children.Where(c => IsOverride(scope, c.Name));

    internal static InspectorView Build(IReadOnlyList<Paragraph> paragraphs, IReadOnlyList<Paragraph> emptyParagraphs, IReadOnlyList<Run> runs)
    {
        var rows = new List<OverrideRow>();
        rows.AddRange(RowsOf(OverrideScope.Paragraph, [.. paragraphs.Select(p => OverridesOf(OverrideScope.Paragraph, p.Properties.Children).ToList())]));
        rows.AddRange(RowsOf(OverrideScope.Run, [.. runs.Select(r => OverridesOf(OverrideScope.Run, r.Properties.Children).ToList())]));
        rows.AddRange(RowsOf(OverrideScope.ParagraphMark, [.. emptyParagraphs.Select(p => OverridesOf(OverrideScope.ParagraphMark, p.Properties.Get(W + "rPr")?.Elements() ?? []).ToList())]));

        var (paragraphStyle, paragraphMixed) = StyleOf(paragraphs.Select(p => p.Properties.StyleId));
        var (characterStyle, characterMixed) = StyleOf(runs.Select(r => r.Properties.StyleId));
        return new InspectorView(paragraphStyle, characterStyle, paragraphMixed, characterMixed, rows);
    }

    private static (string? Id, bool Mixed) StyleOf(IEnumerable<string?> ids)
    {
        var distinct = ids.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count switch
        {
            0 => (null, false),
            1 => (distinct[0], false),
            _ => (null, true),
        };
    }

    // One row per element name over the items (a paragraph, a run, a mark), in the order of first appearance. A duplicate child counts once, with its first value.
    private static IEnumerable<OverrideRow> RowsOf(OverrideScope scope, List<List<XElement>> items)
    {
        var names = new List<XName>();
        foreach (var item in items)
        {
            foreach (var element in item)
            {
                if (!names.Contains(element.Name))
                {
                    names.Add(element.Name);
                }
            }
        }

        foreach (var name in names)
        {
            var found = items.Select(item => item.Find(e => e.Name == name)).OfType<XElement>().ToList();
            var forms = found.Select(e => e.ToString(SaveOptions.DisableFormatting)).Distinct(StringComparer.Ordinal).Count();
            var presence = found.Count < items.Count ? OverridePresence.Partly : forms == 1 ? OverridePresence.All : OverridePresence.Mixed;
            yield return new OverrideRow(scope, name, presence, ValueOf(found[0]));
        }
    }

    private static string? ValueOf(XElement element)
    {
        if (element.Attribute(W + "val") is { } val)
        {
            return val.Value;
        }

        return element.HasAttributes
            ? string.Join(' ', element.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => string.Create(CultureInfo.InvariantCulture, $"{a.Name.LocalName}={a.Value}")))
            : null;
    }
}
