using Avalonia.Controls;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
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

    // ---- the refresh of field results: the first look, the bar and the status text (nothing below runs or exists before the layout of a document with fields is complete) ----

    private readonly Dictionary<WriteTab, (FieldsBar Bar, Control Control)> _fieldBars = [];

    // Called when the tab's view is made: a measurement run follows no refresh, any other tab asks for its first whole-body look when its layout is complete (once), and offers the bar from the first count.
    private void WatchFields(WriteTab tab)
    {
        tab.RefreshesFields = !_options.IsMeasuring;
        if (!tab.RefreshesFields)
        {
            return;
        }

        tab.FieldsNotUpdated += count => OfferFieldsBar(tab, count);
        Action? onChanged = null;
        onChanged = () =>
        {
            if (tab.IsDisposed)
            {
                tab.Runner.Changed -= onChanged;
                return;
            }

            if (tab.Docx.IsPrefix || !tab.Runner.FullModelReady || !tab.Runner.PageLayout.IsComplete)
            {
                return;
            }

            tab.Runner.Changed -= onChanged;
            if (tab.Fields is null && FieldView.AnyIn(tab.Docx.Model))
            {
                tab.EnsureFields().Notify(null);
            }
        };
        tab.Runner.Changed += onChanged;
    }

    private void OfferFieldsBar(WriteTab tab, int count)
    {
        if (!_fieldBars.TryGetValue(tab, out var entry))
        {
            var bar = new FieldsBar();
            if (bar.Offer(count) is null)
            {
                return;
            }

            entry = (bar, bar.Build(SyncFieldsBar));
            _fieldBars[tab] = entry;
        }
        else if (entry.Bar.Offer(count) is null)
        {
            return;
        }

        SyncFieldsBar();
    }

    /// <summary>The bar of the active tab's document session while it is shown (tests); null otherwise.</summary>
    internal FieldsBar? ShownFieldsBar => Workspace.Active is { } tab && _fieldBars.TryGetValue(tab, out var entry) && entry.Bar.IsShown && ReferenceEquals(Frame.Notification, entry.Control) ? entry.Bar : null;

    // The notification row shows the bar of the active tab, or nothing of ours.
    private void SyncFieldsBar()
    {
        if (Workspace.Active is { } tab && _fieldBars.TryGetValue(tab, out var entry) && entry.Bar.IsShown)
        {
            Frame.Notification = entry.Control;
            return;
        }

        if (Frame.Notification is { } shown && _fieldBars.Values.Any(e => ReferenceEquals(e.Control, shown)))
        {
            Frame.Notification = null;
        }
    }

    private void ForgetFields(WriteTab tab)
    {
        _fieldBars.Remove(tab);
        SyncFieldsBar();
    }

    // The status cell: "not updated" while the selection touches a field Write does not update (a field result is atomic, so a caret touches the field at either edge); nothing before the first caret.
    private static string? FieldStatusText(PageView view)
    {
        if (view.Tab.Editor is not { CanEdit: true } session || session.Selection is not { } selection || !selection.Start.Story.Equals(StoryRef.Body) || !ReferenceEquals(selection.Start.Paragraph, selection.End.Paragraph))
        {
            return null;
        }

        var paragraph = selection.Start.Paragraph;
        var host = session.ProjectionHostFor(selection.Start.Story, paragraph);
        var from = Math.Min(selection.Start.Offset, selection.End.Offset);
        var to = Math.Max(selection.Start.Offset, selection.End.Offset);
        return FieldView.IsNotUpdatedAt(paragraph, host, from, to) ? WriteStrings.StatusFieldNotUpdated : null;
    }

    internal string? StatusCountText(PageView view) => StoryStatusText(view) ?? FieldStatusText(view);
}
