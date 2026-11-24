using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// Where an object of the body sits on a page: its box, in points from the top left of the page (y down), like every other page item.
/// The map is built with the page from the items the layouter already places and is immutable like the page; a stale page keeps its own map.
/// </summary>
public sealed class ObjectMap
{
    /// <summary>The one map of every page that has no picture and no table.</summary>
    public static ObjectMap Empty { get; } = new([], []);

    internal ObjectMap(PictureBox[] pictures, TableGrid[] tables)
    {
        Pictures = pictures;
        Tables = tables;
    }

    /// <summary>The pictures of the body (and of its table cells) drawn on the page, in the order they were placed. A picture the layout cannot draw (a marked box), a shape, a text box, a chart and every picture in a header, footer or note has no box.</summary>
    public IReadOnlyList<PictureBox> Pictures { get; }

    /// <summary>The tables of the body drawn on the page, one grid per table piece on the page (a table that continues on the next page has one on each). Tables nested in a cell have none.</summary>
    public IReadOnlyList<TableGrid> Tables { get; }

    public bool IsEmpty => Pictures.Count == 0 && Tables.Count == 0;
}

/// <summary>The eight handles of a picture's box, clockwise from the top left corner.</summary>
public enum HandleSlot
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// A picture on a page. <see cref="Id"/> is worked out from the model each time it is read (so it names the drawing as the model is now, even on a page the layout has not refreshed; it walks the body's paragraphs,
/// so keep it for a click, not for every frame): the identity of the drawing in the body. <see cref="Floating"/> is true for an anchored picture (it is drawn
/// inline in the writing view and in table cells all the same). <see cref="Resizable"/> is false for a picture that is turned (<c>a:xfrm rot</c>).
/// Only a plain <c>pic:pic</c> picture of the body gets a box: shapes, text boxes, charts, VML pictures and the pictures in a header, footer or note do not.
/// <see cref="BehindText"/> marks a floating picture drawn behind the text. In <see cref="ObjectMap.Pictures"/> the pictures behind the text come first (paint order), then the others as placed.
/// Two boxes are equal when they are the same drawing at the same place.
/// </summary>
public sealed class PictureBox : IEquatable<PictureBox>
{
    internal PictureBox(PictureAnchor anchor, float x, float y, float width, float height, bool behindText = false)
    {
        Anchor = anchor;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        BehindText = behindText;
    }

    /// <summary>
    /// The identity of the drawing in its story, worked out from the model on every read: it walks the body's paragraphs up to this one, so it is for click, status and command paths only
    /// (never a hit test loop or a frame; <see cref="DocumentLayout.HitObject(int, float, float, float, ObjectId?, bool)"/> compares drawings by object and reads no <see cref="Id"/>). It is
    /// null when the drawing is no longer in the model and also for a paragraph deeper than the depth cap of <see cref="ObjectIds"/> (32 levels of tables and content controls), so a caller must
    /// handle null for a box that exists. An id read on a page the model has moved on from names the place as the model is now.
    /// </summary>
    public ObjectId? Id => Anchor.IdentityOf();

    /// <summary>True for a floating picture drawn behind the text (<c>behindDoc</c>).</summary>
    public bool BehindText { get; }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public bool Floating => Anchor.Floating;

    public bool Resizable => Anchor.Resizable;

    internal PictureAnchor Anchor { get; }

    public bool Equals(PictureBox? other) =>
        other is not null && ReferenceEquals(Anchor.Picture, other.Anchor.Picture) && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height && Floating == other.Floating && Resizable == other.Resizable && BehindText == other.BehindText;

    public override bool Equals(object? obj) => Equals(obj as PictureBox);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>The centre of a handle slot.</summary>
    public (float X, float Y) HandleCentre(HandleSlot slot) => slot switch
    {
        HandleSlot.TopLeft => (X, Y),
        HandleSlot.Top => (X + (Width / 2f), Y),
        HandleSlot.TopRight => (X + Width, Y),
        HandleSlot.Right => (X + Width, Y + (Height / 2f)),
        HandleSlot.BottomRight => (X + Width, Y + Height),
        HandleSlot.Bottom => (X + (Width / 2f), Y + Height),
        HandleSlot.BottomLeft => (X, Y + Height),
        _ => (X, Y + (Height / 2f)),
    };
}

