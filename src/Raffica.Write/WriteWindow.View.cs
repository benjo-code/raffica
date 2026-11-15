using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Viewing;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>The part of the window that shows the documents: the page view of each tab, the ribbon with its commands and the status bar cells that follow the view.</summary>
internal sealed partial class WriteWindow
{
    private WriteCommandRules _rules = null!;
    private Dictionary<string, Action> _actions = null!;
    private readonly HashSet<WriteTab> _faultShown = [];
    private string? _editHint;
    private int _editHintGeneration;

    /// <summary>How long the hint of a refused key stays when nothing ends it sooner.</summary>
    internal static readonly TimeSpan EditHintDuration = TimeSpan.FromSeconds(6);

    /// <summary>The ribbon (the tabs Home and View); its parts sit in the frame while a document tab is open.</summary>
    public RibbonView Ribbon { get; private set; } = null!;

    /// <summary>The page view of the active tab, or null (the start view).</summary>
    internal PageView? ActiveView => Workspace.Active is { } tab && _areas.TryGetValue(tab, out var area) ? area as PageView : null;

    /// <summary>The page view of a tab that has been shown (tests); null before.</summary>
    internal PageView? ViewOf(WriteTab tab) => AreaOf(tab) as PageView;

    // Made once, before the frame shows anything: the commands, the rules that decide when they run and the ribbon built from them.
    private void CreateRibbon()
    {
        _rules = new WriteCommandRules(() => Workspace.Active is not null, () => ActiveView?.Zoom ?? ZoomSteps.Default, () => true, () => EditCommandState.Of(Workspace.Active));
        _actions = CreateActions();
        var definition = WriteRibbon.Create(_actions, _rules, IsCommandOn);
        Ribbon = new RibbonView { Definition = definition, ActiveTab = definition.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter) };
        TitleSearch.Source = definition;
        TitleSearch.CommandChosen += command => command.Run?.Invoke();
    }

    // What each command does.
    private Dictionary<string, Action> CreateActions() => new()
    {
        [WriteCommandIds.Open] = () => _ = GuardAsync(OpenFromDialogAsync),
        [WriteCommandIds.CloseTab] = CloseActiveTab,
        [WriteCommandIds.Settings] = () => Settings.Open(),
        [WriteCommandIds.Search] = TitleSearch.FocusAndSelect,
        [WriteCommandIds.ZoomIn] = () => ActiveView?.ZoomIn(),
        [WriteCommandIds.ZoomOut] = () => ActiveView?.ZoomOut(),
        [WriteCommandIds.ActualSize] = () => ActiveView?.ActualSize(),
        [WriteCommandIds.FitWidth] = () => ActiveView?.FitWidth(),
        [WriteCommandIds.FitPage] = () => ActiveView?.FitPage(),
        [WriteCommandIds.PageView] = () => SetView(WriteView.Page),
        [WriteCommandIds.WritingView] = () => SetView(WriteView.Writing),
        [WriteCommandIds.Outline] = ToggleOutline,
        [WriteCommandIds.Undo] = () => RunEditFromUi(WriteCommandIds.Undo),
        [WriteCommandIds.Redo] = () => RunEditFromUi(WriteCommandIds.Redo),
        [WriteCommandIds.Cut] = () => RunEditFromUi(WriteCommandIds.Cut),
        [WriteCommandIds.Copy] = () => RunEditFromUi(WriteCommandIds.Copy),
        [WriteCommandIds.Paste] = () => RunEditFromUi(WriteCommandIds.Paste),
        [WriteCommandIds.SelectAll] = () => RunEditFromUi(WriteCommandIds.SelectAll),
    };

    // Whether a switch of the ribbon is on: the view in force and the zoom mode of the active tab.
    private bool IsCommandOn(string id)
    {
        if (ActiveView is not { } view)
        {
            return false;
        }

        return id switch
        {
            WriteCommandIds.PageView => view.Mode == WriteView.Page,
            WriteCommandIds.WritingView => view.Mode == WriteView.Writing,
            WriteCommandIds.ActualSize => view.ZoomMode == ZoomMode.ActualSize,
            WriteCommandIds.FitWidth => view.ZoomMode == ZoomMode.FitWidth,
            WriteCommandIds.FitPage => view.ZoomMode == ZoomMode.FitPage,
            WriteCommandIds.Outline => _outlineWanted,
            _ => false,
        };
    }

    // An edit command that came from the ribbon, the command search or the menu works on the view's selection, so the view takes the focus back: a ribbon button that kept it would
    // swallow the typing and run the command again on Space or Enter. The keys of the page view do not come here.
    private void RunEditFromUi(string id)
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        view.RunEditCommand(id);
        if (!view.IsFocused)
        {
            view.Focus();
        }
    }

    /// <summary>Runs a command of the catalog by id (the keys, the macOS menu and the tests); false when it is not allowed or has no action.</summary>
    internal bool RunCommand(string id)
    {
        if (!_rules.Allows(id) || !_actions.TryGetValue(id, out var run))
        {
            return false;
        }

        run();
        return true;
    }

    /// <summary>Shows the active document in another view (a no-op without one).</summary>
    internal void SetView(WriteView view)
    {
        if (ActiveView is { } active)
        {
            active.Mode = view;
        }
    }

    // The area of a tab: its page view in the view and zoom the settings give a new tab. The first drawn page starts the rest of the layout.
    private PageView CreateDocumentArea(WriteTab tab)
    {
        var view = new PageView(tab, Settings.App.DefaultView, Settings.App.DefaultZoomMode);
        view.ViewChanged += () =>
        {
            if (ReferenceEquals(ActiveView, view))
            {
                UpdateViewStatus();
            }
        };
        // The keys of the page view run the command on the view that has the focus and the rules decide (no focus change: the view has it).
        view.EditCommandHandler = id => _rules.Allows(id) && view.RunEditCommand(id);
        view.SelectionStateChanged += () =>
        {
            if (ReferenceEquals(ActiveView, view))
            {
                RefreshCommandStates();
            }
        };
        view.InputRefused += reason => OnInputRefused(view, tab, reason);
        view.FirstSharpPage += () =>
        {
            FirstSharpPageDrawn?.Invoke(tab);
            tab.Runner.Start();
            if (ReferenceEquals(ActiveView, view))
            {
                FocusActiveView();
            }
        };
        view.AttachedToVisualTree += (_, _) =>
        {
            if (ReferenceEquals(ActiveView, view))
            {
                FocusActiveView();
            }
        };
        tab.Runner.Faulted += () => OnLayoutFaulted(tab);
        return view;
    }

    // Typing or an editing key was refused (only a prefix is open, or editing failed): one hint in the status bar per burst of keys. It stays while the keys go on, goes when the whole
    // file is open (the first case) or after a few seconds, and a later key shows it again. A key during a save is queued and says nothing.
    private void OnInputRefused(PageView view, WriteTab tab, EditBlockedReason reason)
    {
        if (!ReferenceEquals(ActiveView, view))
        {
            return;
        }

        var text = reason == EditBlockedReason.StillOpening ? WriteStrings.EditStillOpening : tab.Runner.FaultedByEdit ? WriteStrings.EditFailed : WriteStrings.EditUnavailable;
        if (_editHint == text && Status.Hint == text)
        {
            return;
        }

        Status.Hint = text;
        _editHint = text;
        var generation = ++_editHintGeneration;
        void Clear()
        {
            if (generation == _editHintGeneration)
            {
                _editHint = null;
                if (Status.Hint == text)
                {
                    Status.Hint = null;
                }
            }
        }

        if (reason == EditBlockedReason.StillOpening)
        {
            var ui = Dispatcher.UIThread;
            _ = tab.Runner.FullModelTask.ContinueWith(_ => ui.Post(Clear), TaskScheduler.Default);
        }

        DispatcherTimer.RunOnce(Clear, EditHintDuration);
    }

    // A layout failed (the whole file could not be read after page 1, or a bug of the layout): one card per tab, in the wording of the open errors (never the text of
    // the exception). The tab stays open on what is laid out; the status no longer promises more pages. Nothing is shown for a window that is closing or a closed tab.
    private void OnLayoutFaulted(WriteTab tab)
    {
        if (_closeConfirmed || tab.IsDisposed)
        {
            return;
        }

        if (tab.Runner.FaultedByEdit)
        {
            // Editing failed in an open document: the file on disk is as it was, so no card about a file that cannot be read; the status says editing stopped.
            if (ActiveView is { } active && ReferenceEquals(active.Tab, tab))
            {
                OnInputRefused(active, tab, EditBlockedReason.Faulted);
                UpdateViewStatus();
            }

            return;
        }

        if (!_faultShown.Add(tab))
        {
            return;
        }

        ShowOpenError(tab.Path, tab.Runner.Fault is DocxFormatException or InvalidDataException ? OpenError.Corrupt : OpenError.Other);
        if (ReferenceEquals(Workspace.Active, tab))
        {
            UpdateViewStatus();
        }
    }

    // Puts the focus on the page view of the active tab so the keys scroll it. A focus that does not take (the view is not on screen yet) is tried again a few times; a
    // text box that has the focus (the command search) and the outline list keep it.
    private void FocusActiveView(int attempts = 6)
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(ActiveView, view) || view.IsFocused || FocusedElement() is TextBox || InOutline(FocusedElement()))
                {
                    return;
                }

                if (!(view.IsEffectivelyVisible && view.Focus()) && attempts > 1)
                {
                    DispatcherTimer.RunOnce(() => FocusActiveView(attempts - 1), TimeSpan.FromMilliseconds(50));
                }
            },
            DispatcherPriority.Background);
    }

    // Whether the focus is inside the outline pane: its list keeps the focus (and its keys) until the user chooses a heading or leaves it.
    private bool InOutline(IInputElement? focused) => _outlineHost is { } host && focused is Avalonia.Visual visual && host.IsVisualAncestorOf(visual);

    private IInputElement? FocusedElement() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

    // Navigation keys that reach the window while the page view does not have the focus: they scroll the active view, so a lost focus cannot swallow them. The arrows
    // only when nothing has the focus (they move inside the ribbon and the tabs); PageUp, PageDown, Home and End also when a button or tab has it, never a text box, a slider or the outline list (they are its own keys).
    private bool ForwardNavigationKey(KeyEventArgs e)
    {
        if (ActiveView is not { IsFocused: false } view || !PageView.IsNavigationKey(e.Key, e.KeyModifiers))
        {
            return false;
        }

        var focused = FocusedElement();
        var arrow = e.Key is Key.Up or Key.Down or Key.Left or Key.Right;
        var inOutline = InOutline(focused);
        var allowed = focused is null || ReferenceEquals(focused, this) || (!arrow && !inOutline && focused is not TextBox and not ComboBox and not Slider);
        return allowed && view.HandleNavigationKey(e.Key, e.KeyModifiers);
    }

    /// <summary>The cells of the status bar follow the view of the active tab: the page (with a plus while the count may still grow) and the zoom.</summary>
    private void UpdateViewStatus()
    {
        if (ActiveView is { PageCount: > 0 } view)
        {
            var format = view.IsLayoutComplete ? WriteStrings.StatusPositionFormat : WriteStrings.StatusPositionMoreFormat;
            Status.Position = WriteStrings.Fill(format, view.CurrentPage + 1, view.PageCount);
            Status.ZoomText = ZoomText(view.Zoom);
            Status.Zoom = view.Zoom * 100;
        }
        else
        {
            Status.Position = null;
        }

        RefreshCommandStates();
    }

    /// <summary>The zoom cell: the zoom as a whole percentage, for example <c>125%</c>.</summary>
    internal static string ZoomText(double zoom) =>
        (double.IsFinite(zoom) ? Math.Round(zoom * 100, MidpointRounding.AwayFromZero) : 100).ToString("0", WriteStrings.Culture) + "%";

    /// <summary>Asks every command of the ribbon again whether it may run, and the macOS menu to read its states.</summary>
    private void RefreshCommandStates()
    {
        Ribbon.RefreshCommandStates();
        StatesChanged?.Invoke();
    }
}
