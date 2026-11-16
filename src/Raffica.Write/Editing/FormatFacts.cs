namespace Raffica.Write.Editing;

/// <summary>What the rules of the formatting commands need to know about the selection, read without changing anything (<see cref="EditSession.ReadFormatFacts"/>).</summary>
/// <param name="HasStylesPart">Whether the file has a styles part (a file without one has no style to choose or update).</param>
/// <param name="StyleDefined">Whether the paragraph style of the selection (the default paragraph style of the file for a paragraph without one) is defined in the file.</param>
/// <param name="StyleMixed">Whether the selection touches paragraphs of different paragraph styles.</param>
/// <param name="HasOverrides">Whether the selection has direct formatting that Clear direct formatting would remove.</param>
/// <param name="CanUpdateStyle">Whether Update style to match would move something into the style.</param>
internal readonly record struct FormatFacts(bool HasStylesPart, bool StyleDefined, bool StyleMixed, bool HasOverrides, bool CanUpdateStyle)
{
    /// <summary>The facts of a selection that is not read (no document state to ask): nothing is held back by the facts.</summary>
    public static FormatFacts Unknown => new(true, true, false, true, true);
}
