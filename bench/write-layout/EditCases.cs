using System.Globalization;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace WriteLayoutBench;

/// <summary>One place to type at: a paragraph of a story, an offset in its projected text and the index of the top-level body block on whose page the place is (where the view looks for it first).</summary>
internal sealed record EditCase(string Name, StoryRef Story, Paragraph Paragraph, int Offset, int HintBlock);

/// <summary>
/// The places the typing measurement types at, found from the model and the pages layout alone (no document text is read out of them): the middle of a
/// long chapter, the end of a paragraph that ends a page (its growth moves the page break), the paragraph before a section break, the end of a chapter,
/// a table cell, a footnote and a header. The app's <c>--measure-typing</c> picks the same places with the same rules (a near copy in
/// <c>Measurement.Typing.cs</c> of the Write app); change both together.
/// </summary>
internal static class EditCases
{
    public static readonly string[] Names = ["middle", "page-break", "section-break", "chapter-end", "table-cell", "footnote", "header"];

    /// <summary>The text of the keystroke <paramref name="index"/>: lower case letters with a space at every sixth one, from a seeded generator.</summary>
    public static string KeyText(Random random, int index) => index % 6 == 5 ? " " : ((char)('a' + random.Next(26))).ToString();

    /// <summary>The cases the document has (a name missing from the result means the document has no such place).</summary>
    public static List<EditCase> Pick(WordDocument document, DocumentLayout pages)
    {
        var found = new List<EditCase>();
        var blocks = document.Body.Blocks;
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);

        int Length(StoryRef story, Paragraph paragraph) => ParagraphProjection.Build(paragraph, pages.ProjectionHost(story, paragraph)).Text.Length;

        // Top-level body paragraphs with their text length (field results and all), without a section break of their own.
        var texts = new List<(int Index, Paragraph Paragraph, int Length)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p && p.SectionBreak is null)
            {
                texts.Add((i, p, Length(StoryRef.Body, p)));
            }
        }

        var middleIndex = blocks.Count / 2;
        Paragraph? middle = null;
        foreach (var t in texts.Where(t => t.Length >= 200).OrderBy(t => Math.Abs(t.Index - middleIndex)))
        {
            middle = t.Paragraph;
            found.Add(new EditCase("middle", StoryRef.Body, t.Paragraph, t.Length / 2, t.Index));
            break;
        }

        // A paragraph that ends on a page whose next block starts on the next page: typing at its end grows the page that is full.
        foreach (var t in texts.Where(t => t.Length >= 1).OrderBy(t => Math.Abs(t.Index - middleIndex)))
        {
            if (t.Index + 1 < blocks.Count
                && !ReferenceEquals(middle, t.Paragraph)
                && pages.Caret(new TextPosition(StoryRef.Body, t.Paragraph, t.Length), 0) is { } end
                && pages.Locate(t.Index + 1) is { } next
                && next.Page == end.Page + 1)
            {
                found.Add(new EditCase("page-break", StoryRef.Body, t.Paragraph, t.Length, t.Index));
                break;
            }
        }

        // The paragraph before a paragraph that ends a section.
        foreach (var i in Enumerable.Range(1, Math.Max(0, blocks.Count - 1)).Where(i => blocks[i] is Paragraph { SectionBreak: not null }).OrderBy(i => Math.Abs(i - middleIndex)))
        {
            if (blocks[i - 1] is Paragraph before && before.SectionBreak is null && Length(StoryRef.Body, before) > 0)
            {
                found.Add(new EditCase("section-break", StoryRef.Body, before, Length(StoryRef.Body, before), i - 1));
                break;
            }
        }

        // The last paragraph with text before a chapter heading (outline level 0), three quarters into the document.
        var headings = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph h && resolver.ForParagraph(h).OutlineLevel == 0).ToList();
        if (headings.Count > 1)
        {
            var heading = headings[Math.Min(headings.Count - 1, headings.Count * 3 / 4)];
            var last = texts.LastOrDefault(t => t.Index < heading && t.Length >= 20);
            if (last.Paragraph is not null && !found.Any(f => ReferenceEquals(f.Paragraph, last.Paragraph)))
            {
                found.Add(new EditCase("chapter-end", StoryRef.Body, last.Paragraph, last.Length, last.Index));
            }
        }

        // The first paragraph with text in the first row of the table nearest to the middle (any paragraph of that row when all are empty).
        foreach (var i in Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Table).OrderBy(i => Math.Abs(i - middleIndex)))
        {
            var cell = FirstRowParagraphs((Table)blocks[i]).ToList();
            var chosen = cell.FirstOrDefault(p => Length(StoryRef.Body, p) > 0) ?? cell.FirstOrDefault();
            if (chosen is not null)
            {
                found.Add(new EditCase("table-cell", StoryRef.Body, chosen, Length(StoryRef.Body, chosen) / 2, i));
                break;
            }
        }

        // The footnote cited nearest to the middle, at the end of its first paragraph with text.
        var citations = new List<(int Index, int Id)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p)
            {
                citations.AddRange(FootnoteIds(p.Inlines).Select(id => (i, id)));
            }
        }

        foreach (var (citedAt, id) in citations.OrderBy(c => Math.Abs(c.Index - middleIndex)))
        {
            var story = new StoryRef(StoryKind.Note, id.ToString(CultureInfo.InvariantCulture));
            var paragraph = document.Footnotes?.Find(id)?.Content.Blocks.OfType<Paragraph>().FirstOrDefault(p => Length(story, p) > 0);
            if (paragraph is not null)
            {
                found.Add(new EditCase("footnote", story, paragraph, Length(story, paragraph), citedAt));
                break;
            }
        }

        // The first header (in key order) with a paragraph with text, at its end.
        foreach (var key in document.HeadersFooters.Keys.Order(StringComparer.Ordinal))
        {
            var part = document.HeadersFooters[key];
            if (part.Kind != HeaderFooterKind.Header)
            {
                continue;
            }

            var story = new StoryRef(StoryKind.HeaderFooter, key);
            var paragraph = part.Content.Blocks.OfType<Paragraph>().FirstOrDefault(p => Length(story, p) > 0);
            if (paragraph is not null)
            {
                found.Add(new EditCase("header", story, paragraph, Length(story, paragraph), middleIndex));
                break;
            }
        }

        return [.. Names.SelectMany(name => found.Where(f => f.Name == name))];
    }

    private static IEnumerable<Paragraph> FirstRowParagraphs(Table table)
    {
        foreach (var row in table.Rows)
        {
            if (row is not TableRow tableRow)
            {
                continue;
            }

            foreach (var item in tableRow.Items)
            {
                if (item is TableCell cell)
                {
                    foreach (var block in cell.Blocks)
                    {
                        if (block is Paragraph paragraph)
                        {
                            yield return paragraph;
                        }
                    }
                }
            }

            yield break;
        }
    }

    private static IEnumerable<int> FootnoteIds(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        if (content is FootnoteReference reference)
                        {
                            yield return reference.Id;
                        }
                    }

                    break;
                case Hyperlink link:
                    foreach (var id in FootnoteIds(link.Inlines))
                    {
                        yield return id;
                    }

                    break;
            }
        }
    }
}
