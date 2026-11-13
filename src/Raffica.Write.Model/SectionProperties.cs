namespace Raffica.Write.Model;

/// <summary>A <c>w:sectPr</c>: its header and footer references typed, everything else kept in the bag. The attributes of the element are <see cref="OoxmlElement.UnknownAttributes"/>.</summary>
public sealed class SectionProperties : PropertyBag
{
    public SectionProperties()
        : base(PropertyKind.Section)
    {
    }

    public IList<HeaderFooterReference> HeaderFooterReferences { get; } = new List<HeaderFooterReference>();
}
