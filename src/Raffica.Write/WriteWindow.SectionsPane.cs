using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The Sections pane of the window. It fills the same right slot as the formatting pane (one at a time: opening one closes the other), made on first use for each tab and kept until the tab closes; nothing of
/// it (no source, no pane, no timer, no read) exists before the user asks for it.
/// </summary>
internal sealed partial class WriteWindow
{
    private readonly Dictionary<WriteTab, SectionsPane> _sectionsPanes = [];
    private bool _sectionsWanted;

    /// <summary>Whether the Sections pane is on (the user asked for it and a document is open).</summary>
    internal bool IsSectionsOpen => _sectionsWanted && _formattingHost is { IsOpen: true };

    /// <summary>The Sections pane of a tab, or null while none was made (tests).</summary>
    internal SectionsPane? SectionsOf(WriteTab tab) => _sectionsPanes.GetValueOrDefault(tab);

    // The command of the pane: a switch. Opening it puts the focus in the pane (and closes the formatting pane, which has the slot); closing it gives the focus back to the document.
    private void ToggleSections()
    {
        _sectionsWanted = !_sectionsWanted;
        if (_sectionsWanted)
        {
            _formattingWanted = false;
        }

        PlaceFormatting();
        FocusSlotOrView(_sectionsWanted);
        RefreshCommandStates();
    }

    private SectionsPane SectionsPaneOf(WriteTab tab)
    {
        if (!_sectionsPanes.TryGetValue(tab, out var pane))
        {
            var actions = new SectionsPaneActions(
                block => tab.Layout.Locate(block) is { } place ? place.Page : -1,
                () => ActiveSectionOf(tab),
                () => SectionsBlockedReason(tab),
                action => RunSectionRow(tab, action));
            pane = new SectionsPane(tab, actions);
            _sectionsPanes[tab] = pane;
        }

        return pane;
    }

    // A tab closed: its pane goes with it.
    private void ForgetSections(WriteTab tab)
    {
        if (_sectionsPanes.Remove(tab, out var pane))
        {
            pane.Dispose();
        }
    }

    // The section the caret is in, or, with a header or footer active, the section that owns that part (the one the status line names); null without a caret.
    private static int? ActiveSectionOf(WriteTab tab)
    {
        if (tab.Editor is not { } session || session.Caret is not { } caret)
        {
            return null;
        }

        var model = tab.Docx.Model;
        if (caret.Story.Kind == StoryKind.HeaderFooter)
        {
            if (caret.Story.Key is { } key && model.HeadersFooters.TryGetValue(key, out var part))
            {
                var owner = session.Editor.PageNumberSharing(caret, part.Kind).OwnerSection;
                return owner >= 0 ? owner : null;
            }

            return null;
        }

        var facts = session.Editor.ReadSectionFacts(caret);
        return facts.Reason == SectionReason.None ? facts.Section : null;
    }

    // Why the controls of the pane cannot change the document now: a prefix document or a document that is still opening, a save that runs, a document that cannot be edited.
    private static string? SectionsBlockedReason(WriteTab tab) =>
        tab.Docx.IsPrefix ? WriteStrings.ReasonEditOpening : tab.Editor?.BlockedReason switch
        {
            EditBlockedReason.StillOpening => WriteStrings.ReasonEditOpening,
            EditBlockedReason.Saving => WriteStrings.ReasonEditSaving,
            EditBlockedReason.Faulted => WriteStrings.EditUnavailable,
            _ => null,
        };

    // A control of a row: the page view runs it (the focus stays in the pane, where the keyboard user is); Number from here asks its card first.
    private bool RunSectionRow(WriteTab tab, SectionRowAction action)
    {
        if (ViewOf(tab) is not { } view)
        {
            return false;
        }

        if (action.Kind == SectionRowKind.NumberFrom)
        {
            _ = GuardAsync(() => NumberPagesFromRowAsync(view, action));
            return true;
        }

        return view.RunSectionRow(action);
    }

    // Number from here for a row: the card first, with the facts of the row's section; the answer is run only when the document is as it was when the card opened (as the ribbon's command does).
    private async Task NumberPagesFromRowAsync(PageView view, SectionRowAction action)
    {
        string? note = WriteStrings.NoteSectionRowStale;
        if (action.Identity is not { } identity || view.SectionFactsOfRow(action.Section, identity, out note) is not { } facts)
        {
            ShowStructureNote(view, note ?? WriteStrings.NoteStructureFailed);
            return;
        }

        var plan = CardPlan.Of(view);
        if (await SectionCards.AskNumberingAsync(new NumberPagesContext(facts, StartsNewPage: false)) is not { } numbering || CardIsStale(view, plan))
        {
            return;
        }

        _ = view.RunSectionRowNumbering(action.Section, identity, numbering);
    }
}
