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
}
