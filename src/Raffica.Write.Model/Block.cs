namespace Raffica.Write.Model;

/// <summary>A block-level item of a story (paragraph, table, ...).</summary>
public abstract class Block : OoxmlElement
{
}

/// <summary>A block-level element the model does not understand, kept as it was read.</summary>
public sealed class UnknownBlock : Block
{
    public UnknownBlock(UnknownContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    public UnknownContent Content { get; }
}
