using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write;

/// <summary>
/// The section commands of the window (the Layout tab: Number pages from here, section breaks, Remove section, the header and footer switches, the page setup commands and Insert page number): what the ribbon,
/// the command search and the macOS menu run, the facts that enable them and the note of a command that did nothing. Nothing exists before the first question: the facts source is made when a rule first
/// asks (the Layout band, the command search or a command by id), it reads nothing for a tab without an editing session, and the lists of the pickers are read when a list opens.
/// </summary>
internal sealed partial class WriteWindow
{
    private SectionFactsSource? _sections;
    private SectionPickers _sectionPickers = null!;

    /// <summary>The cards Number pages from here and Page setup open; the default is <see cref="PendingSectionCards"/> until the real cards replace it.</summary>
    internal ISectionCards SectionCards { get; set; } = new PendingSectionCards();

    /// <summary>The pickers of the Layout tab (tests read how often a list was asked).</summary>
    internal SectionPickers SectionPickerSet => _sectionPickers;

    /// <summary>Whether the facts source of the section commands exists (tests; false before the first question of a section command).</summary>
    internal bool SectionFactsExist => _sections is not null;

    /// <summary>How many times the section list of the body was read for the facts (tests; 0 before the first ask of a section command).</summary>
    internal int SectionFactReads => _sections?.Reads ?? 0;

    // The facts of the rules, the switches and the pickers; the source is made on the first question. Nothing is read before the active tab has an editing session.
    private SectionCommandFacts ReadSectionFacts(string id) => (_sections ??= new SectionFactsSource()).Read(Workspace.Active, id);

    // The pickers of the Layout tab: made with the ribbon (a few small records, no read), the lists are asked when one opens.
    private void CreateSectionPickers() =>
        _sectionPickers = new SectionPickers(ReadSectionFacts, () => Workspace.Active?.Docx.Model, (id, value) => RunCommand(id, value));

    // The ribbon actions of the eleven commands: the buttons run by id, the value commands (a size, a margin set, an orientation, columns, a break, a page number) run through RunCommand(id, payload) with the
    // value their picker chose, and the two commands with a card ask it first.
    private void AddSectionActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsSection(c.Id)))
        {
            var id = command.Id;
            if (command.TakesPayload)
            {
                _payloadActions[id] = payload => RunSectionFromUi(id, payload);
            }
            else if (id == WriteCommandIds.NumberPagesFrom)
            {
                _actions[id] = () => _ = GuardAsync(NumberPagesFromAsync);
            }
            else if (id == WriteCommandIds.PageSetup)
            {
                _actions[id] = () => _ = GuardAsync(PageSetupAsync);
            }
            else
            {
                _actions[id] = () => _ = RunSectionFromUi(id, null);
            }
        }
    }

    // The ribbon, the command search and the menu run a section command on the active view; the view takes the focus back (a ribbon button that kept it would swallow the typing).
    private bool RunSectionFromUi(string id, object? payload)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunSectionCommand(id, payload);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    // Number pages from here: the card first (start, format, prefix), then the command with its answer. A caret the card cannot describe (the facts say no) is the rules' business; a stale answer ends as a note.
    private async Task NumberPagesFromAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        if (view.SectionFactsAtCaret() is not { } facts)
        {
            _ = view.RunSectionCommand(WriteCommandIds.NumberPagesFrom);
            return;
        }

        if (await SectionCards.AskNumberingAsync(facts) is { } numbering)
        {
            _ = RunSectionFromUi(WriteCommandIds.NumberPagesFrom, numbering);
        }
    }

    // Page setup: the card with the values of the caret's section and the scope; until the card exists the window says so and changes nothing.
    private async Task PageSetupAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        if (!SectionCards.HasPageSetupCard)
        {
            ShowStructureNote(view, WriteStrings.NoteSectionCardSoon);
            return;
        }

        if (view.CaretSectionSetup() is not { } current)
        {
            ShowStructureNote(view, WriteStrings.NoteStructureFailed);
            return;
        }

        var scope = current.Sections == 1 ? ApplyScope.WholeDocument : ApplyScope.ThisSection;
        if (await SectionCards.AskPageSetupAsync(current.Setup, scope, current.Sections) is { } request)
        {
            _ = RunSectionFromUi(WriteCommandIds.PageSetup, request);
        }
    }

    // Whether a switch of the Layout tab is on: the caret's section has a different first page, the document has left and right pages.
    private bool SectionIsOn(string id) => id switch
    {
        WriteCommandIds.DifferentFirstPage => ReadSectionFacts(id).FirstPageOn,
        WriteCommandIds.LeftRightPages => ReadSectionFacts(id).LeftRightOn,
        _ => false,
    };
}
