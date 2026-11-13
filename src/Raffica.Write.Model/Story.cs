namespace Raffica.Write.Model;

/// <summary>The content of a body, header, footer, note or comment: the root it came from and its blocks.</summary>
public class Story : OoxmlElement
{
    public Story(OoxmlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

    public OoxmlRoot Root { get; }

    public IList<Block> Blocks { get; } = new List<Block>();

    /// <summary>The body-level <c>w:sectPr</c> (the last section), body stories only. Only a <c>w:sectPr</c> that is the last child of the body; one followed by other content stays an <see cref="UnknownBlock"/>.</summary>
    public SectionProperties? FinalSection { get; set; }

    /// <summary>Children of the root before the block container (for a document: before <c>w:body</c>, for example <c>w:background</c>), kept in order. <see cref="OoxmlElement.UnknownAttributes"/> of a story holds the attributes of <c>w:body</c>.</summary>
    public IList<UnknownContent> LeadingContent { get; } = new List<UnknownContent>();

    /// <summary>Children of the root after the block container, kept in order.</summary>
    public IList<UnknownContent> TrailingContent { get; } = new List<UnknownContent>();
}
