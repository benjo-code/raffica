using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>The reason the last <see cref="InsertCaption(TextPosition, string, string, CaptionPlace, bool, string, IFieldHost?, bool)"/> (either overload) returned null, or null when it succeeded or refused for another cause (a label or text that is not valid). Set by every call.</summary>
    public CaptionRefusal? LastCaptionRefusal { get; private set; }

    /// <summary>
    /// The reason a caption cannot be inserted at <paramref name="at"/> (see <see cref="CaptionRules.Refusal"/>), or null: also a place in a table of contents, and, when <paramref name="place"/> is Below, a paragraph that ends its
    /// section (the caption would land in the next one). Whether a chapter number has a Heading 1 above the caption is known only when the caption is made (<see cref="LastCaptionRefusal"/>).
    /// </summary>
    public CaptionRefusal? RefusalForCaption(TextPosition at, bool chapterNumber, bool prefixDocument = false, CaptionPlace? place = null)
    {
        if (CaptionRules.Refusal(_document, at, chapterNumber, prefixDocument) is { } refused)
        {
            return refused;
        }

        var (_, container, index) = Locate(at);
        if (_tocs.Around(container, index) is not null)
        {
            return CaptionRefusal.InsideToc;
        }

        return place == CaptionPlace.Below && at.Paragraph.SectionBreak is not null ? CaptionRefusal.SectionEnd : null;
    }

    /// <summary>
    /// Inserts a caption paragraph above or below the paragraph of <paramref name="at"/> (the paragraph of a selected picture, or the one the caret is in) as one history step: the paragraph has the <c>Caption</c> style (made as a
    /// companion of the step when the document lacks it, see <see cref="EditResult.Style"/>), the text <paramref name="labelWord"/>, a space, a <c>SEQ</c> field of <paramref name="label"/> and <paramref name="text"/>; with
    /// <paramref name="chapterNumber"/> the field is <c>STYLEREF 1 \s</c>, a hyphen and <c>SEQ ... \s 1</c> (it needs numbered headings and a Heading 1 above the caption). The fields carry the number the refresh would give
    /// (the chapter label comes from <paramref name="host"/>; without one it shows 0 until the refresh). The result's caret is at the end of the caption. Returns null, and changes nothing, for a place that takes no caption
    /// (<see cref="LastCaptionRefusal"/> says why; a position that is not in the document is one), a label that is not a sequence identifier (<see cref="CaptionLabels.Problem"/>) or a text or word that is not a line.
    /// </summary>
    public EditResult? InsertCaption(TextPosition at, string label, string text, CaptionPlace place, bool chapterNumber, string labelWord, IFieldHost? host = null, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(labelWord);
        LastCaptionRefusal = null;
        LastFieldRefusal = null;
        if (!ValidCaptionInput(label, text, labelWord) || TocRefusesAt(at))
        {
            LastCaptionRefusal = LastFieldRefusal is null ? null : CaptionRefusal.InsideToc;
            return null;
        }

        if (RefusalForCaption(at, chapterNumber, prefixDocument, place) is { } refusal)
        {
            LastCaptionRefusal = refusal;
            return null;
        }

        var (story, container, index) = Locate(at);
        var map = Map(story, at.Paragraph);
        var offset = EditRules.Snap(map, Math.Clamp(at.Offset, 0, map.Projection.Text.Length));
        var slot = place == CaptionPlace.Above ? index : index + 1;
        var anchor = new EditHistory.Point(index, offset);
        return PlaceCaption(story, container, slot, anchor, label, text, chapterNumber, labelWord, host);
    }

    /// <summary>
    /// Inserts a caption above or below a table of the body as one history step, in the body (a sibling of the table, or of the content control that holds it). Everything else is as for
    /// <see cref="InsertCaption(TextPosition, string, string, CaptionPlace, bool, string, IFieldHost?, bool)"/>. Returns null when the reference names no table of the body.
    /// </summary>
    public EditResult? InsertCaption(TableRef table, string label, string text, CaptionPlace place, bool chapterNumber, string labelWord, IFieldHost? host = null, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(labelWord);
        LastCaptionRefusal = null;
        LastFieldRefusal = null;
        if (!ValidCaptionInput(label, text, labelWord))
        {
            return null;
        }

        if (prefixDocument)
        {
            LastCaptionRefusal = CaptionRefusal.Prefix;
            return null;
        }

        var body = _document.Body.Blocks;
        if (table.Find(body) is null)
        {
            LastCaptionRefusal = CaptionRefusal.NoPosition;
            return null;
        }

        if (chapterNumber && HeadingNumbering.StateOf(_document) != HeadingNumberingState.On)
        {
            LastCaptionRefusal = CaptionRefusal.ChapterNeedsNumberedHeadings;
            return null;
        }

        var slot = place == CaptionPlace.Above ? table.BlockIndex : table.BlockIndex + 1;
        var anchor = new EditHistory.Point(table.BlockIndex, 0);
        return PlaceCaption(StoryRef.Body, body, slot, anchor, label, text, chapterNumber, labelWord, host);
    }

    private bool ValidCaptionInput(string label, string text, string labelWord) =>
        CaptionLabels.IsAllowed(_document, label) && CaptionLabels.IsLine(text) && CaptionLabels.IsLine(labelWord, 100);

    // The caption paragraph at <slot> of the container, as one step. The number it shows is worked out with an empty paragraph standing in its place (taken out again before anything is recorded), so a refusal that depends
    // on the place (a chapter number with no Heading 1 above) changes nothing. <anchor> is where the caret goes when the step is undone.
    private EditResult? PlaceCaption(StoryRef story, IList<Block> container, int slot, EditHistory.Point anchor, string label, string text, bool chapterNumber, string labelWord, IFieldHost? host)
    {
        var stand = new Paragraph();
        container.Insert(slot, stand);
        (string Number, string? Chapter) results;
        try
        {
            results = CaptionBuilder.Results(_document, stand, label, chapterNumber, host);
        }
        finally
        {
            container.RemoveAt(slot);
        }

        if (chapterNumber && results.Chapter is null)
        {
            LastCaptionRefusal = CaptionRefusal.ChapterNeedsNumberedHeadings;
            return null;
        }

        var revision = History.BeginOperation();
        var style = CaptionBuilder.EnsureStyle(_document, out var styleId);
        var paragraph = CaptionBuilder.Build(styleId, labelWord, label, text, chapterNumber, results.Number, results.Chapter);
        container.Insert(slot, paragraph);
        _document.Edits.Raise();

        var caret = new TextPosition(story, paragraph, Map(story, paragraph).Projection.Text.Length);
        var result = new EditResult(caret, [new BlockChange(story, container, slot, 0, 1, [])], Style: style);
        result.Attribute(_document);
        History.Record(result, anchor, revision, null, after: new EditHistory.Point(slot, caret.Offset));
        return result;
    }
}
