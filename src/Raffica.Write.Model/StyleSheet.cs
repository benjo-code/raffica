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
}
