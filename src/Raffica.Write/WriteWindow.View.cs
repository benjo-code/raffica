using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Raffica.Core.Viewing;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>The part of the window that shows the documents: the page view of each tab, the ribbon with its commands and the status bar cells that follow the view.</summary>
internal sealed partial class WriteWindow
{
    private WriteCommandRules _rules = null!;
    private Dictionary<string, Action> _actions = null!;

    /// <summary>The ribbon (the tabs Home and View); its parts sit in the frame while a document tab is open.</summary>
    public RibbonView Ribbon { get; private set; } = null!;

    /// <summary>The page view of the active tab, or null (the start view).</summary>
    internal PageView? ActiveView => Workspace.Active is { } tab && _areas.TryGetValue(tab, out var area) ? area as PageView : null;

    /// <summary>The page view of a tab that has been shown (tests); null before.</summary>
    internal PageView? ViewOf(WriteTab tab) => AreaOf(tab) as PageView;

    // Made once, before the frame shows anything: the commands, the rules that decide when they run and the ribbon built from them.
    private void CreateRibbon()
    {
        _rules = new WriteCommandRules(() => Workspace.Active is not null, () => ActiveView?.Zoom ?? ZoomSteps.Default);
        _actions = CreateActions();
        var definition = WriteRibbon.Create(_actions, _rules, IsCommandOn);
        Ribbon = new RibbonView { Definition = definition, ActiveTab = definition.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter) };
        TitleSearch.Source = definition;
        TitleSearch.CommandChosen += command => command.Run?.Invoke();
    }

    // What each command does. The outline has no entry until its pane exists: it stays disabled, with its reason.
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
            _ => false,
        };
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
        view.FirstSharpPage += tab.Runner.Start;
        return view;
    }

    // Puts the focus on the page view of the active tab once the slot shows it, so the keys scroll it.
    private void FocusActiveView()
    {
        if (ActiveView is { } view)
        {
            Dispatcher.UIThread.Post(() => view.Focus(), DispatcherPriority.Background);
        }
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
