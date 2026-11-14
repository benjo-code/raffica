namespace Raffica.Write.Layout;

public enum UnderlineKind
{
    None,
    Solid,
    Words,
    DoubleLine,
    Thick,
    Dotted,
    Dashed,
    DotDash,
    Wave,
}

public enum RunVerticalAlignment
{
    Baseline,
    Superscript,
    Subscript,
}

/// <summary>The formatting of one run, fully resolved. The size is in points; the colours are 0xAARRGGBB or null for "automatic" (none).</summary>
public sealed record ResolvedRun
{
    public required string FontName { get; init; }

    public required float SizePoints { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public UnderlineKind Underline { get; init; }

    public bool Strike { get; init; }

    public uint? Color { get; init; }

    public uint? Highlight { get; init; }

    public bool Caps { get; init; }

    public bool SmallCaps { get; init; }

    public RunVerticalAlignment VerticalAlignment { get; init; }

    public string? Language { get; init; }

    public bool Hidden { get; init; }

    /// <summary>True for the formatting of a paragraph mark or a list label, not of a run of text.</summary>
    public bool ParagraphMarkOnly { get; init; }
}
