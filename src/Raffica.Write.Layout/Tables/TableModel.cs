using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>How a preferred width is meant (<c>w:type</c> of <c>w:tblW</c> and <c>w:tcW</c>).</summary>
internal enum WidthKind
{
    Auto,
    Dxa,
    Pct,
}

/// <summary>A preferred width: points for <see cref="WidthKind.Dxa"/>, a fraction of the table (1 is all of it) for <see cref="WidthKind.Pct"/>.</summary>
internal readonly record struct PreferredWidth(WidthKind Kind, float Value)
{
    public static readonly PreferredWidth Auto = new(WidthKind.Auto, 0f);

    /// <summary>Reads a <c>w:tblW</c> or <c>w:tcW</c>; anything that is not a usable number is automatic.</summary>
    public static PreferredWidth Read(XElement? element)
    {
        if (element is null)
        {
            return Auto;
        }

        var text = Ooxml.Attr(element, "w");
        var type = Ooxml.Attr(element, "type");
        if (type is "auto" or "nil" || text is null)
        {
            return Auto;
        }

        if (type == "pct" || text.EndsWith('%'))
        {
            var percent = text.EndsWith('%')
                ? double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p / 100.0 : double.NaN
                : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var q) ? q / 5000.0 : double.NaN;
            return double.IsFinite(percent) && percent > 0 ? new PreferredWidth(WidthKind.Pct, (float)Math.Min(percent, 10.0)) : Auto;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) && double.IsFinite(twips) && twips > 0)
        {
            return new PreferredWidth(WidthKind.Dxa, TableLimits.Length(twips / 20.0));
        }

        return Auto;
    }
}

/// <summary>The bounds that keep a table finite whatever the file says.</summary>
internal static class TableLimits
{
    /// <summary>Word's own limit of columns; cells beyond it share the last column.</summary>
    public const int MaximumColumns = 63;

    /// <summary>No single length of a table (a column, an indent, a row height) is taken beyond this many points.</summary>
    public const float MaximumLength = 31_680f;

    /// <summary>The nesting depth of tables in cells; deeper ones are shown as a marked box.</summary>
    public const int MaximumDepth = 16;

    /// <summary>A finite, non-negative length no larger than <see cref="MaximumLength"/>.</summary>
    public static float Length(double value) =>
        double.IsFinite(value) ? (float)Math.Clamp(value, 0.0, MaximumLength) : 0f;

    public static float Length(float value) => Length((double)value);
}

internal enum RowHeightRule
{
    Auto,
    AtLeast,
    Exact,
}

internal enum CellVerticalAlignment
{
    Top,
    Center,
    Bottom,
}

/// <summary>A cell as the file states it: where it starts in the grid, how far it reaches, its margins, format and the plans of its blocks. No layout happens here.</summary>
internal sealed class CellModel
{
    private float _min = float.NaN;
    private float _max = float.NaN;

    public int Start { get; set; }

    public int Span { get; set; } = 1;

    /// <summary>The first cell of a vertical merge (or the cell itself when it has none).</summary>
    public CellModel? Head { get; set; }

    /// <summary>True for a cell that continues the one above: its content is not shown (Word hides it) and the head's box covers it.</summary>
    public bool IsContinuation => Head is not null;

    /// <summary>For the first cell of a merge: how many rows the box covers (1 without a merge).</summary>
    public int RowSpan { get; set; } = 1;

    public int Row { get; set; }

    public PreferredWidth Width { get; set; }

    public float MarginTop { get; set; }

    public float MarginLeft { get; set; }

    public float MarginBottom { get; set; }

    public float MarginRight { get; set; }

    public CellVerticalAlignment Alignment { get; set; }

    public required ResolvedCellFormat Format { get; init; }

    /// <summary>The right border of the last column this cell covers (the format asked for the first column gives the left one).</summary>
    public ResolvedBorder? RightBorder { get; set; }

    public List<BlockPlan> Content { get; } = [];

    /// <summary>The narrowest the content goes (the longest word), plus the margins; measured once.</summary>
    public float MinWidth
    {
        get
        {
            Measure();
            return _min;
        }
    }

    /// <summary>The width of the content on one line, plus the margins; measured once.</summary>
    public float MaxWidth
    {
        get
        {
            Measure();
            return _max;
        }
    }

