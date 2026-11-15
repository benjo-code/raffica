using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// What the layout knows that the model does not, for <see cref="ParagraphProjection"/>: the values of the fields it evaluates, the numbers of
/// the note references it registers, the runs its styles hide, and which content it gives no room (comment references, floating pictures,
/// note references of a story that numbers none). One host per paragraph.
/// </summary>
internal sealed class LayoutProjectionHost(StyleResolver styles, PlanContext context, ResolvedParagraph owner) : IProjectionHost
{
    private readonly Dictionary<Run, ResolvedRun> _formats = new(ReferenceEqualityComparer.Instance);
    private Dictionary<RunContent, NoteRef>? _registered;

    /// <summary>The resolved format of the run (made once per run).</summary>
    public ResolvedRun Format(Run run)
    {
        if (!_formats.TryGetValue(run, out var format))
        {
            format = styles.ForRun(owner, run);
            _formats[run] = format;
        }

        return format;
    }

    /// <summary>The note a reference content was registered as while the projection was built; null when none was.</summary>
    public NoteRef? RegisteredNote(RunContent reference) => _registered is not null && _registered.TryGetValue(reference, out var note) ? note : null;

    public string? FieldValue(string instruction) => context.Fields?.Evaluate(instruction);

    public bool IsHidden(Run run) => Format(run).Hidden;

    public bool HasText(RunContent content) => content switch
    {
        CommentReference => false,
        FootnoteReference or EndnoteReference => context.Notes is not null,
        Picture { Inline: false } => false,
        _ => true,
    };

    public string? NoteMarkText(RunContent reference)
    {
        switch (reference)
        {
            case FootnoteReference footnote when context.Notes is { } notes:
                return Remember(footnote, notes.Register(false, footnote.Id)).Text;
            case EndnoteReference endnote when context.Notes is { } notes:
                return Remember(endnote, notes.Register(true, endnote.Id)).Text;
            case UnknownRunContent:
                return context.NoteMark;
            default:
                return null;
        }
    }

    private NoteRef Remember(RunContent reference, NoteRef note)
    {
        (_registered ??= new Dictionary<RunContent, NoteRef>(ReferenceEqualityComparer.Instance))[reference] = note;
        return note;
    }
}

/// <summary>
/// The host the layout used for a paragraph, handed to the editor: it answers as the layout answered (field values, hidden runs, what
/// takes no room) and gives the mark text of the note references the layout numbered, but never numbers a note again (that would advance the
/// counters of the layout). A reference the layout has not numbered (one the edit just made) has no mark text until the layout has run again.
/// </summary>
internal sealed class ReplayProjectionHost(LayoutProjectionHost inner) : IProjectionHost
{
    public string? FieldValue(string instruction) => inner.FieldValue(instruction);

    public bool IsHidden(Run run) => inner.IsHidden(run);

    public bool HasText(RunContent content) => inner.HasText(content);

    public string? NoteMarkText(RunContent reference) => reference is UnknownRunContent ? inner.NoteMarkText(reference) : inner.RegisteredNote(reference)?.Text;
}
