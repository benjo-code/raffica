using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The page-based field results counted without the implementation (<see cref="FieldPages"/>, the refresh): the page of a body paragraph is the page the enclosing top-level block starts on (the layout's block table, its own tests compare it
/// with a full layout), found with a walk of this file; the number text of that page is the independent count of <see cref="ReferencePageNumbers"/> (through <see cref="SectionSteps.Expectation"/>), and a page the oracle leaves out (the
/// unverified even-page and odd-page states) has no answer, so a field that points there is not judged. Change it only to follow Word's documented behaviour, never to follow the code.
/// </summary>
internal static class ReferencePageResults
{
    /// <summary>
    /// The expected printed number of the page every body paragraph starts on, null for a paragraph the oracle has no answer for (a page it leaves out, or a block the layout did not place). Lays the rest of the document out first.
    /// </summary>
    public static Func<Paragraph, string?> PageTextOf(DocumentLayout layout, WordDocument document)
    {
        var (_, left, expected) = SectionSteps.Expectation(layout, document);
        var tops = new Dictionary<Paragraph, int>(ReferenceEqualityComparer.Instance);
        for (var top = 0; top < document.Body.Blocks.Count; top++)
        {
            foreach (var paragraph in ModelWalk.Blocks([document.Body.Blocks[top]]).OfType<Paragraph>())
            {
                tops.TryAdd(paragraph, top);
            }
        }

        return paragraph => tops.TryGetValue(paragraph, out var top) && layout.Locate(top) is { } at && at.Page < expected.Count && !left[at.Page] ? expected[at.Page] : null;
    }

    /// <summary>
    /// Asserts that every <c>PAGEREF</c> field of the body outside the tables of contents that the independent count has a page for stores that page, and that every table of contents shows the page of its headings. Returns how many page results were
    /// compared. The layout must be the one the refresh used, complete.
    /// </summary>
    public static int AssertPageResults(DocumentLayout layout, WordDocument document, IFieldHost host, string label, bool includeTocs = true)
    {
        var pageText = PageTextOf(layout, document);
        var paragraphs = ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().ToList();
        var inToc = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var site in TocRefresh.Find(document.Body.Blocks))
        {
            for (var i = site.First; i <= site.Last; i++)
            {
                if (site.Container[i] is Paragraph paragraph)
                {
                    inToc.Add(paragraph);
                }
            }
        }

        var starts = new Dictionary<string, Paragraph>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in paragraphs)
        {
            foreach (var start in ModelWalk.Inlines(paragraph.Inlines).OfType<BookmarkStart>())
            {
                starts.TryAdd(start.Name, paragraph);
            }
        }

        var compared = 0;
        foreach (var paragraph in paragraphs.Where(p => !inToc.Contains(p) && FieldRefresh.HasFieldMarkup(p)))
        {
            var fields = FieldView.InParagraph(paragraph);
            for (var ordinal = 0; ordinal < fields.Count; ordinal++)
            {
                var field = fields[ordinal];
                if (field.Parent is not null || field.Support.Kind != FieldKind.PageRef || !FieldRefresh.IsUsable(field) || field.Instruction.Argument is not { } name || !starts.TryGetValue(name, out var target))
                {
                    continue;
                }

                if (pageText(target) is not { } expected)
                {
                    continue;
                }

                Assert.True(
                    string.Equals(expected, ReferenceFields.Stored(paragraph, ordinal), StringComparison.Ordinal),
                    $"{label}: a PAGEREF field does not store the page its bookmark is on (expected \"{expected}\", stored \"{ReferenceFields.Stored(paragraph, ordinal)}\")");
                compared++;
            }
        }

        return includeTocs ? compared + ReferenceToc.AssertEqualToExpected(document, host, label, pageText) : compared;
    }
}
