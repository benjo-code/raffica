namespace Raffica.Write.Model;

/// <summary>A <c>w:tr</c>: its properties and its cells (and whatever else sits between them, kept as <see cref="UnknownRowItem"/>).</summary>
public sealed class TableRow : RowItem
{
    public PropertyBag Properties { get; } = new(PropertyKind.TableRow);

    /// <summary>How many of <see cref="Items"/> came before <c>w:trPr</c> in the source (for example a <c>w:tblPrEx</c>), so the writer puts it back where it was.</summary>
    public int PropertiesPosition { get; set; }

    public IList<RowItem> Items { get; } = new List<RowItem>();
}
