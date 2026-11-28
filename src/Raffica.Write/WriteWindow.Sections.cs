using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;

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

    private ISectionCards? _sectionCards;

    /// <summary>The cards Number pages from here and Page setup open: the dialogs of the window (made on first use), or the cards a test put in.</summary>
    internal ISectionCards SectionCards
    {
        get => _sectionCards ??= new DialogSectionCards(Dialogs);
        set => _sectionCards = value;
    }

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

    // Number pages from here: the card first (start, format, prefix), then the command with its answer. The card is modal and the answer is run only when the document is as it was when the card opened
    // (the plan version, as the outline drop does): a card answered after the document changed is refused with a note, never applied.
    private async Task NumberPagesFromAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        // The place is described first (the session is made for it when the tab has none): a place that cannot be described ends in a note and never in a default numbering, the card is always asked.
        if (view.SectionFactsAtCaret(out var note) is not { } facts)
        {
            ShowStructureNote(view, note ?? WriteStrings.NoteStructureFailed);
            return;
        }

        var plan = CardPlan.Of(view);
        var context = new NumberPagesContext(facts, view.CaretBlockStartsNewPage(facts));
        if (await SectionCards.AskNumberingAsync(context) is not { } numbering)
        {
            return;
        }

        if (CardIsStale(view, plan))
        {
            return;
        }

        _ = RunSectionFromUi(WriteCommandIds.NumberPagesFrom, numbering);
    }

    // Page setup: the card with the values of the caret's section and the scope, then the command with its answer (the same plan check as the numbering card).
    private async Task PageSetupAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        if (view.CaretSectionSetup(out var note) is not { } current)
        {
            ShowStructureNote(view, note ?? WriteStrings.NoteStructureFailed);
            return;
        }

        var plan = CardPlan.Of(view);
        var scope = current.Sections == 1 ? ApplyScope.WholeDocument : ApplyScope.ThisSection;
        var context = new PageSetupContext(current.Setup, scope, current.Sections, current.ColumnsUnequal, view.MarginsInInches(), current.CaretSection, current.UnequalSections);
        if (await SectionCards.AskPageSetupAsync(context) is not { } request)
        {
            return;
        }

        if (CardIsStale(view, plan))
        {
            return;
        }

        _ = RunSectionFromUi(WriteCommandIds.PageSetup, request);
    }

    // What a card was asked about: the view, its model and the revision of the model. A card that is answered after any of them changed (a replaced model, an edit, an undo, another active tab) must not run.
    private readonly record struct CardPlan(PageView View, WordDocument Model, EditStamp Stamp)
    {
        public static CardPlan Of(PageView view) => new(view, view.Tab.Docx.Model, view.Tab.Docx.Model.Edits.Stamp);
    }

    private bool CardIsStale(PageView view, CardPlan plan)
    {
        if (!ReferenceEquals(ActiveView, view) || view.IsReleased)
        {
            return true;
        }

        var model = view.Tab.Docx.Model;
        if (ReferenceEquals(model, plan.Model) && model.Edits.Stamp == plan.Stamp)
        {
            return false;
        }

        ShowStructureNote(view, WriteStrings.NoteCardStale);
        return true;
    }

    private StoryStatusMemo? _storyStatus;

    /// <summary>How many times the sharing of the active header or footer was worked out from the sections (tests; typing in a footer adds nothing).</summary>
    internal int StoryStatusReads { get; private set; }

    /// <summary>
    /// The words for the status line while a header or footer is active: which one, the section that owns its part and the sections that share it ("Footer, section 2, shared with sections 3 to 5"), because
    /// editing a shared part edits it for each of them. Null in the body and while the caret is not in the active story; the kind alone when no section shows the part (no misleading claim). The text is worked out
    /// from the sections once and kept while the model, the editor, the active story, the language and <see cref="EditSession.SectionEpoch"/> are the same: a view change (every typed character, every scroll step)
    /// walks no section.
    /// </summary>
    internal string? StoryStatusText(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.ActiveStory is not { Kind: StoryKind.HeaderFooter } story || view.Tab.Editor is not { } session || session.Caret is not { } caret || !caret.Story.Equals(story))
        {
            return null;
        }

        var model = view.Tab.Docx.Model;
        var culture = WriteStrings.Culture;
        if (_storyStatus is { } memo && ReferenceEquals(memo.Model, model) && ReferenceEquals(memo.Session, session) && memo.Story.Equals(story) && memo.Epoch == session.SectionEpoch && ReferenceEquals(memo.Culture, culture))
        {
            return memo.Text;
        }

        StoryStatusReads++;
        string? text = null;
        if (story.Key is { } key && model.HeadersFooters.TryGetValue(key, out var part))
        {
            text = StoryStatusLine(part.Kind, session.Editor.PageNumberSharing(caret, part.Kind));
        }

        _storyStatus = new StoryStatusMemo(model, session, story, session.SectionEpoch, culture, text);
        return text;
    }

    private sealed record StoryStatusMemo(WordDocument Model, EditSession Session, StoryRef Story, int Epoch, System.Globalization.CultureInfo Culture, string? Text);

    /// <summary>The line for a part of <paramref name="kind"/> that <paramref name="sharing"/> describes: the owner and the others, or the kind alone when no section (or not the owner) shows it.</summary>
    internal static string StoryStatusLine(HeaderFooterKind kind, SectionSharing sharing)
    {
        ArgumentNullException.ThrowIfNull(sharing);
        var label = kind == HeaderFooterKind.Footer ? WriteStrings.PageNumberPlaceFooter : WriteStrings.PageNumberPlaceHeader;
        if (sharing.OwnerSection < 0 || !sharing.Sections.Contains(sharing.OwnerSection))
        {
            return label;
        }

        var owner = sharing.OwnerSection + 1;
        var others = sharing.Sections.Where(i => i != sharing.OwnerSection).Select(i => i + 1).ToList();
        return others.Count switch
        {
            0 => WriteStrings.Fill(WriteStrings.StatusStoryFormat, label, owner),
            1 => WriteStrings.Fill(WriteStrings.StatusStorySharedOneFormat, label, owner, others[0]),
            _ => WriteStrings.Fill(WriteStrings.StatusStorySharedManyFormat, label, owner, SectionNumberList.Describe(others)),
        };
    }

    // Whether a switch of the Layout tab is on: the caret's section has a different first page, the document has left and right pages.
    private bool SectionIsOn(string id) => id switch
    {
        WriteCommandIds.DifferentFirstPage => ReadSectionFacts(id).FirstPageOn,
        WriteCommandIds.LeftRightPages => ReadSectionFacts(id).LeftRightOn,
        _ => false,
    };
}
