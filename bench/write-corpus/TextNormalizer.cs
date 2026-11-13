using System.Globalization;
using System.Text;

namespace WriteCorpus;

/// <summary>The normalisation both sides of a page comparison go through.</summary>
internal static class TextNormalizer
{
    /// <summary>
    /// NFC; soft hyphens (U+00AD) and control characters (Unicode category Cc) removed; every run of whitespace, no-break space
    /// included, becomes one space; trimmed.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var composed = text.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(composed.Length);
        var pendingSpace = false;
        foreach (var c in composed)
        {
            if (c == '­')
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (char.GetUnicodeCategory(c) == UnicodeCategory.Control)
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
