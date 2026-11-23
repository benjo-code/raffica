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
    /// <c>StartsSection</c> is true when the block at the top of the page is the first block of its section, starts on this page, and the section begins a page: it is not a continuous one, or its paper
    /// (width and height) differs from the section before (a continuous section on other paper starts a new page, as in Word); the first page of the document is always one. Only a section that begins
    /// a page restarts the count; a continuous section that starts in the middle of a page and runs on to the next page does not (the page it starts on has its number already).
    /// </para>
    /// <para>
    /// <c>MaybeBlank</c> marks a page with no block that is followed by a page where an even-page or odd-page section begins: it may be the blank page that section needs. The layout gives such a page
    /// the number format of the section that follows it; what Word does there is not settled, so the oracle does not compare these pages (see <c>SectionSteps.AssertNumbersEqualTheReference</c>).
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Number, int Section, bool StartsSection, bool MaybeBlank)> Read(DocumentLayout layout, WordDocument document)
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

        bool BeginsPage(int index)
        {
            if (TypeOf(index) != "continuous")
            {
                return true;
            }

            var now = SectionPageSetup.Read(sections[Math.Min(index, sections.Count - 1)]);
            var before = SectionPageSetup.Read(index > 0 ? sections[index - 1] : null);
            return Math.Abs(now.WidthTwips - before.WidthTwips) >= 10 || Math.Abs(now.HeightTwips - before.HeightTwips) >= 10;
        }

        var top = new int[layout.LaidOutPages];
        for (var p = 0; p < top.Length; p++)
        {
            top[p] = layout.BlockAt(p, 0f);
        }

        var drawn = new List<(string, int, bool, bool)>(top.Length);
        var current = 0;
        for (var p = 0; p < top.Length; p++)
        {
            var begins = p == 0;
            var block = top[p];
            if (block >= 0)
            {
                current = sectionOfBlock[block];
                begins |= startOfSection[current] == block && layout.Locate(block)?.Page == p && BeginsPage(current);
            }

            var maybeBlank = false;
            if (block < 0 && p + 1 < top.Length && top[p + 1] is var next and >= 0)
            {
                var following = sectionOfBlock[next];
                maybeBlank = startOfSection[following] == next && TypeOf(following) is "evenPage" or "oddPage";
            }

            drawn.Add((layout.Page(p).Number, current, begins, maybeBlank));
        }

        return drawn;
    }
}
