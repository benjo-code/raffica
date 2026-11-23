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
/// <b>UNVERIFIED rules (not established from a documentation source; the oracle leaves the affected pages out, see <c>SectionSteps.CheckNumbers</c>, and the Word check of Task 16 decides them).</b>
/// (1) A continuous section on the same paper that carries a <c>w:start</c> does not restart the count (this reference computes it that way; Microsoft Q&amp;A answers disagree: one says continuous breaks restart
/// like any other, another that Word reads them as next-page breaks for page numbering; Aspose documents a mode that restarts only when no content precedes the section on its page). (2) A continuous section
/// on other paper starts a new page and so restarts (generally known, no source found). (3) The blank page Word inserts before an even-page or odd-page section: which number and format it gets, and what
/// Word does with a <c>w:start</c> of the wrong parity (a forum report says Word will not start an even-page section at 1 and starts at 2).
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
    /// each page whether its section begins on it (only then does a <c>w:start</c> apply: a continuous section that starts in the middle of a page does not restart the count on the next page);
    /// null takes the first page of each run of pages of one section as the page the section begins on.
    /// </summary>
    public static IReadOnlyList<string> Numbers(WordDocument document, IReadOnlyList<int> sectionOfPage, IReadOnlyList<bool>? startsSection = null)
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
