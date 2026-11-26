using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>Why an object command (today the note commands of the Insert tab) cannot run at the caret (<see cref="None"/>: it can).</summary>
internal enum ObjectBlock
{
    None,

    /// <summary>The writing view draws no notes, so a note cannot be inserted there: the page view shows them.</summary>
    WritingView,

    /// <summary>Only the first blocks of the file are open: the editor cannot see that the body was cut, so the command is refused.</summary>
    PrefixDocument,

    /// <summary>The caret is in a header, a footer or a comment: a note goes into the text of the document.</summary>
    HeaderFooterOrComment,

    /// <summary>The caret is in a note: a note holds no note.</summary>
    InNote,

    /// <summary>The caret is in a content control that holds plain text only.</summary>
    RestrictedControl,

    /// <summary>There is no paragraph at the caret.</summary>
    NoPosition,

    /// <summary>The caret is in a table of contents, which updates itself: nothing is inserted into it.</summary>
    InsideToc,

    /// <summary>The caret is in a content control that holds one line, which takes no caption.</summary>
    CaptionRestricted,

    /// <summary>Back to the text needs the caret in a note.</summary>
    NotInNote,
}

/// <summary>What the rules of the object commands need to know about the caret; <paramref name="Block"/> is the reason the asked command cannot run.</summary>
internal sealed record ObjectCommandFacts(ObjectBlock Block)
{
    /// <summary>The facts of a tab that has not been asked: every command is offered (the operation decides when it runs).</summary>
    public static ObjectCommandFacts Permissive { get; } = new(ObjectBlock.None);
}

/// <summary>The words for the reasons of the object commands (the rules show them as the reason of a disabled command).</summary>
internal static class ObjectWording
{
    /// <summary>The words for a block; null for none.</summary>
    public static string? ReasonText(ObjectBlock block) => block switch
    {
        ObjectBlock.WritingView => WriteStrings.ReasonNoteWritingView,
        ObjectBlock.PrefixDocument => WriteStrings.ReasonSectionPrefix,
        ObjectBlock.HeaderFooterOrComment => WriteStrings.ReasonNoteNotInBody,
        ObjectBlock.InNote => WriteStrings.ReasonNoteInNote,
        ObjectBlock.RestrictedControl => WriteStrings.ReasonNoteRestricted,
        ObjectBlock.NoPosition => WriteStrings.ReasonSectionNotInBody,
        ObjectBlock.NotInNote => WriteStrings.ReasonNoteNotInNote,
        ObjectBlock.InsideToc => WriteStrings.NoteTocProtected,
        ObjectBlock.CaptionRestricted => WriteStrings.ReasonCaptionRestricted,
        _ => null,
    };

    /// <summary>The block of a refusal of the editor to insert a caption (the chapter number and the end of a section depend on the choice of the card and are worded as notes when the caption is made).</summary>
    public static ObjectBlock BlockOf(CaptionRefusal refusal) => refusal switch
    {
        CaptionRefusal.Prefix => ObjectBlock.PrefixDocument,
        CaptionRefusal.RestrictedControl => ObjectBlock.CaptionRestricted,
        CaptionRefusal.InsideToc => ObjectBlock.InsideToc,
        CaptionRefusal.SectionEnd or CaptionRefusal.ChapterNeedsNumberedHeadings => ObjectBlock.None,
        _ => ObjectBlock.NoPosition,
    };

    /// <summary>The block of a refusal of the editor.</summary>
    public static ObjectBlock BlockOf(NoteRefusal refusal) => refusal switch
    {
        NoteRefusal.PrefixDocument => ObjectBlock.PrefixDocument,
        NoteRefusal.HeaderOrFooter or NoteRefusal.Comment => ObjectBlock.HeaderFooterOrComment,
        NoteRefusal.Note => ObjectBlock.InNote,
        NoteRefusal.RestrictedControl => ObjectBlock.RestrictedControl,
        _ => ObjectBlock.NoPosition,
    };
}

/// <summary>
/// Reads <see cref="ObjectCommandFacts"/> for the rules of the note commands of the Insert tab, lazily per command: nothing exists before the first question, and nothing is read for a tab without an editing
/// session (the prefix refusal, a flag of the file, and the writing view, a flag of the view, are answered without one). The answer is remembered for the model, its edit stamp and the selection. The asks come
/// from the refresh of the ribbon, never from the key path. The later object commands (tables, pictures) join this source.
/// </summary>
internal sealed class ObjectFactsSource
{
    private ((WordDocument Model, EditStamp Stamp, TextRange Range) Key, Computed Value)? _known;

    /// <summary>How many times the place of the caret was worked out for the facts (tests; 0 before the first ask of an object command).</summary>
    public int Reads { get; private set; }

    /// <summary>The facts for the command <paramref name="id"/> of the tab; <paramref name="writingView"/> is whether the tab shows the writing view (it draws no notes).</summary>
    public ObjectCommandFacts Read(WriteTab? tab, string id, bool writingView)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (tab is null)
        {
            return ObjectCommandFacts.Permissive;
        }

        var back = id == WriteCommandIds.NoteBack;
        if (!back && tab.Docx.IsPrefix)
        {
            return new ObjectCommandFacts(ObjectBlock.PrefixDocument);
        }

        // A bookmark command is offered in either view; only a prefix document refuses it.
        if (WriteCommandIds.IsBookmark(id))
        {
            return ObjectCommandFacts.Permissive;
        }

        if (!back && writingView)
        {
            return new ObjectCommandFacts(ObjectBlock.WritingView);
        }

        // A tab with no editing session has no caret, so it cannot be in a note; every other command is offered until the session exists.
        if (tab.Editor is null)
        {
            return back ? new ObjectCommandFacts(ObjectBlock.NotInNote) : ObjectCommandFacts.Permissive;
        }

        if (tab.Editor is not { BlockedReason: null } session)
        {
            return ObjectCommandFacts.Permissive;
        }

        if (session.Selection is not { } range)
        {
            return back ? new ObjectCommandFacts(ObjectBlock.NotInNote) : ObjectCommandFacts.Permissive;
        }

        var model = tab.Docx.Model;
        var key = (model, model.Edits.Stamp, range);
        if (_known is not { } known || !known.Key.Equals(key))
        {
            try
            {
                known = (key, Compute(session, range.End));
            }
            catch (StalePositionException)
            {
                // A selection that does not belong to the model any more has no facts; the next selection or edit reads again.
                return ObjectCommandFacts.Permissive;
            }

            _known = known;
        }

        var c = known.Value;
        return back ? new ObjectCommandFacts(c.InNote ? ObjectBlock.None : ObjectBlock.NotInNote) : new ObjectCommandFacts(c.Refusal is { } refusal ? ObjectWording.BlockOf(refusal) : ObjectBlock.None);
    }

    private Computed Compute(EditSession session, TextPosition caret)
    {
        Reads++;
        return new Computed(session.Editor.RefusalForNote(caret), caret.Story.Kind == StoryKind.Note);
    }

    private readonly record struct Computed(NoteRefusal? Refusal, bool InNote);
}
