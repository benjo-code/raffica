using Raffica.Pdf.Commands;
using Raffica.Shell.Frame;

namespace Raffica.Pdf;

/// <summary>
/// The ribbon of the PDF app: the tabs Home (letter H) and View (letter V), built from <see cref="PdfCommandCatalog"/>. The field codes follow
/// the scheme of the frame: the digit of the group plus the position in the group (13 is the third command of group 1), so the codes of a
/// tab never begin one another. A command is enabled when an action is wired in <see cref="PdfCommands"/> and the rules allow it (see
/// <see cref="PdfCommandRules"/>); a disabled command does nothing, is dimmed and is skipped by the field codes.
/// </summary>
internal static class PdfRibbon
{
    public const string HomeLetter = "H";
    public const string ViewLetter = "V";

    /// <param name="commands">What the commands do.</param>
    /// <param name="rules">When they may run (null: whenever an action is wired).</param>
    public static RibbonDefinition Create(PdfCommands commands, PdfCommandRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var mac = OperatingSystem.IsMacOS();
        CommandDefinition Cmd(CatalogCommand command)
        {
            var run = commands.RunOf(command.Id);
            return new CommandDefinition(
                command.Id,
                command.Name,
                "Raffica.Icon." + command.Icon,
                command.FieldCode,
                run,
                command.Size,
                CompactPlacement.Inline,
                IconOnly: false,
                Shortcut: command.Shortcut?.Text(mac),
                EnabledWhen: () => run is not null && (rules is null || rules.Allows(command.Id)),
                DisabledReason: () => rules?.ReasonFor(command.Id));
        }

        var tabs = PdfCommandCatalog.Tabs
            .Select(tab => new RibbonTabDefinition(
                tab.Name,
                tab.Letter,
                tab.Groups.Select(group => new RibbonGroupDefinition(group.Label, group.Digit, group.Commands.Select(Cmd).ToList())).ToList()))
            .ToList();

        var definition = new RibbonDefinition(tabs);
        definition.Validate();
        return definition;
    }
}
