using Raffica.Write.Documents;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What a field refresh asks the app: the number labels and note marks of the tab's model (<see cref="FieldLabels"/>: the layout's own numbering counter and note format, worked out for a revision), whether the layout is complete and the
/// page of a position (<see cref="FieldPages"/>: the page the paragraph's block starts on and the number the footer's <c>PAGE</c> field prints there). Pages exist only in the pages view: while the session follows the writing view's strips
/// <see cref="IsComplete"/> is false and <see cref="PageOf"/> says "no answer", so the timer writes no page result and the stored ones stay; a save pays them from a pages layout of its own (<see cref="SettlePages"/>). UI thread only.
/// </summary>
internal sealed class FieldHost(WriteTab tab) : IFieldHost
{
    private WordDocument? _model;
    private FieldLabels? _labels;
    private DocumentLayout? _pagesLayout;
    private FieldPages? _pages;
    private WordDocument? _pagesModel;
    private DocumentLayout? _settlePages;

    /// <summary>The layout the refresh follows: the one the editing session follows, or the pages layout while there is no session; null while the pages layout is stale.</summary>
    internal DocumentLayout? Layout => tab.Editor is { } session ? session.ActiveLayout : (tab.Runner.PageLayoutIsStale ? null : tab.Runner.PageLayout);

    /// <summary>How many times the labels object was made (tests; one per model).</summary>
    internal int LabelObjects { get; private set; }

    /// <summary>
    /// A pages layout a save made for itself in the writing view (<see cref="FieldRefreshRunner"/>): while it is set it answers the page questions instead of the followed layout, which stays what it is. Set to null it is let go,
    /// the page map made over it too.
    /// </summary>
    internal DocumentLayout? SettlePages
    {
        get => _settlePages;
        set
        {
            _settlePages = value;
            if (value is null)
            {
                _pages = null;
                _pagesLayout = null;
                _pagesModel = null;
            }
        }
    }

    /// <summary>True when the layout the page questions go to has pages (not the writing view's strips) and is complete: only then is a page answer final.</summary>
    public bool IsComplete => PageSource is { Mode: LayoutMode.Pages, IsComplete: true };

    /// <summary>True when the layout the page questions go to has pages, complete or not: a layout that can be completed for a page answer.</summary>
    internal bool HasPages => PageSource is { Mode: LayoutMode.Pages };

    private DocumentLayout? PageSource => _settlePages ?? Layout;

    public string? NumberLabel(Paragraph paragraph) => Labels().NumberLabel(paragraph);

    public string? ChapterNumber(Paragraph paragraph) => Labels().ChapterNumber(paragraph);

    public (int PageIndex, string PageText)? PageOf(TextPosition position)
    {
        if (position.Story.Kind != StoryKind.Body || PageSource is not { Mode: LayoutMode.Pages } layout)
        {
            return null;
        }

        return PagesFor(layout).PageOf(position.Paragraph);
    }

    public long? PageSignature => PageSource is { Mode: LayoutMode.Pages } layout ? PagesFor(layout).Signature() : null;

    private FieldPages PagesFor(DocumentLayout layout)
    {
        var model = tab.Docx.Model;
        if (_pages is null || !ReferenceEquals(_pagesLayout, layout) || !ReferenceEquals(_pagesModel, model))
        {
            _pagesLayout = layout;
            _pagesModel = model;
            _pages = new FieldPages(model, layout);
        }

        return _pages;
    }

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
