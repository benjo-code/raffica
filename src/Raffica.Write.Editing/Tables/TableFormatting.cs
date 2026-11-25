using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The pure part of the table formatting operations of milestone 4.5a: each method changes a table it is given (the editor passes a clone of a regular, consistent table) and says what happened. Property
/// writing follows the character formatting: attributes are merged into the element that is there, a child that is new goes where <see cref="SchemaOrder"/> puts it, and a removal takes exactly that one
/// element away. Nothing here knows the history, the layout or the session.
/// </summary>
internal static class TableFormatting
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // The children of w:tcBorders in the order of the schema; start and left (and end and right) are two spellings of one place.
    private static readonly string[] s_borderOrder = ["top", "start|left", "bottom", "end|right", "insideH", "insideV", "tl2br", "tr2bl"];

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Hex(uint? color) => color is { } value ? (value & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture) : "auto";

    // A range of grid cells: rows by their ordinal among the rows of the grid, columns by grid column.
    private readonly record struct Rect(int Top, int Left, int Bottom, int Right);

    // ---- the range ---------------------------------------------------------------------------------------------------------------

    // The rectangle of the range in grid coordinates, widened until it is a union of whole merged cells; null for a range outside the table or a reversed one.
    private static Rect? Closure(Table table, TableGridMap map, CellRange range)
    {
        if (range.FirstRow < 0 || range.LastRow >= table.Rows.Count || range.FirstRow > range.LastRow || range.FirstColumn < 0 || range.LastColumn >= map.Columns || range.FirstColumn > range.LastColumn)
        {
            return null;
        }

        var top = -1;
        var bottom = -1;
        for (var ordinal = 0; ordinal < map.RowCount; ordinal++)
        {
            var index = map.RowIndexes[ordinal];
            if (index >= range.FirstRow && index <= range.LastRow)
            {
                top = top < 0 ? ordinal : top;
                bottom = ordinal;
            }
        }

        if (top < 0)
        {
            return null;
        }

        var rect = new Rect(top, range.FirstColumn, bottom, range.LastColumn);
        bool grew;
        do
        {
            grew = false;
            foreach (var cell in map.Cells)
            {
                var head = cell.Head;
                var cellTop = head.RowOrdinal;
                var cellBottom = head.RowOrdinal + head.RowSpan - 1;
                var overlaps = cellTop <= rect.Bottom && cellBottom >= rect.Top && cell.Column <= rect.Right && cell.LastColumn >= rect.Left;
                if (overlaps && (cellTop < rect.Top || cellBottom > rect.Bottom || cell.Column < rect.Left || cell.LastColumn > rect.Right))
                {
                    rect = new Rect(Math.Min(rect.Top, cellTop), Math.Min(rect.Left, cell.Column), Math.Max(rect.Bottom, cellBottom), Math.Max(rect.Right, cell.LastColumn));
                    grew = true;
                }
            }
        }
        while (grew);

        return rect;
    }

    private static IEnumerable<GridCell> CellsIn(TableGridMap map, Rect rect) =>
        map.Cells.Where(c => c.RowOrdinal >= rect.Top && c.RowOrdinal <= rect.Bottom && c.Column >= rect.Left && c.LastColumn <= rect.Right);

    private static Paragraph? CaretOf(TableGridMap map, Rect rect) =>
        CellsIn(map, rect).Select(c => TableOperations.FirstParagraphOf(c.Cell)).FirstOrDefault(p => p is not null);

    private static Paragraph? FirstCaret(TableGridMap map) => map.Cells.Select(c => TableOperations.FirstParagraphOf(c.Cell)).FirstOrDefault(p => p is not null);

    // ---- borders -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the borders of <paramref name="spec"/> on the cells of the range (widened to whole merged cells). The outer edges of the range are the top of its first row, the bottom of its last row and the
    /// left and right of its first and last column; the lines between its cells are the inside sides. Every border is written on <b>both</b> cells it is shared by: the other cell of an inside line is in the
    /// range anyway, and the cell outside the range on the other side of an outer edge gets the opposite side too (a thinner or wider line there would otherwise win in the layout, which draws the wider of two).
    /// Every cell of a vertically merged cell carries its left and right, the first its top and the last its bottom. Where two writes disagree about one border the later one wins on both cells.
    /// </summary>
    public static TableMutation Borders(Table table, TableGridMap map, CellRange range, BorderSpec spec)
    {
        if (spec is null || !spec.IsValid)
        {
            return TableMutation.Refuse(TableRefusal.InvalidValue);
        }

        if (Closure(table, map, range) is not { } rect)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        var written = new HashSet<(TableCell, string)>();
        foreach (var cell in CellsIn(map, rect))
        {
            var head = cell.Head;
            var lastRow = cell.RowOrdinal == head.RowOrdinal + head.RowSpan - 1;
            if (!cell.IsContinuation)
            {
                var outer = cell.RowOrdinal == rect.Top;
                Apply(cell.Cell, "top", outer ? BorderSides.Top : BorderSides.InsideHorizontal, spec, written);
                if (outer)
                {
                    for (var column = cell.Column; column <= cell.LastColumn; column++)
                    {
                        if (map.Slot(cell.RowOrdinal - 1, column) is { } above)
                        {
                            Apply(above.Cell, "bottom", BorderSides.Top, spec, written);
                        }
                    }
                }
            }

            if (lastRow)
            {
                var outer = cell.RowOrdinal == rect.Bottom;
                Apply(cell.Cell, "bottom", outer ? BorderSides.Bottom : BorderSides.InsideHorizontal, spec, written);
                if (outer)
                {
                    for (var column = cell.Column; column <= cell.LastColumn; column++)
                    {
                        if (map.Slot(cell.RowOrdinal + 1, column) is { IsContinuation: false } below)
                        {
                            Apply(below.Cell, "top", BorderSides.Bottom, spec, written);
                        }
                    }
                }
            }

            var outerLeft = cell.Column == rect.Left;
            Apply(cell.Cell, "left", outerLeft ? BorderSides.Left : BorderSides.InsideVertical, spec, written);
            if (outerLeft && map.Slot(cell.RowOrdinal, cell.Column - 1) is { } before)
            {
                Apply(before.Cell, "right", BorderSides.Left, spec, written);
            }

            var outerRight = cell.LastColumn == rect.Right;
            Apply(cell.Cell, "right", outerRight ? BorderSides.Right : BorderSides.InsideVertical, spec, written);
            if (outerRight && map.Slot(cell.RowOrdinal, cell.LastColumn + 1) is { } after)
            {
                Apply(after.Cell, "left", BorderSides.Right, spec, written);
            }
        }

        return TableMutation.Done(CaretOf(map, rect));
    }

    // Writes a side of a cell when the spec says something about the place it stands for (a line, or off), once per cell and side.
    private static void Apply(TableCell cell, string side, BorderSides place, BorderSpec spec, HashSet<(TableCell, string)> written)
    {
        if ((spec.Set & place) != 0)
        {
            if (written.Add((cell, side)))
            {
                WriteSide(cell, side, spec);
            }
        }
        else if ((spec.Cleared & place) != 0 && written.Add((cell, side)))
        {
            WriteSide(cell, side, null);
        }
    }

    private static int OrderOf(XName name)
    {
        for (var i = 0; i < s_borderOrder.Length; i++)
        {
            if (name.Namespace == W && s_borderOrder[i].Split('|').Contains(name.LocalName))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSide(XName name, string side) =>
        name.Namespace == W && (name.LocalName == side || (side == "left" && name.LocalName == "start") || (side == "right" && name.LocalName == "end"));

    // The element of one side of w:tcBorders, made where the schema puts it when the cell has none; an element that is there keeps its name (start and end stay).
    private static XElement SideElement(TableCell cell, string side)
    {
        var borders = cell.Properties.Get(W + "tcBorders");
        if (borders is null)
        {
            borders = new XElement(W + "tcBorders");
            cell.Properties.Set(W + "tcBorders", borders);
        }

        var element = borders.Elements().FirstOrDefault(e => IsSide(e.Name, side));
        if (element is null)
        {
            element = new XElement(W + side);
            var mine = OrderOf(element.Name);
            var next = borders.Elements().FirstOrDefault(e => OrderOf(e.Name) > mine);
            if (next is null)
            {
                borders.Add(element);
            }
            else
            {
                next.AddBeforeSelf(element);
            }
        }

        return element;
    }

    /// <summary>
    /// Makes the border of <paramref name="targetSide"/> of a cell the same as the border of <paramref name="sourceSide"/> of another (a copy of the element; no element on the source side removes the target's), so a
    /// border two cells share stays the same on both after a structure operation made one of the two cells from the other.
    /// </summary>
    internal static void MirrorSide(TableCell target, string targetSide, TableCell source, string sourceSide)
    {
        var from = source.Properties.Get(W + "tcBorders")?.Elements().FirstOrDefault(e => IsSide(e.Name, sourceSide));
        if (from is null)
        {
            var borders = target.Properties.Get(W + "tcBorders");
            borders?.Elements().FirstOrDefault(e => IsSide(e.Name, targetSide))?.Remove();
            if (borders is not null && !borders.HasElements)
            {
                target.Properties.Remove(W + "tcBorders");
            }

            return;
        }

        SideElement(target, targetSide).ReplaceAttributes(from.Attributes().Select(a => new XAttribute(a)));
    }

    // Writes one side of w:tcBorders: the element that is there keeps its name and its other attributes, a new one goes where the schema puts it. A null spec turns the side off (w:val nil).
    private static void WriteSide(TableCell cell, string side, BorderSpec? spec)
    {
        var element = SideElement(cell, side);

        foreach (var theme in new[] { "themeColor", "themeTint", "themeShade" })
        {
            element.Attribute(W + theme)?.Remove();
        }

        if (spec is null)
        {
            element.SetAttributeValue(W + "val", "nil");
            foreach (var name in new[] { "sz", "space", "color" })
            {
                element.Attribute(W + name)?.Remove();
            }

            return;
        }

        element.SetAttributeValue(W + "val", spec.Style switch
        {
            TableBorderStyle.DoubleLine => "double",
            TableBorderStyle.Dashed => "dashed",
            TableBorderStyle.Dotted => "dotted",
            _ => "single",
        });
        element.SetAttributeValue(W + "sz", Number(spec.WidthEighths));
        element.SetAttributeValue(W + "space", "0");
        element.SetAttributeValue(W + "color", Hex(spec.Color));
    }

    // ---- shading -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Shades every cell of the range (widened to whole merged cells) with a solid fill: <c>w:shd</c> with <c>w:val="clear"</c>, <c>w:color="auto"</c> and the fill as six hex digits (the alpha is ignored); the element
    /// that is there is merged into (its theme fill attributes go, a pattern becomes a solid fill). A null colour removes the element and nothing else.
    /// </summary>
    public static TableMutation Shading(Table table, TableGridMap map, CellRange range, uint? color)
    {
        if (Closure(table, map, range) is not { } rect)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        foreach (var cell in CellsIn(map, rect))
        {
            if (color is not { } value)
            {
                cell.Cell.Properties.Remove(W + "shd");
                continue;
            }

            var shd = cell.Cell.Properties.Get(W + "shd");
            if (shd is null)
            {
                shd = new XElement(W + "shd");
                cell.Cell.Properties.Set(W + "shd", shd);
            }

            foreach (var theme in new[] { "themeFill", "themeFillTint", "themeFillShade", "themeColor", "themeTint", "themeShade" })
            {
                shd.Attribute(W + theme)?.Remove();
            }

            shd.SetAttributeValue(W + "val", "clear");
            shd.SetAttributeValue(W + "color", "auto");
            shd.SetAttributeValue(W + "fill", Hex(value));
        }

        return TableMutation.Done(CaretOf(map, rect));
    }

    // ---- header row and row height -----------------------------------------------------------------------------------------------

    private static bool IsOn(XElement? element) => element is not null && (string?)element.Attribute(W + "val") is not ("0" or "false" or "off");

    /// <summary>
    /// Turns the repeated header row on or off. On marks the first row (<c>w:tblHeader</c>; rows that already lead the table as header rows stay, so the header stays as tall as it was). Off removes the mark from every
    /// header row that counts, which are the leading rows that carry it (the layout, like Word, repeats only rows that start at the top); a stray mark further down has no effect and is left alone.
    /// </summary>
    public static TableMutation HeaderRow(Table table, TableGridMap map, bool on)
    {
        if (map.RowCount == 0)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        var rows = map.RowIndexes.Select(i => (TableRow)table.Rows[i]).ToList();
        if (on)
        {
            rows[0].Properties.Set(W + "tblHeader", new XElement(W + "tblHeader"));
        }
        else
        {
            foreach (var row in rows)
            {
                if (!IsOn(row.Properties.Get(W + "tblHeader")))
                {
                    break;
                }

                row.Properties.Remove(W + "tblHeader");
            }
        }

        return TableMutation.Done(map.CellsOfRow(0).Select(c => TableOperations.FirstParagraphOf(c.Cell)).FirstOrDefault(p => p is not null));
    }

    /// <summary>
    /// Sets the height of the row at <paramref name="row"/> (an index of <c>Table.Rows</c>): <c>w:trHeight</c> with the rule at least or exact. Zero twips removes the element and nothing else.
    /// </summary>
    public static TableMutation RowHeight(Table table, TableGridMap map, int row, int twips, bool exact)
    {
        var ordinal = map.OrdinalOf(row);
        if (ordinal < 0)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        if (twips < 0 || twips > TableRules.MaxTwips)
        {
            return TableMutation.Refuse(TableRefusal.InvalidValue);
        }

        var properties = ((TableRow)table.Rows[row]).Properties;
        if (twips == 0)
        {
            properties.Remove(W + "trHeight");
        }
        else
        {
            properties.Set(W + "trHeight", new XElement(W + "trHeight", new XAttribute(W + "val", Number(twips)), new XAttribute(W + "hRule", exact ? "exact" : "atLeast")));
        }

        return TableMutation.Done(map.CellsOfRow(ordinal).Select(c => TableOperations.FirstParagraphOf(c.Cell)).FirstOrDefault(p => p is not null));
    }

    // ---- widths ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the width of every column: the grid, <c>w:tblW</c> in twips (the sum), <c>w:tblLayout fixed</c> and <c>w:tcW</c> in twips on <b>every</b> cell (the sum of the columns it spans, a vertical-merge
    /// continuation included). The widths already in the table are never trusted: a table that is autofit or in percentages is made fixed here, whatever its cells said, so the grid, the cells and the table width
    /// agree after the first width write (the layout reads exactly these numbers back, see <c>TableWidthRoundTripTests</c>).
    /// </summary>
    public static TableMutation ColumnWidths(Table table, TableGridMap map, IReadOnlyList<int> twips)
    {
        if (twips is null || twips.Count != map.Columns || twips.Any(w => w < TableRules.MinColumnTwips || w > TableRules.MaxTwips) || twips.Sum(w => (long)w) > TableRules.MaxTwips)
        {
            return TableMutation.Refuse(TableRefusal.InvalidValue);
        }

        Write(table, map, [.. twips]);
        return TableMutation.Done(FirstCaret(map));
    }

    // True when the table is already fixed and in twips: a width write then changes nothing about how its widths are read, so the widths in the model are the truth and the drawn widths are not asked.
    private static bool IsFixedDxa(Table table) =>
        (string?)table.Properties.Get(W + "tblLayout")?.Attribute(W + "type") == "fixed"
        && table.Properties.Get(W + "tblW") is { } width && (string?)width.Attribute(W + "type") is "dxa" or null
        && int.TryParse((string?)table.Properties.Get(W + "tblW")?.Attribute(W + "w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var preferred) && preferred > 0;

    // The drawn widths the caller passed, checked: one per grid column, each above zero, the sum within the table limit.
    private static bool ValidDrawn(IReadOnlyList<int>? drawn, TableGridMap map) =>
        drawn is null || (drawn.Count == map.Columns && drawn.All(w => w > 0) && drawn.Sum(w => (long)w) <= TableRules.MaxTwips);

    /// <summary>
    /// Sets the width of the table: the columns keep their proportions (largest remainder, so the sum is exactly <paramref name="twips"/>; when a column would get narrower than the minimum, every column takes the
    /// minimum and the rest is shared in proportion), then the first-width-write rule of <see cref="ColumnWidths"/> applies. The proportions are those of <paramref name="drawnColumnTwips"/> when the caller passes them
    /// and the table is not yet fixed and in twips (an autofit or percentage table is drawn by the layout from its content or from the room, so the model's grid is not what the reader sees: the conversion freezes the
    /// drawn widths, as Word does); otherwise they are those of the grid, which is the documented fallback.
    /// </summary>
    public static TableMutation TableWidth(Table table, TableGridMap map, int twips, IReadOnlyList<int>? drawnColumnTwips = null)
    {
        if (!ValidDrawn(drawnColumnTwips, map) || twips < map.Columns * TableRules.MinColumnTwips || twips > TableRules.MaxTwips)
        {
            return TableMutation.Refuse(TableRefusal.InvalidValue);
        }

        var weights = drawnColumnTwips is not null && !IsFixedDxa(table)
            ? drawnColumnTwips.Select(w => (long)w).ToArray()
            : table.GridColumns.Take(map.Columns).Select(w => (long)Math.Max(w, 0)).ToArray();
        var widths = Apportion(weights, twips);
        if (widths.Any(w => w < TableRules.MinColumnTwips))
        {
            var rest = weights.Select(w => Math.Max(w - TableRules.MinColumnTwips, 0)).ToArray();
            var shares = Apportion(rest, twips - (map.Columns * TableRules.MinColumnTwips));
            widths = [.. shares.Select(s => s + TableRules.MinColumnTwips)];
        }

        Write(table, map, widths);
        return TableMutation.Done(FirstCaret(map));
    }

    /// <summary>
    /// Makes the columns equal, adding up to the width the table has now. The width is the sum of <paramref name="drawnColumnTwips"/> when the caller passes them and the table is not yet fixed and in twips (the
    /// conversion freezes what the reader sees); otherwise <c>w:tblW</c> when it is in twips, else the sum of the grid, which is the documented fallback (a percentage width has no twip value without the layout). The
    /// remainder of the division goes to the last column, so the sum is exactly the table width.
    /// </summary>
    public static TableMutation Distribute(Table table, TableGridMap map, IReadOnlyList<int>? drawnColumnTwips = null)
    {
        if (!ValidDrawn(drawnColumnTwips, map))
        {
            return TableMutation.Refuse(TableRefusal.InvalidValue);
        }

        var total = drawnColumnTwips is not null && !IsFixedDxa(table)
            ? (int)drawnColumnTwips.Sum(w => (long)w)
            : table.Properties.Get(W + "tblW") is { } width && (string?)width.Attribute(W + "type") is "dxa" or null && int.TryParse((string?)width.Attribute(W + "w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var preferred) && preferred > 0
                ? preferred
                : table.GridColumns.Take(map.Columns).Sum();
        if (total < map.Columns * TableRules.MinColumnTwips || total > TableRules.MaxTwips)
        {
            return TableMutation.Refuse(TableRefusal.InvalidValue);
        }

        var each = total / map.Columns;
        var widths = new int[map.Columns];
        Array.Fill(widths, each);
        widths[^1] = total - (each * (map.Columns - 1));
        Write(table, map, widths);
        return TableMutation.Done(FirstCaret(map));
    }

    private static void Write(Table table, TableGridMap map, int[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            table.GridColumns[i] = widths[i];
        }

        table.Properties.Set(W + "tblW", new XElement(W + "tblW", new XAttribute(W + "w", Number(widths.Sum())), new XAttribute(W + "type", "dxa")));
        table.Properties.Set(W + "tblLayout", new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")));
        foreach (var cell in map.Cells)
        {
            var span = 0;
            for (var c = cell.Column; c <= cell.LastColumn; c++)
            {
                span += widths[c];
            }

            TableOperations.SetWidth(cell.Cell, span);
        }
    }

    // The integers that add up to total and are as close as possible to total * weight / sum(weights) (largest remainder, ties to the earlier column); equal shares when every weight is zero.
    private static int[] Apportion(long[] weights, int total)
    {
        var sum = weights.Sum();
        if (sum <= 0)
        {
            weights = [.. weights.Select(_ => 1L)];
            sum = weights.Length;
        }

        var result = new int[weights.Length];
        var remainders = new long[weights.Length];
        var given = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            var scaled = weights[i] * total;
            result[i] = (int)(scaled / sum);
            remainders[i] = scaled % sum;
            given += result[i];
        }

        foreach (var index in Enumerable.Range(0, weights.Length).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take(total - given))
        {
            result[index]++;
        }

        return result;
    }
}
