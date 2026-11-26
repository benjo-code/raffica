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
    public bool IsValid => CaptionLabels.Problem(Label) is null && CaptionBuilder.IsLine(Text) && Enum.IsDefined(Place);
}

/// <summary>
/// What the Caption card needs: the labels to offer, the label to start with (Table for a selected table, Figure otherwise; the place follows the label: a table is captioned above, anything else below), whether a chapter number is possible (numbered headings), the word that is shown for a
/// label in this document (the document language, then the interface language).
/// </summary>
internal sealed record CaptionContext(IReadOnlyList<string> Labels, string DefaultLabel, bool CanChapterNumber, Func<string, string> WordFor);

/// <summary>What the Bookmarks card needs: the names the user may see (sorted) and every name of the document, hidden ones included, which a new name must not repeat.</summary>
internal sealed record BookmarkContext(IReadOnlyList<string> Names, IReadOnlyList<string> AllNames);

/// <summary>
/// The cards of the References tab, which the window owns (the Bookmarks card and the Caption card). A command asks its card and runs the answer (null: the user cancelled). The window uses <see cref="DialogFieldCards"/>; a test puts
/// its own in.
/// </summary>
internal interface IFieldCards
{
    /// <summary>Asks what to do with the bookmarks: list, Go to, Add, Delete; null when closed.</summary>
    Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context);

    /// <summary>Asks for a caption: the label, the text, the place and the chapter number; null when closed.</summary>
    Task<CaptionRequest?> AskCaptionAsync(CaptionContext context);
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
}
