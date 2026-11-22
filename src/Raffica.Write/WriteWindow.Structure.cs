using Avalonia.Input;
using Avalonia.Threading;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The structure commands of the window (lists, heading numbering, outline move, promote and demote): what the ribbon, the command search and the macOS menu run, the facts that enable them and the note
/// of a command that did nothing. Nothing exists before the first question: the facts source is made when a rule first asks, and it reads nothing for a tab without an editing session.
/// </summary>
internal sealed partial class WriteWindow
{
    private StructureFactsSource? _structure;

    /// <summary>How many times the headings of the body were walked for the outline commands (tests; 0 before the first ask of an outline command).</summary>
    internal int StructureHeadingReads => _structure?.HeadingReads ?? 0;

    // Whether the outline pane being engaged should refresh the ribbon states: it does not. The pane row it selects follows a click or a key that already moved the selection or the caret, which refreshes the states
    // through the debounced path; a change of only the engaged flag (focus moving between the list and the page) leaves every state as it was until the next selection or edit.
    // The outline row the commands act on: the selected row of the pane of the active tab while the user works in it, else null (the heading at the caret).
    // A button of the pane (or a drop) that is running or being asked about acts on the pane's selected row whether or not the list has the keyboard.
    private Paragraph? EngagedOutlineParagraph() =>
        _outlineActing is { } acting ? acting.SelectedParagraph
        : Workspace.Active is { } tab && _outlines.TryGetValue(tab, out var pane) && pane.Engaged ? pane.SelectedParagraph : null;

    // The pane whose button is being asked about or run (set only for the duration of that call).
    private OutlinePane? _outlineActing;

    private T ActingFor<T>(OutlinePane pane, Func<T> ask)
    {
        var before = _outlineActing;
        _outlineActing = pane;
        try
        {
            return ask();
        }
        finally
        {
            _outlineActing = before;
        }
    }

    // What the outline pane of a tab asks of the window: the same rules, reasons and run path as the ribbon (for the pane's selected row), the Alt and Shift keys typed in its list, a drop and a note.
    private OutlinePaneActions CreateOutlineActions(WriteTab tab) => new()
    {
        Allows = (pane, id) => ActingFor(pane, () => _rules.Allows(id)),
        ReasonFor = (pane, id) => ActingFor(pane, () => _rules.ReasonFor(id)),
        Run = (pane, id) => ActingFor(pane, () =>
        {
            if (_rules.Allows(id))
            {
                return RunCommand(id);
            }

            if (_rules.ReasonFor(id) is { } reason && ViewOf(tab) is { } view)
            {
                ShowStructureNote(view, reason);
            }

            return false;
        }),
        Key = OutlineKey,
        Drop = (dragged, onto, place) => ViewOf(tab) is { } view && view.RunOutlineDrop(dragged, onto, place),
        Note = text =>
        {
            if (ViewOf(tab) is { } view)
            {
                ShowStructureNote(view, text);
            }
        },
    };

    // An outline key typed in the pane's list (its tunnel handler asks first: the list would take the arrows itself). The window's own handler never saw the key, so the repeat bookkeeping is done here.
    private bool OutlineKey(KeyEventArgs e)
    {
        if (Host.IsOpen)
        {
            return false;
        }

        return ForwardStructureKey(e, !_keysDown.Add(e.Key));
    }

    // The buttons of the active tab's outline pane follow the rules again (the ribbon's states were refreshed).
    private void RefreshOutlineButtons()
    {
        if (_outlineHost is { IsOpen: true } && Workspace.Active is { } tab && _outlines.TryGetValue(tab, out var pane))
        {
            pane.RefreshButtons();
        }
    }

    // The facts of the rules and the switches; the source is made on the first question. Nothing is read before the active tab has an editing session.
    private StructureFacts ReadStructureFacts(string id) => (_structure ??= new StructureFactsSource()).Read(Workspace.Active, EngagedOutlineParagraph(), id);

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

        var inPane = _outlineActing is not null || _outlines.Values.Any(p => p.IsKeyboardFocusWithin);
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
            // A key the rules refuse shows its reason, as in the page view.
            if (!_rules.Allows(id) && _rules.ReasonFor(id) is { } reason && ActiveView is { } view)
            {
                ShowStructureNote(view, reason);
            }
            else
            {
                RunCommand(id);
            }
        }

        return true;
    }

    // A structure key of the page view: the rules decide, and a key they refuse shows its reason (the key stays claimed, as the formatting keys do).
    private bool RunStructureKey(PageView view, string id)
    {
        if (_rules.Allows(id))
        {
            return view.RunStructureCommand(id);
        }

        if (_rules.ReasonFor(id) is { } reason)
        {
            ShowStructureNote(view, reason);
        }

        return false;
    }

    // Whether a structure switch is on: bullets and numbers follow the list facts of the selection (every touched paragraph is in a list of that kind), the heading numbering follows the styles.
    private bool StructureIsOn(string id) => id switch
    {
        WriteCommandIds.NumberHeadings => Workspace.Active is { } tab && HeadingNumbering.StateOf(tab.Docx.Model) == HeadingNumberingState.On,
        WriteCommandIds.Bullets => ReadStructureFacts(id).List is { InList: true, Kind: ListKind.Bullet },
        WriteCommandIds.Numbers => ReadStructureFacts(id).List is { InList: true, Kind: ListKind.Numbered },
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
