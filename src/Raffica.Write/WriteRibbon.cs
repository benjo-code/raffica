using Raffica.Shell.Frame;
using Raffica.Write.Commands;

namespace Raffica.Write;

/// <summary>
/// The ribbon of the Write app: the tabs Home (letter H), Insert (letter I), Table (letter T), Layout (letter P) and View (letter V), built from <see cref="WriteCommandCatalog"/>. The field codes follow the scheme of the
/// frame: the digit of the group plus the position in the group, so the codes of a tab never begin one another. A command is enabled when an action is wired and the
/// rules allow it (<see cref="WriteCommandRules"/>); a disabled command does nothing, is dimmed, tells its reason and is skipped by the field codes.
/// </summary>
internal static class WriteRibbon
{
    public const string HomeLetter = "H";
    public const string InsertLetter = "I";
    public const string TableLetter = "T";
    public const string LayoutLetter = "P";
    public const string ViewLetter = "V";

    /// <summary>
    /// A width in px that the full band of the tab Home is not narrower than, in English and in German (it measures about 3000 px; a test pins this value against the real band): a
    /// window narrower than that starts with the compact band and never measures the full one (<see cref="RibbonTabDefinition.FullWidthAtLeast"/>).
    /// </summary>
    internal const double HomeFullWidthAtLeast = 2500;

    /// <param name="actions">What the commands do, by command id; a command with no entry is disabled.</param>
    /// <param name="rules">When they may run (null: whenever an action is wired).</param>
    /// <param name="isOn">Whether a switch or choice command (<see cref="CatalogCommand.Toggle"/>) is on now, by command id (null: none is).</param>
    /// <param name="pickers">The picker of a value command (font, size, colour, highlight, spacing, line spacing, styles) by command id, or null for a command that is a button; null: every command is a button.</param>
    public static RibbonDefinition Create(IReadOnlyDictionary<string, Action> actions, WriteCommandRules? rules = null, Func<string, bool>? isOn = null, Func<string, PickerDefinition?>? pickers = null)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var mac = OperatingSystem.IsMacOS();
        CommandDefinition Cmd(CatalogCommand command)
        {
            var run = actions.GetValueOrDefault(command.Id);
            var picker = command.TakesPayload ? pickers?.Invoke(command.Id) : null;
            return new CommandDefinition(
                command.Id,
                command.Name,
                "Raffica.Icon." + command.Icon,
                command.FieldCode,
                run,
                command.Size,
                CompactPlacement.Inline,
                IconOnly: false,
                Shortcut: command.ShortcutFor(mac)?.Text(mac),
                Description: command.Description,
                EnabledWhen: () => (run is not null || picker is not null) && (rules is null || rules.Allows(command.Id)),
                DisabledReason: () => rules?.ReasonFor(command.Id),
                CheckedWhen: command.Toggle ? () => isOn?.Invoke(command.Id) == true : null,
                Picker: picker);
        }

        var tabs = WriteCommandCatalog.Tabs
            .Select(tab => new RibbonTabDefinition(
                tab.Name,
                tab.Letter,
                tab.Groups.Select(group => new RibbonGroupDefinition(group.Label, group.Digit, group.Commands.Select(Cmd).ToList(), Destructive: false)).ToList(),
                tab.Letter == HomeLetter ? HomeFullWidthAtLeast : 0))
            .ToList();

        var definition = new RibbonDefinition(tabs);
        definition.Validate();
        return definition;
    }
}
