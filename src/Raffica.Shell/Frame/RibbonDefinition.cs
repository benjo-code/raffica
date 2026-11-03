namespace Raffica.Shell.Frame;

/// <summary>A labelled group of commands inside a ribbon tab.</summary>
public sealed record RibbonGroupDefinition(
    string Label,
    string Digit,
    IReadOnlyList<CommandDefinition> Commands,
    bool Destructive = false);

/// <summary>A ribbon tab; <paramref name="Letter"/> is its field-code letter.</summary>
public sealed record RibbonTabDefinition(
    string Name,
    string Letter,
    IReadOnlyList<RibbonGroupDefinition> Groups);

/// <summary>The whole ribbon of an app.</summary>
public sealed record RibbonDefinition(IReadOnlyList<RibbonTabDefinition> Tabs)
{
    /// <summary>Throws <see cref="InvalidOperationException"/> naming the tab and the code on a faulty definition.</summary>
    public void Validate()
    {
        var letters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tab in Tabs)
        {
            if (!IsValidCode(tab.Letter, 1))
            {
                throw new InvalidOperationException($"Tab '{tab.Name}': malformed tab letter '{tab.Letter}'.");
            }

            if (!letters.Add(tab.Letter))
            {
                throw new InvalidOperationException($"Tab '{tab.Name}': duplicate tab letter '{tab.Letter}'.");
            }

            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in tab.Groups)
            {
                if (group.Commands.Count == 0)
                {
                    throw new InvalidOperationException($"Tab '{tab.Name}': group '{group.Label}' is empty.");
                }

                foreach (var command in group.Commands)
                {
                    ValidateCommand(tab, command, codes);
                }
            }
        }
    }

    internal static bool IsValidCode(string? code, int maxLength = 2)
    {
        if (string.IsNullOrEmpty(code) || code.Length > maxLength)
        {
            return false;
        }

        foreach (var c in code)
        {
            if (!(c is >= 'A' and <= 'Z' || c is >= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateCommand(RibbonTabDefinition tab, CommandDefinition command, HashSet<string> codes)
    {
        if (!IsValidCode(command.FieldCode))
        {
            throw new InvalidOperationException($"Tab '{tab.Name}': malformed field code '{command.FieldCode}' on command '{command.Id}'.");
        }

        if (!codes.Add(command.FieldCode))
        {
            throw new InvalidOperationException($"Tab '{tab.Name}': duplicate field code '{command.FieldCode}' on command '{command.Id}'.");
        }

        if (command is { Size: CommandSize.Large, IconOnly: true })
        {
            throw new InvalidOperationException($"Tab '{tab.Name}': command '{command.Id}' (code '{command.FieldCode}') cannot be both large and icon-only.");
        }

        if (command.Items is { } items)
        {
            var itemCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                ValidateCommand(tab, item, itemCodes);
            }
        }
    }
}