    private void Measure()
    {
        if (!float.IsNaN(_min))
        {
            return;
        }

        float min = 0f, max = 0f;
        foreach (var plan in Content)
        {
            var (lo, hi) = plan.ContentWidths();
            min = Math.Max(min, lo);
            max = Math.Max(max, hi);
        }

        var margins = MarginLeft + MarginRight;
        _min = TableLimits.Length(min + margins);
        _max = TableLimits.Length(max + margins);
    }
}

internal sealed class RowModel
{
    public CellModel[] Cells { get; set; } = [];

    public float Height { get; set; }

    public RowHeightRule Rule { get; set; }

    public bool CantSplit { get; set; }

    public bool IsHeader { get; set; }

    /// <summary>True when a cell of this row belongs to a merge that reaches into the row above or below: the row is not cut inside.</summary>
    public bool InMerge { get; set; }

    /// <summary>True when a merge crosses the line between the row above and this one: no page break goes there.</summary>
    public bool NoCutBefore { get; set; }
}

/// <summary>A table as the file states it, planned but not laid out: the grid, the rows with their cells, and the table-level properties.</summary>
internal sealed class TableModel
{
    public RowModel[] Rows { get; set; } = [];

    public int Columns { get; set; }

    /// <summary>The widths of the grid columns in points (0 where the file gives none).</summary>
    public float[] Grid { get; set; } = [];

    public PreferredWidth Width { get; set; }

    public bool IsFixed { get; set; }

    /// <summary>The distance of the table from the left edge of the text area to the edge of the first cell's border (0 for a table that starts at the margin).</summary>
    public float Indent { get; set; }

    public ParagraphAlignment Alignment { get; set; }

    /// <summary>How many leading rows repeat on each page (<c>w:tblHeader</c>); 0 when every row is a header row.</summary>
    public int HeaderRows { get; set; }

    public int CellCount { get; set; }

    public int Depth { get; set; }
}

