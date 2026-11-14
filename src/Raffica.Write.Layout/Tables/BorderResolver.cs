namespace Raffica.Write.Layout;

/// <summary>One row, or one cut of a row, placed on the page: where it is and what its cells show.</summary>
internal readonly record struct RowRun(RowLayout Row, CellStack?[] Stacks, float Y, float Height, bool Whole);

/// <summary>A cell as it is drawn: its rectangle (a vertical merge makes it taller than a row), what it shows and its four borders.</summary>
internal sealed class DrawBox
{
    public DrawBox(CellModel cell, float x0, float x1, float y0, float y1, CellStack? stack, bool whole)
    {
        Cell = cell;
        X0 = x0;
        X1 = x1;
        Y0 = y0;
        Y1 = y1;
        Stack = stack;
        Whole = whole;
        Bottom = cell.Format.Bottom;
    }

    public CellModel Cell { get; }

    public float X0 { get; }

    public float X1 { get; }

    public float Y0 { get; }

    public float Y1 { get; set; }

    public CellStack? Stack { get; }

    /// <summary>True when <see cref="Stack"/> is all of the cell's content (it may then be centred or bottom aligned).</summary>
    public bool Whole { get; }

    public ResolvedBorder? Bottom { get; set; }

    public ResolvedBorder? Top => Cell.Format.Top;

    public ResolvedBorder? Left => Cell.Format.Left;

    public ResolvedBorder? Right => Cell.RightBorder;
}

/// <summary>
/// Draws the part of a table that is on a page: the shading of each cell, then the content, then the borders. A border shared by two
/// cells is drawn once, as the wider of the two (the darker one when they are as wide), and runs of equal border along one line
/// are one rule.
/// </summary>
internal static class TableDrawing
{
    private const uint Black = 0xFF000000;
    private const float MinimumWidth = 0.25f;
    private const float Tolerance = 0.001f;

    public static void Emit(PageBuilder page, float left, IReadOnlyList<RowRun> runs, IReadOnlyList<float> columns)
    {
        var boxes = Boxes(left, runs, columns);
        foreach (var box in boxes)
        {
            if (box.Cell.Format.Fill is { } fill && box.X1 > box.X0 && box.Y1 > box.Y0)
            {
                page.Add(new FillItem(box.X0, box.Y0, box.X1 - box.X0, box.Y1 - box.Y0, fill));
            }
        }

        foreach (var box in boxes)
        {
            if (box.Stack is not { HasContent: true } stack)
            {
                continue;
            }

            var cell = box.Cell;
            var offset = 0f;
            if (box.Whole && cell.Alignment != CellVerticalAlignment.Top)
            {
                var spare = Math.Max(box.Y1 - box.Y0 - (stack.Height + cell.MarginTop + cell.MarginBottom), 0f);
                offset = cell.Alignment == CellVerticalAlignment.Center ? spare / 2f : spare;
            }

            stack.Emit(page, box.X0 + cell.MarginLeft, box.Y0 + cell.MarginTop + offset);
        }

        EmitBorders(page, boxes);
    }

    private static List<DrawBox> Boxes(float left, IReadOnlyList<RowRun> runs, IReadOnlyList<float> columns)
    {
        var boxes = new List<DrawBox>();
        var open = new Dictionary<int, DrawBox>();
        var previous = int.MinValue;
        foreach (var run in runs)
        {
            if (run.Row.Index != previous + 1)
            {
                open.Clear();
            }

            previous = run.Row.Index;
            var y1 = run.Y + run.Height;
            var cells = run.Row.Cells;
            for (var k = 0; k < cells.Length; k++)
            {
                var cell = cells[k].Model;
                if (cell.IsContinuation && open.TryGetValue(cell.Start, out var above) && above.X0 == left + columns[cell.Start])
                {
                    above.Y1 = y1;
                    above.Bottom = cell.Format.Bottom;
                    continue;
                }

                var stack = cell.IsContinuation ? null : k < run.Stacks.Length ? run.Stacks[k] : null;
                var box = new DrawBox(cell, left + columns[cell.Start], left + columns[cell.Start + cell.Span], run.Y, y1, stack, run.Whole);
                boxes.Add(box);
                if (cell.RowSpan > 1 || cell.IsContinuation)
                {
                    open[cell.Start] = box;
                }
                else
                {
                    open.Remove(cell.Start);
                }
            }
        }

        return boxes;
    }

