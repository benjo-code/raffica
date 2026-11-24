using Raffica.Write.Commands;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The object commands of the window (the Insert tab; today the note commands: footnote, endnote and Back to the text): what the ribbon, the command search and the macOS menu run and the facts that enable them.
/// Nothing exists before the first question: the facts source is made when a rule first asks (the Insert band, the command search or a command by id), it reads nothing for a tab without an editing session, and
/// the writing view and a prefix document are answered from flags. The later object commands (tables, pictures) join this file and the source.
/// </summary>
internal sealed partial class WriteWindow
{
    private ObjectFactsSource? _objects;

    /// <summary>Whether the facts source of the object commands exists (tests; false before the first question of an object command).</summary>
    internal bool ObjectFactsExist => _objects is not null;

    /// <summary>How many times the caret's place was worked out for the facts of the object commands (tests; 0 before the first ask).</summary>
    internal int ObjectFactReads => _objects?.Reads ?? 0;

    // The facts of the rules; the source is made on the first question. Nothing is read before the active tab has an editing session.
    private ObjectCommandFacts ReadObjectFacts(string id) =>
        (_objects ??= new ObjectFactsSource()).Read(Workspace.Active, id, ActiveView?.Mode == WriteView.Writing);

    // The ribbon actions of the note commands: the buttons run by id.
    private void AddObjectActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsNote(c.Id)))
        {
            var id = command.Id;
            _actions[id] = () => _ = RunNoteFromUi(id);
        }
    }

    // The ribbon, the command search and the menu run a note command on the active view; the view takes the focus back (a ribbon button that kept it would swallow the typing).
    private bool RunNoteFromUi(string id)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunNoteCommand(id);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }
}
