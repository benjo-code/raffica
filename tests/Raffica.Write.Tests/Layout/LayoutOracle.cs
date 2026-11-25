using System.Text;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The oracle of the incremental layout: after an edit and a finished <see cref="DocumentLayout.LayOutRestAsync"/>, a new layout over the same
/// model laid out from scratch must give the same pages. The comparison is exact (page count; per page the index, number, text, provisional flag,
/// every item with its values, the block spans, the text lines with their paragraphs and geometry), and so are the tables that
/// <see cref="DocumentLayout.Locate"/> and <see cref="DocumentLayout.BlockAt"/> answer from.
/// </summary>
internal static class LayoutOracle
{
    public static void AssertEqualToFull(DocumentLayout incremental, WordDocument model, LayoutOptions? options = null, string label = "")
    {
        if (!incremental.IsSettled)
        {
            incremental.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        }

        Assert.True(incremental.IsComplete, label + ": the incremental layout is not complete");
        Assert.False(incremental.ChromeRefreshPending, label + ": a header or footer refresh is still pending");
        var full = DocumentLayout.Create(model, PageTestKit.Fonts, options ?? new LayoutOptions());
        full.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(full.IsComplete, label + ": the full layout is not complete");

        Assert.True(full.LaidOutPages == incremental.LaidOutPages, $"{label}: page count {incremental.LaidOutPages} (incremental) against {full.LaidOutPages} (full)");
        Assert.True(full.FirstPageIsFinal == incremental.FirstPageIsFinal, label + ": FirstPageIsFinal");
        Assert.True(full.Notes.SequenceEqual(incremental.Notes, StringComparer.Ordinal), $"{label}: layout notes differ ({string.Join(" | ", incremental.Notes)} against {string.Join(" | ", full.Notes)})");
        var cells = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        var cellsBack = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < full.LaidOutPages; i++)
        {
            var expected = full.Page(i);
            var actual = incremental.Page(i);
            var where = $"{label}: page {i}";
            Assert.True(actual.Index == i, where + " has index " + actual.Index);
            Assert.True(expected.Number == actual.Number, $"{where}: number '{actual.Number}' against '{expected.Number}'");
            Assert.True(expected.IsProvisional == actual.IsProvisional, where + ": provisional flag");
            Assert.True(expected.ChromeType == actual.ChromeType && expected.NumberValue == actual.NumberValue, $"{where}: header and footer type {actual.ChromeType} / number value {actual.NumberValue} against {expected.ChromeType} / {expected.NumberValue}");
            Assert.True(string.Equals(expected.Text, actual.Text, StringComparison.Ordinal), where + ": text differs" + FirstDifference(expected.Text, actual.Text));
            Assert.True(expected.Width == actual.Width && expected.Height == actual.Height, where + ": size");
            Assert.True(expected.Items.Count == actual.Items.Count, $"{where}: item count {actual.Items.Count} against {expected.Items.Count}");
            for (var k = 0; k < expected.Items.Count; k++)
            {
                Assert.True(SameItem(expected.Items[k], actual.Items[k]), $"{where}: item {k} ({expected.Items[k].GetType().Name}) differs");
            }

            Assert.True(expected.Spans.Length == actual.Spans.Length, where + ": span count");
            for (var k = 0; k < expected.Spans.Length; k++)
            {
                Assert.True(expected.Spans[k] == actual.Spans[k], $"{where}: span {k} {actual.Spans[k]} against {expected.Spans[k]}");
            }

            Assert.True(expected.TextLines.Length == actual.TextLines.Length, $"{where}: text line count {actual.TextLines.Length} against {expected.TextLines.Length}");
            for (var k = 0; k < expected.TextLines.Length; k++)
            {
                var e = expected.TextLines[k];
                var a = actual.TextLines[k];
                Assert.True(
                    ReferenceEquals(e.Paragraph, a.Paragraph) && e.Story == a.Story && e.Start == a.Start && e.End == a.End && e.X == a.X && e.Top == a.Top && e.Baseline == a.Baseline && e.Height == a.Height && e.Runs.Length == a.Runs.Length,
                    $"{where}: text line {k} differs");
                Assert.True(e.ContentLeft == a.ContentLeft && e.ContentRight == a.ContentRight, $"{where}: text line {k} content edges");
                Assert.True(string.Equals(e.Projection.Text, a.Projection.Text, StringComparison.Ordinal), $"{where}: text line {k} projection text");
                Assert.True((e.Host is null) == (a.Host is null) && e.Host?.GetType() == a.Host?.GetType(), $"{where}: text line {k} host");
                for (var o = 0; o <= (e.End - e.Start) + 3; o++)
                {
                    Assert.True(e.Map.Map(o) == a.Map.Map(o), $"{where}: text line {k} offset map at {o}");
                }

                Assert.True((e.Cell is null) == (a.Cell is null), $"{where}: text line {k} cell");
                if (e.Cell is not null && a.Cell is not null)
                {
                    // Cells are objects of their own layout: the two layouts must group the lines into cells the same way.
                    Assert.True(cells.TryAdd(a.Cell, e.Cell) || ReferenceEquals(cells[a.Cell], e.Cell), $"{where}: text line {k} cell grouping");
                    Assert.True(cellsBack.TryAdd(e.Cell, a.Cell) || ReferenceEquals(cellsBack[e.Cell], a.Cell), $"{where}: text line {k} cell grouping (back)");
                }

                for (var r = 0; r < e.Runs.Length; r++)
                {
                    var er = e.Runs[r];
                    var ar = a.Runs[r];
                    Assert.True(er.TextStart == ar.TextStart && er.TextEnd == ar.TextEnd && er.IsHyphen == ar.IsHyphen && er.X == ar.X && er.Width == ar.Width && er.Size == ar.Size && er.Raise == ar.Raise, $"{where}: text line {k} glyph run {r}");
                }
            }

            AssertSameObjects(expected.Objects, actual.Objects, where);

            foreach (var y in new[] { 0f, expected.Height * 0.25f, expected.Height * 0.5f, expected.Height * 0.75f, expected.Height }.Concat(expected.Spans.SelectMany(s => new[] { s.Top, s.Bottom - 0.01f })))
            {
                Assert.True(full.BlockAt(i, y) == incremental.BlockAt(i, y), $"{where}: BlockAt({y})");
            }
        }

