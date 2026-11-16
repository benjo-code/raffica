using System.Globalization;
using System.Text;

namespace Raffica.Write.Editing;

/// <summary>Where a delete stops: grapheme clusters and words in a paragraph's flat text, and the clean-up of typed text.</summary>
internal static class TextBoundaries
{
    private const int Window = 256;

    private enum CharClass
    {
        Space,
        Word,
        Other,
    }

    /// <summary>The start of the grapheme cluster that ends at <paramref name="offset"/> (or contains the character before it).</summary>
    public static int PreviousCluster(string text, int offset)
    {
        var from = WindowStart(text, offset - Window);
        var to = Math.Min(text.Length, offset + Window);
        var elements = StringInfo.GetTextElementEnumerator(text[from..to]);
        while (elements.MoveNext())
        {
            var start = from + elements.ElementIndex;
            var length = ((string)elements.Current).Length;
            if (start <= offset - 1 && offset - 1 < start + length)
            {
                return start;
            }
        }

        return offset - 1;
    }

    /// <summary>The end of the grapheme cluster that starts at <paramref name="offset"/> (or contains the character there).</summary>
    public static int NextCluster(string text, int offset)
    {
        var from = WindowStart(text, offset - Window);
        var to = Math.Min(text.Length, offset + Window);
        var elements = StringInfo.GetTextElementEnumerator(text[from..to]);
        while (elements.MoveNext())
        {
            var start = from + elements.ElementIndex;
            var end = start + ((string)elements.Current).Length;
            if (offset < end)
            {
                return end;
            }
        }

        return offset + 1;
    }

    /// <summary>
    /// The start of the word before <paramref name="offset"/>: blanks right before the caret are skipped first, then one run of word
    /// characters (letters, digits, underscore, combining marks) or one run of any other single kind of character (punctuation, symbols).
    /// </summary>
    public static int PreviousWord(string text, int offset)
    {
        var i = offset;
        while (i > 0 && Classify(text[i - 1]) == CharClass.Space)
        {
            i--;
        }

        if (i > 0)
        {
            var kind = Classify(text[i - 1]);
            while (i > 0 && Classify(text[i - 1]) == kind)
            {
                i--;
            }
        }

        return i;
    }

    /// <summary>
    /// The end of the word at <paramref name="offset"/>: one run of word characters or of another single kind of character, then the
    /// blanks after it; when the caret is in blanks, only those.
    /// </summary>
    public static int NextWord(string text, int offset)
    {
        var i = offset;
        if (i < text.Length && Classify(text[i]) != CharClass.Space)
        {
            var kind = Classify(text[i]);
            while (i < text.Length && Classify(text[i]) == kind)
            {
                i++;
            }
        }

        while (i < text.Length && Classify(text[i]) == CharClass.Space)
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// The run of one kind of character around <paramref name="offset"/> (the word, the blanks or the punctuation the character at the offset belongs to; at the end of the text the
    /// one before it): what a double click selects. An empty text gives (0, 0).
    /// </summary>
    public static (int Start, int End) RunAt(string text, int offset)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var at = Math.Clamp(offset, 0, text.Length);
        if (at == text.Length)
        {
            at--;
        }

        var kind = Classify(text[at]);
        var start = at;
        while (start > 0 && Classify(text[start - 1]) == kind)
        {
            start--;
        }

        var end = at + 1;
        while (end < text.Length && Classify(text[end]) == kind)
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>
    /// Typed or pasted text as lines: <c>\r\n</c>, <c>\n</c> and <c>\r</c> each end a line once, a tab stays (the caller makes a tab content of
    /// it), every other control character is dropped, and so is
    /// what XML cannot hold (a lone surrogate, U+FFFE, U+FFFF); a valid surrogate pair stays.
    /// </summary>
    public static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                lines.Add(line.ToString());
                line.Clear();
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                line.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (c == '\t' || !(char.IsControl(c) || char.IsSurrogate(c) || c is '\uFFFE' or '\uFFFF'))
            {
                line.Append(c);
            }
        }

        lines.Add(line.ToString());
        return lines;
    }

    private static int WindowStart(string text, int wanted)
    {
        var from = Math.Max(0, wanted);
        return from > 0 && from < text.Length && char.IsLowSurrogate(text[from]) ? from - 1 : from;
    }

    private static CharClass Classify(char c)
    {
        if (char.IsWhiteSpace(c))
        {
            return CharClass.Space;
        }

        if (char.IsLetterOrDigit(c) || c == '_')
        {
            return CharClass.Word;
        }

        return char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            ? CharClass.Word
            : CharClass.Other;
    }
}
