using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The page numbers of a document counted from its sections alone (no field context, no <c>PageNumbers.Format</c>, no layout type): the first page of a section with
/// <c>w:pgNumType w:start</c> gets that number, any other page the previous number plus one, the first page of the document is 1 without a start. The format comes from
/// <c>w:fmt</c>. It is the oracle of the layout's numbers: change it only to follow Word's documented behaviour, never to follow the layout.
/// <para>
/// <b>Sources.</b> ECMA-376 Part 1, 17.6.12 pgNumType: <c>w:start</c> is the number of the first page of the section, <c>w:fmt</c> the format; without a start the count continues from the section before
/// (the Word 2003 XML reference says the same; MS-OI29500 2.1.219 only narrows the value to an unsigned 32-bit integer). A letter format counts a, b, ... z, aa, bb, ... (Word's rule). That is all the
/// reference is built on. A page begins its section when it is the page the section's first block starts on and the section starts a page (see <c>DrawnPageNumbers</c>).
/// </para>
/// <para>
/// <b>Rules checked against Word on 2026-11-23</b> (<c>Docs/write-word-check.md</c>, section "Sections, page numbers and page setup (milestone 4.4b)", read through Word automation).
/// (1) A <c>w:start</c> of a continuous section applies as for any section when its first block is at the top of a page (row continuous-start-page-top: Word 1 2 5 6); a continuous section that begins in
/// the middle of a page leaves that page's number alone and the next page is start + 1 (row continuous-start-same-paper: Word 1 2 6 7, page 3 is 5 + 1), which <c>startedMidPage</c> carries. (2) A continuous
/// section on other paper starts a new page and so restarts (row continuous-start-other-paper: Word 1 2 5 6).
/// </para>
/// <para>
/// <b>UNVERIFIED rules (the oracle leaves the affected pages out, see <c>SectionSteps.CheckNumbers</c>).</b> (3) The blank page Word inserts before an even-page or odd-page section: its number format is unread.
/// (4) What Word does with a <c>w:start</c> of the wrong parity: it ignores the start and continues the count (rows even-page-start-one and odd-page-start-two), and a start of the right parity is applied
/// (rows even-page-start-two and odd-page-start-one); the layout does not follow this yet (Docs/DECISIONS.md, "Raffica Write: sections, page numbers and page setup (M4.4b)").
/// </para>
/// </summary>
internal static class ReferencePageNumbers
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The section properties in document order: every section-break paragraph of the body, then the final section (null when it has none).</summary>
    public static List<SectionProperties?> Sections(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sections = new List<SectionProperties?>();
        foreach (var block in document.Body.Blocks)
        {
            if (block is Paragraph { SectionBreak: { } properties })
            {
                sections.Add(properties);
            }
        }

        sections.Add(document.Body.FinalSection);
        return sections;
    }

    /// <summary>
    /// The number text of every page; <paramref name="sectionOfPage"/> holds the index of the section of each page (counted from 0, in document order). <paramref name="startsSection"/> says for
    /// each page whether a section begins at its top (only then does that section's <c>w:start</c> apply to the page); <paramref name="startedMidPage"/> holds, per page, the <c>w:start</c> of a section that began in the middle of the page before (this page is then start + 1);
    /// null takes the first page of each run of pages of one section as the page the section begins on.
    /// </summary>
    public static IReadOnlyList<string> Numbers(WordDocument document, IReadOnlyList<int> sectionOfPage, IReadOnlyList<bool>? startsSection = null, IReadOnlyList<int?>? startedMidPage = null)
    {
        ArgumentNullException.ThrowIfNull(sectionOfPage);
        var sections = Sections(document);
        var numbers = new List<string>(sectionOfPage.Count);
        var number = 0;
        var previous = -1;
        for (var page = 0; page < sectionOfPage.Count; page++)
        {
            var index = sectionOfPage[page];
            var begins = startsSection is null ? index != previous : startsSection[page];
            var properties = sections[Math.Min(index, sections.Count - 1)];
            var numbering = properties?.Get(W + "pgNumType");
            if (begins && int.TryParse((string?)numbering?.Attribute(W + "start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
            {
                number = start;
            }
            else if (startedMidPage?[page] is { } mid)
            {
                number = mid + 1;
            }
            else
            {
                number++;
            }

            previous = index;
            numbers.Add(Format(number, (string?)numbering?.Attribute(W + "fmt")));
        }

        return numbers;
    }

    private static string Format(int number, string? format)
    {
        if (number < 1)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        switch (format)
        {
            case "lowerRoman":
                return number < 4000 ? Roman(number).ToLowerInvariant() : number.ToString(CultureInfo.InvariantCulture);
            case "upperRoman":
                return number < 4000 ? Roman(number) : number.ToString(CultureInfo.InvariantCulture);
            case "lowerLetter":
                return Letters(number, 'a');
            case "upperLetter":
                return Letters(number, 'A');
            default:
                return number.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string Roman(int number)
    {
        string[] thousands = ["", "M", "MM", "MMM"];
        string[] hundreds = ["", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM"];
        string[] tens = ["", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC"];
        string[] ones = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];
        return thousands[number / 1000] + hundreds[number / 100 % 10] + tens[number / 10 % 10] + ones[number % 10];
    }

    // Word counts a, b, ... z, aa, bb, ... zz, aaa: the letter of the remainder repeated once more every 26 pages.
    private static string Letters(int number, char first)
    {
        var text = new StringBuilder();
        var letter = (char)(first + ((number - 1) % 26));
        text.Append(letter, ((number - 1) / 26) + 1);
        return text.ToString();
    }
}
