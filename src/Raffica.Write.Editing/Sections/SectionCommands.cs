namespace Raffica.Write.Editing;

/// <summary>The format of the page numbers of a section (<c>w:pgNumType</c> <c>w:fmt</c>).</summary>
public enum PageNumberFormat
{
    /// <summary>1, 2, 3.</summary>
    Arabic,

    /// <summary>i, ii, iii.</summary>
    LowerRoman,

    /// <summary>I, II, III.</summary>
    UpperRoman,

    /// <summary>a, b, c.</summary>
    LowerLetter,

    /// <summary>A, B, C.</summary>
    UpperLetter,
}

/// <summary>
/// How a section numbers its pages. <paramref name="Start"/> is the number of its first page (0 to 99999); null continues the count of the section before. <paramref name="Prefix"/> is text written before the
/// page field in the footers the command writes ("A-" gives "A-1"); it is footer text, not a section property.
/// </summary>
public sealed record PageNumbering(int? Start, PageNumberFormat Format, string Prefix)
{
    /// <summary>The most characters of a prefix.</summary>
    public const int MaximumPrefixLength = 32;

    /// <summary>The highest first page number.</summary>
    public const int MaximumStart = 99999;

    /// <summary>
    /// Whether <paramref name="text"/> is a prefix the editor accepts: at most <see cref="MaximumPrefixLength"/> characters, no control character, no lone surrogate and nothing a document cannot store
    /// (U+FFFE, U+FFFF); a valid surrogate pair stays. The card asks this rule before the editor can refuse.
    /// </summary>
    public static bool IsValidPrefix(string? text)
    {
        if (text is null || text.Length > MaximumPrefixLength)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsControl(c) || char.IsSurrogate(c) || !System.Xml.XmlConvert.IsXmlChar(c))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>How a section starts (<c>w:type</c>).</summary>
public enum SectionBreakKind
{
    NextPage,
    Continuous,
    EvenPage,
    OddPage,
}

/// <summary>Why the section commands are not available at a place (<see cref="SectionReason.None"/> when they are).</summary>
public enum SectionReason
{
    None,

    /// <summary>The caret is in a header, footer, note or comment.</summary>
    NotInBody,

    /// <summary>Only the first blocks of the file are open.</summary>
    PrefixDocument,

    /// <summary>There is no place to work at.</summary>
    NoDocument,
}

/// <summary>
/// What the section commands know about a place in the body: <paramref name="Section"/> is the section the caret's top-level block is in (from 0), <paramref name="AtSectionStart"/> is true when that block
/// starts its section (a break there makes no new section: Number pages from here then edits that section only), <paramref name="FollowingSections"/> counts the sections after the one that gets the numbers
/// and <paramref name="FollowingWithOwnSetting"/> those of them that keep their own numbering or footer; the others continue from here.
/// </summary>
public sealed record SectionFacts(bool InBody, int Section, bool AtSectionStart, int FollowingSections, int FollowingWithOwnSetting, SectionReason Reason);
