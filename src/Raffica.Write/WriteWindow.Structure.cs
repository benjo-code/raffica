using Avalonia.Input;
using Avalonia.Threading;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The structure commands of the window (lists, heading numbering, outline move, promote and demote): what the ribbon, the command search and the macOS menu run, the facts that enable them and the note
/// of a command that did nothing. Nothing exists before the first question: the facts source is made when a rule first asks, and it reads nothing for a tab without an editing session.
/// </summary>
internal sealed partial class WriteWindow
{
    private StructureFactsSource? _structure;

    /// <summary>How many times the model was read for the facts of the structure commands (tests; 0 before the first ask).</summary>
    internal int StructureReads => _structure?.Reads ?? 0;

    // The outline row the commands act on: the selected row of the pane of the active tab while the user works in it, else null (the heading at the caret).
    private Paragraph? EngagedOutlineParagraph() =>
        Workspace.Active is { } tab && _outlines.TryGetValue(tab, out var pane) && pane.Engaged ? pane.SelectedParagraph : null;

    // The facts of the rules and the switches; the source is made on the first question. Nothing is read before the active tab has an editing session.
    private StructureFacts ReadStructureFacts() => (_structure ??= new StructureFactsSource()).Read(Workspace.Active, EngagedOutlineParagraph());

    // The ribbon actions of the nine commands.
    private void AddStructureActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsStructure(c.Id)))
        {
            var id = command.Id;
            _actions[id] = () => _ = RunStructureFromUi(id);
        }
    }

    // The ribbon, the command search and the menu run a structure command on the active view. The view takes the focus back unless the user is working in the outline pane (then its keys go on working).
    private bool RunStructureFromUi(string id)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var inPane = _outlines.Values.Any(p => p.IsKeyboardFocusWithin);
        var taken = view.RunStructureCommand(id);
        if (!inPane && !view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    // The keys of the outline commands in the outline pane: the page view owns them while it has the focus, the window forwards them when the focus is in the pane's list.
    private bool ForwardStructureKey(KeyEventArgs e, bool repeat)
    {
        if (!_outlines.Values.Any(p => p.IsKeyboardFocusWithin) || EditingKeys.StructureCommandFor(e.Key, e.KeyModifiers, OperatingSystem.IsMacOS()) is not { } id)
        {
            return false;
        }

        e.Handled = true;
        if (!repeat)
        {
            RunCommand(id);
        }

        return true;
    }

    // Whether a structure switch is on: bullets and numbers follow the list facts of the selection (every touched paragraph is in a list of that kind), the heading numbering follows the styles.
    private bool StructureIsOn(string id) => id switch
    {
        WriteCommandIds.NumberHeadings => Workspace.Active is { } tab && HeadingNumbering.StateOf(tab.Docx.Model) == HeadingNumberingState.On,
        WriteCommandIds.Bullets => ReadStructureFacts().List.Kind == ListKind.Bullet && ReadStructureFacts().List.InList,
        WriteCommandIds.Numbers => ReadStructureFacts().List.Kind == ListKind.Numbered && ReadStructureFacts().List.InList,
        _ => false,
    };

    // A structure command did nothing and the user must hear of it: the words stay in the status bar for a few seconds (the hint of a refused key uses the same slot).
    private void ShowStructureNote(PageView view, string text)
    {
        if (!ReferenceEquals(ActiveView, view))
        {
            return;
        }

        Status.Hint = text;
        _editHint = text;
        var generation = ++_editHintGeneration;
        DispatcherTimer.RunOnce(
            () =>
            {
                if (generation == _editHintGeneration)
                {
                    _editHint = null;
                    if (Status.Hint == text)
                    {
                        Status.Hint = null;
                    }
                }
            },
            EditHintDuration);
    }
}
