using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>The styles part (<c>w:styles</c>): the document defaults, the latent styles and the styles, plus whatever else the part holds.</summary>
public sealed class StyleSheet : OrderedElement
{
    public StyleSheet(OoxmlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

    public OoxmlRoot Root { get; }

    /// <summary>The <c>w:docDefaults</c> element, kept whole; null when absent.</summary>
    public XElement? Defaults { get; set; }

    /// <summary>The <c>w:latentStyles</c> element, kept whole; null when absent.</summary>
    public XElement? LatentStyles { get; set; }

    /// <summary>The styles are written at the position they were read in (reordering read items is not reflected: they are sorted by <see cref="OrderedElement.Position"/>); items added in code go in schema order after those read.</summary>
    public IList<Style> Styles { get; } = new List<Style>();

    /// <summary>The first style with this id (<c>w:styleId</c>, compared exactly), or null.</summary>
    public Style? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var style in Styles)
        {
            if (style.Id == id)
            {
                return style;
            }
        }

        return null;
    }

    /// <summary>Puts <paramref name="replacement"/> in the place of <paramref name="old"/> (found by object identity), at the same index. False when <paramref name="old"/> is not in the list.</summary>
    public bool Replace(Style old, Style replacement)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(replacement);
        for (var i = 0; i < Styles.Count; i++)
        {
            if (ReferenceEquals(Styles[i], old))
            {
                Styles[i] = replacement;
                return true;
            }
        }

        return false;
    }
}
