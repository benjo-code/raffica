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
        if (!incremental.IsComplete)
        {
            incremental.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        }

        Assert.True(incremental.IsComplete, label + ": the incremental layout is not complete");
        var full = DocumentLayout.Create(model, PageTestKit.Fonts, options ?? new LayoutOptions());
        full.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(full.IsComplete, label + ": the full layout is not complete");

        Assert.True(full.LaidOutPages == incremental.LaidOutPages, $"{label}: page count {incremental.LaidOutPages} (incremental) against {full.LaidOutPages} (full)");
        for (var i = 0; i < full.LaidOutPages; i++)
        {
            var expected = full.Page(i);
            var actual = incremental.Page(i);
            var where = $"{label}: page {i}";
            Assert.True(actual.Index == i, where + " has index " + actual.Index);
            Assert.True(expected.Number == actual.Number, $"{where}: number '{actual.Number}' against '{expected.Number}'");
            Assert.True(expected.IsProvisional == actual.IsProvisional, where + ": provisional flag");
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
