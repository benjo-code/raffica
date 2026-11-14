using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

public enum BorderLineStyle
{
    None,
    Solid,
    DoubleLine,
    Dotted,
    Dashed,
    Thick,
}

/// <summary>One border line: width in points, colour 0xAARRGGBB or null for automatic (black).</summary>
public sealed record ResolvedBorder(BorderLineStyle Style, float WidthPoints, uint? Color);

/// <summary>What a table style (and the cell's own properties) say about one cell: its fill and its four borders.</summary>
public sealed class ResolvedCellFormat
{
    internal ResolvedCellFormat(CellLayer layer, uint? fill, ResolvedBorder? top, ResolvedBorder? left, ResolvedBorder? bottom, ResolvedBorder? right)
    {
        Layer = layer;
        Fill = fill;
        Top = top;
        Left = left;
        Bottom = bottom;
        Right = right;
    }

    /// <summary>The background colour as 0xAARRGGBB, or null for none.</summary>
    public uint? Fill { get; }

    public ResolvedBorder? Top { get; }

    public ResolvedBorder? Left { get; }

    public ResolvedBorder? Bottom { get; }

    public ResolvedBorder? Right { get; }

    internal CellLayer Layer { get; }
}

/// <summary>A table's resolved properties: the cell margins and a way to ask for the format of each cell. Direct cell properties are passed in by the caller of <see cref="CellFormat"/>.</summary>
public sealed class ResolvedTable
{
    // Slots of the table borders: top, left, bottom, right, inside horizontal, inside vertical.
    private readonly ResolvedBorder?[] _direct = new ResolvedBorder?[6];
    private readonly TableStyleData _style;
    private readonly bool _hasDirectBorders;

    internal ResolvedTable(TableStyleData style, PropertyBag direct)
    {
        _style = style;
        StyleId = direct.StyleId;
        var margins = (float[])style.Margins.Clone();
        if (direct.Get(Ooxml.W + "tblCellMar") is { } mar)
        {
            TableStyleData.ReadMargins(mar, margins);
        }

        CellMarginTop = margins[0];
        CellMarginLeft = margins[1];
        CellMarginBottom = margins[2];
        CellMarginRight = margins[3];
        if (direct.Get(Ooxml.W + "tblBorders") is { } borders)
        {
            TableStyleData.ReadBorders(borders, _direct, table: true);
            _hasDirectBorders = true;
        }

        Look = TableStyleData.ReadLook(direct.Get(Ooxml.W + "tblLook"));
    }

    public string? StyleId { get; }

    public float CellMarginTop { get; }

    public float CellMarginLeft { get; }

    public float CellMarginBottom { get; }

    public float CellMarginRight { get; }

    internal TableLook Look { get; }

    /// <summary>
    /// The format of the cell at a row and a column of the grid (zero based; <paramref name="rowCount"/> and <paramref name="columnCount"/>
    /// say where the last row and column are). <paramref name="cell"/> adds the cell's own shading and borders on top of the style's.
    /// </summary>
    public ResolvedCellFormat CellFormat(int row, int column, int rowCount, int columnCount, TableCell? cell = null)
    {
        var layer = _style.LayerFor(Look, row, column, rowCount, columnCount);
        var tbl = layer.Table;
        ResolvedBorder? Slot(int slot) => _hasDirectBorders && _direct[slot] is { } direct ? direct : tbl[slot];

        var top = layer.Cell[0] ?? Slot(row <= 0 ? 0 : 4);
        var left = layer.Cell[1] ?? Slot(column <= 0 ? 1 : 5);
        var bottom = layer.Cell[2] ?? Slot(row >= rowCount - 1 ? 2 : 4);
        var right = layer.Cell[3] ?? Slot(column >= columnCount - 1 ? 3 : 5);
        var fill = layer.Fill;
        if (cell is not null)
        {
            var properties = cell.Properties;
            if (properties.Get(Ooxml.W + "shd") is { } shading)
            {
                fill = TableStyleData.ReadFill(shading);
            }

            if (properties.Get(Ooxml.W + "tcBorders") is { } cellBorders)
            {
                var own = new ResolvedBorder?[4];
                TableStyleData.ReadBorders(cellBorders, own, table: false);
                top = own[0] ?? top;
                left = own[1] ?? left;
                bottom = own[2] ?? bottom;
                right = own[3] ?? right;
            }
        }

        return new ResolvedCellFormat(layer, fill is >= 0 ? (uint)fill.Value : null, Visible(top), Visible(left), Visible(bottom), Visible(right));
    }

    private static ResolvedBorder? Visible(ResolvedBorder? border) => border is { Style: BorderLineStyle.None } ? null : border;
}

