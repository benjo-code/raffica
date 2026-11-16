namespace Raffica.Write.Model;

/// <summary>Where a run sits relative to the baseline.</summary>
public enum VerticalPosition
{
    Baseline,
    Superscript,
    Subscript,
}

/// <summary>The character formatting that is really in force for a run: styles, table style, defaults and direct formatting resolved.</summary>
public sealed record EffectiveRun(bool Bold, bool Italic, bool Underline, bool Strike, bool SmallCaps, bool Caps, VerticalPosition Position);
