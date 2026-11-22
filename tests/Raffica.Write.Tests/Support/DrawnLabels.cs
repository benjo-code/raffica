using System.Xml.Linq;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// What reading the labels of pages skipped or collapsed: the most pages and paragraphs one reading could not read (a page whose text does not line up with its lines, a text box) and how many
/// labels of repeated table header rows were collapsed (summed over the readings).
/// </summary>
internal sealed class LabelTally
{
    /// <summary>The most pages one reading skipped.</summary>
    public int MaxUnreadablePages { get; private set; }

    /// <summary>The most paragraphs (with a label on a skipped page) one reading left out of both sides.</summary>
    public int MaxUnreadableParagraphs { get; private set; }

    /// <summary>How many drawn-again labels of repeated header rows were collapsed, summed over the readings.</summary>
    public int RepeatedHeaderLabels { get; internal set; }

    internal void Note(int pages, int paragraphs)
    {
        MaxUnreadablePages = Math.Max(MaxUnreadablePages, pages);
        MaxUnreadableParagraphs = Math.Max(MaxUnreadableParagraphs, paragraphs);
    }

    public override string ToString() => $"unreadable pages {MaxUnreadablePages}, unreadable paragraphs {MaxUnreadableParagraphs}, repeated header labels {RepeatedHeaderLabels}";
}

/// <summary>
/// The list labels a layout drew, read from its pages, against the independent count of <see cref="ReferenceNumbering"/>: the same paragraphs in the same order with the same labels.
/// </summary>
internal static class DrawnLabels
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// The labels the layout drew, per paragraph: the first line of a body paragraph that has a label run, its label cut from the page text. A paragraph drawn again is collapsed only when it is in a
    /// table row that repeats as a header (<c>w:tblHeader</c>) and carries the label it had the first time (counted in <paramref name="tally"/>); any other second drawing stays in the list and shows
    /// as a mismatch. With <paramref name="tolerateUnreadablePages"/> a page whose text does not line up with its lines is skipped (counted in <paramref name="tally"/>) and its labelled paragraphs are
    /// returned in <paramref name="unreadable"/>; without it such a page with labels fails.
    /// </summary>
    public static List<(Paragraph Paragraph, string Label)> Read(DocumentLayout layout, string id, WordDocument model, LabelTally tally, bool tolerateUnreadablePages, HashSet<Paragraph>? unreadable = null)
    {
        var drawn = new List<(Paragraph, string)>();
        var first = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
        var headerParagraphs = HeaderRowParagraphs(model);
        var pagesSkipped = 0;
        var skippedParagraphs = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            var page = layout.Page(i);
            var lines = page.Text.Length == 0 ? [] : page.Text.Split('\n');
            if (lines.Length != page.TextLines.Length)
            {
                // A line that has no model paragraph (a text box) makes the two lists differ in length; the labels of such a page cannot be read.
                var labelled = page.TextLines.Where(l => l.Story.Kind == StoryKind.Body && l.Runs.Any(r => r.IsLabel)).ToList();
                Assert.True(tolerateUnreadablePages || labelled.Count == 0, $"{id}: page {i} has labels and its text does not line up");
                foreach (var line in labelled)
                {
                    skippedParagraphs.Add(line.Paragraph);
                }

                pagesSkipped++;
                continue;
            }

            for (var l = 0; l < lines.Length; l++)
            {
                var line = page.TextLines[l];
                if (line.Story.Kind != StoryKind.Body || !line.Runs.Any(r => r.IsLabel))
                {
                    continue;
                }

                var text = lines[l];
                var cut = text.IndexOf(' ', StringComparison.Ordinal);
                var label = cut < 0 ? text : text[..cut];
                if (first.TryGetValue(line.Paragraph, out var earlier) && line.Cell is not null && headerParagraphs.Contains(line.Paragraph))
                {
                    // A repeated table header row draws its paragraphs again on the next page, with the label they had.
                    Assert.True(label == earlier, $"{id}: the repeated header row draws '{label}' where it drew '{earlier}'");
                    tally.RepeatedHeaderLabels++;
                    continue;
                }

                first.TryAdd(line.Paragraph, label);
                drawn.Add((line.Paragraph, label));
            }
        }

        tally.Note(pagesSkipped, skippedParagraphs.Count);
        if (unreadable is not null)
        {
            unreadable.UnionWith(skippedParagraphs);
        }

        return drawn;
    }

    // Every paragraph in a row of a table that has w:tblHeader on, nested tables included.
    private static HashSet<Paragraph> HeaderRowParagraphs(WordDocument model)
    {
        var found = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var block in ModelWalk.Blocks(model.Body.Blocks).OfType<Table>())
        {
            Collect(block.Rows, found);
        }

        return found;
    }

    private static void Collect(IEnumerable<RowItem> items, HashSet<Paragraph> found)
    {
        foreach (var item in items)
        {
            if (item is TableRow row)
            {
                if (row.Properties.Get(W + "tblHeader") is { } header && (string?)header.Attribute(W + "val") is null or "1" or "true" or "on")
                {
                    foreach (var cell in row.Items.OfType<TableCell>())
                    {
                        foreach (var paragraph in ModelWalk.Blocks(cell.Blocks).OfType<Paragraph>())
                        {
                            found.Add(paragraph);
                        }
                    }
                }

                Collect(row.Items, found);
            }
        }
    }

    /// <summary>The layout draws the Symbol and Wingdings bullets of Word as plain bullets (the fonts are not shipped).</summary>
    public static string Normalized(string label, bool bullet)
    {
        if (!bullet)
        {
            return label;
        }

        return string.Concat(label.Select(c => c is >= '\uF000' and <= '\uF0FF' or '\u25AA' ? '\u2022' : c));
    }

    /// <summary>
    /// Fails unless the layout drew exactly the labels the reference counts for the model. Returns the reference's labels that were compared. With a <paramref name="tolerate"/> tally a page whose
    /// text does not line up with its lines (a text box) is skipped: the paragraphs that have a label on such a page are left out of both sides, and the tally records how many.
    /// </summary>
    public static IReadOnlyList<(Paragraph Paragraph, string Label)> AssertEqualToReference(DocumentLayout layout, WordDocument model, string id, LabelTally? tolerate = null)
    {
        var all = ReferenceNumbering.Labels(model);
        var skipped = tolerate is not null ? new HashSet<Paragraph>(ReferenceEqualityComparer.Instance) : null;
        var drawn = Read(layout, id, model, tolerate ?? new LabelTally(), tolerate is not null, skipped);
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
