using System.Globalization;
using System.Text;

namespace Raffica.Write.Editing;

/// <summary>
/// The running number of one <c>SEQ</c> label. <see cref="Next"/> applies one field in document order: <c>\r n</c> sets the number, <c>\s n</c> restarts it after the heading of level n (the headings are
/// announced through <see cref="NoteHeading"/>), <c>\c</c> repeats the current number and the default counts on; the number is then written in the format of <c>\* ARABIC</c>, <c>ROMAN</c>, <c>roman</c>,
/// <c>ALPHABETIC</c> or <c>alphabetic</c> (<c>\* MERGEFORMAT</c> and <c>\* CHARFORMAT</c> change nothing). A field the counter cannot read leaves it untouched.
/// </summary>
internal sealed class SeqCounter
{
    private static readonly (int Value, string Roman)[] s_roman =
    [
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
    ];

    private readonly HashSet<int> _restarts = [];
    private int _value;

    /// <summary>A heading of that level (1 to 9) went by: a later field with <c>\s</c> of that level starts again.</summary>
    public void NoteHeading(int level) => _restarts.Add(level);

    /// <summary>The text of the field and the counter moved on, or null (and the counter untouched) when a switch or a value is outside what Write reads.</summary>
    public string? Next(FieldInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (!TryFormat(instruction, out var format))
        {
            return null;
        }

        int? set = null;
        if (instruction.Has("r"))
        {
            if (!int.TryParse(instruction.ValueOf("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            set = number;
        }

        int? level = null;
        if (instruction.Has("s"))
        {
            if (!int.TryParse(instruction.ValueOf("s"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 9)
            {
                return null;
            }

            level = parsed;
        }

        if (set is { } fixedNumber)
        {
            _value = fixedNumber;
        }
        else
        {
            if (level is { } restart && _restarts.Remove(restart))
            {
                _value = 0;
            }

            if (instruction.Has("c") && _value > 0)
            {
                return Text(_value, format);
            }

            _value++;
        }

        return Text(_value, format);
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
