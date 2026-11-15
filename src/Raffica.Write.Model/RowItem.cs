namespace Raffica.Write.Model;

/// <summary>A child of a table or of a table row: a row, a cell or an element the model does not understand.</summary>
public abstract class RowItem : OoxmlElement
{
    /// <summary>A deep copy that writes the same XML; <see cref="UnknownContent"/> is shared.</summary>
    public abstract RowItem Clone();
}

/// <summary>A child of a table or row the model does not understand (a <c>w:sdt</c>, <c>w:ins</c> or <c>w:customXml</c> wrapper around rows or cells, <c>w:tblPrEx</c>, a foreign element), kept in place.</summary>
public sealed class UnknownRowItem : RowItem
{
    public UnknownRowItem(UnknownContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    public UnknownContent Content { get; }

    public override UnknownRowItem Clone()
    {
        var copy = new UnknownRowItem(Content);
        CopyAttributesTo(copy);
        return copy;
    }
}
