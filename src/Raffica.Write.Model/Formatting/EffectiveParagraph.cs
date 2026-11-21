namespace Raffica.Write.Model;

/// <summary>What a paragraph is after every style layer: its style, its list (numbering id 0 means no list) and its outline level (0 to 8, or null for body text).</summary>
public sealed record EffectiveParagraph(string? StyleId, int NumberingId, int NumberingLevel, int? OutlineLevel);
