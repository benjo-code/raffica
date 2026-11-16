using System.Globalization;

namespace Raffica.Write.Layout;

/// <summary>
/// The fields the layout evaluates in 4.2: <c>PAGE</c>, <c>NUMPAGES</c>, <c>SECTIONPAGES</c> and <c>SECTION</c>, in headers and footers.
/// Every other field (TOC, REF, PAGEREF, DATE, ...) shows the result stored in the file; so does a field the layout was not asked to evaluate.
/// </summary>
internal sealed class FieldContext
{
    /// <summary>What <c>NUMPAGES</c> and <c>SECTIONPAGES</c> show while the layout does not know the total yet; the finished layout replaces the page.</summary>
    public const string PendingTotal = "…";

    public FieldContext(int pageNumber, string pageText, int sectionNumber, int? totalPages, int? sectionPages)
    {
        PageNumber = pageNumber;
        PageText = pageText;
        SectionNumber = sectionNumber;
        TotalPages = totalPages;
        SectionPages = sectionPages;
    }

    public int PageNumber { get; }

    /// <summary>The page number as printed, in the number format of the section.</summary>
    public string PageText { get; }

    public int SectionNumber { get; }

    /// <summary>The number of pages of the document; null while the layout is still going on.</summary>
    public int? TotalPages { get; }

    /// <summary>The number of pages of the section; null while the layout is still going on.</summary>
    public int? SectionPages { get; }

    /// <summary>True when a PAGE field was evaluated (the text then depends on the page number).</summary>
    public bool UsedPage { get; private set; }

    /// <summary>True when a NUMPAGES or SECTIONPAGES field was evaluated, whether its total was known or not (the page then depends on the count of pages).</summary>
    public bool UsedTotal { get; private set; }

    /// <summary>True when an evaluated field needed a total that was not known (the page has to be made again at the end).</summary>
    public bool UsedPendingTotal { get; private set; }

    /// <summary>The text of a field, or null when the instruction is not one the layout evaluates.</summary>
    public string? Evaluate(string instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var trimmed = instruction.Trim();
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
        {
            end++;
        }

        var name = trimmed[..end];
        var format = Switch(trimmed[end..]);
        if (name.Equals("PAGE", StringComparison.OrdinalIgnoreCase))
        {
            UsedPage = true;
            return format is null ? PageText : PageNumbers.Format(PageNumber, format);
        }

        if (name.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
        {
            return Number(SectionNumber, format);
        }

        if (name.Equals("NUMPAGES", StringComparison.OrdinalIgnoreCase))
        {
            return Total(TotalPages, format);
        }

        if (name.Equals("SECTIONPAGES", StringComparison.OrdinalIgnoreCase))
        {
            return Total(SectionPages, format);
        }

        return null;
    }

    private string Total(int? value, string? format)
    {
        UsedTotal = true;
        if (value is not { } known)
        {
            UsedPendingTotal = true;
            return PendingTotal;
        }

        return Number(known, format);
    }

    private static string Number(int value, string? format) =>
        format is null ? value.ToString(CultureInfo.InvariantCulture) : PageNumbers.Format(value, format);

    // The number format of a "\* FORMAT" switch: ROMAN, roman, ALPHABETIC, alphabetic, Arabic. Any other switch is not a number format.
    private static string? Switch(string rest)
    {
        var tokens = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (tokens[i] != "\\*")
            {
                continue;
            }

            switch (tokens[i + 1])
            {
                case "ROMAN":
                    return "upperRoman";
                case "roman":
                    return "lowerRoman";
                case "ALPHABETIC":
                    return "upperLetter";
                case "alphabetic":
                    return "lowerLetter";
                case "Arabic" or "ARABIC":
                    return "decimal";
                default:
                    break;
            }
        }

        return null;
    }
}
