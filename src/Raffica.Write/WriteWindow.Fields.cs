using Avalonia.Controls;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The commands of the References tab (the Bookmarks, Caption and Cross-reference cards): what the ribbon, the command search and the macOS menu run; the object facts source answers the prefix refusal. Nothing exists before the first use: the
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

    // The ribbon actions of the References tab: the bookmark, caption and cross-reference commands open their cards.
    private void AddFieldActions()
    {
        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsBookmark(c.Id)))
        {
            _actions[command.Id] = () => _ = GuardAsync(BookmarksAsync);
        }

        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsCaption(c.Id)))
        {
            _actions[command.Id] = () => _ = GuardAsync(CaptionAsync);
        }

        foreach (var command in WriteCommandCatalog.Commands.Where(c => WriteCommandIds.IsCrossReference(c.Id)))
        {
            _actions[command.Id] = () => _ = GuardAsync(CrossReferenceAsync);
        }

        _actions[WriteCommandIds.TocInsert] = () => _ = GuardAsync(TocAsync);
        _actions[WriteCommandIds.TocRemove] = () => RunTocFromUi(new TocRequest(TocAction.Remove, null));
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

    // The Caption card (the labels and the chapter possibility are read from the model when it opens), then the answer under the same plan check as the other cards: an answer after the document changed is refused with a note.
    private async Task CaptionAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        var plan = CardPlan.Of(view);
        if (await FieldCards.AskCaptionAsync(view.CaptionCardContext()) is not { } request)
        {
            return;
        }

        if (CardIsStale(view, plan))
        {
            return;
        }

        _ = RunCaptionFromUi(request);
    }

    /// <summary>Runs an answer of the Caption card on the active view (the view takes the focus back, a ribbon button that kept it would swallow the typing).</summary>
    internal bool RunCaptionFromUi(CaptionRequest request)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunCaptionCommand(request);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    // The Cross-reference card (the targets are read from the model when it opens), then the answer under the same plan check as the other cards: an answer after the document changed is refused with a note.
    private async Task CrossReferenceAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        var plan = CardPlan.Of(view);
        if (await FieldCards.AskCrossReferenceAsync(view.CrossReferenceCardContext()) is not { } request)
        {
            return;
        }

        if (CardIsStale(view, plan))
        {
            return;
        }

        _ = RunCrossReferenceFromUi(request);
    }

    /// <summary>Runs an answer of the Cross-reference card on the active view (the view takes the focus back, a ribbon button that kept it would swallow the typing).</summary>
    internal bool RunCrossReferenceFromUi(CrossRefRequest request)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunCrossReferenceCommand(request);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    // The Table of contents card (the labels and whether the caret is in a table of contents are read from the model when it opens), then the answer under the same plan check as the other cards: an answer after the document changed is refused with a note.
    private async Task TocAsync()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        var plan = CardPlan.Of(view);
        if (await FieldCards.AskTocAsync(view.TocCardContext()) is not { } request)
        {
            return;
        }

        if (CardIsStale(view, plan))
        {
            return;
        }

        _ = RunTocFromUi(request);
    }

    /// <summary>Runs an answer of the Table of contents card, or the Remove command, on the active view (the view takes the focus back, a ribbon button that kept it would swallow the typing).</summary>
    internal bool RunTocFromUi(TocRequest request)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        var taken = view.RunTocCommand(request);
        if (!view.IsFocused)
        {
            view.Focus();
        }

        return taken;
    }

    // ---- the refresh of field results: the first look, the bar and the status text (nothing below runs or exists before the layout of a document with fields is complete) ----

    private readonly Dictionary<WriteTab, (FieldsBar Bar, Control Control)> _fieldBars = [];
    private readonly HashSet<WriteTab> _fieldWatch = [];

    /// <summary>Whether the window watches the tab for the completion of its layout (tests).</summary>
    internal bool IsWatchingFields(WriteTab tab) => _fieldWatch.Contains(tab);

    // Called when the tab's view is made: a measurement run follows no refresh (the two fields runs do, and drive it themselves). Any other tab starts to watch its layout only after its first sharp page (nothing of the refresh exists, and nothing is scanned, before it),
    // asks for its first whole-body look when the layout is complete (once), and offers the bar from the first count. Nothing here may break the layout's event, so every failure is counted and ignored.
    private void WatchFields(WriteTab tab, PageView view)
    {
        tab.RefreshesFields = !_options.IsMeasuring || _options.MeasuresFields;
        if (!tab.RefreshesFields)
        {
            return;
        }

        tab.FieldsNotUpdated += count => OfferFieldsBar(tab, count);
        view.FirstSharpPage += () => ArmFieldWatch(tab);
    }

    internal void ArmFieldWatch(WriteTab tab)
    {
        if (tab.IsDisposed || !_fieldWatch.Add(tab))
        {
            return;
        }

        // True when the watch is over: the tab is gone, or its layout is complete and the first look was asked for (if the document has a field).
        bool Check()
        {
            try
            {
                if (tab.IsDisposed)
                {
                    return true;
                }

                if (tab.Docx.IsPrefix || !tab.Runner.FullModelReady || !tab.Runner.PageLayout.IsComplete)
                {
                    return false;
                }

                if (tab.Fields is null && tab.AnyFields())
                {
                    tab.EnsureFields().Notify(null);
                }

                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                tab.FieldWiringFailures++;
                return true;
            }
        }

        // A layout that is complete already (a short document, or a fast machine) reports nothing more, so the check is made once now and the event is waited for only when it is not complete.
        if (Check())
        {
            return;
        }

        Action? onChanged = null;
        onChanged = () =>
        {
            if (Check())
            {
                tab.Runner.Changed -= onChanged;
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
        _fieldWatch.Remove(tab);
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
