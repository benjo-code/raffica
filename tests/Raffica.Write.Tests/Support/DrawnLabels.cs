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
        var seen = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
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
                    // A repeated table header row draws its paragraphs again on the next page: a paragraph has its label counted once.
                    if (seen.Add(line.Paragraph))
                    {
                        drawn.Add((line.Paragraph, cut < 0 ? text : text[..cut]));
                    }
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
        Assert.True(expected.Count == drawn.Count, $"{id}: the reference counts {expected.Count} labels, the layout drew {drawn.Count}{FirstDifference(expected, drawn, model)}");
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

    // Where the reference and the layout first part: the index, and the text and label of the paragraph each one has there.
    private static string FirstDifference(IReadOnlyList<(Paragraph Paragraph, string Label)> expected, List<(Paragraph Paragraph, string Label)> drawn, WordDocument model)
    {
        var at = 0;
        while (at < expected.Count && at < drawn.Count && ReferenceEquals(expected[at].Paragraph, drawn[at].Paragraph))
        {
            at++;
        }

        static string Describe((Paragraph Paragraph, string Label)? item) =>
            item is { } i ? $"'{i.Label}' on a paragraph of {string.Concat(i.Paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).Select(t => t.Text)).Length} characters (style {i.Paragraph.Properties.StyleId ?? "none"}, direct numId {i.Paragraph.Properties.NumberingId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} ilvl {i.Paragraph.Properties.NumberingLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, outline {i.Paragraph.Properties.Get(System.Xml.Linq.XName.Get("outlineLvl", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")) is not null})" : "nothing";
        var extra = at < drawn.Count ? drawn[at].Paragraph : null;
        var where = extra is null ? "" : $" [layout paragraph: top-level {model.Body.Blocks.Contains(extra)}, in the walk {ModelWalk.Blocks(model.Body.Blocks).Any(b => ReferenceEquals(b, extra))}, text \"{string.Concat(extra.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).Select(t => t.Text))}\", inlines {extra.Inlines.Count}, container {Container(model, extra)}]";
        var window = string.Join(" ", Enumerable.Range(Math.Max(0, at - 2), 6).Select(k => $"[{k}: ref {(k < expected.Count ? expected[k].Label : "-")} / layout {(k < drawn.Count ? drawn[k].Label : "-")}]"));
        return $"{where} {window}; first difference at {at}: reference {Describe(at < expected.Count ? expected[at] : null)}, layout {Describe(at < drawn.Count ? drawn[at] : null)}";
    }

    private static string Container(WordDocument model, Paragraph target)
    {
        var top = model.Body.Blocks.ToList();
        for (var i = 0; i < top.Count; i++)
        {
            if (ModelWalk.Blocks([top[i]]).Any(b => ReferenceEquals(b, target)))
            {
                return $"{top[i].GetType().Name} at {i} of {top.Count}";
            }
        }

        return "none";
    }
}