/// <summary>Reads a <c>w:tbl</c> into a <see cref="TablePlan"/>: structure, formats and the plans of the cell blocks; nothing is laid out.</summary>
internal static class TableReader
{
    public static TablePlan Read(BlockPlanner planner, Table table, int bodyIndex, int depth)
    {
        var styles = planner.Styles;
        var resolved = styles.ForTable(table);
        var model = new TableModel { Depth = depth };

        // A null row stands for an element the model keeps as unknown between the rows (an sdt around rows); a null cell for one between
        // the cells. Both are shown as a marked box, never left out.
        var rows = new List<(TableRow? Row, List<TableCell?> Cells)>();
        foreach (var item in table.Rows)
        {
            if (item is TableRow row)
            {
                if (row.Properties.Get(Ooxml.W + "del") is null)
                {
                    // A row with a tracked deletion is gone in the accepted view.
                    var cells = new List<TableCell?>(row.Items.Count);
                    foreach (var part in row.Items)
                    {
                        if (part is TableCell cell)
                        {
                            cells.Add(cell);
                        }
                        else if (part is UnknownRowItem unknown && Takes(unknown))
                        {
                            cells.Add(null);
                        }
                    }

                    rows.Add((row, cells));
                }
            }
            else if (item is UnknownRowItem unknownRow && Takes(unknownRow))
            {
                rows.Add((null, [null]));
            }
        }

        // The columns: the grid, widened to the widest row.
        var grid = new List<float>();
        foreach (var twips in table.GridColumns)
        {
            if (grid.Count < TableLimits.MaximumColumns)
            {
                grid.Add(TableLimits.Length(twips / 20.0));
            }
        }

        var columns = grid.Count;
        var starts = new List<int[]>(rows.Count);
        var spans = new List<int[]>(rows.Count);
        foreach (var (row, cells) in rows)
        {
            var at = row is null ? 0 : Math.Min(Math.Max(Value(row.Properties.Get(Ooxml.W + "gridBefore")) ?? 0, 0), TableLimits.MaximumColumns - 1);
            var rowStarts = new int[cells.Count];
            var rowSpans = new int[cells.Count];
            for (var c = 0; c < cells.Count; c++)
            {
                var span = Math.Clamp(cells[c] is { } own ? Value(own.Properties.Get(Ooxml.W + "gridSpan")) ?? 1 : 1, 1, TableLimits.MaximumColumns);
                var start = Math.Min(at, TableLimits.MaximumColumns - 1);
                span = Math.Min(span, TableLimits.MaximumColumns - start);
                rowStarts[c] = start;
                rowSpans[c] = span;
                at = start + span;
                columns = Math.Max(columns, at);
            }

            starts.Add(rowStarts);
            spans.Add(rowSpans);
        }

        // An unknown row spans the whole grid.
        columns = Math.Max(columns, 1);
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Row is null)
            {
                spans[r][0] = columns;
            }
        }

        while (grid.Count < columns)
        {
            grid.Add(0f);
        }

        model.Columns = columns;
        model.Grid = [.. grid];

        var tblPr = table.Properties;
        model.Width = PreferredWidth.Read(tblPr.Get(Ooxml.W + "tblW"));
        model.IsFixed = tblPr.Get(Ooxml.W + "tblLayout") is { } layout && Ooxml.Attr(layout, "type") == "fixed";
        model.Alignment = tblPr.Get(Ooxml.W + "jc") is not { } justification ? ParagraphAlignment.Left : Ooxml.Val(justification) switch
        {
            "center" => ParagraphAlignment.Center,
            "right" or "end" => ParagraphAlignment.Right,
            _ => ParagraphAlignment.Left,
        };
        var indent = PreferredWidth.Read(tblPr.Get(Ooxml.W + "tblInd"));
        var indentPoints = indent.Kind == WidthKind.Dxa ? indent.Value : 0f;
        if (tblPr.Get(Ooxml.W + "tblInd") is { } indentElement && Ooxml.IntAttr(indentElement, "w") is { } signed && signed < 0)
        {
            indentPoints = -TableLimits.Length(-signed / 20.0);
        }

        model.Indent = indentPoints;

        var rowModels = new RowModel[rows.Count];
        var rowCount = rows.Count;
        var headers = 0;
        var counting = true;
        var cellCount = 0;
        var open = new CellModel?[Math.Max(columns, 1)];
        for (var r = 0; r < rowCount; r++)
        {
            var (row, cells) = rows[r];
            var rowModel = new RowModel { Cells = new CellModel[cells.Count] };
            if (row is not null)
            {
                ReadRow(row, rowModel);
            }

            rowModels[r] = rowModel;
            if (counting && rowModel.IsHeader)
            {
                headers++;
            }
            else
            {
                counting = false;
            }

            var seen = new bool[open.Length];
            for (var c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                var start = starts[r][c];
                var span = spans[r][c];
                var format = resolved.CellFormat(r, start, rowCount, columns, cell);
                var cellModel = new CellModel { Format = format, Start = start, Span = span, Row = r };
                rowModel.Cells[c] = cellModel;
                cellCount++;
                if (span > 1)
                {
                    var last = resolved.CellFormat(r, start + span - 1, rowCount, columns, cell);
                    cellModel.RightBorder = last.Right;
                }
                else
                {
                    cellModel.RightBorder = format.Right;
                }

                ReadCell(cell, cellModel, resolved);

                // Vertical merge: a restart or a lone continuation heads a group, a continuation joins the one above.
                var merge = cell?.Properties.Get(Ooxml.W + "vMerge");
                var continues = merge is not null && Ooxml.Val(merge) is null or "continue";

                if (continues && open[start] is { } head && head.Row + head.RowSpan == r && head.Span == span)
                {
                    cellModel.Head = head;
                    head.RowSpan++;
                    rowModel.InMerge = true;
                    rowModel.NoCutBefore = true;
                    open[start] = head;
                    seen[start] = true;
                    continue;
                }

                if (merge is not null)
                {
                    // The first cell of a group; its rows are counted as continuations arrive.
                    open[start] = cellModel;
                    rowModel.InMerge = true;
                }
                else
                {
                    open[start] = null;
                }

                seen[start] = true;
                PlanContent(planner, cell, cellModel, bodyIndex, depth);
            }

            // A group that no cell of this row continues is closed.
            for (var k = 0; k < open.Length; k++)
            {
                if (!seen[k])
                {
                    open[k] = null;
                }
            }
        }

        // A group reaching past a row marks every row it covers.
        for (var r = 0; r < rowCount; r++)
        {
            foreach (var cell in rowModels[r].Cells)
            {
                if (cell.RowSpan > 1)
                {
                    for (var k = r; k < Math.Min(r + cell.RowSpan, rowCount); k++)
                    {
                        rowModels[k].InMerge = true;
                    }
                }
            }
        }

        model.Rows = rowModels;
        model.HeaderRows = headers >= rowCount ? 0 : headers;
        model.CellCount = cellCount;
        return new TablePlan(bodyIndex, model, planner.TableTextAtIndent);
    }

    // Marks that sit between rows or cells take no room; any other element the model keeps there (an sdt, a custom XML wrapper) hides content.
    private static readonly HashSet<string> s_marks = new(StringComparer.Ordinal)
    {
        "tblPrEx", "bookmarkStart", "bookmarkEnd", "commentRangeStart", "commentRangeEnd", "permStart", "permEnd", "proofErr",
        "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
    };

    private static bool Takes(UnknownRowItem item) => !s_marks.Contains(item.Content.Element.Name.LocalName);

    private static int? Value(XElement? element) => element is null ? null : Ooxml.IntAttr(element, "val");

    private static void ReadRow(TableRow row, RowModel model)
    {
        var properties = row.Properties;
        if (properties.Get(Ooxml.W + "trHeight") is { } height)
        {
            var value = Ooxml.IntAttr(height, "val") ?? 0;
            model.Height = TableLimits.Length(value / 20.0);
            model.Rule = Ooxml.Attr(height, "hRule") switch
            {
                "exact" => RowHeightRule.Exact,
                "auto" => RowHeightRule.Auto,
                _ => RowHeightRule.AtLeast,
            };
            if (model.Rule == RowHeightRule.Auto)
            {
                model.Height = 0f;
            }
        }

        model.CantSplit = properties.Get(Ooxml.W + "cantSplit") is { } split && Ooxml.OnOff(split);
        model.IsHeader = properties.Get(Ooxml.W + "tblHeader") is { } header && Ooxml.OnOff(header);
    }

    private static void ReadCell(TableCell? cell, CellModel model, ResolvedTable table)
    {
        var properties = cell?.Properties;
        model.Width = PreferredWidth.Read(properties?.Get(Ooxml.W + "tcW"));
        var margins = new[] { table.CellMarginTop, table.CellMarginLeft, table.CellMarginBottom, table.CellMarginRight };
        if (properties?.Get(Ooxml.W + "tcMar") is { } own)
        {
            TableStyleData.ReadMargins(own, margins);
        }

        model.MarginTop = TableLimits.Length(margins[0]);
        model.MarginLeft = TableLimits.Length(margins[1]);
        model.MarginBottom = TableLimits.Length(margins[2]);
        model.MarginRight = TableLimits.Length(margins[3]);
        model.Alignment = properties?.Get(Ooxml.W + "vAlign") is { } align
            ? Ooxml.Val(align) switch
            {
                "center" => CellVerticalAlignment.Center,
                "bottom" => CellVerticalAlignment.Bottom,
                _ => CellVerticalAlignment.Top,
            }
            : CellVerticalAlignment.Top;
    }

    private static void PlanContent(BlockPlanner planner, TableCell? cell, CellModel model, int bodyIndex, int depth)
    {
        if (cell is null)
        {
            model.Content.Add(new PlaceholderPlan(bodyIndex, BlockPlanner.PlaceholderHeight));
            return;
        }

        var format = model.Format;
        foreach (var flat in FlatBlocks.From(cell.Blocks, bodyIndex))
        {
            if (planner.PlanBlock(flat, format, depth + 1) is { } plan)
            {
                model.Content.Add(plan);
            }
        }
    }
}

/// <summary>A table ready to be laid out at whatever width the filler asks for.</summary>
internal sealed class TablePlan : BlockPlan
{
    private (float Min, float Max)? _widths;

    public TablePlan(int bodyIndex, TableModel model, bool textAtIndent)
        : base(bodyIndex)
    {
        Model = model;
        TextAtIndent = textAtIndent;
    }

    public TableModel Model { get; }

    /// <summary>True for a document that places the table's text, not its border, at the indent (compatibility mode below 15): the border then sits one cell margin further left.</summary>
    public bool TextAtIndent { get; }

    protected override IBlockFlow Make(float width) => TableFlow.Create(this, width);

    /// <summary>The plans inside the cells carry the index of the table's body block as well.</summary>
    public override void Rebase(int bodyIndex)
    {
        if (BodyIndex == bodyIndex)
        {
            return;
        }

        base.Rebase(bodyIndex);
        foreach (var row in Model.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var plan in cell.Content)
                {
                    plan.Rebase(bodyIndex);
                }
            }
        }
    }

    public override (float Min, float Max) ContentWidths()
    {
        _widths ??= GridSolver.ContentWidths(Model);
        return _widths.Value;
    }
}
