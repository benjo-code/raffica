using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The host of a field refresh over a real layout: labels and note marks as <see cref="FakeFieldHost"/> counts them (<see cref="ReferenceNumbering"/>), the page of a paragraph from the layout through <see cref="FieldPages"/> (the app's definition of
/// "the page and the printed number"), complete when the layout is. Make a new one when the layout or the model's block order changes.
/// </summary>
internal sealed class LayoutFieldHost(WordDocument document, DocumentLayout layout) : IFieldHost
{
    private readonly FakeFieldHost _labels = new(document);
    private readonly FieldPages _pages = new(document, layout);

    /// <summary>How many page answers were asked for.</summary>
    public int PageQuestions { get; private set; }

    public bool IsComplete => layout.Mode == LayoutMode.Pages && layout.IsComplete;

    public string? NumberLabel(Paragraph paragraph) => _labels.NumberLabel(paragraph);

    public string? ChapterNumber(Paragraph paragraph) => _labels.ChapterNumber(paragraph);

    public bool LabelEndsInTab(Paragraph paragraph) => _labels.LabelEndsInTab(paragraph);

    public LineGeometry MeasureLine(Paragraph paragraph, string text) => _labels.MeasureLine(paragraph, text);

    public (int PageIndex, string PageText)? PageOf(TextPosition position)
    {
        PageQuestions++;
        return position.Story.Kind == StoryKind.Body ? _pages.PageOf(position.Paragraph) : null;
    }

    public long? PageSignature => IsComplete ? _pages.Signature() : null;

    public string? NoteMarkText(int noteId, NoteType type) => _labels.NoteMarkText(noteId, type);
}
