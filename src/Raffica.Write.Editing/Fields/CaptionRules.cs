using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Where a caption goes relative to the paragraph or object it labels.</summary>
public enum CaptionPlace
{
    /// <summary>The caption paragraph comes before the paragraph, picture or table (the usual place for a table).</summary>
    Above,

    /// <summary>The caption paragraph comes after it (the usual place for a figure).</summary>
    Below,
}

/// <summary>Why a caption cannot be inserted at a place. The caller words it for the user in the language of the interface.</summary>
public enum CaptionRefusal
{
    /// <summary>There is no paragraph at the place (or it is not in the document).</summary>
    NoPosition,

    /// <summary>The document is opened as a prefix (the first blocks only): the editor sees a part of the body and the file cannot be saved.</summary>
    Prefix,

    /// <summary>The place is not in the body (a header, a footer, a note or a comment): a caption labels something of the text.</summary>
    NotInBody,

    /// <summary>The place is in a plain-text, combo box, drop-down list or date content control, which holds one paragraph only.</summary>
    RestrictedControl,

    /// <summary>The place is in a table of contents, which updates itself.</summary>
    InsideToc,

    /// <summary>The caption would go below a paragraph that ends its section, so it would land in the next section, away from what it labels.</summary>
    SectionEnd,

    /// <summary>A chapter number needs numbered headings (the switch of the Home tab) and a Heading 1 above the caption.</summary>
    ChapterNeedsNumberedHeadings,
}

/// <summary>Where a caption may be inserted: in the body, its tables and its block content controls that hold several paragraphs; and what a chapter number needs.</summary>
public static class CaptionRules
{
    /// <summary>
    /// The reason a caption cannot be inserted at <paramref name="at"/>, or null when the place takes one (the table of contents, the end of a section and the position of a chapter number are judged by
    /// <see cref="TextEditor.RefusalForCaption"/>, which knows the editor's state). A prefix document is refused first (<paramref name="prefixDocument"/>: the editor cannot see that the body was cut), then the story, then a
    /// content control of the kinds text, combo box, drop-down list or date, then a chapter number without numbered headings (<see cref="HeadingNumbering.StateOf"/> is not On).
    /// </summary>
    public static CaptionRefusal? Refusal(WordDocument document, TextPosition at, bool chapterNumber, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (prefixDocument)
        {
            return CaptionRefusal.Prefix;
        }

        if (at.Paragraph is null)
        {
            return CaptionRefusal.NoPosition;
        }

        if (at.Story.Kind != StoryKind.Body)
        {
            return CaptionRefusal.NotInBody;
        }

        if (TextEditor.FindIn(document.Body.Blocks, at.Paragraph) is not { } found)
        {
            return CaptionRefusal.NoPosition;
        }

        if (TextEditor.InSingleParagraphControl(document.Body.Blocks, found.Container))
        {
            return CaptionRefusal.RestrictedControl;
        }

        return chapterNumber && HeadingNumbering.StateOf(document) != HeadingNumberingState.On ? CaptionRefusal.ChapterNeedsNumberedHeadings : null;
    }
}
