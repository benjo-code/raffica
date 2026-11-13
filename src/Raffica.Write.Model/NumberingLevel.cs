namespace Raffica.Write.Model;

/// <summary>One <c>w:lvl</c>. The format and the justification are plain strings (an unknown value is kept). A typed child that carries more than its <c>w:val</c> or repeats stays a kept child.</summary>
public sealed class NumberingLevel : OrderedElement
{
    /// <summary><c>w:ilvl</c>; -1 when absent or not a number (the text then stays an unknown attribute).</summary>
    public int Level { get; set; } = -1;

    public int? Start { get; set; }

    /// <summary><c>w:numFmt</c>, for example <c>decimal</c> or <c>bullet</c>.</summary>
    public string? Format { get; set; }

    /// <summary><c>w:lvlText</c>, for example <c>%1.%2</c>.</summary>
    public string? Text { get; set; }

    /// <summary><c>w:lvlJc</c>.</summary>
    public string? Justification { get; set; }

    public PropertyBag ParagraphProperties { get; } = new(PropertyKind.Paragraph);

    public PropertyBag RunProperties { get; } = new(PropertyKind.Run);
}
