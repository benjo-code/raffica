using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Why a footnote or endnote cannot be inserted at a place. The caller words it for the user in the language of the interface.</summary>
public enum NoteRefusal
{
    /// <summary>There is no paragraph at the place (or it is not in the document).</summary>
    NoPosition,

    /// <summary>The document is opened as a prefix (the first blocks only): the editor sees a part of the body and the notes would be written to a file that cannot be saved.</summary>
    PrefixDocument,

    /// <summary>The place is in a header or a footer.</summary>
    HeaderOrFooter,

    /// <summary>The place is in a footnote or an endnote (a note holds no note).</summary>
    Note,

    /// <summary>The place is in a comment.</summary>
    Comment,

    /// <summary>The place is in a plain-text, combo box, drop-down list or date content control, which holds text only.</summary>
    RestrictedControl,
}

/// <summary>Where a note reference may stand: in the body and in the cells of its tables, nowhere else.</summary>
public static class NoteRules
{
    /// <summary>
    /// The reason a note reference cannot be inserted at <paramref name="caret"/>, or null when it can. A prefix document is refused first (<paramref name="prefixDocument"/>: the editor cannot see that
    /// the body was cut), then the story: only the body takes a reference. A paragraph in a content control of the kinds text, combo box, drop-down list or date is refused too (a paragraph of an inline control of
    /// those kinds is judged by <see cref="TextEditor.RefusalForNote"/>, which knows the offset).
    /// </summary>
    public static NoteRefusal? Refusal(WordDocument document, TextPosition caret, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (prefixDocument)
        {
            return NoteRefusal.PrefixDocument;
        }

        if (caret.Paragraph is null)
        {
            return NoteRefusal.NoPosition;
        }

        switch (caret.Story.Kind)
        {
            case StoryKind.HeaderFooter:
                return NoteRefusal.HeaderOrFooter;
            case StoryKind.Note:
                return NoteRefusal.Note;
            case StoryKind.Comment:
                return NoteRefusal.Comment;
            case StoryKind.Body:
                break;
            default:
                return NoteRefusal.NoPosition;
        }

        if (TextEditor.FindIn(document.Body.Blocks, caret.Paragraph) is not { } found)
        {
            return NoteRefusal.NoPosition;
        }

        return TextEditor.InSingleParagraphControl(document.Body.Blocks, found.Container) ? NoteRefusal.RestrictedControl : null;
    }
}
