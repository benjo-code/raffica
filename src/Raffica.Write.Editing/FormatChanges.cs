using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Editing;

/// <summary>The property changes of the value commands (font, size, colour, highlight) for <see cref="TextEditor.ApplyCharacterChanges"/>.</summary>
public static class FormatChanges
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The font family: <c>ascii</c> and <c>hAnsi</c> are written and their theme attributes dropped; <c>eastAsia</c> and <c>cs</c> stay.</summary>
    public static IReadOnlyList<PropertyChange> Font(string family)
    {
        ArgumentException.ThrowIfNullOrEmpty(family);
        return [new PropertyChange(W + "rFonts", new XElement(W + "rFonts", new XAttribute(W + "ascii", family), new XAttribute(W + "hAnsi", family)))];
    }

    /// <summary>The size in points (written as half-points to <c>w:sz</c> and <c>w:szCs</c>).</summary>
    public static IReadOnlyList<PropertyChange> Size(float points)
    {
        if (!float.IsFinite(points) || points <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points));
        }

        var half = ((int)MathF.Round(points * 2)).ToString(CultureInfo.InvariantCulture);
        return
        [
            new PropertyChange(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", half))),
            new PropertyChange(W + "szCs", new XElement(W + "szCs", new XAttribute(W + "val", half))),
        ];
    }

    /// <summary>The text colour (the low 24 bits, RRGGBB): <c>val</c> is written and the theme attributes are dropped.</summary>
    public static IReadOnlyList<PropertyChange> Color(uint rgb) =>
        [new PropertyChange(W + "color", new XElement(W + "color", new XAttribute(W + "val", (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture))))];

    /// <summary>The highlight (a Word highlight name such as <c>yellow</c>); null removes it.</summary>
    public static IReadOnlyList<PropertyChange> Highlight(string? name) =>
        [new PropertyChange(W + "highlight", name is null ? null : new XElement(W + "highlight", new XAttribute(W + "val", name)))];

    /// <summary>
    /// The pending-format change of a character switch (<see cref="CharacterToggle"/>): on writes the plain element (<c>w:b</c>, <c>w:u</c> single, <c>w:vertAlign</c> superscript ...), off writes
    /// an explicit off (<c>w:val="0"</c>, <c>none</c>, <c>baseline</c>), which is right whatever the style gives. Superscript and subscript share <c>w:vertAlign</c>, so one replaces the other.
    /// </summary>
    public static IReadOnlyList<PropertyChange> Toggle(CharacterToggle toggle, bool on)
    {
        var (name, onValue, offValue) = ToggleElement(toggle);
        XElement element;
        if (onValue is null)
        {
            element = on ? new XElement(W + name) : new XElement(W + name, new XAttribute(W + "val", offValue));
        }
        else
        {
            element = new XElement(W + name, new XAttribute(W + "val", on ? onValue : offValue));
        }

        return [new PropertyChange(W + name, element)];
    }

    /// <summary>Whether <paramref name="changes"/> (a pending format) turn the switch on (true) or off (false); null when they say nothing about it.</summary>
    public static bool? IsOn(IReadOnlyList<PropertyChange> changes, CharacterToggle toggle)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var (name, onValue, offValue) = ToggleElement(toggle);
        foreach (var change in changes)
        {
            if (change.Name != W + name)
            {
                continue;
            }

            if (change.Value is null)
            {
                return false;
            }

            var val = change.Value.Attribute(W + "val")?.Value;
            return onValue is null ? val is null || (val != offValue && val is not ("false" or "off")) : val == onValue;
        }

        return null;
    }

    /// <summary>The changes of <paramref name="existing"/> with <paramref name="added"/> on top: a change of the same element replaces the older one.</summary>
    public static IReadOnlyList<PropertyChange> Merge(IReadOnlyList<PropertyChange> existing, IReadOnlyList<PropertyChange> added)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(added);
        var merged = existing.Where(e => added.All(a => a.Name != e.Name)).ToList();
        merged.AddRange(added);
        return merged;
    }

    private static (string Name, string? OnValue, string OffValue) ToggleElement(CharacterToggle toggle) => toggle switch
    {
        CharacterToggle.Bold => ("b", null, "0"),
        CharacterToggle.Italic => ("i", null, "0"),
        CharacterToggle.Underline => ("u", "single", "none"),
        CharacterToggle.Strike => ("strike", null, "0"),
        CharacterToggle.SmallCaps => ("smallCaps", null, "0"),
        CharacterToggle.AllCaps => ("caps", null, "0"),
        CharacterToggle.Superscript => ("vertAlign", "superscript", "baseline"),
        CharacterToggle.Subscript => ("vertAlign", "subscript", "baseline"),
        _ => throw new ArgumentOutOfRangeException(nameof(toggle)),
    };
}
