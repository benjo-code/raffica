using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The list labels a layout drew, read from its pages, against the independent count of <see cref="ReferenceNumbering"/>: the same paragraphs in the same order with the same labels.
/// </summary>
internal static class DrawnLabels
{
    /// <summary>The labels the layout drew, per paragraph: the first line of a body paragraph that has a label run, its label cut from the page text.</summary>
    public static List<(Paragraph Paragraph, string Label)> Read(DocumentLayout layout, string id, out int unaligned, HashSet<Paragraph>? unreadable = null)
    {
        var drawn = new List<(Paragraph, string)>();
        unaligned = 0;
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            var page = layout.Page(i);
            var lines = page.Text.Length == 0 ? [] : page.Text.Split('\n');
            if (lines.Length != page.TextLines.Length)
            {
                // A line that has no model paragraph (a text box) makes the two lists differ in length; the labels of such a page cannot be read.
                if (unreadable is null)
                {
                    Assert.False(page.TextLines.Any(l => l.Story.Kind == StoryKind.Body && l.Runs.Any(r => r.IsLabel)), $"{id}: page {i} has labels and its text does not line up");
                }
                else
                {
                    foreach (var line in page.TextLines.Where(l => l.Story.Kind == StoryKind.Body && l.Runs.Any(r => r.IsLabel)))
                    {
                        unreadable.Add(line.Paragraph);
                    }
                }

                unaligned++;
                continue;
            }

            for (var l = 0; l < lines.Length; l++)
            {
                var line = page.TextLines[l];
                if (line.Story.Kind == StoryKind.Body && line.Runs.Any(r => r.IsLabel))
                {
                    var text = lines[l];
                    var cut = text.IndexOf(' ', StringComparison.Ordinal);
                    drawn.Add((line.Paragraph, cut < 0 ? text : text[..cut]));
                }
            }
        }

        return drawn;
    }

    /// <summary>The layout draws the Symbol and Wingdings bullets of Word as plain bullets (the fonts are not shipped).</summary>
    public static string Normalized(string label, bool bullet)
    {
        if (!bullet)
        {
            return label;
        }

        return string.Concat(label.Select(c => c is >= '' and <= '' or '▪' ? '•' : c));
    }

    /// <summary>
    /// Fails unless the layout drew exactly the labels the reference counts for the model. Returns the reference's labels that were compared. With <paramref name="tolerateUnreadablePages"/> a page whose
    /// text does not line up with its lines (a text box) is skipped: the paragraphs that have a label on such a page are left out of both sides.
    /// </summary>
    public static IReadOnlyList<(Paragraph Paragraph, string Label)> AssertEqualToReference(DocumentLayout layout, WordDocument model, string id, bool tolerateUnreadablePages = false)
    {
        var all = ReferenceNumbering.Labels(model);
        var skipped = tolerateUnreadablePages ? new HashSet<Paragraph>(ReferenceEqualityComparer.Instance) : null;
        var drawn = Read(layout, id, out var unaligned, skipped);
        Assert.True(skipped is not null || unaligned == 0 || all.Count == 0, $"{id}: {unaligned} pages could not be read");
        IReadOnlyList<(Paragraph Paragraph, string Label)> expected = skipped is null ? all : [.. all.Where(l => !skipped.Contains(l.Paragraph))];

        // The same paragraphs in the same order, then the same labels.
        Assert.True(expected.Count == drawn.Count, $"{id}: the reference counts {expected.Count} labels, the layout drew {drawn.Count}");
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(ReferenceEquals(expected[i].Paragraph, drawn[i].Paragraph), $"{id}: label {i} belongs to another paragraph");
            var bullet = expected[i].Label.Length > 0 && expected[i].Label.All(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '%' && c != ')' && c != '(' && c != ' ');
            var want = Normalized(expected[i].Label, bullet);
            var got = Normalized(drawn[i].Label, bullet);
            Assert.True(want == got, $"{id}: label {i} is '{got}', the reference says '{want}'");
        }

        return expected;
    }
}
