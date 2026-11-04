using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

/// <summary>
/// The window of the PDF app: the shared frame with the drawn title bar (mark, document tabs, search over the ribbon), the ribbon, the
/// pages pane on the left (204 px), a reserved comments pane on the right (300 px, closed), the document slot and the status bar. Without
/// a document tab it shows the start view (no panes, no status bar). The document slot only holds an empty placeholder pane; a later
/// milestone fills it with the page view.
/// </summary>
internal sealed partial class PdfWindow : Window
{
    /// <summary>Width of the pages pane in device-independent pixels.</summary>
    internal const double PagesPaneWidth = 204;

    /// <summary>Width of the (reserved) comments pane.</summary>
    internal const double CommentsPaneWidth = 300;

    private readonly IDisposable _fieldCodes;
    private readonly IDisposable _selectedSubscription;
    private readonly CloseGuard _closeGuard;
    private bool _closeConfirmed;

    public PdfWindow(PdfOptions options, ShellSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        Title = PdfStrings.WindowTitle;

        Frame = new AppFrame();
        var definition = PdfRibbon.Create(new PdfCommands());
        Ribbon = new RibbonView { Definition = definition, ActiveTab = definition.Tabs.Single(t => t.Letter == PdfRibbon.HomeLetter) };

        var titleBar = new TitleBar { Mark = CreateAppMark() };
        TitleSearch = titleBar.Search;
        TitleSearch.Source = definition;
        TitleSearch.CommandChosen += command => command.Run?.Invoke();
        Tabs = titleBar.Tabs;
        Tabs.CloseRequested += OnCloseRequested;
        Tabs.Items.CollectionChanged += OnTabsChanged;
        _selectedSubscription = Tabs.GetObservable(DocumentTabs.SelectedProperty).Subscribe(new SelectedObserver(this));

        // 2.1 has no command that changes a document, so there is nothing to save yet: a Save choice cannot succeed and keeps the tab.
        _closeGuard = new CloseGuard(PromptAsync, _ => Task.FromResult(false));
        Workspace = new Workspace(_closeGuard);
        Workspace.TabsChanged += OnWorkspaceTabsChanged;
        Workspace.ActiveChanged += OnWorkspaceActiveChanged;

        PagesPane = new Pane { Title = PdfStrings.PanePages, Width = PagesPaneWidth };
        DocumentPane = new Pane { IsDocument = true, PaneName = ShellStrings.PaneDocument };
        CommentsPane = new Pane { Title = ShellStrings.PaneComments, Width = CommentsPaneWidth, IsOpen = false };
        FieldCodeBadge.SetCode(PagesPane, "6");
        FieldCodeBadge.SetCode(CommentsPane, "7");

        Status = new StatusBar();
        Start = new StartView { Tiles = StartTile.Standard(), Recent = [], OwnApp = StartApp.Pdf };

        Frame.TitleBar = titleBar;
        Host = new DialogHost { Content = Frame };
        Content = Host;
        SetCompact(settings.CompactCommandBar);
        Refresh();

        // Window-wide keys: Ctrl+K, F6 / Shift+F6 and the Alt field codes (Q the search field, 6 the pages pane, 7 the comments pane).
        FrameKeys.Attach(this, TitleSearch);
        var paneFocus = new PaneFocus(this, [PagesPane, DocumentPane, CommentsPane], Status);
        FieldCodeTarget[] targets =
        [
            new("Q", TitleSearch.FocusAndSelect),
            new("6", () => paneFocus.Focus(PagesPane)),
            new("7", () => paneFocus.Focus(CommentsPane)),
        ];
        _fieldCodes = FieldCodeInput.Attach(this, new FieldCodeSession(definition), Ribbon, Status, targets);
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            _fieldCodes.Dispose();
            _selectedSubscription.Dispose();
            foreach (var tab in Workspace.Tabs)
            {
                tab.Dispose();
            }
        };
    }

    /// <summary>The open documents; the title bar tabs and the status bar follow it.</summary>
    internal Workspace Workspace { get; }

    /// <summary>The host of the dialogs of the window.</summary>
    internal DialogHost Host { get; }

    public AppFrame Frame { get; }

    public RibbonView Ribbon { get; }

    public DocumentTabs Tabs { get; }

    public SearchField TitleSearch { get; }

    public StatusBar Status { get; }

    public Pane PagesPane { get; }

    public Pane DocumentPane { get; }

    public Pane CommentsPane { get; }

    public StartView Start { get; }

    /// <summary>The one place that sets the compact command bar: the frame (band height) and the ribbon (its content) follow together.</summary>
    internal void SetCompact(bool compact)
    {
        Frame.IsCompact = compact;
        Ribbon.IsCompact = compact;
    }

    private static Border CreateAppMark()
    {
        var mark = new Border { Child = new TextBlock { Text = "R" }, VerticalAlignment = VerticalAlignment.Center };
        mark.Classes.Add("app-mark");
        AutomationProperties.SetName(mark, PdfStrings.AppMarkName);
        return mark;
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private PdfTab? FindDocument(DocumentTab? tab) => tab is null ? null : Workspace.Tabs.FirstOrDefault(t => ReferenceEquals(t.Tab, tab));

    private async void OnCloseRequested(DocumentTab tab)
    {
        if (FindDocument(tab) is { } document)
        {
            await Workspace.CloseAsync(document);
        }
        else
        {
            Tabs.Items.Remove(tab);
        }
    }

    private void OnWorkspaceTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            var tab = (PdfTab)e.NewItems![0]!;
            tab.DirtyChanged += UpdateSaveState;
            Tabs.Items.Insert(Math.Min(e.NewStartingIndex, Tabs.Items.Count), tab.Tab);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            var tab = (PdfTab)e.OldItems![0]!;
            tab.DirtyChanged -= UpdateSaveState;
            Tabs.Items.Remove(tab.Tab);
        }
    }

    private void OnWorkspaceActiveChanged()
    {
        Tabs.Selected = Workspace.Active?.Tab;
        UpdateSaveState();
    }

    /// <summary>The user selected a tab in the strip: the workspace follows (a tab that is no document, such as a placeholder, is ignored).</summary>
    private void OnTabSelected(DocumentTab? tab)
    {
        if (FindDocument(tab) is { } document)
        {
            Workspace.Select(document);
        }
    }

    /// <summary>The save cell of the status bar shows "Unsaved changes" while the active document is dirty, else nothing.</summary>
    private void UpdateSaveState() => Status.SaveState = Workspace.Active is { IsDirty: true } ? ShellStrings.TabUnsavedChanges : null;

    private Task<CloseChoice> PromptAsync(PdfTab tab)
    {
        if (Host.IsOpen)
        {
            return Task.FromResult(CloseChoice.Cancel);
        }

        Workspace.Select(tab);
        var answer = new TaskCompletionSource<CloseChoice>();
        Host.Show(UnsavedChangesCard.Create(tab.Title, choice =>
        {
            Host.Close();
            answer.TrySetResult(choice);
        }));
        return answer.Task;
    }

    /// <summary>Asks about every document with unsaved changes before the window closes; a Cancel keeps the window open.</summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        e.Cancel = true;
        if (await _closeGuard.CanCloseAllAsync(Workspace.Tabs.ToList()))
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private sealed class SelectedObserver(PdfWindow window) : IObserver<DocumentTab?>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DocumentTab? value) => window.OnTabSelected(value);
    }

    /// <summary>The start view while no tab is open (no panes, no status bar, no ribbon), else the document view with every frame part.</summary>
    private void Refresh()
    {
        if (Tabs.Items.Count == 0)
        {
            PagesPane.IsOpen = false;
            DocumentPane.IsOpen = false;
            Frame.LeftPane = null;
            Frame.RightPane = null;
            Frame.StatusBar = null;
            Frame.TabRow = null;
            Frame.Band = null;
            Frame.Document = Start;
            return;
        }

        Frame.TabRow = Ribbon.TabRow;
        Frame.Band = Ribbon.Band;
        Frame.LeftPane = PagesPane;
        Frame.RightPane = CommentsPane;
        Frame.Document = DocumentPane;
        Frame.StatusBar = Status;
        PagesPane.IsOpen = true;
        DocumentPane.IsOpen = true;
    }
}
