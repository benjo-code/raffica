using System.Xml.Linq;

namespace Raffica.Write.Editing;

/// <summary>
/// One change of a property bag: the child <paramref name="Name"/> is set to <paramref name="Value"/> (a copy of it is stored), or removed
/// when <paramref name="Value"/> is null. A <c>w:rFonts</c> or <c>w:color</c> value is merged into the element that is already there
/// (see <see cref="FormatChanges"/>); every other value replaces the child.
/// </summary>
public readonly record struct PropertyChange(XName Name, XElement? Value);
