using System.Xml.Linq;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>Why a section command cannot run at the caret (<see cref="None"/>: it can).</summary>
internal enum SectionBlock
{
    None,

    /// <summary>The caret is in a note, a comment or a place that is neither the body nor (for the page number) a header or footer.</summary>
    NotInBody,

    /// <summary>Only the first blocks of the file are open: every section command is refused.</summary>
    PrefixDocument,

    /// <summary>A section already starts at the caret's block: a break there would make an empty section.</summary>
    StartsHere,

    /// <summary>The document has one section: there is nothing to merge.</summary>
    NothingToMerge,

    /// <summary>The caret is in the first section, which has no marker to remove.</summary>
    FirstSection,
}

/// <summary>
/// What the rules and the switches of the Layout tab need to know about the caret: <paramref name="Block"/> is the reason the asked command cannot run (the other fields hold what the caret's section has).
/// <paramref name="Section"/> is the section of the caret (-1: unknown), <paramref name="SectionCount"/> the sections of the body (0: unknown, for a body above <see cref="SectionFactsSource.MaxSections"/>),
/// <paramref name="FirstPageOn"/> whether that section has a different first page, <paramref name="LeftRightOn"/> whether the document has left and right pages and <paramref name="Setup"/> the page setup
/// of that section (null: unknown).
/// </summary>
internal sealed record SectionCommandFacts(SectionBlock Block, int Section, int SectionCount, bool FirstPageOn, bool LeftRightOn, SectionPageSetup? Setup)
{
    /// <summary>The facts of a tab that has not been asked: every command is offered (the operation decides when it runs).</summary>
    public static SectionCommandFacts Permissive { get; } = new(SectionBlock.None, -1, 0, false, false, null);

    /// <summary>Where a page setup applies when the user does not say: the whole document while it has one section, else the caret's section.</summary>
    public ApplyScope DefaultScope => SectionCount == 1 ? ApplyScope.WholeDocument : ApplyScope.ThisSection;
}

/// <summary>The words for the reasons of the section commands (the rules show them as the reason of a disabled command).</summary>
internal static class SectionWording
{
    /// <summary>The words for a block; null for none.</summary>
    public static string? ReasonText(SectionBlock block) => block switch
    {
        SectionBlock.NotInBody => WriteStrings.ReasonSectionNotInBody,
        SectionBlock.PrefixDocument => WriteStrings.ReasonSectionPrefix,
        SectionBlock.StartsHere => WriteStrings.ReasonSectionStartsHere,
        SectionBlock.NothingToMerge => WriteStrings.ReasonSectionNothingToMerge,
        SectionBlock.FirstSection => WriteStrings.ReasonSectionFirst,
        _ => null,
    };
}

/// <summary>
/// Reads <see cref="SectionCommandFacts"/> for the rules and the switches of the Layout tab, lazily per command: nothing exists before the first question, and nothing is read for a tab without an editing
/// session (only the prefix refusal, a flag of the file, is answered without one). The read walks the top-level blocks of the body once for the section list; a body with more than
/// <see cref="MaxSections"/> sections is not described (the commands are offered and the operation, which reads the live model, is authoritative). Answers are remembered for the model, its revision and the
/// selection. The asks come from the refresh of the ribbon, never from the key path.
/// </summary>
internal sealed class SectionFactsSource
{
    /// <summary>The most sections the facts describe; beyond it they are permissive (the Sections pane says "too many to list").</summary>
    public const int MaxSections = 200;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private ((WordDocument Model, int Revision, TextRange Range) Key, Computed Value)? _known;

    /// <summary>How many times the section list of the body was read for the facts (tests; 0 before the first ask of a section command).</summary>
    public int Reads { get; private set; }

    /// <summary>The facts for the command <paramref name="id"/> of the tab; permissive while the tab has no session, cannot be edited now or has no selection.</summary>
    public SectionCommandFacts Read(WriteTab? tab, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (tab is null)
        {
            return SectionCommandFacts.Permissive;
        }

        if (tab.Docx.IsPrefix)
        {
            return SectionCommandFacts.Permissive with { Block = SectionBlock.PrefixDocument };
        }

        if (tab.Editor is not { BlockedReason: null, Selection: { } range } session)
        {
            return SectionCommandFacts.Permissive;
        }

        var model = tab.Docx.Model;
        var key = (model, model.Edits.Revision, range);
        if (_known is not { } known || !known.Key.Equals(key))
        {
            try
            {
                known = (key, Compute(session, model, range.End));
            }
            catch (StalePositionException)
            {
                // A selection that does not belong to the model any more has no facts; the next selection or edit reads again.
                return SectionCommandFacts.Permissive;
            }

            _known = known;
        }

        var c = known.Value;
        return new SectionCommandFacts(BlockFor(id, c), c.Section, c.Count, c.FirstPage, c.LeftRight, c.Setup);
    }

    private Computed Compute(EditSession session, WordDocument model, TextPosition caret)
    {
        Reads++;
        var leftRight = model.Settings?.EvenAndOddHeaders ?? false;
        var kind = caret.Story.Kind;
        if (kind != StoryKind.Body)
        {
            return new Computed(false, kind == StoryKind.HeaderFooter, -1, 0, false, leftRight, false, null);
        }

        var sections = SectionList.Of(model);
        if (sections.Count > MaxSections)
        {
            return new Computed(true, false, -1, 0, false, leftRight, false, null);
        }

        var facts = session.Editor.ReadSectionFacts(caret);
        var properties = sections[facts.Section].Properties;
        return new Computed(true, false, facts.Section, sections.Count, facts.AtSectionStart, leftRight, properties.GetFlag(W + "titlePg"), SectionPageSetup.Read(properties));
    }

    private static SectionBlock BlockFor(string id, Computed c) => id switch
    {
        WriteCommandIds.InsertPageNumber => c.InBody || c.InHeaderFooter ? SectionBlock.None : SectionBlock.NotInBody,
        WriteCommandIds.LeftRightPages => SectionBlock.None,
        _ when !c.InBody => SectionBlock.NotInBody,
        WriteCommandIds.SectionBreak when c.Count > 0 && c.AtStart => SectionBlock.StartsHere,
        WriteCommandIds.RemoveSection when c.Count > 0 && c.Section == 0 => c.Count == 1 ? SectionBlock.NothingToMerge : SectionBlock.FirstSection,
        _ => SectionBlock.None,
    };

    private readonly record struct Computed(bool InBody, bool InHeaderFooter, int Section, int Count, bool AtStart, bool LeftRight, bool FirstPage, SectionPageSetup? Setup);
}
