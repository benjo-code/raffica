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

/// <summary>What the Bookmarks card needs: the names the user may see (sorted) and every name of the document, hidden ones included, which a new name must not repeat.</summary>
internal sealed record BookmarkContext(IReadOnlyList<string> Names, IReadOnlyList<string> AllNames);

/// <summary>
/// The cards of the References tab, which the window owns (today the Bookmarks card). A command asks its card and runs the answer (null: the user cancelled). The window uses <see cref="DialogFieldCards"/>; a test puts
/// its own in.
/// </summary>
internal interface IFieldCards
{
    /// <summary>Asks what to do with the bookmarks: list, Go to, Add, Delete; null when closed.</summary>
    Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context);
}

/// <summary>The real cards, shown one at a time through the window's <see cref="DialogQueue"/>.</summary>
internal sealed class DialogFieldCards(DialogQueue dialogs) : IFieldCards
{
    /// <inheritdoc/>
    public Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context) =>
        dialogs.ShowAsync<BookmarkRequest?>(done => BookmarkCard.Create(context, done));
}
