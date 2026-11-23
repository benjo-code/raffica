namespace Raffica.Write.Model;

/// <summary>A <c>w:sectPr</c>: its header and footer references typed, everything else kept in the bag. The attributes of the element are <see cref="OoxmlElement.UnknownAttributes"/>.</summary>
public sealed class SectionProperties : PropertyBag
{
    public SectionProperties()
        : base(PropertyKind.Section)
    {
    }

    public IList<HeaderFooterReference> HeaderFooterReferences { get; } = new List<HeaderFooterReference>();

    /// <summary>Replaces the attributes, the kept children and the header and footer references of this object with clones of those of <paramref name="other"/>; the object itself stays where it is in the document.</summary>
    public void CopyContentFrom(SectionProperties other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            return;
        }

        ReplaceStateFrom(other);
        HeaderFooterReferences.Clear();
        foreach (var reference in other.HeaderFooterReferences)
        {
            HeaderFooterReferences.Add(reference.Clone());
        }
    }

    public override SectionProperties Clone()
    {
        var copy = new SectionProperties();
        CopyStateTo(copy);
        foreach (var reference in HeaderFooterReferences)
        {
            copy.HeaderFooterReferences.Add(reference.Clone());
        }

        return copy;
    }
}
