namespace Raffica.Write.Model;

public enum HeaderFooterKind
{
    Header,
    Footer,
}

public enum HeaderFooterType
{
    Default,
    First,
    Even,
}

/// <summary>A <c>w:headerReference</c> or <c>w:footerReference</c> of a section: which part (by relationship id) serves which page type.</summary>
public sealed class HeaderFooterReference : OoxmlElement
{
    public HeaderFooterReference(HeaderFooterKind kind, HeaderFooterType type, string relationshipId)
    {
        ArgumentNullException.ThrowIfNull(relationshipId);
        Kind = kind;
        Type = type;
        RelationshipId = relationshipId;
    }

    public HeaderFooterKind Kind { get; set; }

    public HeaderFooterType Type { get; set; }

    public string RelationshipId { get; set; }

    /// <summary>How many of the bag's children came before this reference in the source (null for a new one, which is written first).</summary>
    public int? BagPosition { get; set; }
}
