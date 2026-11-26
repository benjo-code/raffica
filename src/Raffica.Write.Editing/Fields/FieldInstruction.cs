using System.Text;

namespace Raffica.Write.Editing;

/// <summary>
/// A parsed field instruction: the upper-cased name, the first token that is not a switch, and the switches in order. Names and switch letters are case-insensitive
/// (switch names are kept lower case without the backslash, <c>*</c> for <c>\*</c>), arguments may be quoted, switches may come in any order. <see cref="HasUnknownToken"/>
/// says that a token is outside what Write's support table (the spec's table of supported fields) covers; for a field name the table does not know it is false (the name alone decides there).
/// </summary>
public sealed class FieldInstruction
{
    private static readonly Dictionary<string, Spec> Table = BuildTable();

    private FieldInstruction(string name, string? argument, IReadOnlyList<(string Name, string? Value)> switches, bool hasUnknownToken)
    {
        Name = name;
        Argument = argument;
        Switches = switches;
        HasUnknownToken = hasUnknownToken;
    }

    /// <summary>The field name, upper case, or an empty string for an empty instruction.</summary>
    public string Name { get; }

    /// <summary>The first token that is not a switch (or the value of one), unquoted, or null.</summary>
    public string? Argument { get; }

    /// <summary>The switches in the order they stand: the name lower case without the backslash, the value unquoted or null.</summary>
    public IReadOnlyList<(string Name, string? Value)> Switches { get; }

    /// <summary>True when a switch, a value or an extra argument is not in the support table of the field.</summary>
    public bool HasUnknownToken { get; }

    /// <summary>True when the instruction has the switch (the name in lower case, no backslash).</summary>
    public bool Has(string switchName) => Switches.Any(s => string.Equals(s.Name, switchName, StringComparison.Ordinal));

    /// <summary>The value of the first switch of that name, or null.</summary>
    public string? ValueOf(string switchName) => Switches.FirstOrDefault(s => string.Equals(s.Name, switchName, StringComparison.Ordinal)).Value;

    public static FieldInstruction Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = Tokenize(text);
        if (tokens.Count == 0)
        {
            return new FieldInstruction(string.Empty, null, [], false);
        }

        var name = tokens[0].Text.ToUpperInvariant();
        Table.TryGetValue(name, out var spec);
        var switches = new List<(string Name, string? Value)>();
        var positional = new List<string>();
        var unknown = false;
        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.IsSwitch)
            {
                positional.Add(token.Text);
                continue;
            }

            var switchName = token.Text[1..2].ToLowerInvariant();
            string? value = token.Text.Length > 2 ? token.Text[2..] : null;
            SwitchValue? kind = spec is not null && spec.Switches.TryGetValue(switchName, out var found) ? found : null;
            var next = i + 1 < tokens.Count && !tokens[i + 1].IsSwitch ? tokens[i + 1].Text : null;

            // An unknown field or switch takes the next plain token as its value, so it is not mistaken for the argument.
            if (value is null && next is not null && (spec is null || kind is null || kind == SwitchValue.Required || (kind == SwitchValue.Optional && LooksLikeRange(next))))
            {
                value = next;
                i++;
            }

            if (spec is not null && !IsAccepted(name, switchName, kind, value))
            {
                unknown = true;
            }

            switches.Add((switchName, value));
        }

        if (spec is not null && (positional.Count > spec.Positionals || (name == "STYLEREF" && !IsChapterNumber(positional, switches))))
        {
            unknown = true;
        }

        return new FieldInstruction(name, positional.Count > 0 ? positional[0] : null, switches, unknown);
    }

    private static bool IsChapterNumber(List<string> positional, List<(string Name, string? Value)> switches) =>
        positional.Count == 1 && positional[0] == "1" && switches.Exists(s => s.Name == "s");

    private static bool LooksLikeRange(string text)
    {
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 && dash < text.Length - 1 && text[..dash].All(char.IsAsciiDigit) && text[(dash + 1)..].All(char.IsAsciiDigit);
    }

    private static bool IsAccepted(string field, string switchName, SwitchValue? kind, string? value)
    {
        if (kind is null)
        {
            return false;
        }

        if (kind == SwitchValue.None)
        {
            return value is null;
        }

        if (kind == SwitchValue.Optional)
        {
            return value is null || LooksLikeRange(value);
        }

        if (value is null)
        {
            return false;
        }

        // Only the two character-format words are known after \* of REF (the format of the result stays as it is).
        return field != "REF" || switchName != "*" || value.Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase) || value.Equals("CHARFORMAT", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Token(string Text, bool IsSwitch);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var builder = new StringBuilder();
        var inQuotes = false;
        var quoted = false;
        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : ' ';
            if (c == '"')
            {
                inQuotes = !inQuotes;
                quoted = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (builder.Length > 0 || quoted)
                {
                    var value = builder.ToString();
                    tokens.Add(new Token(value, !quoted && value.Length > 1 && value[0] == '\\' && (char.IsLetter(value[1]) || value[1] is '*' or '#' or '@')));
                    builder.Clear();
                    quoted = false;
                }

                continue;
            }

            builder.Append(c);
        }

        return tokens;
    }

    private enum SwitchValue
    {
        None,
        Required,
        Optional,
    }

    // The support table: per field name the switches Write handles, whether each carries a value, and how many plain arguments the field has.
    private sealed record Spec(int Positionals, Dictionary<string, SwitchValue> Switches);

    private static Dictionary<string, Spec> BuildTable() => new(StringComparer.Ordinal)
    {
        ["SEQ"] = new(1, new() { ["*"] = SwitchValue.Required, ["n"] = SwitchValue.None, ["c"] = SwitchValue.None, ["r"] = SwitchValue.Required, ["s"] = SwitchValue.Required }),
        ["STYLEREF"] = new(1, new() { ["s"] = SwitchValue.None }),
        ["REF"] = new(1, new() { ["h"] = SwitchValue.None, ["n"] = SwitchValue.None, ["r"] = SwitchValue.None, ["*"] = SwitchValue.Required }),
        ["PAGEREF"] = new(1, new() { ["h"] = SwitchValue.None }),
        ["NOTEREF"] = new(1, new() { ["h"] = SwitchValue.None, ["f"] = SwitchValue.None }),
        ["TOC"] = new(0, new() { ["o"] = SwitchValue.Required, ["u"] = SwitchValue.None, ["h"] = SwitchValue.None, ["z"] = SwitchValue.None, ["n"] = SwitchValue.Optional, ["c"] = SwitchValue.Required }),
    };
}
