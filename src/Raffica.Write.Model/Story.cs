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
}
