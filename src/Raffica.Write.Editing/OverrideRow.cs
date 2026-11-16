using System.Xml.Linq;

namespace Raffica.Write.Editing;

/// <summary>Where an override lives: a direct child of a paragraph's properties, of a run's properties, or of the properties of the mark of a paragraph without text.</summary>
public enum OverrideScope
{
    Paragraph,
    Run,
    ParagraphMark,
}

/// <summary>How many of the touched paragraphs or runs carry the element: all with an equal value, all with different values, or only some.</summary>
public enum OverridePresence
{
    All,
    Partly,
    Mixed,
}

/// <summary>
/// One override row of the formatting inspector: one XML element (a direct child of <c>w:pPr</c> or <c>w:rPr</c>). <paramref name="Value"/> is
/// the <c>w:val</c> of the first element that has it, or its attributes as <c>name=value</c> pairs joined by a space when it has no <c>w:val</c>
/// (null when it has neither). The row carries no wording: the app words it.
/// </summary>
public sealed record OverrideRow(OverrideScope Scope, XName Element, OverridePresence Presence, string? Value);

/// <summary>
/// What the inspector shows for a selection: the style of the paragraphs and of the runs (null when none; a mixed line has no id) and the override rows
/// (paragraph rows first, then run rows, then paragraph mark rows, each in the order of first appearance).
/// </summary>
public sealed record InspectorView(
    string? ParagraphStyleId,
    string? CharacterStyleId,
    bool ParagraphStyleMixed,
    bool CharacterStyleMixed,
    IReadOnlyList<OverrideRow> Rows);
