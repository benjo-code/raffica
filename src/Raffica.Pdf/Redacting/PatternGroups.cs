using Raffica.Core.Redaction;

namespace Raffica.Pdf.Redacting;

/// <summary>The hits of one kind of pattern with one certainty. A group whose checksum failed is not ticked on its own: it is added by its own button.</summary>
internal sealed record PatternGroup(RedactionPatternKind Kind, PatternCertainty Certainty, IReadOnlyList<RedactionHit> Hits, bool Preselected)
{
    /// <summary>The certainty in words (never a colour alone).</summary>
    public string CertaintyText => PdfStrings.FindCertaintyText(Certainty);

    /// <summary>The heading of the group: the kind and the certainty in words.</summary>
    public string Heading => PdfStrings.FindGroupFormat
        .Replace("{0}", PdfStrings.FindKindText(Kind), StringComparison.Ordinal)
        .Replace("{1}", CertaintyText, StringComparison.Ordinal);
}

/// <summary>Sorts the hits of a pattern search into groups by kind and certainty.</summary>
internal static class PatternGroups
{
    /// <summary>
    /// The groups in the order of the kinds, with the checksum-ok group first, then the shape-only one, then the failed one. Hits that did not
    /// come from a pattern search are left out. Checksum ok and shape only are preselected, checksum failed is not.
    /// </summary>
    public static IReadOnlyList<PatternGroup> Of(IEnumerable<RedactionHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        return
        [
            .. hits
                .Where(h => h.Pattern is not null && h.Certainty is not null)
                .GroupBy(h => (Kind: h.Pattern!.Value, Certainty: h.Certainty!.Value))
                .OrderBy(g => g.Key.Kind)
                .ThenBy(g => g.Key.Certainty == PatternCertainty.ChecksumOk ? 0 : g.Key.Certainty == PatternCertainty.ShapeOnly ? 1 : 2)
                .Select(g => new PatternGroup(g.Key.Kind, g.Key.Certainty, [.. g], g.Key.Certainty != PatternCertainty.ChecksumFailed)),
        ];
    }
}
