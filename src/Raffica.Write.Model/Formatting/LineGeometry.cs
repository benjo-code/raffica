namespace Raffica.Write.Model;

/// <summary>
/// How a text lies at the start of a paragraph's first line, in twips from the left edge of the text area: the left indent of the paragraph, where the text ends, and the tab stops that wait on that line in the order of their
/// positions (those of the styles and the paragraph, without bar stops, and the stop a hanging indent gives).
/// </summary>
public sealed record LineGeometry(int LeftIndent, int TextEnd, IReadOnlyList<LineStop> Stops);

/// <summary>A tab stop: its position in twips and whether it is a right-aligned one (the text after the tab ends at it).</summary>
public readonly record struct LineStop(int Position, bool Right);