/// <summary>The enabled conditional regions of a table (<c>w:tblLook</c>).</summary>
internal readonly record struct TableLook(bool FirstRow, bool LastRow, bool FirstColumn, bool LastColumn, bool NoHorizontalBands, bool NoVerticalBands);

/// <summary>The properties one style (or one conditional region of a style) contributes, as the elements it holds.</summary>
internal sealed record StyleSource(IReadOnlyList<XElement> Paragraph, IReadOnlyList<XElement> Run, IReadOnlyList<XElement> Cell, IReadOnlyList<XElement> Table);

/// <summary>The table-style part of one cell: what the style chain says for the conditions that apply. Cached with the paragraphs resolved inside it.</summary>
internal sealed class CellLayer
{
    public List<XElement> Paragraph { get; } = [];

    public RunProps Run { get; } = new();

    public long? Fill { get; set; }

    public ResolvedBorder?[] Table { get; } = new ResolvedBorder?[6];

    public ResolvedBorder?[] Cell { get; } = new ResolvedBorder?[4];

    public Dictionary<string, ResolvedParagraph> Paragraphs { get; } = [];
}

/// <summary>A table style's chain, read once: the base sources, the conditional ones and the cell margins; layers are cached per combination of conditions.</summary>
internal sealed class TableStyleData
{
    private static readonly string[] s_conditions =
    [
        "band1Vert", "band2Vert", "band1Horz", "band2Horz", "firstCol", "lastCol", "firstRow", "lastRow", "neCell", "nwCell", "seCell", "swCell",
    ];

    private readonly List<StyleSource> _base = [];
    private readonly Dictionary<string, List<StyleSource>> _conditional = [];
    private readonly Dictionary<int, CellLayer> _layers = [];

    public TableStyleData(IReadOnlyList<Style> chain)
    {
        foreach (var style in chain)
        {
            _base.Add(new StyleSource(
                style.ParagraphProperties.Children,
                style.RunProperties.Children,
                style.KeptChildren.FirstOrDefault(k => k.Content.Element.Name == Ooxml.W + "tcPr")?.Content.Element.Elements().ToList() ?? [],
                style.TableProperties.Children));
            foreach (var kept in style.KeptChildren)
            {
                var element = kept.Content.Element;
                if (element.Name != Ooxml.W + "tblStylePr" || Ooxml.Attr(element, "type") is not { } type)
                {
                    continue;
                }

                if (!_conditional.TryGetValue(type, out var list))
                {
                    list = [];
                    _conditional[type] = list;
                }

                list.Add(new StyleSource(
                    element.Element(Ooxml.W + "pPr")?.Elements().ToList() ?? [],
                    element.Element(Ooxml.W + "rPr")?.Elements().ToList() ?? [],
                    element.Element(Ooxml.W + "tcPr")?.Elements().ToList() ?? [],
                    element.Element(Ooxml.W + "tblPr")?.Elements().ToList() ?? []));
            }
        }

        // Word's built-in default: no top and bottom margin, 0.075 inch left and right.
        Margins = [0f, 5.4f, 0f, 5.4f];
        foreach (var source in _base)
        {
            if (source.Table.FirstOrDefault(e => e.Name == Ooxml.W + "tblCellMar") is { } margins)
            {
                ReadMargins(margins, Margins);
            }
        }
    }

    /// <summary>Top, left, bottom and right cell margin in points.</summary>
    public float[] Margins { get; }