    /// <summary>Of two borders that meet on one edge: the wider one, the darker one when they are as wide; null when neither is there.</summary>
    internal static ResolvedBorder? Pick(ResolvedBorder? a, ResolvedBorder? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        if (MathF.Abs(a.WidthPoints - b.WidthPoints) > Tolerance)
        {
            return a.WidthPoints > b.WidthPoints ? a : b;
        }

        return Darkness(b) > Darkness(a) ? b : a;
    }

    private static float Darkness(ResolvedBorder border)
    {
        var color = border.Color ?? Black;
        var red = (color >> 16) & 0xFF;
        var green = (color >> 8) & 0xFF;
        var blue = color & 0xFF;
        return 765f - red - green - blue;
    }

    private readonly record struct Segment(float Start, float End, ResolvedBorder? Border);

    private sealed class Edge
    {
        public List<Segment> Before { get; } = [];

        public List<Segment> After { get; } = [];
    }

    private static void EmitBorders(PageBuilder page, List<DrawBox> boxes)
    {
        var vertical = new SortedDictionary<long, Edge>();
        var horizontal = new SortedDictionary<long, Edge>();
        foreach (var box in boxes)
        {
            Add(vertical, box.X0, new Segment(box.Y0, box.Y1, box.Left), after: true);
            Add(vertical, box.X1, new Segment(box.Y0, box.Y1, box.Right), after: false);
            Add(horizontal, box.Y0, new Segment(box.X0, box.X1, box.Top), after: true);
            Add(horizontal, box.Y1, new Segment(box.X0, box.X1, box.Bottom), after: false);
        }

        foreach (var (key, edge) in horizontal)
        {
            foreach (var (start, end, border) in Resolve(edge))
            {
                Rule(page, false, key / 100f, start, end, border);
            }
        }

        foreach (var (key, edge) in vertical)
        {
            foreach (var (start, end, border) in Resolve(edge))
            {
                Rule(page, true, key / 100f, start, end, border);
            }
        }
    }

    private static void Add(SortedDictionary<long, Edge> edges, float at, Segment segment, bool after)
    {
        if (!float.IsFinite(at) || segment.End - segment.Start <= Tolerance)
        {
            return;
        }

        var key = (long)MathF.Round(at * 100f);
        if (!edges.TryGetValue(key, out var edge))
        {
            edge = new Edge();
            edges[key] = edge;
        }

        (after ? edge.After : edge.Before).Add(segment);
    }

    // Splits the line at every start and end of a segment, takes the border of each piece and joins neighbours that are the same.
    private static List<Segment> Resolve(Edge edge)
    {
        var cuts = new List<float>();
        foreach (var s in edge.Before)
        {
            cuts.Add(s.Start);
            cuts.Add(s.End);
        }

        foreach (var s in edge.After)
        {
            cuts.Add(s.Start);
            cuts.Add(s.End);
        }

        cuts.Sort();
        var result = new List<Segment>();
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var a = cuts[i];
            var b = cuts[i + 1];
            if (b - a <= Tolerance)
            {
                continue;
            }

            var middle = (a + b) / 2f;
            var border = Pick(Find(edge.Before, middle), Find(edge.After, middle));
            if (border is null)
            {
                continue;
            }

            if (result.Count > 0 && result[^1].Border == border && MathF.Abs(result[^1].End - a) <= Tolerance)
            {
                result[^1] = result[^1] with { End = b };
            }
            else
            {
                result.Add(new Segment(a, b, border));
            }
        }

        return result;
    }

    private static ResolvedBorder? Find(List<Segment> segments, float at)
    {
        foreach (var s in segments)
        {
            if (at > s.Start && at < s.End)
            {
                return s.Border;
            }
        }

        return null;
    }

    private static void Rule(PageBuilder page, bool vertical, float at, float start, float end, ResolvedBorder? border)
    {
        if (border is null)
        {
            return;
        }

        var width = Math.Max(border.WidthPoints, MinimumWidth);
        var color = border.Color ?? Black;
        var lines = border.Style == BorderLineStyle.DoubleLine ? new[] { -width, width } : new[] { 0f };
        foreach (var shift in lines)
        {
            var center = at + shift;
            page.Add(vertical
                ? new RuleItem(center - (width / 2f), start, width, end - start, color)
                : new RuleItem(start, center - (width / 2f), end - start, width, color));
        }
    }
}
