using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What a split did. <paramref name="Later"/> is the end properties of the later half (the object that was the section's before: a break paragraph's or the body's), changed in place;
/// <paramref name="OldLater"/> is a clone of it from before the split; <paramref name="Carrier"/> is the paragraph that ends the earlier half and <paramref name="CarrierProperties"/> its new break (a clone of the old properties).
/// <paramref name="Change"/> is the one block change (the previous paragraph took the break, or an empty carrier paragraph was inserted).
/// </summary>
internal sealed record SectionSplit(SectionProperties Later, SectionProperties OldLater, Paragraph Carrier, SectionProperties CarrierProperties, BlockChange Change);

/// <summary>The one primitive that makes a section start before a top-level block of the body, for Number pages from here, plain section breaks and page setup "from here on".</summary>
internal static class SectionSplitter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Makes a section start before the top-level block <paramref name="blockIndex"/>: the earlier half ends with a paragraph that carries a clone of the properties the section had (the previous paragraph when it
    /// is a plain paragraph with no break, else a new empty paragraph), and the later half keeps the existing end properties, which get the type <paramref name="kind"/>, lose <c>w:titlePg</c> (a new section never
    /// inherits "different first page") and lose the <c>w:start</c> of the page numbering (the earlier half keeps the restart; this one continues). A body with no final <c>w:sectPr</c> gets one first.
    /// Null, and nothing changed, when the block is the first of the document or of its section: that would make an empty section.
    /// </summary>
    public static SectionSplit? Split(WordDocument document, int blockIndex, SectionBreakKind kind)
    {
        ArgumentNullException.ThrowIfNull(document);
        var blocks = document.Body.Blocks;
        if (blockIndex <= 0 || blockIndex >= blocks.Count)
        {
            return null;
        }

        var sections = SectionList.Of(document);
        var index = SectionIndexOf(sections, blockIndex);
        if (index < 0 || sections[index].FirstBlock >= blockIndex)
        {
            return null;
        }

        var target = sections[index];
        if (target.Break is null && document.Body.FinalSection is null)
        {
            document.Body.FinalSection = new SectionProperties();
            target = target with { Properties = document.Body.FinalSection };
        }

        var later = target.Properties;
        var oldLater = later.Clone();
        var carrierProperties = later.Clone();
        Paragraph carrier;
        BlockChange change;
        if (blocks[blockIndex - 1] is Paragraph { SectionBreak: null } previous)
        {
            var old = previous.Clone();
            previous.SectionBreak = carrierProperties;
            carrier = previous;
            change = new BlockChange(StoryRef.Body, blocks, blockIndex - 1, 1, 1, [old]);
        }
        else
        {
            carrier = new Paragraph { SectionBreak = carrierProperties };
            blocks.Insert(blockIndex, carrier);
            change = new BlockChange(StoryRef.Body, blocks, blockIndex, 0, 1, []);
        }

        later.Set(W + "type", new XElement(W + "type", new XAttribute(W + "val", TypeName(kind))));
        later.Remove(W + "titlePg");
        if (later.Get(W + "pgNumType") is { } numbering)
        {
            numbering.SetAttributeValue(W + "start", null);
            if (!numbering.HasAttributes)
            {
                later.Remove(W + "pgNumType");
            }
        }

        return new SectionSplit(later, oldLater, carrier, carrierProperties, change);
    }

    /// <summary>The index of the section a top-level block belongs to: the first one whose last block is at or after it (a content control that holds a break belongs to the section that ends in it).</summary>
    public static int SectionIndexOf(IReadOnlyList<SectionInfo> sections, int blockIndex)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].LastBlock >= blockIndex)
            {
                return i;
            }
        }

        return sections.Count - 1;
    }

    public static string TypeName(SectionBreakKind kind) => kind switch
    {
        SectionBreakKind.Continuous => "continuous",
        SectionBreakKind.EvenPage => "evenPage",
        SectionBreakKind.OddPage => "oddPage",
        _ => "nextPage",
    };

    /// <summary>True when two section property objects hold the same content (attributes, children, references).</summary>
    public static bool SameContent(SectionProperties a, SectionProperties b)
    {
        if (a.Children.Count != b.Children.Count || a.UnknownAttributes.Count != b.UnknownAttributes.Count || a.HeaderFooterReferences.Count != b.HeaderFooterReferences.Count)
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

        for (var i = 0; i < a.UnknownAttributes.Count; i++)
        {
            if (a.UnknownAttributes[i].Name != b.UnknownAttributes[i].Name || a.UnknownAttributes[i].Value != b.UnknownAttributes[i].Value)
            {
                return false;
            }
        }

        for (var i = 0; i < a.HeaderFooterReferences.Count; i++)
        {
            var x = a.HeaderFooterReferences[i];
            var y = b.HeaderFooterReferences[i];
            if (x.Kind != y.Kind || x.Type != y.Type || x.RelationshipId != y.RelationshipId)
            {
                return false;
            }
        }

        return true;
    }
}
