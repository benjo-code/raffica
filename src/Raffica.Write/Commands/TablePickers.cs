using Raffica.Shell.Frame;

namespace Raffica.Write.Commands;

/// <summary>
/// The pickers of the Table tab: Borders (the presets, the single sides and "More borders" for the card) and Shading (no shading and the fixed colours of the Home tab). Each shows the name of its command (neither has a
/// selection of its own: the cells can differ), lists its items when it opens and never before, and runs a choice through <paramref name="run"/> (<c>WriteWindow.RunCommand(id, payload)</c>, which validates it);
/// "More borders" calls <paramref name="openBordersCard"/> instead. The grid of Insert table is no picker of the shell (a list cannot show a hover grid): it is the popup of <c>Viewing/TableGridPicker</c>, made when it first opens.
/// </summary>
/// <param name="run">Runs a value command with its payload; false when it was refused.</param>
/// <param name="openBordersCard">Opens the Borders card (the window asks it and runs the answer).</param>
internal sealed class TablePickers(Func<string, object?, bool> run, Action openBordersCard)
{
    private const string Unset = "-";
    private const string More = "more";

    /// <summary>How many times a list was asked (a list is read when it opens, never before; tests pin that).</summary>
    internal int ListReads { get; private set; }

    /// <summary>The picker of a command, or null when the command is no picker.</summary>
    public PickerDefinition? For(string commandId) => commandId switch
    {
        WriteCommandIds.TableBorders => Counted(new PickerDefinition(
            BorderItems,
            id =>
            {
                if (id == More)
                {
                    openBordersCard();
                }
                else
                {
                    run(WriteCommandIds.TableBorders, id);
                }
            },
            () => Unset,
            SelectedLabel: () => NameOf(WriteCommandIds.TableBorders),
            Width: 130)),
        WriteCommandIds.TableShading => Counted(new PickerDefinition(
            ShadingItems,
            id => run(WriteCommandIds.TableShading, id),
            () => Unset,
            SelectedLabel: () => NameOf(WriteCommandIds.TableShading),
            Width: 130)),
        _ => null,
    };

    private PickerDefinition Counted(PickerDefinition picker) => picker with
    {
        Items = () =>
        {
            ListReads++;
            return picker.Items();
        },
    };

    // The words of a border preset in the UI language.
    internal static string BorderLabel(string id) => id switch
    {
        "all" => WriteStrings.TableBordersAll,
        "outside" => WriteStrings.TableBordersOutside,
        "box" => WriteStrings.TableBordersBox,
        "none" => WriteStrings.TableBordersNone,
        "top" => WriteStrings.TableBordersTop,
        "bottom" => WriteStrings.TableBordersBottom,
        "left" => WriteStrings.TableBordersLeft,
        "right" => WriteStrings.TableBordersRight,
        "insideH" => WriteStrings.TableBordersInsideHorizontal,
        "insideV" => WriteStrings.TableBordersInsideVertical,
        _ => WriteStrings.TableBordersMore,
    };

    private static IReadOnlyList<PickerItem> BorderItems() => [.. TablePayloads.BorderPresets.Select(id => new PickerItem(id, BorderLabel(id))), new PickerItem(More, BorderLabel(More))];

    private static IReadOnlyList<PickerItem> ShadingItems() =>
        [new PickerItem(TablePayloads.NoShading, WriteStrings.TableShadingNone), .. ColorChoices.TextColors.Select(c => new PickerItem(c.Id, WriteStrings.Get(c.NameKey)))];

    private static string NameOf(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id).Name;
}
