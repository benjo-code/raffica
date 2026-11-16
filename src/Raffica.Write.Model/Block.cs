namespace Raffica.Write.Model;

/// <summary>A block-level item of a story (paragraph, table, ...).</summary>
public abstract class Block : OoxmlElement
{
    /// <summary>A deep copy that writes the same XML. <see cref="UnknownContent"/> is shared by reference, everything else is a new object.</summary>
    public abstract Block Clone();
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

    public override UnknownBlock Clone()
    {
        var copy = new UnknownBlock(Content);
        CopyAttributesTo(copy);
        return copy;
    }
}
