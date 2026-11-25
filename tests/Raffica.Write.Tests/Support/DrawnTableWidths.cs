using Raffica.Write.Editing;
using Raffica.Write.Layout;

namespace Raffica.Write.Tests.Support;

/// <summary>The widths the layout draws for the columns of a table, in twips, read from the column borders of the object map (the caller of the width commands does the same).</summary>
internal static class DrawnTableWidths
{
    /// <summary>
    /// The drawn width of each of <paramref name="columns"/> grid columns of the table at the top-level block of <paramref name="reference"/>, in twips (rounded), from the first page piece that has the table; null when
    /// the layout draws the table on no laid-out page.
    /// </summary>
    public static int[]? Of(DocumentLayout layout, TableRef reference, int columns)
    {
        for (var p = 0; p < layout.LaidOutPages; p++)
        {
            var grid = layout.Page(p).Objects.Tables.FirstOrDefault(t => t.BlockIndex == reference.BlockIndex && t.ColumnBorders.Count == columns + 1);
            if (grid is not null)
            {
                return [.. Enumerable.Range(0, columns).Select(c => (int)Math.Round((grid.ColumnBorders[c + 1] - grid.ColumnBorders[c]) * 20.0))];
            }
        }

        return null;
    }

    /// <summary>The same widths in points, unrounded, for a comparison with what was written.</summary>
    public static float[]? Points(DocumentLayout layout, TableRef reference, int columns)
    {
        for (var p = 0; p < layout.LaidOutPages; p++)
        {
            var grid = layout.Page(p).Objects.Tables.FirstOrDefault(t => t.BlockIndex == reference.BlockIndex && t.ColumnBorders.Count == columns + 1);
            if (grid is not null)
            {
                return [.. Enumerable.Range(0, columns).Select(c => grid.ColumnBorders[c + 1] - grid.ColumnBorders[c])];
            }
        }

        return null;
    }
}
