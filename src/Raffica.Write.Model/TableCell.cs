namespace Raffica.Write.Model;

/// <summary>A <c>w:tc</c>: its properties and its blocks (a nested table is a block of the cell).</summary>
public sealed class TableCell : RowItem
{
    public PropertyBag Properties { get; } = new(PropertyKind.TableCell);

    /// <summary>How many of <see cref="Blocks"/> came before <c>w:tcPr</c> in the source, so the writer puts it back where it was.</summary>
    public int PropertiesPosition { get; set; }

    public IList<Block> Blocks { get; } = new List<Block>();

    public override TableCell Clone()
    {
        var copy = new TableCell { PropertiesPosition = PropertiesPosition };
        Properties.CopyStateTo(copy.Properties);
        CopyAttributesTo(copy);
        foreach (var block in Blocks)
        {
            copy.Blocks.Add(block.Clone());
        }

        return copy;
    }
}
