using System.Globalization;
using System.Text;

namespace Raffica.Write.Editing;

/// <summary>
/// The running number of one <c>SEQ</c> label, as Word 16 counts it (Docs/write-word-check.md, "Fields, captions and tables of contents (milestone 4.5b)"). <see cref="Next"/> applies one field in document order (only a
/// field that <see cref="Counts"/>): <c>\r n</c> sets the number (a value that is no number sets 0), <c>\s n</c> starts again when a heading of level n or higher (announced through <see cref="NoteHeading"/>) stands
/// between this field and the one before it of the same label, <c>\c</c> without <c>\n</c> repeats the current number (0 at a new start) and anything else counts on; the number is then written in the format of
/// <c>\* ARABIC</c>, <c>ROMAN</c>, <c>roman</c>, <c>ALPHABETIC</c> or <c>alphabetic</c> (<c>\* MERGEFORMAT</c> and <c>\* CHARFORMAT</c> change nothing). A field in another format counts like any other; it only has no text.
/// </summary>
internal sealed class SeqCounter
{
    private static readonly (int Value, string Roman)[] s_roman =
    [
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
    ];

    private const int NoHeading = 10;

    private int _value;

    // The highest heading (the lowest level number) since the last field of the label.
    private int _heading = NoHeading;

    /// <summary>
    /// Whether Word counts the field in its sequence: a closed <c>SEQ</c> field with a label alone (one that names a bookmark shows the number at that place instead), with switches Word knows for it (<c>\c \h \n \r \s \* \#</c>;
    /// any other makes Word show an error), not deleted or moved away, and not part of a table of contents. It may be one Write does not update (hidden, another number format, in a tracked insertion, nested in another field).
    /// </summary>
    public static bool Counts(FieldSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        if (span.Support.Kind != FieldKind.Seq || !span.IsComplete || span.InDeletion || span.Instruction.ArgumentCount != 1
            || span.Instruction.Switches.Any(s => s.Name is not ("c" or "h" or "n" or "r" or "s" or "*" or "#")))
        {
            return false;
        }

        for (var parent = span.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Support.Kind == FieldKind.Toc)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A heading of that level (1 to 9) went by: a later field with <c>\s</c> of that level or a deeper one starts again.</summary>
    public void NoteHeading(int level) => _heading = Math.Min(_heading, level);

    /// <summary>The counter moved on by a field that <see cref="Counts"/>; the text of the field, or null when it asks for a number format Write does not write.</summary>
    public string? Next(FieldInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.Has("r"))
        {
            _value = int.TryParse(instruction.ValueOf("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
        }
        else
        {
            if (instruction.Has("s") && int.TryParse(instruction.ValueOf("s"), NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level is >= 1 and <= 9 && _heading <= level)
            {
                _value = 0;
            }

            if (!instruction.Has("c") || instruction.Has("n"))
            {
                _value++;
            }
        }

        _heading = NoHeading;
        return TryFormat(instruction, out var format) ? Text(_value, format) : null;
    }

    private enum Format
    {
        Arabic,
        UpperRoman,
        LowerRoman,
        UpperLetters,
        LowerLetters,
    }

    private static bool TryFormat(FieldInstruction instruction, out Format format)
    {
        format = Format.Arabic;
        foreach (var (name, value) in instruction.Switches)
        {
            if (name != "*" || value is null)
            {
                continue;
            }

            if (value.Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase) || value.Equals("CHARFORMAT", StringComparison.OrdinalIgnoreCase) || value.Equals("ARABIC", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (value)
            {
                case "ROMAN":
                    format = Format.UpperRoman;
                    break;
                case "roman":
                    format = Format.LowerRoman;
                    break;
                case "ALPHABETIC":
                    format = Format.UpperLetters;
                    break;
                case "alphabetic":
                    format = Format.LowerLetters;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static string Text(int number, Format format)
    {
        var decimalText = number.ToString(CultureInfo.InvariantCulture);
        if (number < 1)
        {
            return decimalText;
        }

        return format switch
        {
            Format.LowerRoman => number < 4000 ? Roman(number) : decimalText,
            Format.UpperRoman => number < 4000 ? Roman(number).ToUpperInvariant() : decimalText,
            Format.LowerLetters => Letters(number, 'a') ?? decimalText,
            Format.UpperLetters => Letters(number, 'A') ?? decimalText,
            _ => decimalText,
        };
    }

    private static string Roman(int number)
    {
        var text = new StringBuilder();
        foreach (var (value, roman) in s_roman)
        {
            while (number >= value)
            {
                text.Append(roman);
                number -= value;
            }
        }

        return text.ToString();
    }

    // Word's letter numbering: a to z, then aa, bb, cc (the same letter repeated), not base 26.
    private static string? Letters(int number, char first)
    {
        var count = ((number - 1) / 26) + 1;
        return count > 64 ? null : new string((char)(first + ((number - 1) % 26)), count);
    }
}
