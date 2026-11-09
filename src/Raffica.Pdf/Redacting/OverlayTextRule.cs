using System.Globalization;
using Raffica.Core.Annotations;
using Raffica.Core.Redaction;

namespace Raffica.Pdf.Redacting;

/// <summary>The kind of a character of an overlay text that the text box font cannot draw; the wording of the refusal names it, the character itself is never shown.</summary>
internal enum OverlayCharacterClass
{
    /// <summary>A line break, tab or another control or unusual space character.</summary>
    Control,

    /// <summary>A letter outside the set the font draws (Greek, Cyrillic, Arabic, Asian scripts and others).</summary>
    Letters,

    /// <summary>A symbol, a picture character (emoji) or any other sign.</summary>
    Symbols,
}

/// <summary>
/// What the interface knows about an overlay text before the plan is asked: the length and the characters the font can draw (the set of
/// <see cref="TextBoxCharacters"/>), so a refusal can say which kind of character stopped the text. Core's plan stays the one that decides;
/// this only classifies the first character it would refuse. White space alone is no text (the plan makes it none).
/// </summary>
internal static class OverlayTextRule
{
    /// <summary>Gets the kind of the first character that cannot be drawn, or null if every character can (or the text is empty or white space).</summary>
    public static OverlayCharacterClass? FirstRefused(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (TextBoxCharacters.IsSupported(character))
            {
                continue;
            }

            if (char.IsSurrogate(character))
            {
                var point = char.IsHighSurrogate(character) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(character, text[i + 1]) : -1;
                return point >= 0 && char.IsLetter(text, i) ? OverlayCharacterClass.Letters : OverlayCharacterClass.Symbols;
            }

            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                return OverlayCharacterClass.Control;
            }

            return char.IsLetter(character) || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                ? OverlayCharacterClass.Letters
                : OverlayCharacterClass.Symbols;
        }

        return null;
    }

    /// <summary>Whether the text is longer than a plan takes.</summary>
    public static bool IsTooLong(string? text) => text is { Length: > RedactionLimits.MaxOverlayTextLength };
}
