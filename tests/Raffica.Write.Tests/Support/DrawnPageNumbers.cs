using System.Xml.Linq;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>The page numbers a layout drew, read from its pages, with the section of each page and whether that section begins on the page, for <see cref="ReferencePageNumbers"/>.</summary>
internal static class DrawnPageNumbers
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Lays the rest of the document out, then returns for every page its <c>LaidOutPage.Number</c> and the section of the block at its top (<see cref="DocumentLayout.BlockAt"/> at y 0, a block
    /// continued from the page before included: the section that begins the page decides its number format, as in Word; a page with no block at all, a blank page, belongs to the section of the page
    /// before it, the first one to section 0).
    /// <para>
    /// <c>StartsSection</c> is true when the block at the top of the page is the first block of its section and starts on this page (the first page of the document is always one); a continuous section
    /// whose first block goes to the top of a page begins that page, as any other (Word, <c>Docs/write-word-check.md</c>, row continuous-start-page-top). <c>StartedMidPage</c> is the <c>w:start</c> of the last section
    /// that begins in the middle of the page before this one: this page then counts from that start + 1 (Word, row continuous-start-same-paper; the page the section begins on keeps its number).
    /// </para>
    /// <para>
    /// <c>MaybeBlank</c> marks a page with no block that is followed by a page where an even-page or odd-page section begins: it may be the blank page that section needs. The layout gives such a page
    /// the number format of the section that follows it; what Word does there is not settled, so the oracle does not compare these pages (see <c>SectionSteps.AssertNumbersEqualTheReference</c>).
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Number, int Section, bool StartsSection, bool MaybeBlank, int? StartedMidPage)> Read(DocumentLayout layout, WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(document);
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();

        var blocks = document.Body.Blocks;
        var sections = ReferencePageNumbers.Sections(document);
        var sectionOfBlock = new int[blocks.Count];
        var startOfSection = new List<int> { 0 };
        var section = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            sectionOfBlock[i] = section;
            if (blocks[i] is Paragraph { SectionBreak: not null })
            {
                section++;
                startOfSection.Add(i + 1);
            }
        }

        string? TypeOf(int index) => (string?)sections[Math.Min(index, sections.Count - 1)]?.Get(W + "type")?.Attribute(W + "val");


        var top = new int[layout.LaidOutPages];
        for (var p = 0; p < top.Length; p++)
        {
            top[p] = layout.BlockAt(p, 0f);
        }

        // A section that carries a start and begins in the middle of a page (not at its top): the page keeps its number and the next page is start + 1 (Word, Docs/write-word-check.md, row continuous-start-same-paper).
        var midOnPage = new int?[top.Length];
        for (var k = 1; k < startOfSection.Count; k++)
        {
            var first = startOfSection[k];
            if (first >= blocks.Count || !int.TryParse((string?)sections[Math.Min(k, sections.Count - 1)]?.Get(W + "pgNumType")?.Attribute(W + "start"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var start))
            {
                continue;
            }

            var page = layout.Locate(first)?.Page;
            if (page is { } at && at < top.Length && top[at] != first)
            {
                midOnPage[at] = start;
            }
        }

        var drawn = new List<(string, int, bool, bool, int?)>(top.Length);
        var current = 0;
        for (var p = 0; p < top.Length; p++)
        {
            var begins = p == 0;
            var block = top[p];
            if (block >= 0)
            {
                current = sectionOfBlock[block];
                begins |= startOfSection[current] == block && layout.Locate(block)?.Page == p;
            }

            var maybeBlank = false;
            if (block < 0 && p + 1 < top.Length && top[p + 1] is var next and >= 0)
            {
                var following = sectionOfBlock[next];
                maybeBlank = startOfSection[following] == next && TypeOf(following) is "evenPage" or "oddPage";
            }

            drawn.Add((layout.Page(p).Number, current, begins, maybeBlank, p > 0 ? midOnPage[p - 1] : null));
        }

        return drawn;
    }
}