        var count = model.Body.Blocks.Count;
        for (var b = 0; b < count; b++)
        {
            Assert.True(full.Locate(b) == incremental.Locate(b), $"{label}: Locate({b}) is {incremental.Locate(b)}, a full layout says {full.Locate(b)}");
        }

        // The way back from a position to a page (the paragraph to pages map): every fifth line of every page, found without a hint.
        for (var i = 0; i < full.LaidOutPages; i++)
        {
            var lines = full.Page(i).TextLines;
            for (var k = 0; k < lines.Length; k += 5)
            {
                var position = new TextPosition(lines[k].Story, lines[k].Paragraph, lines[k].Start);
                Assert.True(full.Caret(position, -1) == incremental.Caret(position, -1), $"{label}: the caret of page {i} line {k} is {incremental.Caret(position, -1)}, a full layout says {full.Caret(position, -1)}");
            }
        }

        Assert.True(full.Revision >= 0);
    }

    /// <summary>The object maps of two pages are equal: the same pictures (the same drawings, identity included, at the same boxes) and the same tables with every number equal. The records hold lists, so the lists are compared by element.</summary>
    public static void AssertSameObjects(ObjectMap expected, ObjectMap actual, string where)
    {
        Assert.True(expected.IsEmpty == actual.IsEmpty && (!expected.IsEmpty || ReferenceEquals(expected, actual)), $"{where}: the shared empty map is not used for a page without objects");
        Assert.True(expected.Pictures.Count == actual.Pictures.Count, $"{where}: picture count {actual.Pictures.Count} against {expected.Pictures.Count}");
        for (var p = 0; p < expected.Pictures.Count; p++)
        {
            var e = expected.Pictures[p];
            var a = actual.Pictures[p];
            Assert.True(e.Equals(a) && Equals(e.Id, a.Id), $"{where}: picture {p} differs ({a.Id} at {a.X},{a.Y} against {e.Id} at {e.X},{e.Y})");
        }

        Assert.True(expected.Tables.Count == actual.Tables.Count, $"{where}: table count {actual.Tables.Count} against {expected.Tables.Count}");
        for (var t = 0; t < expected.Tables.Count; t++)
        {
            var e = expected.Tables[t];
            var a = actual.Tables[t];
            Assert.True(
                e.Story == a.Story && e.BlockIndex == a.BlockIndex && e.X == a.X && e.Y == a.Y && e.Width == a.Width && e.Height == a.Height
                && e.ColumnBorders.SequenceEqual(a.ColumnBorders) && e.RowEdges.SequenceEqual(a.RowEdges) && e.RowIndexes.SequenceEqual(a.RowIndexes) && e.Cells.SequenceEqual(a.Cells),
                $"{where}: table {t} differs (block {a.BlockIndex} against {e.BlockIndex}, {a.Cells.Count} cells against {e.Cells.Count})");
        }
    }

    private static string FirstDifference(string expected, string actual)
    {
        var n = Math.Min(expected.Length, actual.Length);
        var i = 0;
        while (i < n && expected[i] == actual[i])
        {
            i++;
        }

        string Around(string s) => s.Substring(Math.Max(0, i - 20), Math.Min(60, s.Length - Math.Max(0, i - 20)));
        return new StringBuilder().Append(" at ").Append(i).Append(": '").Append(Around(actual)).Append("' against '").Append(Around(expected)).Append('\'').ToString();
    }

    private static bool SameItem(PageItem a, PageItem b)
    {
        switch (a)
        {
            case GlyphRunItem x when b is GlyphRunItem y:
                return ReferenceEquals(x.Face, y.Face) && x.Size == y.Size && x.Color == y.Color && x.X == y.X && x.Baseline == y.Baseline && x.Glyphs.Span.SequenceEqual(y.Glyphs.Span) && x.Xs.Span.SequenceEqual(y.Xs.Span);
            case RuleItem x when b is RuleItem y:
                return x.X == y.X && x.Y == y.Y && x.Width == y.Width && x.Height == y.Height && x.Color == y.Color;
            case FillItem x when b is FillItem y:
                return x.X == y.X && x.Y == y.Y && x.Width == y.Width && x.Height == y.Height && x.Color == y.Color;
            case ImageItem x when b is ImageItem y:
                return x.Image == y.Image && x.X == y.X && x.Y == y.Y && x.Width == y.Width && x.Height == y.Height;
            case PlaceholderItem x when b is PlaceholderItem y:
                return x.X == y.X && x.Y == y.Y && x.Width == y.Width && x.Height == y.Height;
            default:
                return false;
        }
    }
}
