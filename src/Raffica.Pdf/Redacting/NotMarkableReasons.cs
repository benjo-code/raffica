using Raffica.Core.Redaction;

namespace Raffica.Pdf.Redacting;

/// <summary>Why a hit of the search cannot become a mark, in the order the classes are asked.</summary>
internal enum NotMarkableKind
{
    /// <summary>The hit has no rectangle with an area.</summary>
    NoArea,

    /// <summary>The hit spans more lines than a mark can hold.</summary>
    TooManyLines,

    /// <summary>The matched text is white space only.</summary>
    WhiteSpace,

    /// <summary>The matched text is longer than a term can be.</summary>
    TooLong,

    /// <summary>A letter or digit of the hit has no box with an area, so marking the rest would leave part of the text in place.</summary>
    NoBox,
}

/// <summary>
/// Finds the reason for a hit that is not <see cref="RedactionHit.Markable"/> from the hit itself, so the list can say it in the UI language. Core
/// gives an English sentence; it is never read, because wording that is matched by its text breaks with the first rewording.
/// </summary>
internal static class NotMarkableReasons
{
    /// <summary>The first class that fits the hit: no area, too many lines, white space only, too long, else a letter without a box.</summary>
    public static NotMarkableKind Classify(RedactionHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (hit.Areas.Count == 0)
        {
            return NotMarkableKind.NoArea;
        }

        if (hit.Areas.Count > RedactionLimits.MaxAreasPerMark)
        {
            return NotMarkableKind.TooManyLines;
        }

        if (string.IsNullOrWhiteSpace(hit.Text))
        {
            return NotMarkableKind.WhiteSpace;
        }

        return hit.Text.Length > RedactionLimits.MaxTermLength ? NotMarkableKind.TooLong : NotMarkableKind.NoBox;
    }

    /// <summary>The reason in the UI language.</summary>
    public static string TextOf(RedactionHit hit) => PdfStrings.FindNotMarkableReason(Classify(hit));
}
