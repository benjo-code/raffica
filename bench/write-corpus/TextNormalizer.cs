using System.Globalization;
using System.Text;

namespace WriteCorpus;

/// <summary>The normalisation both sides of a page comparison go through.</summary>
internal static class TextNormalizer
{
    /// <summary>
    /// NFC; every run of whitespace (a newline, a tab and a no-break space included) becomes one space; every control character
    /// (Unicode category Cc) and every format character (category Cf: soft hyphen U+00AD, zero-width space U+200B, ZWNJ/ZWJ, U+2060,
    /// the byte order mark U+FEFF, the left-to-right and right-to-left marks and the bidi embedding, override and isolate controls)
    /// is removed without leaving a space; trimmed. Ligatures (for example U+FB01) are not folded: a known limitation of the comparison.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var composed = text.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(composed.Length);
        var pendingSpace = false;
        foreach (var c in composed)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
