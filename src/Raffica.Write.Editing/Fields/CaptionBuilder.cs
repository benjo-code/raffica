using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Makes the paragraph of a caption in Word's own shape: the label word, a space, the <c>SEQ</c> field and the text; with a chapter number the word, a space, <c>STYLEREF 1 \s</c>, a hyphen, <c>SEQ ... \s 1</c> and the text.
/// The paragraph has the <c>Caption</c> style (made as a companion step when the document lacks it). A new caption carries the number the refresh would give, worked out here from the model alone (the same counter,
/// the same order), so the caret never lands on a caption that shows a wrong number.
/// </summary>
internal static class CaptionBuilder
{
    /// <summary>The id of the caption paragraph style.</summary>
    public const string StyleId = "Caption";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The id of the paragraph style that is the caption style of the document (<c>Caption</c>, or any paragraph style named "caption" in a file whose ids are localized), or null when it has none.</summary>
    public static string? ExistingStyleId(WordDocument document)
    {
        if (document.Styles is not { } sheet)
        {
            return null;
        }

        if (sheet.Find(StyleId) is { } byId && string.Equals(byId.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
        {
            return byId.Id;
        }

        return sheet.Styles.FirstOrDefault(s => string.Equals(s.Type, "paragraph", StringComparison.OrdinalIgnoreCase) && string.Equals(s.Name, "caption", StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// Adds the caption style to the style sheet when the document has none (the look of the blank document's: 9 pt, italic, grey, a little room around). Returns the change a step records (old and new are equal clones:
    /// the style is made in place) or null when the style exists or the document has no style sheet; <paramref name="styleId"/> is the id the paragraph takes (null without a style sheet).
    /// </summary>
    public static StyleChange? EnsureStyle(WordDocument document, out string? styleId)
    {
        styleId = ExistingStyleId(document);
        if (styleId is not null || document.Styles is not { } sheet)
        {
            return null;
        }

        // The id Caption may be taken by a style of another kind (a character style): the paragraph style then gets the first free Caption1, Caption2, ... and the paragraph uses that.
        var id = StyleId;
        for (var n = 1; sheet.Find(id) is not null; n++)
        {
            id = StyleId + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var style = new Style { Id = id, Type = "paragraph", Name = "caption", BasedOn = sheet.Find("Normal") is null ? null : "Normal", Next = sheet.Find("Normal") is null ? null : "Normal" };
        style.ParagraphProperties.Set(W + "spacing", new XElement(W + "spacing", new XAttribute(W + "before", "60"), new XAttribute(W + "after", "200"), new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto")));
        style.RunProperties.Set(W + "i", new XElement(W + "i"));
        style.RunProperties.Set(W + "iCs", new XElement(W + "iCs"));
        style.RunProperties.Set(W + "color", new XElement(W + "color", new XAttribute(W + "val", "595959")));
        style.RunProperties.Set(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", "18")));
        style.RunProperties.Set(W + "szCs", new XElement(W + "szCs", new XAttribute(W + "val", "18")));
        style.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "uiPriority", new XAttribute(W + "val", "35")))));
        style.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "unhideWhenUsed"))));
        style.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "qFormat"))));
        sheet.Styles.Add(style);
        styleId = id;
        return new StyleChange(style.Clone(), style.Clone());
    }

    /// <summary>The instruction of the sequence field of a label (<c>SEQ Figure \* ARABIC</c>, with <c>\s 1</c> for a chapter number).</summary>
    public static string SeqInstruction(string label, bool chapterNumber) =>
        "SEQ " + label + @" \* ARABIC" + (chapterNumber ? @" \s 1" : string.Empty);

    /// <summary>The instruction of the chapter number field.</summary>
    public const string ChapterInstruction = @"STYLEREF 1 \s";

    /// <summary>
    /// What a new caption at the place of <paramref name="placeholder"/> (a paragraph already in the document where the caption will stand) shows: the number of its <c>SEQ</c> field, counted like the refresh does (in document
    /// order, a heading of level n restarts the numbers that have <c>\s n</c>), and, for a chapter number, the label of the last Heading 1 above it (<c>Chapter</c> is null when there is none, which the caller refuses).
    /// </summary>
    // Keep in step with FieldRefresh.State.Walk: the same walk and the same fields counted (SeqCounter.Counts), so the number a new caption carries equals the number the refresh gives.
    public static (string Number, string? Chapter) Results(WordDocument document, Paragraph placeholder, string label, bool chapterNumber, IFieldHost? host)
    {
        // Like the refresh, the counter of the label exists from its first counted field on: before it there is nothing to restart.
        SeqCounter? counter = null;
        Paragraph? lastChapter = null;
        foreach (var paragraph in BookmarkOperations.ParagraphsOf(document.Body.Blocks))
        {
            if (ReferenceEquals(paragraph, placeholder))
            {
                break;
            }

            if (FieldRefresh.HeadingLevel(paragraph) is { } level)
            {
                counter?.NoteHeading(level);
                if (level == 1)
                {
                    lastChapter = paragraph;
                }
            }

            if (!FieldRefresh.HasFieldMarkup(paragraph))
            {
                continue;
            }

            foreach (var span in FieldView.InParagraph(paragraph))
            {
                if (SeqCounter.Counts(span) && string.Equals(span.Instruction.Argument, label, StringComparison.OrdinalIgnoreCase))
                {
                    (counter ??= new SeqCounter()).Next(span.Instruction);
                }
            }
        }

        var number = (counter ??= new SeqCounter()).Next(FieldInstruction.Parse(SeqInstruction(label, chapterNumber))) ?? "1";
        string? chapter = null;
        if (chapterNumber && lastChapter is not null)
        {
            chapter = host?.ChapterNumber(lastChapter) is { Length: > 0 } text ? text : "0";
        }

        return (number, chapter);
    }

    /// <summary>The caption paragraph: the style (when the document has one), the label word, a space, the fields and the text.</summary>
    public static Paragraph Build(string? styleId, string labelWord, string label, string text, bool chapterNumber, string number, string? chapter)
    {
        var paragraph = new Paragraph();
        if (styleId is not null)
        {
            paragraph.Properties.StyleId = styleId;
        }

        if (labelWord.Length > 0)
        {
            paragraph.Inlines.Add(TextRun(labelWord + " "));
        }

        if (chapterNumber)
        {
            foreach (var inline in FieldWriter.Make(ChapterInstruction, chapter ?? string.Empty, null))
            {
                paragraph.Inlines.Add(inline);
            }

            paragraph.Inlines.Add(TextRun(CaptionLabels.ChapterSeparator));
        }

        foreach (var inline in FieldWriter.Make(SeqInstruction(label, chapterNumber), number, null))
        {
            paragraph.Inlines.Add(inline);
        }

        if (text.Length > 0)
        {
            paragraph.Inlines.Add(TextRun(text));
        }

        return paragraph;
    }

    private static Run TextRun(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }
}
