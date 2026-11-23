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

        return true;
    }

    // The payload must be a value of the command; Number pages from here and Page setup take a typed object (or nothing), the others take no payload.
    private static bool PayloadFits(string id, object? payload) => id switch
    {
        WriteCommandIds.NumberPagesFrom => payload is null or PageNumbering,
        WriteCommandIds.PageSetup => payload is PageSetupRequest,
        _ when WriteCommandIds.TakesPayload(id) => SectionPayloads.IsValid(id, payload),
        _ => payload is null,
    };

    /// <summary>The facts the number-pages card needs for the caret's place, or null without a caret in the body, without a session or in a prefix document (the card then does not open).</summary>
    internal SectionFacts? SectionFactsAtCaret()
    {
        if (_released || _tab.Docx.IsPrefix || _tab.Editor is not { BlockedReason: null, Caret: { } caret } session)
        {
            return null;
        }

        var facts = session.Editor.ReadSectionFacts(caret);
        return facts.Reason == SectionReason.None ? facts : null;
    }

    /// <summary>The page setup of the caret's section and the number of sections, or null when the caret is not in the body (the page setup card shows them).</summary>
    internal (SectionPageSetup Setup, int Sections)? CaretSectionSetup()
    {
        if (_released || _tab.Docx.IsPrefix || _tab.Editor is not { BlockedReason: null, Caret: { } caret } session)
        {
            return null;
        }

        var facts = session.Editor.ReadSectionFacts(caret);
        if (facts.Reason != SectionReason.None)
        {
            return null;
        }

        var sections = SectionList.Of(_tab.Docx.Model);
        return (SectionPageSetup.Read(sections[facts.Section].Properties), sections.Count);
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
                return InsertPageNumber(ed, model, caret, place, align, look);
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
    private EditResult? InsertPageNumber(TextEditor ed, WordDocument model, TextPosition caret, HeaderFooterKind place, PageNumberAlign align, PageNumberLook look)
    {
        var (type, number, total) = PageNumberContext(model, caret);
        return ed.InsertPageNumber(caret, look, place, align, new PageNumberWords(WriteStrings.PageNumberWordPage, WriteStrings.PageNumberWordOf), number, total, type);
    }

    // The type of the part the caret's page shows, the number of that page and the pages laid out so far. A caret in a header or footer needs none of it (the editor uses that story), and a layout that does
    // not show the caret's page yet (a page that is not laid out) gives the default part and "1".
    private (HeaderFooterType Type, string Number, string Total) PageNumberContext(WordDocument model, TextPosition caret)
    {
        var layout = ActiveLayout();
        if (layout is null || caret.Story.Kind != StoryKind.Body)
        {
            return (HeaderFooterType.Default, "1", Math.Max(1, layout?.LaidOutPages ?? 1).ToString(CultureInfo.InvariantCulture));
        }

        var index = CaretBounds(caret, Affinity) is not null ? PageHint : CurrentPage;
        var total = Math.Max(1, layout.LaidOutPages).ToString(CultureInfo.InvariantCulture);
        if (!layout.TryGetPage(index, out var page) || page is null)
        {
            return (HeaderFooterType.Default, "1", total);
        }

        var sections = SectionList.Of(model);
        var facts = _tab.Editor?.Editor.ReadSectionFacts(caret);
        var type = HeaderFooterType.Default;
        if (facts is { Reason: SectionReason.None })
        {
            var info = sections[facts.Section];
            if (info.Properties.GetFlag(SectionW + "titlePg") && layout.Locate(info.FirstBlock) is { } start && start.Page == index)
            {
                type = HeaderFooterType.First;
            }
            else if ((model.Settings?.EvenAndOddHeaders ?? false) && PageNumberValue(page.Number, (string?)info.Properties.Get(SectionW + "pgNumType")?.Attribute(SectionW + "fmt")) is { } value && value % 2 == 0)
            {
                type = HeaderFooterType.Even;
            }
        }

        return (type, page.Number, total);
    }

    /// <summary>
    /// The number a displayed page number stands for, read in the format of its section (<c>w:fmt</c> of <c>w:pgNumType</c>: lower or upper roman, lower or upper letter, else digits; the letters are a to z, then aa to zz),
    /// or null when the text is no number of that format; the parity of it decides which pages are even. A letter such as "c" is a letter or a roman numeral only by the format, so the format decides.
    /// </summary>
    internal static int? PageNumberValue(string text, string? format)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var lower = text.ToLowerInvariant();
        switch (format)
        {
            case "lowerRoman" or "upperRoman":
                return lower.All(c => c is 'i' or 'v' or 'x' or 'l' or 'c' or 'd' or 'm') ? Roman(lower) : null;
            case "lowerLetter" or "upperLetter":
                return lower.All(c => c is >= 'a' and <= 'z') && lower.Distinct().Count() == 1 ? ((lower.Length - 1) * 26) + (lower[0] - 'a' + 1) : null;
            default:
                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var digits) ? digits : null;
        }
    }

    private static int? Roman(string lower)
    {
        var total = 0;
        for (var i = 0; i < lower.Length; i++)
        {
            var value = RomanValue(lower[i]);
            var next = i + 1 < lower.Length ? RomanValue(lower[i + 1]) : 0;
            total += value < next ? -value : value;
        }

        return total > 0 ? total : null;
    }

    private static int RomanValue(char c) => c switch { 'i' => 1, 'v' => 5, 'x' => 10, 'l' => 50, 'c' => 100, 'd' => 500, _ => 1000 };
}
