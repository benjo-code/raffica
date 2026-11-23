using System.Globalization;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Writes the footers of "Number pages from here". The rule for one footer type of a section, given the footer it resolves to (<paramref name="source"/>, null when none):
/// a footer that holds a <c>PAGE</c> field is left alone without a prefix, and with a prefix gets an own copy with the prefix in front of every <c>PAGE</c> field that lacks it (nothing at all when every field has it);
/// a footer that holds no <c>PAGE</c> field gets an own copy (or a new part) with the page number in a paragraph of its own below the existing content, or in place of a lone empty paragraph. Existing footer text is never dropped.
/// The existing part is never edited (undo has no step for a part), so every change is a new part and a new reference.
/// </summary>
internal static class FooterNumbering
{
    /// <summary>The relationship id of the new own footer part this type needs, or null when the footer it resolves to already is what the command wants.</summary>
    public static string? Write(WordDocument document, Story? source, PageNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(numbering);
        var holdsPage = source is not null && PageNumberField.HoldsPage(source.Blocks);
        if (holdsPage && !NeedsPrefix(source!, numbering.Prefix))
        {
            return null;
        }

        var id = HeaderFooterParts.Create(document, HeaderFooterKind.Footer, source);
        var blocks = document.HeadersFooters[id].Content.Blocks;
        if (holdsPage)
        {
            // Every PAGE field the walk can reach gets the prefix; when it reached none (a field it cannot see into), the footer keeps its content and gets a prefixed number below it.
            var reached = 0;
            foreach (var paragraph in PageNumberField.Paragraphs(blocks))
            {
                PageNumberField.PrefixPageFields(paragraph, numbering.Prefix, dry: false, out var found, out _);
                reached += found;
            }

            if (reached > 0)
            {
                return id;
            }
        }

        AddNumber(document, blocks, numbering);
        return id;
    }

    /// <summary>Puts a reference to the footer part into the section's properties: the reference of that type is replaced in place, else one is added.</summary>
    public static void SetReference(SectionProperties properties, HeaderFooterType type, string id)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var references = properties.HeaderFooterReferences;
        for (var i = 0; i < references.Count; i++)
        {
            if (references[i].Kind == HeaderFooterKind.Footer && references[i].Type == type)
            {
                references[i] = new HeaderFooterReference(HeaderFooterKind.Footer, type, id) { BagPosition = references[i].BagPosition };
                return;
            }
        }

        references.Add(new HeaderFooterReference(HeaderFooterKind.Footer, type, id));
    }

    private static bool NeedsPrefix(Story source, string prefix)
    {
        if (prefix.Length == 0)
        {
            return false;
        }

        foreach (var paragraph in PageNumberField.Paragraphs(source.Blocks))
        {
            PageNumberField.PrefixPageFields(paragraph, prefix, dry: true, out _, out var needed);
            if (needed > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddNumber(WordDocument document, IList<Block> blocks, PageNumbering numbering)
    {
        var number = PageNumberField.Build(
            PageNumberLook.Plain,
            PageNumberAlign.Center,
            numbering.Prefix,
            string.Empty,
            string.Empty,
            (numbering.Start ?? 1).ToString(CultureInfo.InvariantCulture),
            "1");
        if (document.Styles?.Find("Footer") is not null)
        {
            number.Properties.StyleId = "Footer";
        }

        if (blocks.Count == 1 && blocks[0] is Paragraph { Inlines.Count: 0 })
        {
            blocks.Clear();
        }

        blocks.Add(number);
    }
}