/// <summary>
/// One table piece on a page, in page space (points from the top left, y down). <paramref name="BlockIndex"/> is the table's index in the body's
/// blocks: the index of the TOP-LEVEL body block that is the table or, for a table inside a block-level content control (nested controls included), the control that holds it; two tables of one
/// control share the index and are told apart by their order on the page (and across pages), a table in a cell has no grid. <paramref name="ColumnBorders"/> holds the x of every column border including both outer edges (columns + 1 values, ascending);
/// <paramref name="RowEdges"/> the y of every row edge of the piece including its top and bottom (rows + 1 values): the rows are the rows the
/// piece draws, in order, a header row repeated at the top of a continuation page and the part of a row cut by the page included.
/// <paramref name="Cells"/> are the cell boxes, a merged cell once. The story is always the body.
/// </summary>
public sealed record TableGrid(StoryRef Story, int BlockIndex, float X, float Y, float Width, float Height, IReadOnlyList<float> ColumnBorders, IReadOnlyList<float> RowEdges, IReadOnlyList<CellRect> Cells)
{
    /// <summary>The model row of each row of <see cref="RowEdges"/> (an index into the table's <c>Rows</c>, the same numbers as <see cref="CellRect.Row"/>): one less than the edges. A repeated header row has its own number, so the list is not always ascending.</summary>
    public IReadOnlyList<int> RowIndexes { get; init; } = [];
}

/// <summary>
/// A cell box on a page. <paramref name="Row"/> is the MODEL row: an index into the table's <c>Rows</c> (a row with a tracked deletion is not laid out, so the numbers skip it; a
/// marked unknown row between the rows has its own number) and <paramref name="Column"/> the grid column the cell starts in. A cell merged across rows is one rectangle of
/// the rows it covers on this page (<paramref name="RowSpan"/>); a cell that continues a vertical merge from the page before has its own
/// rectangle on this page, with the row of its first row on the page.
/// </summary>
public sealed record CellRect(int Row, int Column, int RowSpan, int ColumnSpan, float X, float Y, float Width, float Height);

/// <summary>What a row, column or table margin selects.</summary>
public enum TableMarginKind
{
    /// <summary>The margin left of a row.</summary>
    Row,

    /// <summary>The margin above a column.</summary>
    Column,

    /// <summary>The corner above and left of the table.</summary>
    Table,
}

/// <summary>What <see cref="DocumentLayout.HitObject"/> found.</summary>
public abstract record ObjectHit
{
    private protected ObjectHit()
    {
    }

    /// <summary>The picture itself.</summary>
    public sealed record Picture(PictureBox Box) : ObjectHit;

    /// <summary>A handle of the selected picture.</summary>
    public sealed record Handle(PictureBox Box, HandleSlot Slot) : ObjectHit;

    /// <summary>A column border; <paramref name="Index"/> indexes <see cref="TableGrid.ColumnBorders"/>.</summary>
    public sealed record ColumnBorder(TableGrid Grid, int Index) : ObjectHit;

    /// <summary>A row edge; <paramref name="Index"/> indexes <see cref="TableGrid.RowEdges"/>.</summary>
    public sealed record RowEdge(TableGrid Grid, int Index) : ObjectHit;

    /// <summary>A cell.</summary>
    public sealed record Cell(TableGrid Grid, CellRect Rect) : ObjectHit;

    /// <summary>A margin of the table: <paramref name="Index"/> is the row of the table for <see cref="TableMarginKind.Row"/>, the grid column for <see cref="TableMarginKind.Column"/> and 0 for the corner.</summary>
    public sealed record Margin(TableGrid Grid, TableMarginKind Kind, int Index) : ObjectHit;
}

/// <summary>Where a picture of the body comes from, kept by the layouter while it plans the paragraph (no page yet).</summary>
internal sealed record PictureAnchor(StoryRef Story, Paragraph Paragraph, Run Run, Picture Picture, bool Floating, bool Resizable, Func<IList<Block>?>? Blocks)
{
    [ThreadStatic]
    private static long s_identityReads;

    /// <summary>How many identities this thread worked out (each one a walk of the model): a test seam that proves the hit test makes none.</summary>
    public static long IdentityReads => s_identityReads;

    public ObjectId? IdentityOf()
    {
        s_identityReads++;
        return Blocks?.Invoke() is { } blocks ? ObjectIds.Of(Story, blocks, Paragraph, Run, Picture) : null;
    }
}

/// <summary>Hit testing of an <see cref="ObjectMap"/>: pure geometry on a page.</summary>
internal static class ObjectHits
{
    private const float MarginFactor = 3f;

