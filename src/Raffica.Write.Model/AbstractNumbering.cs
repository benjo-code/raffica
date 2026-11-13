namespace Raffica.Write.Model;

/// <summary>One <c>w:abstractNum</c>: its levels; <c>w:nsid</c>, <c>w:multiLevelType</c>, <c>w:tmpl</c>, <c>w:name</c> and the style links are kept children.</summary>
public sealed class AbstractNumbering : OrderedElement
{
    /// <summary><c>w:abstractNumId</c>; -1 when the attribute is absent or not a number (the text then stays an unknown attribute).</summary>
    public int AbstractNumId { get; set; } = -1;

    /// <summary>The levels are written at the position they were read in (reordering read items is not reflected: they are sorted by <see cref="OrderedElement.Position"/>); items added in code go in schema order after those read.</summary>
    public IList<NumberingLevel> Levels { get; } = new List<NumberingLevel>();
}
