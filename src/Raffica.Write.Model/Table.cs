namespace Raffica.Write.Model;

/// <summary>A <c>w:tbl</c>: properties, the column grid and the rows.</summary>
public sealed class Table : Block
{
    private int[]? _keptWidths;

    public PropertyBag Properties { get; } = new(PropertyKind.Table);

    /// <summary>How many of <see cref="Rows"/> came before <c>w:tblPr</c> in the source, so the writer puts it back where it was.</summary>
    public int PropertiesPosition { get; set; }

    /// <summary>The <c>w:w</c> of every <c>w:gridCol</c> (0 where it is missing or not a number).</summary>
    public IList<int> GridColumns { get; } = new List<int>();

    /// <summary>How many of <see cref="Rows"/> came before <c>w:tblGrid</c> in the source.</summary>
    public int GridPosition { get; set; }

    /// <summary>The <c>w:tblGrid</c> as read (with its attributes, <c>gridCol</c> attributes beyond <c>w:w</c> and children such as <c>w:tblGridChange</c>), or null when the table was not read from a file. The writer writes it back as it is while <see cref="GridColumns"/> still equals what was read; after an edit it writes the new widths and keeps the grid's attributes and its children that are not <c>w:gridCol</c>.</summary>
    public UnknownContent? KeptGrid { get; private set; }

    /// <summary>True when the grid was read from a file and <see cref="GridColumns"/> still holds what was read.</summary>
    public bool GridIsUnedited => _keptWidths is not null && _keptWidths.AsSpan().SequenceEqual(GridColumns.ToArray());

    /// <summary>The width read for a column, or null for an index beyond the grid as read.</summary>
    public int? KeptWidthAt(int index) => _keptWidths is not null && index >= 0 && index < _keptWidths.Length ? _keptWidths[index] : null;

    /// <summary>Remembers the grid element and the widths read from it, and fills <see cref="GridColumns"/>. Used by the reader.</summary>
    public void SetKeptGrid(UnknownContent grid, IEnumerable<int> widths)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(widths);
        KeptGrid = grid;
        _keptWidths = widths.ToArray();
        GridColumns.Clear();
        foreach (var width in _keptWidths)
        {
            GridColumns.Add(width);
        }
    }

    public IList<RowItem> Rows { get; } = new List<RowItem>();
}
