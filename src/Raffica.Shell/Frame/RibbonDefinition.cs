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

            var scope = new List<CommandDefinition>();
            foreach (var group in tab.Groups)
            {
                if (group.Commands.Count == 0)
                {
                    throw new InvalidOperationException($"Tab '{tab.Name}': group '{group.Label}' is empty.");
                }

                foreach (var command in group.Commands)
                {
                    ValidateCommand(tab, command);
                    scope.Add(command);
                }
            }

            ValidateScope(tab, scope, "tab");
            foreach (var command in scope)
            {
                if (command.Items is { } items)
                {
                    ValidateScope(tab, items, $"menu of command '{command.Id}'");
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

    private static void ValidateCommand(RibbonTabDefinition tab, CommandDefinition command)
    {
        if (!IsValidCode(command.FieldCode))
        {
            throw new InvalidOperationException($"Tab '{tab.Name}': malformed field code '{command.FieldCode}' on command '{command.Id}'.");
        }

        if (command is { Size: CommandSize.Large, IconOnly: true })
        {
            throw new InvalidOperationException($"Tab '{tab.Name}': command '{command.Id}' (code '{command.FieldCode}') cannot be both large and icon-only.");
        }

        if (command.Items is not { } items)
        {
            return;
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException($"Tab '{tab.Name}': command '{command.Id}' (code '{command.FieldCode}') has an empty menu; leave Items out for a plain command.");
        }

        foreach (var item in items)
        {
            if (item.Items is not null)
            {
                throw new InvalidOperationException($"Tab '{tab.Name}': menu item '{item.Id}' (code '{item.FieldCode}') has its own menu; menus nest one level only.");
            }

            ValidateCommand(tab, item);
        }
    }

    /// <summary>
    /// The codes of one scope (a tab, or one menu) must be distinct and prefix-free: the session runs a command as soon as the typed
    /// characters equal its code, so a code that begins another code could never be reached.
    /// </summary>
    private static void ValidateScope(RibbonTabDefinition tab, IReadOnlyList<CommandDefinition> commands, string scope)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                var (a, b) = (commands[j], commands[i]);
                if (a.FieldCode == b.FieldCode)
                {
                    throw new InvalidOperationException($"Tab '{tab.Name}': duplicate field code '{b.FieldCode}' on command '{b.Id}' in the {scope}.");
                }

                var (shorter, longer) = a.FieldCode.Length < b.FieldCode.Length ? (a, b) : (b, a);
                if (longer.FieldCode.StartsWith(shorter.FieldCode, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Tab '{tab.Name}': field code '{shorter.FieldCode}' (command '{shorter.Id}') is a prefix of '{longer.FieldCode}' (command '{longer.Id}') in the {scope}, so the longer code could never be typed.");
                }
            }
        }
    }
}
