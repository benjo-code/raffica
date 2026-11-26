using Raffica.Write.Documents;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What a field refresh asks the app: the number labels and note marks of the tab's model (<see cref="FieldLabels"/>: the layout's own numbering counter and note format, worked out for a revision) and whether the layout is complete.
/// The page of a position is not answered yet (the page-based fields come with the table of contents, milestone 4.5b task 11): <see cref="PageOf"/> says "no answer". UI thread only.
/// </summary>
internal sealed class FieldHost(WriteTab tab) : IFieldHost
{
    private WordDocument? _model;
    private FieldLabels? _labels;

    /// <summary>The layout the refresh follows: the one the editing session follows, or the pages layout while there is no session; null while the pages layout is stale.</summary>
    internal DocumentLayout? Layout => tab.Editor is { } session ? session.ActiveLayout : (tab.Runner.PageLayoutIsStale ? null : tab.Runner.PageLayout);

    /// <summary>How many times the labels object was made (tests; one per model).</summary>
    internal int LabelObjects { get; private set; }

    public bool IsComplete => Layout?.IsComplete ?? false;

    public string? NumberLabel(Paragraph paragraph) => Labels().NumberLabel(paragraph);

    public (int PageIndex, string PageText)? PageOf(TextPosition position) => null;

    public string? NoteMarkText(int noteId, NoteType type) => Labels().NoteMarkText(noteId, type);

    private FieldLabels Labels()
    {
        var model = tab.Docx.Model;
        if (_labels is null || !ReferenceEquals(_model, model))
        {
            _model = model;
            _labels = new FieldLabels(model);
            LabelObjects++;
        }

        return _labels;
    }
}
