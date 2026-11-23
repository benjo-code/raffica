namespace Raffica.Write.Editing;

/// <summary>The format of the page numbers of a section (<c>w:pgNumType</c> <c>w:fmt</c>).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member is the OOXML number format decimal and the name every caller of the plan uses.")]
public enum PageNumberFormat
{
    /// <summary>1, 2, 3.</summary>
    Decimal,

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
public sealed record PageNumbering(int? Start, PageNumberFormat Format, string Prefix);

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
