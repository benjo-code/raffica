using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;

namespace Raffica.Write.Viewing;

/// <summary>
/// What the rows of the Sections pane run: the header and footer switches, the first-page switch, Remove marker and the left and right switch of one section, each one <see cref="EditSession.Apply"/> through
/// <see cref="ApplyGuarded"/> (one history step). A row names its section by index and by its properties object; both are checked against the model as it is when the operation runs, so a row that was
/// read before the document changed can never act on another section. The caret handed to the editor is the user's own (the history puts it back there).
/// </summary>
internal sealed partial class PageView
{
    /// <summary>
    /// Runs a row's request (not Number from here: that asks its card first, see <see cref="SectionFactsOfRow"/>); false, with nothing changed, for a released view, a prefix document, a request that does not belong
    /// to the document any more (the note says so) and a request the editor has nothing to do for (the note says so).
    /// </summary>
    internal bool RunSectionRow(SectionRowAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_released || action.Kind == SectionRowKind.NumberFrom)
        {
            return false;
        }

        if (_tab.Docx.IsPrefix)
        {
            StructureNote?.Invoke(WriteStrings.ReasonSectionPrefix);
            return false;
        }

        if (action.Kind != SectionRowKind.LeftRight && !RowBelongsToTheDocument(action.Section, action.Identity))
        {
            StructureNote?.Invoke(WriteStrings.NoteSectionRowStale);
            return false;
        }

        if (SessionForCommand() is not { } session)
        {
            return false;
        }

        var result = ApplyGuarded(session, ed => RowOperation(ed, session, action));
        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(WriteStrings.NoteStructureFailed);
        }

        return result is not null;
    }

    /// <summary>The facts the number-pages card needs for the start of a row's section; null with a note (as <see cref="SectionFactsAtCaret"/>) when the section is not there any more or cannot be described.</summary>
    internal SectionFacts? SectionFactsOfRow(int section, SectionProperties identity, out string? note)
    {
        if (SectionSessionForCard(out note) is not { } session)
        {
            return null;
        }

        if (RowStart(section, identity) is not { } at)
        {
            note = WriteStrings.NoteSectionRowStale;
            return null;
        }

        var facts = session.Editor.ReadSectionFacts(at);
        if (facts.Reason != SectionReason.None)
        {
            note = WriteStrings.NoteStructureFailed;
            return null;
        }

        return facts;
    }

    /// <summary>Numbers the pages from the start of a row's section (the section is edited alone: its first block starts it, so nothing is split); false when the row is not there any more or the editor did nothing.</summary>
    internal bool RunSectionRowNumbering(int section, SectionProperties identity, PageNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(numbering);
        if (_released || _tab.Docx.IsPrefix)
        {
            return false;
        }

        if (RowStart(section, identity) is null)
        {
            StructureNote?.Invoke(WriteStrings.NoteSectionRowStale);
            return false;
        }

        if (SessionForCommand() is not { } session)
        {
            return false;
        }

        var result = ApplyGuarded(session, ed => RowStart(section, identity) is { } at ? ed.NumberPagesFrom(at, numbering) : null);
        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(WriteStrings.NoteStructureFailed);
        }

        return result is not null;
    }

    private static EditResult? RowOperation(TextEditor ed, EditSession session, SectionRowAction action)
    {
        var caret = session.Caret is { } at && at.Story.Kind is StoryKind.Body or StoryKind.HeaderFooter ? at : (TextPosition?)null;
        return action.Kind switch
        {
            SectionRowKind.OwnHeader => ed.SetOwnHeaderFooter(action.Section, HeaderFooterKind.Header, HeaderFooterType.Default, action.On, caret),
            SectionRowKind.OwnFooter => ed.SetOwnHeaderFooter(action.Section, HeaderFooterKind.Footer, HeaderFooterType.Default, action.On, caret),
            SectionRowKind.FirstPage => ed.SetDifferentFirstPage(action.Section, action.On, caret),
            SectionRowKind.RemoveMarker => ed.RemoveSectionBreak(action.Section),
            SectionRowKind.LeftRight => ed.SetLeftRightPages(action.On, caret),
            _ => null,
        };
    }

    private bool RowBelongsToTheDocument(int section, SectionProperties? identity)
    {
        var sections = SectionList.Of(_tab.Docx.Model);
        return identity is not null && (uint)section < (uint)sections.Count && ReferenceEquals(sections[section].Properties, identity);
    }

    // The first paragraph of a section's first block, as a position: where the numbering command is told the section starts.
    private TextPosition? RowStart(int section, SectionProperties identity)
    {
        var model = _tab.Docx.Model;
        var sections = SectionList.Of(model);
        if ((uint)section >= (uint)sections.Count || !ReferenceEquals(sections[section].Properties, identity) || sections[section].FirstBlock >= model.Body.Blocks.Count)
        {
            return null;
        }

        return FirstParagraphOf(model.Body.Blocks[sections[section].FirstBlock]) is { } paragraph ? new TextPosition(StoryRef.Body, paragraph, 0) : null;
    }

    private static Paragraph? FirstParagraphOf(Block block) => block switch
    {
        Paragraph paragraph => paragraph,
        ContentControlBlock control => control.Content.Select(FirstParagraphOf).FirstOrDefault(found => found is not null),
        Table table => table.Rows.Select(FirstParagraphIn).FirstOrDefault(found => found is not null),
        _ => null,
    };

    private static Paragraph? FirstParagraphIn(RowItem item) => item switch
    {
        TableRow row => row.Items.Select(FirstParagraphIn).FirstOrDefault(found => found is not null),
        TableCell cell => cell.Blocks.Select(FirstParagraphOf).FirstOrDefault(found => found is not null),
        _ => null,
    };
}