    /// <summary>
    /// Hit test of one page's map. <paramref name="selected"/> resolves the selected picture to its drawing; it is called at most once, and only when the page has a resizable picture
    /// (so a page of many pictures and a call without a selection read no model). Boxes are compared with the drawing by object, never by <see cref="PictureBox.Id"/>.
    /// </summary>
    public static ObjectHit? Test(ObjectMap map, float x, float y, float tolerance, Func<Picture?>? selected, bool includeBehindText)
    {
        if (map.IsEmpty)
        {
            return null;
        }

        tolerance = Math.Max(0f, tolerance);
        if (selected is not null && map.Pictures.Any(p => p.Resizable) && selected() is { } drawing)
        {
            foreach (var box in map.Pictures)
            {
                if (box.Resizable && ReferenceEquals(box.Anchor.Picture, drawing) && HandleAt(box, x, y, tolerance) is { } slot)
                {
                    return new ObjectHit.Handle(box, slot);
                }
            }
        }

        // The last box is the frontmost; a picture behind the text is not hit unless asked for, and then only where nothing else is.
        for (var i = map.Pictures.Count - 1; i >= 0; i--)
        {
            var box = map.Pictures[i];
            if (!box.BehindText && Contains(box, x, y))
            {
                return new ObjectHit.Picture(box);
            }
        }

        for (var i = map.Tables.Count - 1; i >= 0; i--)
        {
            if (TestTable(map.Tables[i], x, y, tolerance) is { } hit)
            {
                return hit;
            }
        }

        if (includeBehindText)
        {
            for (var i = map.Pictures.Count - 1; i >= 0; i--)
            {
                var box = map.Pictures[i];
                if (box.BehindText && Contains(box, x, y))
                {
                    return new ObjectHit.Picture(box);
                }
            }
        }

        return null;
    }

    private static bool Contains(PictureBox box, float x, float y) => x >= box.X && x <= box.X + box.Width && y >= box.Y && y <= box.Y + box.Height;

    private static HandleSlot? HandleAt(PictureBox box, float x, float y, float tolerance)
    {
        HandleSlot? best = null;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < 8; index++)
        {
            var slot = (HandleSlot)index;
            var (cx, cy) = box.HandleCentre(slot);
            var dx = Math.Abs(x - cx);
            var dy = Math.Abs(y - cy);
            if (dx <= tolerance && dy <= tolerance && dx + dy < bestDistance)
            {
                best = slot;
                bestDistance = dx + dy;
            }
        }

        return best;
    }

    private static ObjectHit? TestTable(TableGrid grid, float x, float y, float tolerance)
    {
        var right = grid.X + grid.Width;
        var bottom = grid.Y + grid.Height;
        var reach = tolerance * MarginFactor;
        if (x < grid.X - reach || x > right + tolerance || y < grid.Y - reach || y > bottom + tolerance)
        {
            return null;
        }

        // A border within the tolerance wins over the cell it separates; of a column border and a row edge the nearer one.
        var columnDistance = float.MaxValue;
        var columnIndex = -1;
        if (y >= grid.Y - tolerance && y <= bottom + tolerance)
        {
            for (var i = 0; i < grid.ColumnBorders.Count; i++)
            {
                var distance = Math.Abs(x - grid.ColumnBorders[i]);
                if (distance <= tolerance && distance < columnDistance)
                {
                    columnDistance = distance;
                    columnIndex = i;
                }
            }
        }

        var rowDistance = float.MaxValue;
        var rowIndex = -1;
        if (x >= grid.X - tolerance && x <= right + tolerance)
        {
            for (var i = 0; i < grid.RowEdges.Count; i++)
            {
                var distance = Math.Abs(y - grid.RowEdges[i]);
                if (distance <= tolerance && distance < rowDistance)
                {
                    rowDistance = distance;
                    rowIndex = i;
                }
            }
        }

        if (columnIndex >= 0 && (rowIndex < 0 || columnDistance <= rowDistance))
        {
            return new ObjectHit.ColumnBorder(grid, columnIndex);
        }

        if (rowIndex >= 0)
        {
            return new ObjectHit.RowEdge(grid, rowIndex);
        }

        var left = x < grid.X;
        var above = y < grid.Y;
        if (left && above)
        {
            return new ObjectHit.Margin(grid, TableMarginKind.Table, 0);
        }

        if (left)
        {
            return RowAt(grid, y) is { } row ? new ObjectHit.Margin(grid, TableMarginKind.Row, row) : null;
        }

        if (above)
        {
            return x <= right && ColumnAt(grid, x) is { } column ? new ObjectHit.Margin(grid, TableMarginKind.Column, column) : null;
        }

        foreach (var cell in grid.Cells)
        {
            if (x >= cell.X && x <= cell.X + cell.Width && y >= cell.Y && y <= cell.Y + cell.Height)
            {
                return new ObjectHit.Cell(grid, cell);
            }
        }

        return null;
    }

    // The row of the table that covers y (rows lie between consecutive edges).
    private static int? RowAt(TableGrid grid, float y)
    {
        for (var i = 0; i + 1 < grid.RowEdges.Count && i < grid.RowIndexes.Count; i++)
        {
            if (y >= grid.RowEdges[i] && y <= grid.RowEdges[i + 1])
            {
                return grid.RowIndexes[i];
            }
        }

        return null;
    }

    private static int? ColumnAt(TableGrid grid, float x)
    {
        for (var i = 0; i + 1 < grid.ColumnBorders.Count; i++)
        {
            if (x >= grid.ColumnBorders[i] && x <= grid.ColumnBorders[i + 1])
            {
                return i;
            }
        }

        return null;
    }
}
