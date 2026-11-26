using Raffica.Hosting;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>What the Bookmarks card asks for.</summary>
internal enum BookmarkAction
{
    /// <summary>Put the selection on the range of the bookmark.</summary>
    GoTo,

    /// <summary>Add a bookmark of the name over the selection.</summary>
    Add,

    /// <summary>Remove the bookmark (the text stays).</summary>
    Delete,
}

/// <summary>The answer of the Bookmarks card: what to do and with which name.</summary>
internal sealed record BookmarkRequest(BookmarkAction Action, string Name)
{
    /// <summary>Whether the request is well formed: a name, and for Add a name that follows Word's rule (the duplicate check is the editor's, against the document as it is then).</summary>
    public bool IsValid => Name is { Length: > 0 } && Enum.IsDefined(Action) && (Action != BookmarkAction.Add || BookmarkNames.Problem(Name, []) is null);
}

/// <summary>The answer of the Caption card: the label (a sequence identifier), the text after the number, where the caption goes and whether it carries the chapter number.</summary>
internal sealed record CaptionRequest(string Label, string Text, CaptionPlace Place, bool ChapterNumber)
{
    /// <summary>Whether the request is well formed: a label that is a sequence identifier, a text that is one line, a known place.</summary>
    public bool IsValid => CaptionLabels.IsUsable(Label) && CaptionLabels.IsLine(Text) && Enum.IsDefined(Place);
}

/// <summary>
/// What the Caption card needs: the labels to offer, the label to start with (Table for a selected table, Figure otherwise; the place follows the label: a table is captioned above, anything else below), whether a chapter number is possible (numbered headings), the word that is shown for a
/// label in this document (the document language, then the interface language).
/// </summary>
internal sealed record CaptionContext(IReadOnlyList<string> Labels, string DefaultLabel, bool CanChapterNumber, Func<string, string> WordFor);

/// <summary>The answer of the Cross-reference card: the target (as the card listed it) and what the reference shows of it.</summary>
internal sealed record CrossRefRequest(CrossRefTarget Target, CrossRefKind Kind)
{
    /// <summary>Whether the request is well formed: a known kind that the target offers.</summary>
    public bool IsValid => Target is not null && Enum.IsDefined(Target.Kind) && CrossReferences.Refusal(Kind, Target) is null;
}

/// <summary>What the Cross-reference card needs: the targets of a type, asked when the type is chosen (read from the model when the card opens, never earlier), and the type to start with.</summary>
internal sealed record CrossRefContext(Func<CrossRefTargetKind, IReadOnlyList<CrossRefTarget>> TargetsOf, CrossRefTargetKind DefaultType);

/// <summary>What the Table of contents card asks for.</summary>
internal enum TocAction
{
    /// <summary>Insert a table of contents (or a list of figures or tables) with the options.</summary>
    Insert,

    /// <summary>Remove the table of contents the caret is in.</summary>
    Remove,
}

/// <summary>The answer of the Table of contents card: what to do and, for Insert, the options (levels, page numbers, alignment, leader and the caption label of a list).</summary>
internal sealed record TocRequest(TocAction Action, TocOptions? Options)
{
    /// <summary>Whether the request is well formed: a known action, and for Insert options with a known leader and a label (when it is a list) that can be written into an instruction. The order of the levels is the editor's to refuse.</summary>
    public bool IsValid => Enum.IsDefined(Action) && (Action == TocAction.Remove || (Options is { } o && Enum.IsDefined(o.Leader) && (o.Label is null || CaptionLabels.IsUsable(o.Label))));
}

/// <summary>What the Table of contents card needs: the caption labels a list can be made of (the labels of the document), and whether the caret is in a table of contents (the card then offers Remove).</summary>
internal sealed record TocContext(IReadOnlyList<string> Labels, bool CanRemove);

/// <summary>What the Bookmarks card needs: the names the user may see (sorted) and every name of the document, hidden ones included, which a new name must not repeat.</summary>
internal sealed record BookmarkContext(IReadOnlyList<string> Names, IReadOnlyList<string> AllNames);

/// <summary>
/// The cards of the References tab, which the window owns (the Bookmarks, Caption, Cross-reference and Table of contents cards). A command asks its card and runs the answer (null: the user cancelled). The window uses <see cref="DialogFieldCards"/>; a test puts
/// its own in.
/// </summary>
internal interface IFieldCards
{
    /// <summary>Asks what to do with the bookmarks: list, Go to, Add, Delete; null when closed.</summary>
    Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context);

    /// <summary>Asks for a caption: the label, the text, the place and the chapter number; null when closed.</summary>
    Task<CaptionRequest?> AskCaptionAsync(CaptionContext context);

    /// <summary>Asks for a cross-reference: the type of the target, the target and what to show of it; null when closed.</summary>
    Task<CrossRefRequest?> AskCrossReferenceAsync(CrossRefContext context);

    /// <summary>Asks for a table of contents or a list: the kind, the levels, the page numbers; or Remove; null when closed.</summary>
    Task<TocRequest?> AskTocAsync(TocContext context);
}

/// <summary>The real cards, shown one at a time through the window's <see cref="DialogQueue"/>.</summary>
internal sealed class DialogFieldCards(DialogQueue dialogs) : IFieldCards
{
    /// <inheritdoc/>
    public Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context) =>
        dialogs.ShowAsync<BookmarkRequest?>(done => BookmarkCard.Create(context, done));

    /// <inheritdoc/>
    public Task<CaptionRequest?> AskCaptionAsync(CaptionContext context) =>
        dialogs.ShowAsync<CaptionRequest?>(done => CaptionCard.Create(context, done));

    /// <inheritdoc/>
    public Task<CrossRefRequest?> AskCrossReferenceAsync(CrossRefContext context) =>
        dialogs.ShowAsync<CrossRefRequest?>(done => CrossReferenceCard.Create(context, done));

    /// <inheritdoc/>
    public Task<TocRequest?> AskTocAsync(TocContext context) =>
        dialogs.ShowAsync<TocRequest?>(done => TocCard.Create(context, done));
}
