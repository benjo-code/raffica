using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Viewing;

/// <summary>
/// The section commands of the page view (the Layout tab: <c>section.*</c>, <c>page.*</c> and <c>insert.pageNumber</c>): the ribbon, the command search and the menu end here. Every change is one
/// <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> (one history step, the failure rule of a key). The commands have no keys. Each one reads the caret's section from the model as it is when
/// the operation runs, so a stale answer of the facts can never change the wrong section; a command that finds nothing to do says so through <see cref="StructureNote"/>, never silently.
/// </summary>
/// <remarks>
/// The page number is written into the story the caret's page uses: the first-page part when the section has a different first page and the caret is on its first page, the even part with left and right
/// pages on an even page, else the default part; the cached results are the number of the caret's page and the pages laid out so far (a reader that does not evaluate fields shows them; Word and the layout
/// evaluate the fields).
/// </remarks>
internal sealed partial class PageView
{
    private static readonly XNamespace SectionW = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Whether the margin presets are the inch ones (tests set it; the default is the rule of the current region).</summary>
    internal Func<bool> MarginsInInches { get; set; } = () => PagePresets.UsesInches(RegionInfo.CurrentRegion);

    /// <summary>
    /// Runs a section command; true when it was taken (also when it changed nothing), false for a released view, an unknown command or a payload that is not a value of the command (nothing changes then).
    /// <paramref name="payload"/> is the value of a payload command, a <see cref="PageNumbering"/> for Number pages from here (null: start at 1, the format 1 2 3, no prefix) and a
    /// <see cref="PageSetupRequest"/> for Page setup.
    /// </summary>
    internal bool RunSectionCommand(string id, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_released || !WriteCommandIds.IsSection(id) || !PayloadFits(id, payload))
        {
            return false;
        }

        if (_tab.Docx.IsPrefix)
        {
            StructureNote?.Invoke(WriteStrings.ReasonSectionPrefix);
            return true;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        var result = ApplyGuarded(session, ed => session.Caret is { } caret ? SectionOperation(ed, session, id, payload, caret) : null);

        // The rules said yes but the editor had nothing to do (a refusal, or nothing changed): a visible note, never silence. A save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(id.StartsWith("page.", StringComparison.Ordinal) ? WriteStrings.NoteSectionNoChange : WriteStrings.NoteStructureFailed);
        }
        else if (result is not null && id == WriteCommandIds.InsertPageNumber && session.Caret is { } after)
        {
            NoteSharedPageNumber(session, after, payload);
        }