    public static TableLook ReadLook(XElement? look)
    {
        if (look is null)
        {
            return default;
        }

        var value = 0;
        if (Ooxml.Attr(look, "val") is { Length: > 0 } hex)
        {
            _ = int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        bool Flag(string name, int bit) => Ooxml.Attr(look, name) is { } text ? text is "1" or "true" or "on" : (value & bit) != 0;
        return new TableLook(
            Flag("firstRow", 0x0020),
            Flag("lastRow", 0x0040),
            Flag("firstColumn", 0x0080),
            Flag("lastColumn", 0x0100),
            Flag("noHBand", 0x0200),
            Flag("noVBand", 0x0400));
    }

    public static void ReadMargins(XElement margins, float[] target)
    {
        foreach (var side in margins.Elements())
        {
            if (side.Name.Namespace != Ooxml.W || Ooxml.IntAttr(side, "w") is not { } width)
            {
                continue;
            }

            var slot = side.Name.LocalName switch
            {
                "top" => 0,
                "left" or "start" => 1,
                "bottom" => 2,
                "right" or "end" => 3,
                _ => -1,
            };
            if (slot >= 0)
            {
                target[slot] = Ooxml.Points(Math.Max(width, 0));
            }
        }
    }

    /// <summary>Reads the border children of a <c>w:tblBorders</c> (six slots) or a <c>w:tcBorders</c> (four slots) into the target; a slot the element does not name stays as it is.</summary>
    public static void ReadBorders(XElement borders, ResolvedBorder?[] target, bool table)
    {
        foreach (var side in borders.Elements())
        {
            if (side.Name.Namespace != Ooxml.W)
            {
                continue;
            }

            var slot = side.Name.LocalName switch
            {
                "top" => 0,
                "left" or "start" => 1,
                "bottom" => 2,
                "right" or "end" => 3,
                "insideH" when table => 4,
                "insideV" when table => 5,
                _ => -1,
            };
            if (slot < 0)
            {
                continue;
            }

            var style = Ooxml.Val(side) switch
            {
                null or "nil" or "none" => BorderLineStyle.None,
                "double" or "triple" or "thinThickSmallGap" or "thickThinSmallGap" or "thinThickThinSmallGap" => BorderLineStyle.DoubleLine,
                "dotted" => BorderLineStyle.Dotted,
                "dashed" or "dashSmallGap" or "dotDash" or "dotDotDash" => BorderLineStyle.Dashed,
                "thick" => BorderLineStyle.Thick,
                _ => BorderLineStyle.Solid,
            };
            // w:sz is in eighths of a point; an unstated width is the thinnest line Word draws.
            var width = (Ooxml.IntAttr(side, "sz") ?? 4) / 8f;
            target[slot] = new ResolvedBorder(style, width, Ooxml.Rgb(Ooxml.Attr(side, "color")));
        }
    }

    /// <summary>The fill of a <c>w:shd</c> as 0xAARRGGBB, or -1 for "no fill".</summary>
    public static long ReadFill(XElement shading) =>
        Ooxml.Val(shading) is "nil" ? -1 : Ooxml.Rgb(Ooxml.Attr(shading, "fill")) is { } rgb ? rgb : -1;

    public CellLayer LayerFor(TableLook look, int row, int column, int rowCount, int columnCount)
    {
        var mask = 0;
        void Add(string name) => mask |= 1 << Array.IndexOf(s_conditions, name);

        var firstRow = look.FirstRow && row == 0;
        var lastRow = look.LastRow && row == rowCount - 1 && !firstRow;
        var firstColumn = look.FirstColumn && column == 0;
        var lastColumn = look.LastColumn && column == columnCount - 1 && !firstColumn;
        if (!look.NoHorizontalBands && !firstRow && !lastRow)
        {
            Add(((row - (look.FirstRow ? 1 : 0)) & 1) == 0 ? "band1Horz" : "band2Horz");
        }

        if (!look.NoVerticalBands && !firstColumn && !lastColumn)
        {
            Add(((column - (look.FirstColumn ? 1 : 0)) & 1) == 0 ? "band1Vert" : "band2Vert");
        }

        if (firstColumn)
        {
            Add("firstCol");
        }

        if (lastColumn)
        {
            Add("lastCol");
        }

        if (firstRow)
        {
            Add("firstRow");
        }

        if (lastRow)
        {
            Add("lastRow");
        }

        if (firstRow && lastColumn)
        {
            Add("neCell");
        }

        if (firstRow && firstColumn)
        {
            Add("nwCell");
        }

        if (lastRow && lastColumn)
        {
            Add("seCell");
        }

        if (lastRow && firstColumn)
        {
            Add("swCell");
        }

        if (!_layers.TryGetValue(mask, out var layer))
        {
            layer = Build(mask);
            _layers[mask] = layer;
        }

        return layer;
    }

    private CellLayer Build(int mask)
    {
        var layer = new CellLayer();
        foreach (var source in _base)
        {
            Apply(layer, source);
        }

        for (var i = 0; i < s_conditions.Length; i++)
        {
            if ((mask & (1 << i)) != 0 && _conditional.TryGetValue(s_conditions[i], out var sources))
            {
                foreach (var source in sources)
                {
                    Apply(layer, source);
                }
            }
        }

        return layer;
    }

    private static void Apply(CellLayer layer, StyleSource source)
    {
        layer.Paragraph.AddRange(source.Paragraph);
        layer.Run.Overlay(source.Run);
        foreach (var element in source.Cell)
        {
            if (element.Name == Ooxml.W + "shd")
            {
                layer.Fill = ReadFill(element);
            }
            else if (element.Name == Ooxml.W + "tcBorders")
            {
                ReadBorders(element, layer.Cell, table: false);
            }
        }

        foreach (var element in source.Table)
        {
            if (element.Name == Ooxml.W + "tblBorders")
            {
                ReadBorders(element, layer.Table, table: true);
            }
        }
    }
}
