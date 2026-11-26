using Raffica.Write.Commands;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The commands of the References tab (today the Bookmarks card): what the ribbon, the command search and the macOS menu run; the object facts source answers the prefix refusal. Nothing exists before the first use: the
/// cards object and the bookmark list are made when the command first runs.
/// </summary>
internal sealed partial class WriteWindow
{
    private IFieldCards? _fieldCards;

    /// <summary>The cards of the References tab: the dialogs of the window (made on first use), or the cards a test put in.</summary>
    internal IFieldCards FieldCards
    {
        get => _fieldCards ??= new DialogFieldCards(Dialogs);
        set => _fieldCards = value;
    }

    /// <summary>Whether the cards object of the References tab exists (tests; false before the first run of a command of the tab).</summary>
    internal bool FieldCardsExist => _fieldCards is not null;

    // The ribbon actions of the References tab: the bookmark command opens its card.
    private void AddFieldActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsBookmark(c.Id)))
        {
            _actions[command.Id] = () => _ = GuardAsync(BookmarksAsync);
        }
    }

    // The Bookmarks card first (the list is read from the model when it opens), then the answer. The card is modal and its answer is run only when the document is as it was when the card opened (the same plan check as
    // the section cards): an answer after the document changed is refused with a note, never applied.
    private async Task BookmarksAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        var plan = CardPlan.Of(view);
        if (await FieldCards.AskBookmarkAsync(view.BookmarkCardContext()) is not { } request)
        {
            return;
        }

        if (CardIsStale(view, plan))
        {
            return;
        }

        _ = RunBookmarkFromUi(request);
    }

    /// <summary>Runs an answer of the Bookmarks card on the active view (the view takes the focus back, a ribbon button that kept it would swallow the typing).</summary>
    internal bool RunBookmarkFromUi(BookmarkRequest request)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunBookmarkCommand(request);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }
}
