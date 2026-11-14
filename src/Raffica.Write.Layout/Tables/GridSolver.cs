namespace Raffica.Write.Layout;

/// <summary>
/// Works out the column widths of a table at the width it may take. Fixed tables follow the grid; automatic ones start from the
/// preferred widths of the cells (or the grid) and take the width of their content where nothing else is said; a table wider than
/// the room is shrunk to it, as far as the longest words allow and then in proportion. The result is always finite and never
/// negative, whatever the file states.
/// </summary>
internal static class GridSolver
{
    /// <summary>Tables with more cells than this are not measured by content: measuring lays out every cell twice.</summary>
    public const int MeasureBudget = 4000;

    private const float Epsilon = 0.01f;
    private const float EmptyColumn = 4f;

    /// <summary>The column widths in points for a table that may be at most <paramref name="available"/> points wide.</summary>
    public static float[] Solve(TableModel model, float available)
    {
        ArgumentNullException.ThrowIfNull(model);
        var count = model.Columns;
        if (count == 0)
        {
            return [];
        }

        available = float.IsFinite(available) && available >= 1f ? Math.Min(available, TableLimits.MaximumLength) : 1f;
        var target = model.Width.Kind switch
        {
            WidthKind.Dxa => Math.Min(model.Width.Value, TableLimits.MaximumLength),
            WidthKind.Pct => TableLimits.Length(model.Width.Value * available),
            _ => 0f,
        };
        var hasTarget = target >= 1f;
        var room = hasTarget ? target : available;
        var preferred = PreferredWidths(model, room);
        var widths = new float[count];
        var unknown = false;
        for (var c = 0; c < count; c++)
        {
            var grid = model.Grid[c];
            var value = float.NaN;
            if (model.IsFixed)
            {
                value = grid > 0f ? grid : preferred[c];
            }
            else
            {
                value = !float.IsNaN(preferred[c]) ? preferred[c] : grid > 0f ? grid : float.NaN;
            }

            widths[c] = value;
            unknown |= float.IsNaN(value);
        }

        var open = unknown ? widths.Select(float.IsNaN).ToArray() : [];
        if (unknown)
        {
            FillUnknown(model, widths, room);
        }

        var total = Sum(widths);
        if (hasTarget && total > 0f && MathF.Abs(total - target) > Epsilon)
        {
            if (!FitOpenColumns(widths, open, target))
            {
                Scale(widths, target / total);
            }

            total = Sum(widths);
        }

        if (total > available + Epsilon)
        {
            Shrink(model, widths, total, available);
        }

        for (var c = 0; c < count; c++)
        {
            widths[c] = float.IsFinite(widths[c]) ? Math.Max(widths[c], 0f) : 0f;
        }

        return widths;
    }

    /// <summary>The narrowest and the widest the whole table gets, for a table inside a cell of an automatic table.</summary>
    public static (float Min, float Max) ContentWidths(TableModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var count = model.Columns;
        if (count == 0 || model.CellCount > MeasureBudget)
        {
            return (0f, 0f);
        }

        if (model.IsFixed && model.Grid.All(g => g > 0f))
        {
            var fixedTotal = TableLimits.Length(Sum(model.Grid));
            return (fixedTotal, fixedTotal);
        }

        var min = new float[count];
        var max = new float[count];
        foreach (var row in model.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (cell.IsContinuation || cell.Span != 1)
                {
                    continue;
                }

                min[cell.Start] = Math.Max(min[cell.Start], cell.MinWidth);
                max[cell.Start] = Math.Max(max[cell.Start], cell.MaxWidth);
            }
        }

        float lo = 0f, hi = 0f;
        for (var c = 0; c < count; c++)
        {
            var wide = Math.Max(max[c], min[c]);
            if (model.Grid[c] > 0f && !model.IsFixed)
            {
                wide = Math.Max(wide, model.Grid[c]);
            }

            lo += min[c];
            hi += wide;
        }