        return true;
    }

    // Insert page number writes into a part other sections may show too (a footer a later section inherits): the user is told, because editing a shared part edits it for each of them. The sections come from
    // the editor's own rule (only sections that really show the page's type); a body caret whose section is not among them is a mismatch and says nothing rather than something misleading.
    private void NoteSharedPageNumber(EditSession session, TextPosition caret, object? payload)
    {
        if (!SectionPayloads.TryPageNumber(payload, out var place, out _, out _))
        {
            return;
        }

        try
        {
            var section = session.Editor.ReadSectionFacts(caret);
            var sharing = session.Editor.PageNumberSharing(caret, place, PageNumberContext(caret).Type);
            if (section.Reason == SectionReason.None && SharedNote(place, sharing, section.Section) is { } note)
            {
                StructureNote?.Invoke(note);
            }
        }
        catch (StalePositionException)
        {
            // The place moved while the command ran (a queued replay): nothing is claimed.
        }
    }

    /// <summary>
    /// The note after a page number went into a part that other sections show too: null when the part is shown by one section only, and null when the caret's own section (<paramref name="caretSection"/>) is not among
    /// the sections that show it (a mismatch says nothing rather than something misleading).
    /// </summary>
    internal static string? SharedNote(HeaderFooterKind place, SectionSharing sharing, int caretSection)
    {
        ArgumentNullException.ThrowIfNull(sharing);
        if (sharing.Sections.Count < 2 || !sharing.Sections.Contains(caretSection))
        {
            return null;
        }

        return WriteStrings.Fill(place == HeaderFooterKind.Footer ? WriteStrings.NoteSharedFooter : WriteStrings.NoteSharedHeader, SectionNumberList.Describe(sharing.Sections.Select(i => i + 1)));
    }

    // The payload must be a value of the command; Number pages from here and Page setup take a typed object (or nothing), the others take no payload.
    private static bool PayloadFits(string id, object? payload) => id switch
    {
        WriteCommandIds.NumberPagesFrom => payload is null or PageNumbering,
        WriteCommandIds.PageSetup => payload is PageSetupRequest,
        _ when WriteCommandIds.TakesPayload(id) => SectionPayloads.IsValid(id, payload),
        _ => payload is null,
    };

    /// <summary>
    /// The facts the number-pages card needs for the caret's place. The session is made here when the tab has none yet (a freshly opened document has no caret until the first command or key, so a command that asks a
    /// card first must make it before it can describe the place). Null, with <paramref name="note"/> naming why in the UI language, when the command cannot be described: a prefix document, a document that cannot be edited
    /// (still opening, failed), a caret outside the body. Nothing changes in either case.
    /// </summary>
    internal SectionFacts? SectionFactsAtCaret(out string? note)
    {
        note = null;
        if (SectionSessionForCard(out note) is not { } session)
        {
            return null;
        }

        var facts = session.Editor.ReadSectionFacts(session.Caret!.Value);
        if (facts.Reason == SectionReason.None)
        {
            return facts;
        }

        note = facts.Reason == SectionReason.NotInBody ? WriteStrings.ReasonSectionNotInBody : WriteStrings.NoteStructureFailed;
        return null;
    }

    /// <summary>
    /// The page setup of the caret's section and what the page setup card needs besides it (the number of sections, whether its columns have different widths: the count shown is then the number of
    /// <c>w:col</c> elements the layout draws); null with a <paramref name="note"/> as for <see cref="SectionFactsAtCaret"/>.
    /// </summary>
    internal (SectionPageSetup Setup, int Sections, bool ColumnsUnequal, int CaretSection, IReadOnlyList<int> UnequalSections)? CaretSectionSetup(out string? note)
    {
        note = null;
        if (SectionSessionForCard(out note) is not { } session)
        {
            return null;
        }

        var facts = session.Editor.ReadSectionFacts(session.Caret!.Value);
        if (facts.Reason != SectionReason.None)
        {
            note = facts.Reason == SectionReason.NotInBody ? WriteStrings.ReasonSectionNotInBody : WriteStrings.NoteStructureFailed;
            return null;
        }

        var sections = SectionList.Of(_tab.Docx.Model);
        var setup = SectionPageSetup.Read(sections[facts.Section].Properties);
        var unequalSections = Enumerable.Range(0, sections.Count).Where(i => UnequalColumnCount(sections[i].Properties) > 0).ToList();
        var count = UnequalColumnCount(sections[facts.Section].Properties);
        var unequal = count > 0;
        return (unequal ? setup with { Columns = count } : setup, sections.Count, unequal, facts.Section, unequalSections);
    }

    // The number of columns a section draws at different widths (the w:col elements, when the section says its columns are not equal, as the layout reads them); 0 for equal columns.
    private static int UnequalColumnCount(SectionProperties? properties)
    {
        var cols = properties?.Get(SectionW + "cols");
        return cols is not null && !ColumnFlag(cols, "equalWidth", true) ? cols.Elements(SectionW + "col").Count() : 0;
    }

    // A boolean attribute of the section properties: absent gives the default; "0", "false" and "off" are off.
    private static bool ColumnFlag(System.Xml.Linq.XElement element, string name, bool defaultValue) =>
        element.Attribute(SectionW + name)?.Value is { } text ? text is not ("0" or "false" or "off") : defaultValue;

    /// <summary>
    /// Whether the section that starts before the caret's block would start a new page there: the block is not the first one of the page the layout put it on (false at the start of a section, which gets no
    /// new section, and when the layout cannot say where the block is: nothing is claimed then).
    /// </summary>
    internal bool CaretBlockStartsNewPage(SectionFacts facts)
    {
        if (facts.AtSectionStart || _tab.Editor is not { } session || session.Caret is not { } caret || session.Editor.TopLevelBlockIndex(caret) is not { } index || ActiveLayout() is not { } layout)
        {
            return false;
        }

        return layout.Locate(index) is { } place && layout.BlockAt(place.Page, 0) != index;
    }

    // The session a card command reads its place from, made on first use like every command's (the hint of a refusal is shown by the window's note, not twice); null with the words for a prefix document, a tab that cannot
    // be edited now (still opening, failed, a save running) and a session without a caret.
    private EditSession? SectionSessionForCard(out string? note)
    {
        note = null;
        if (_released)
        {
            note = WriteStrings.NoteStructureFailed;
            return null;
        }

        if (_tab.Docx.IsPrefix)
        {
            note = WriteStrings.ReasonSectionPrefix;
            return null;
        }

        if (BlockReason() is { } reason)
        {
            note = reason == EditBlockedReason.Saving ? WriteStrings.ReasonEditSaving : reason == EditBlockedReason.StillOpening ? WriteStrings.ReasonEditOpening : WriteStrings.EditUnavailable;
            return null;
        }

        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        if (session.Caret is null)
        {
            note = WriteStrings.NoteStructureFailed;
            return null;
        }

        return session;
    }
    private EditResult? SectionOperation(TextEditor ed, EditSession session, string id, object? payload, TextPosition caret)
    {
        var model = _tab.Docx.Model;
        switch (id)
        {
            case WriteCommandIds.NumberPagesFrom:
                return ed.NumberPagesFrom(caret, payload as PageNumbering ?? new PageNumbering(1, PageNumberFormat.Arabic, string.Empty));
            case WriteCommandIds.SectionBreak:
                SectionPayloads.TryBreak(payload, out var kind);
                return ed.InsertSectionBreak(caret, kind);
            case WriteCommandIds.RemoveSection:
                var facts = ed.ReadSectionFacts(caret);
                return facts.Reason == SectionReason.None ? ed.RemoveSectionBreak(facts.Section) : null;
            case WriteCommandIds.DifferentFirstPage:
                var own = ed.ReadSectionFacts(caret);
                return own.Reason == SectionReason.None ? ed.SetDifferentFirstPage(own.Section, !SectionList.Of(model)[own.Section].Properties.GetFlag(SectionW + "titlePg"), caret) : null;
            case WriteCommandIds.LeftRightPages:
                return ed.SetLeftRightPages(!(model.Settings?.EvenAndOddHeaders ?? false), caret.Story.Kind is StoryKind.Body or StoryKind.HeaderFooter ? caret : null);
            case WriteCommandIds.PageSetup:
                return PageSetupOperation(ed, caret, (PageSetupRequest)payload!);
            case WriteCommandIds.InsertPageNumber:
                SectionPayloads.TryPageNumber(payload, out var place, out var align, out var look);
                return InsertPageNumber(ed, caret, place, align, look);
            default:
                // page.size, page.margins, page.orientation, page.columns: the caret's section when it has several, the whole document when it has one.
                var section = ed.ReadSectionFacts(caret);
                if (section.Reason != SectionReason.None)
                {
                    return null;
                }

                var sections = SectionList.Of(model);
                var change = SectionPayloads.PageSetupFor(id, payload, SectionPageSetup.Read(sections[section.Section].Properties), MarginsInInches());
                return change is null ? null : ed.ApplyPageSetup(caret, change, sections.Count == 1 ? ApplyScope.WholeDocument : ApplyScope.ThisSection);
        }
    }

    private static EditResult? PageSetupOperation(TextEditor ed, TextPosition caret, PageSetupRequest request) =>
        ed.ReadSectionFacts(caret).Reason == SectionReason.None ? ed.ApplyPageSetup(caret, request.Change, request.Scope) : null;

    // Inserts the number: the page type, the cached results and the words come from the layout and the UI language at this moment.
    private EditResult? InsertPageNumber(TextEditor ed, TextPosition caret, HeaderFooterKind place, PageNumberAlign align, PageNumberLook look)
    {
        var (type, number, total) = PageNumberContext(caret);
        return ed.InsertPageNumber(caret, look, place, align, new PageNumberWords(WriteStrings.PageNumberWordPage, WriteStrings.PageNumberWordOf), number, total, type);
    }

    /// <summary>
    /// What the layout says about the page the caret is on, for the page number the caret's section gets: the type of the header and footer parts the layout used for that page (<see cref="LaidOutPage.ChromeType"/>,
    /// decided from the different first page, left and right pages and the page's own number, never re-derived here), the number as the page prints it (<see cref="LaidOutPage.Number"/>, in its format after a restart)
    /// and the pages laid out so far (the total when the layout is complete, a lower bound while it goes on: the field is evaluated again when the page count is known). A caret in a header or footer needs none of it (the
    /// editor uses that story). The writing view has no page chrome: its strips give the default type and the strip's number. A caret the layout does not show (no caret box: its page is not laid out) gives the default type and "1".
    /// </summary>
    internal (HeaderFooterType Type, string Number, string Total) PageNumberContext(TextPosition caret)
    {
        var layout = ActiveLayout();
        var total = Math.Max(1, layout?.LaidOutPages ?? 1).ToString(CultureInfo.InvariantCulture);
        if (layout is null || caret.Story.Kind != StoryKind.Body || CaretBoxOf(layout, caret, Affinity) is not { } box || !layout.TryGetPage(box.Page, out var page) || page is null)
        {
            return (HeaderFooterType.Default, "1", total);
        }

        return (page.ChromeType ?? HeaderFooterType.Default, page.Number, total);
    }
}
