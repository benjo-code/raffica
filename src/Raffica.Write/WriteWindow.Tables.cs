using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The table commands of the window (the grid of the Insert tab and the Table tab): what the ribbon, the command search and the macOS menu run, the facts that enable them and the three cards. Nothing exists before the
/// first question: the facts source is made when a rule first asks (the Insert or Table band, the command search or a command by id), it reads nothing for a tab without an editing session, the lists of the two pickers are
/// read when one opens, and the grid of Insert table is made when its popup first opens.
/// </summary>
internal sealed partial class WriteWindow
{
    private TableFactsSource? _tables;
    private TablePickers _tablePickers = null!;
    private ITableCards? _tableCards;
    private Flyout? _tableGrid;
    private TableGridPicker? _tableGridPicker;

    /// <summary>The cards Insert table (more sizes), Borders and Table properties open: the dialogs of the window (made on first use), or the cards a test put in.</summary>
    internal ITableCards TableCards
    {
        get => _tableCards ??= new DialogTableCards(Dialogs);
        set => _tableCards = value;
    }

    /// <summary>The pickers of the Table tab (tests read how often a list was asked).</summary>
    internal TablePickers TablePickerSet => _tablePickers;

    /// <summary>Whether the facts source of the table commands exists (tests; false before the first question of a table command).</summary>
    internal bool TableFactsExist => _tables is not null;

    /// <summary>How many times the editor was asked for the facts of the table commands (tests; 0 before the first ask).</summary>
    internal int TableFactReads => _tables?.Reads ?? 0;

    /// <summary>How many times the grid of Insert table was built (tests; 0 before its popup first opens, then 1 for the life of the window).</summary>
    internal int TableGridBuilds { get; private set; }

    /// <summary>The grid of Insert table, once built (tests).</summary>
    internal TableGridPicker? TableGrid => _tableGridPicker;

    // The facts of the rules, the switch and the pickers; the source is made on the first question. Nothing is read before the active tab has an editing session.
    private TableCommandFacts ReadTableFacts(string id) => (_tables ??= new TableFactsSource()).Read(Workspace.Active, id, ActiveView?.Mode == WriteView.Writing);

    // The pickers of the Table tab: made with the ribbon (two small records, no read), the lists are asked when one opens.
    private void CreateTablePickers() =>
        _tablePickers = new TablePickers((id, value) => RunCommand(id, value), () => _ = GuardAsync(TableBordersAsync));

    // The ribbon actions of the table commands: the buttons run by id; the value commands (the size of a table, a border, a shading colour, the properties) run through RunCommand(id, payload) with the value their
    // picker or card chose; Insert table opens the grid and Table properties its card.
    private void AddTableActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsTable(c.Id)))
        {
            var id = command.Id;
            if (command.TakesPayload)
            {
                _payloadActions[id] = payload => RunTableFromUi(id, payload);
            }

            switch (id)
            {
                case WriteCommandIds.TableInsert:
                    _actions[id] = ShowTableGrid;
                    break;
                case WriteCommandIds.TableProperties:
                    _actions[id] = () => _ = GuardAsync(TablePropertiesAsync);
                    break;
                case WriteCommandIds.TableBorders:
                    // The picker offers the presets and the sides; this action (the menu, "More borders") opens the card.
                    _actions[id] = () => _ = GuardAsync(TableBordersAsync);
                    break;
                case WriteCommandIds.TableShading:
                    // The picker runs its choice; the command search opens the picker (a picker has nothing to run).
                    break;
                default:
                    _actions[id] = () => _ = RunTableFromUi(id, null);
                    break;
            }
        }
    }

    // The ribbon, the command search and the menu run a table command on the active view; the view takes the focus back (a ribbon button that kept it would swallow the typing).
    private bool RunTableFromUi(string id, object? payload)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunTableCommand(id, payload);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    /// <summary>
    /// Opens the grid of Insert table under its button (under the ribbon when the Insert band is not shown, as when the command search or the menu runs it). The grid is built the first time and reset each time; a
    /// choice runs Insert table with the size and closes it, "More…" closes it and asks the card.
    /// </summary>
    internal void ShowTableGrid()
    {
        if (ActiveView is null || !_rules.Allows(WriteCommandIds.TableInsert))
        {
            return;
        }

        if (_tableGrid is null)
        {
            TableGridBuilds++;
            var picker = new TableGridPicker(
                size =>
                {
                    _tableGrid?.Hide();
                    _ = RunTableFromUi(WriteCommandIds.TableInsert, size);
                },
                () =>
                {
                    _tableGrid?.Hide();
                    _ = GuardAsync(TableInsertAsync);
                },
                () => _tableGrid?.Hide());
            _tableGridPicker = picker;
            _tableGrid = new Flyout { Content = picker, Placement = PlacementMode.BottomEdgeAlignedLeft };
            _tableGrid.Opened += (_, _) => picker.Reset();
        }

        Control anchor = Ribbon.GetVisualDescendants().OfType<RibbonButton>().FirstOrDefault(b => b.Definition.Id == WriteCommandIds.TableInsert) ?? (Control)Ribbon;
        _tableGrid.ShowAt(anchor);
    }

    // Insert table with any size: the card first, then the command with its answer (the plan check of the other cards: a card answered after the document changed is refused with a note).
    private async Task TableInsertAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        var plan = CardPlan.Of(view);
        if (await TableCards.AskInsertAsync() is not { } size || CardIsStale(view, plan))
        {
            return;
        }

        _ = RunTableFromUi(WriteCommandIds.TableInsert, size);
    }

    // Borders: the card, then the command with the spec the card made (the same plan check).
    private async Task TableBordersAsync()
    {
        if (ActiveView is not { } view || !_rules.Allows(WriteCommandIds.TableBorders))
        {
            return;
        }

        var plan = CardPlan.Of(view);
        if (await TableCards.AskBordersAsync() is not { } spec || CardIsStale(view, plan))
        {
            return;
        }

        _ = RunTableFromUi(WriteCommandIds.TableBorders, spec);
    }

    // Table properties: the card with the values of the table as the page draws it, then the command with the parts that changed (the same plan check).
    private async Task TablePropertiesAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        if (view.TablePropertiesFactsAtCaret(out var note) is not { } facts)
        {
            ShowStructureNote(view, note ?? WriteStrings.NoteStructureFailed);
            return;
        }

        var plan = CardPlan.Of(view);
        if (await TableCards.AskPropertiesAsync(new TablePropertiesContext(facts, view.MarginsInInches())) is not { } properties || CardIsStale(view, plan))
        {
            return;
        }

        _ = RunTableFromUi(WriteCommandIds.TableProperties, properties);
    }
}
