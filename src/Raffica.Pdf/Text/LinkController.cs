using Avalonia;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Text;

/// <summary>What a link click does.</summary>
internal abstract record LinkAction;

/// <summary>Go to a place in the same document.</summary>
/// <param name="Page">The 0-based page, a page the document has.</param>
/// <param name="Top">Points from the top of the page, or null for the page itself.</param>
internal sealed record PageJump(int Page, double? Top) : LinkAction;

/// <summary>Open an address outside the document, after the user confirmed it.</summary>
/// <param name="Link">The validated address: only http, https and mailto, never longer than Core allows.</param>
internal sealed record OpenExternal(ExternalLink Link) : LinkAction;

/// <summary>
/// The links of the page view. Core hands over only two kinds of target: a place in the document and an address (it drops launch actions,
/// JavaScript, remote go-to and anything else it cannot follow, so the app never sees them, and it has no embedded-file link). An address
/// is clickable only when Core's <see cref="ExternalLink.TryParse"/> accepts it; whatever it refuses has no hand cursor and no card. A
/// click on an address shows <see cref="LinkCard"/> with the address in full; only the Open button of the card, after its delay, hands
/// the address to the launcher, and the launcher gets exactly the displayed string.
/// </summary>
internal sealed class LinkController
{
    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly DialogQueue _dialogs;
    private readonly Action<Uri> _launcher;
    private readonly TimeProvider _time;
    private bool _cardPending;

    /// <param name="tab">The document whose page texts hold the links.</param>
    /// <param name="view">The page view that shows it.</param>
    /// <param name="dialogs">Where the card is shown.</param>
    /// <param name="launcher">Opens an address with the system; null takes <see cref="SystemLauncher.Open"/>. It is called only for confirmed addresses.</param>
    /// <param name="time">The clock of the arming delay of the card (tests move it); null takes the system clock.</param>
    public LinkController(PdfTab tab, PageView view, DialogQueue dialogs, Action<Uri>? launcher = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(dialogs);
        _tab = tab;
        _view = view;
        _dialogs = dialogs;
        _launcher = launcher ?? SystemLauncher.Open;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>What a click on the link does, or null when the link is not clickable (its target is not safe or does not exist).</summary>
    /// <param name="link">The link read by Core.</param>
    /// <param name="pageCount">The number of pages of the document; an internal link to another page is not clickable.</param>
    public static LinkAction? Resolve(PageLink link, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(link);
        return link.Target switch
        {
            PageTarget target when target.PageIndex >= 0 && target.PageIndex < pageCount => new PageJump(target.PageIndex, target.Top),
            UriTarget target when ExternalLink.TryParse(target.Url, out var external) => new OpenExternal(external),
            _ => null,
        };
    }

    /// <summary>The clickable link under a point of the view, or null (also while the text of the page is not loaded).</summary>
    public PageLink? LinkAt(Point point)
    {
        if (_view.PageAt(point) is not { } spot || !PageTexts.TryGet(_tab, spot.Page, out var text))
        {
            return null;
        }

        // The first link that contains the point decides (Core's order: annotations first); a link that is not safe is not clickable.
        return text.LinkAt(spot.X, spot.Y) is { } link && Resolve(link, _view.PageCount) is not null ? link : null;
    }

    /// <summary>Follows a link: an internal one scrolls the view, an external one asks first. Returns when the card is closed (and the address opened or not).</summary>
    public async Task ActivateAsync(LinkAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action)
        {
            case PageJump jump:
                _view.GoToDestination(jump.Page, jump.Top);
                break;
            case OpenExternal external:
                await ConfirmAndOpenAsync(external.Link);
                break;
        }
    }

    /// <summary>Follows the link under a point if there is one.</summary>
    public Task ActivateAsync(PageLink link) =>
        Resolve(link, _view.PageCount) is { } action ? ActivateAsync(action) : Task.CompletedTask;

    private async Task ConfirmAndOpenAsync(ExternalLink link)
    {
        // A second click while a card is up (or waits) must not queue a second card.
        if (_cardPending)
        {
            return;
        }

        _cardPending = true;
        try
        {
            var confirmed = await _dialogs.ShowAsync<bool>(done => LinkCard.Create(link, () => done(true), () => done(false), _time));
            if (confirmed)
            {
                Launch(link);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The card could not be shown: nothing is opened.
        }
        finally
        {
            _cardPending = false;
        }
    }

    private void Launch(ExternalLink link)
    {
        try
        {
            // The string of the card, nothing else: the Uri keeps it as the original string.
            _launcher(new Uri(link.Url, UriKind.Absolute));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // No browser or mail program, or the system refused: the user can copy the address from the document.
        }
    }
}