        return (TableLimits.Length(lo), TableLimits.Length(hi));
    }

    // The widest preferred width stated by a cell that sits in one column; NaN where no cell states one.
    private static float[] PreferredWidths(TableModel model, float room)
    {
        var preferred = new float[model.Columns];
        Array.Fill(preferred, float.NaN);
        foreach (var row in model.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (cell.IsContinuation || cell.Span != 1 || cell.Width.Kind == WidthKind.Auto)
                {
                    continue;
                }

                var value = cell.Width.Kind == WidthKind.Dxa ? cell.Width.Value : TableLimits.Length(cell.Width.Value * room);
                if (value >= 1f)
                {
                    preferred[cell.Start] = float.IsNaN(preferred[cell.Start]) ? value : Math.Max(preferred[cell.Start], value);
                }
            }
        }

        return preferred;
    }

    // Columns without any stated width: fixed tables share what is left; automatic ones take the width of their content.
    private static void FillUnknown(TableModel model, float[] widths, float room)
    {
        var count = widths.Length;
        var measured = !model.IsFixed && model.CellCount <= MeasureBudget;
        if (measured)
        {
            var content = new float[count];
            foreach (var row in model.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (!cell.IsContinuation && cell.Span == 1 && float.IsNaN(widths[cell.Start]))
                    {
                        content[cell.Start] = Math.Max(content[cell.Start], cell.MaxWidth);
                    }
                }
            }

            for (var c = 0; c < count; c++)
            {
                if (float.IsNaN(widths[c]))
                {
                    widths[c] = content[c];
                }
            }

            // A cell that spans columns asks for more than they have: the columns that had nothing to say share the difference.
            foreach (var row in model.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell.IsContinuation || cell.Span == 1)
                    {
                        continue;
                    }

                    var current = 0f;
                    var empty = 0;
                    for (var c = cell.Start; c < cell.Start + cell.Span; c++)
                    {
                        current += widths[c];
                        empty += content[c] <= 0f && model.Grid[c] <= 0f ? 1 : 0;
                    }

                    if (cell.MaxWidth > current && empty > 0)
                    {
                        var extra = (cell.MaxWidth - current) / empty;
                        for (var c = cell.Start; c < cell.Start + cell.Span; c++)
                        {
                            if (content[c] <= 0f && model.Grid[c] <= 0f)
                            {
                                widths[c] += extra;
                            }
                        }
                    }
                }
            }

            for (var c = 0; c < count; c++)
            {
                widths[c] = Math.Max(widths[c], EmptyColumn);
            }

            return;
        }

        var known = 0f;
        var unknown = 0;
        foreach (var value in widths)
        {
            if (float.IsNaN(value))
            {
                unknown++;
            }
            else
            {
                known += value;
            }
        }

        var share = Math.Max(room - known, 0f) / Math.Max(unknown, 1);
        if (share < EmptyColumn)
        {
            share = Math.Max(room / count, EmptyColumn);
        }

        for (var c = 0; c < count; c++)
        {
            if (float.IsNaN(widths[c]))
            {
                widths[c] = share;
            }
        }
    }

    // Too wide: automatic tables give up what is above the longest word of each column first, then everything gives up in proportion.
    private static void Shrink(TableModel model, float[] widths, float total, float available)
    {
        var count = widths.Length;
        if (!model.IsFixed && model.CellCount <= MeasureBudget)
        {
            var min = new float[count];
            foreach (var row in model.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (!cell.IsContinuation && cell.Span == 1)
                    {
                        min[cell.Start] = Math.Max(min[cell.Start], cell.MinWidth);
                    }
                }
            }

            var slack = 0f;
            for (var c = 0; c < count; c++)
            {
                min[c] = Math.Min(min[c], widths[c]);
                slack += widths[c] - min[c];
            }

            var need = total - available;
            if (slack >= need && slack > 0f)
            {
                var factor = need / slack;
                for (var c = 0; c < count; c++)
                {
                    widths[c] -= (widths[c] - min[c]) * factor;
                }

                return;
            }

            // Even the longest words do not fit: scale what they ask for.
            var minimum = Sum(min);
            if (minimum > 0f)
            {
                for (var c = 0; c < count; c++)
                {
                    widths[c] = min[c] * (available / minimum);
                }

                return;
            }
        }

        Scale(widths, available / total);
    }

    // A table of a stated width: the columns that stated nothing take what the others leave, as long as that is room for them.
    private static bool FitOpenColumns(float[] widths, bool[] open, float target)
    {
        if (open.Length == 0)
        {
            return false;
        }

        float known = 0f, opened = 0f;
        var count = 0;
        for (var c = 0; c < widths.Length; c++)
        {
            if (open[c])
            {
                opened += widths[c];
                count++;
            }
            else
            {
                known += widths[c];
            }
        }

        var rest = target - known;
        if (count == 0 || rest < count * EmptyColumn)
        {
            return false;
        }

        for (var c = 0; c < widths.Length; c++)
        {
            if (open[c])
            {
                widths[c] = opened > 0f ? widths[c] * (rest / opened) : rest / count;
            }
        }

        return true;
    }

    private static void Scale(float[] widths, float factor)
    {
        for (var c = 0; c < widths.Length; c++)
        {
            widths[c] *= factor;
        }
    }

    private static float Sum(float[] widths)
    {
        var total = 0f;
        foreach (var value in widths)
        {
            total += value;
        }

        return total;
    }
}
