using Avalonia.Controls;
using Avalonia.Threading;
using Raffica.Shell.Frame;
using Raffica.Write.Documents;
using Raffica.Write.Layout;
using Raffica.Write.Panes;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The outline of the window: the left slot of the frame with the outline pane of the active tab. Nothing exists before the user asks for the outline (neither the
/// slot's pane nor an outline pane); each tab's pane is made on first use and kept until the tab closes. A choice in the outline scrolls the view to the heading; a
/// heading on a page that is not laid out yet is waited for off the UI thread.
/// </summary>
internal sealed partial class WriteWindow
{
    /// <summary>The width of the outline pane in device-independent pixels.</summary>
    internal const double OutlinePaneWidth = 240;

    private readonly Dictionary<WriteTab, OutlinePane> _outlines = [];
    private Pane? _outlineHost;
    private bool _outlineWanted;
    private Navigation? _navigation;

    /// <summary>The left slot of the frame; null until the outline was asked for the first time (tests).</summary>
    internal Pane? OutlineHost => _outlineHost;

    /// <summary>Whether the outline is on (the user asked for it and a document is open).</summary>
    internal bool IsOutlineOpen => _outlineWanted && _outlineHost is { IsOpen: true };

    /// <summary>The outline pane of a tab, or null while none was made (tests).</summary>
    internal OutlinePane? OutlineOf(WriteTab tab) => _outlines.GetValueOrDefault(tab);

    /// <summary>True while a choice in the outline waits for the layout to reach its heading (tests).</summary>
    internal bool IsNavigationWaiting => _navigation is not null;

    // The command of the outline: a switch. Opening it puts the focus in the list so the keys work at once; closing it gives the focus back to the document.
    private void ToggleOutline()
    {
        _outlineWanted = !_outlineWanted;
        PlaceOutline();
        if (_outlineWanted && Workspace.Active is { } tab && _outlines.TryGetValue(tab, out var pane))
        {
            pane.FocusContent();
        }
        else
        {
            FocusActiveView();
        }

        RefreshCommandStates();
    }

    // Puts the outline pane of the active tab into the slot (the slot and the pane are made when first needed); no tab or no wish: the slot is closed.
    private void PlaceOutline()
    {
        if (_outlineHost is null && !_outlineWanted)
        {
            return;
        }

        var host = _outlineHost ??= new Pane { Title = WriteStrings.PaneOutline, Width = OutlinePaneWidth, IsOpen = false };
        var active = Workspace.Active;
        var show = _outlineWanted && active is not null && Tabs.Items.Count > 0 && ViewOf(active) is not null;
        var focusWasInside = host.IsKeyboardFocusWithin;
        var before = host.Content;
        host.IsOpen = show;
        host.Content = show ? PaneOf(active!) : null;
        Frame.LeftPane = Tabs.Items.Count > 0 ? host : null;
        if (focusWasInside && (!show || !ReferenceEquals(before, host.Content)))
        {
            FocusActiveView();
        }
    }

    private OutlinePane PaneOf(WriteTab tab)
    {
        if (!_outlines.TryGetValue(tab, out var pane))
        {
            pane = new OutlinePane(tab, ViewOf(tab) ?? throw new InvalidOperationException("The tab has no view yet."));
            pane.Navigate += block =>
            {
                NavigateTo(tab, block);

                // As in the PDF app: after a choice the keys belong to the page again (PageUp and PageDown scroll it), at once, also while the target is still waited for.
                // Posted once more because a click gives the focus to the row after this handler ran.
                if (ViewOf(tab) is { } chosenView)
                {
                    chosenView.Focus();
                    Dispatcher.UIThread.Post(
                        () =>
                        {
                            if (!tab.IsDisposed && ReferenceEquals(ActiveView, chosenView))
                            {
                                chosenView.Focus();
                            }
                        },
                        DispatcherPriority.Input);
                }
            };
            _outlines[tab] = pane;
        }

        return pane;
    }

    // A tab closed: its pane goes with it, and so does a choice that waited for it.
    private void ForgetOutline(WriteTab tab)
    {
        if (_outlines.Remove(tab, out var pane))
        {
            pane.Dispose();
        }

        if (_navigation is { } navigation && ReferenceEquals(navigation.Tab, tab))
        {
            EndNavigation(navigation);
        }
    }

    /// <summary>
    /// Scrolls the view of a tab so that the block is at the top. A block on a page that is not laid out yet is waited for: the view holds the wish and the pages up to
    /// it are laid out on a pool thread (never the UI thread) while the status bar says so. A scroll by the user, another choice or the closing of the tab ends the wait.
    /// </summary>
    internal void NavigateTo(WriteTab tab, int block)
    {
        if (tab.IsDisposed || ViewOf(tab) is not { } view)
        {
            return;
        }

        if (_navigation is { } previous)
        {
            EndNavigation(previous);
        }

        view.ScrollToBlock(block);
        if (!view.IsWaitingForAnchor)
        {
            return;
        }

        if (Settled(view) && CurrentLayout(view)?.Locate(block) is null)
        {
            // A block the layout never placed (nothing to wait for).
            view.CancelPendingAnchor();
            return;
        }

        view.Runner.Start();
        var navigation = new Navigation(tab, view, CancellationTokenSource.CreateLinkedTokenSource(tab.Token));
        _navigation = navigation;
        Status.Hint = WriteStrings.OutlineWaiting;
        navigation.Changed = () =>
        {
            if (!view.IsWaitingForAnchor)
            {
                EndNavigation(navigation);
            }
        };
        view.ViewChanged += navigation.Changed;
        _ = WaitForBlockAsync(navigation, block);
    }

    // The layout of the view's mode now (the runner may replace it; the strips may not exist yet).
    private static DocumentLayout? CurrentLayout(PageView view) => view.Runner.LayoutFor(view.Mode == WriteView.Writing ? LayoutMode.Flow : LayoutMode.Pages);

    // Nothing more will come: the layout of the mode is complete and it works on the whole model, or the layout failed. The readiness is read first: the runner makes the
    // whole-model layout current before it says "ready", so the layout read after it is the right one.
    private static bool Settled(PageView view)
    {
        var runner = view.Runner;
        var ready = runner.FullModelReady;
        return runner.IsFaulted || (ready && CurrentLayout(view) is { IsComplete: true });
    }

    // Waits for the whole model first (a layout of the prefix model must not get a second page: the whole model takes its place only while page 1 is the only one), then lays
    // out the pages up to the block on the pool (the layout takes one caller at a time, so this shares the work with the background run and never repeats it); then the view
    // is asked again, on the UI thread.
    private async Task WaitForBlockAsync(Navigation navigation, int block)
    {
        var token = navigation.Source.Token;
        var view = navigation.View;
        try
        {
            await view.Runner.FullModelTask.WaitAsync(token);
            await Task.Run(
                async () =>
                {
                    while (!token.IsCancellationRequested && !Settled(view))
                    {
                        if (CurrentLayout(view) is not { } layout)
                        {
                            // The strips of the writing view are not made yet (they start once the whole model is there).
                            await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
                            continue;
                        }

                        if (layout.Locate(block) is not null)
                        {
                            return;
                        }

                        layout.LayOutThrough(layout.LaidOutPages);
                    }
                },
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A cancelled wait, or a layout that failed (its runner reports that): the wish just ends.
        }

        if (navigation.Ended)
        {
            return;
        }

        if (!token.IsCancellationRequested && !view.IsReleased)
        {
            view.ScrollToBlock(block);
            if (view.IsWaitingForAnchor && Settled(view))
            {
                view.CancelPendingAnchor();
            }
        }

        EndNavigation(navigation);
    }

    private void EndNavigation(Navigation navigation)
    {
        if (navigation.Ended)
        {
            return;
        }

        navigation.Ended = true;
        if (navigation.Changed is { } changed)
        {
            navigation.View.ViewChanged -= changed;
        }

        navigation.Source.Cancel();
        navigation.Source.Dispose();
        if (ReferenceEquals(_navigation, navigation))
        {
            _navigation = null;
            Status.Hint = null;
        }
    }

    // One choice that waits for its page.
    private sealed class Navigation(WriteTab tab, PageView view, CancellationTokenSource source)
    {
        public WriteTab Tab { get; } = tab;

        public PageView View { get; } = view;

        public CancellationTokenSource Source { get; } = source;

        public Action? Changed { get; set; }

        public bool Ended { get; set; }
    }
}
