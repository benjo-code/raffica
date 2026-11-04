using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Raffica.Core.Hosting;
using Raffica.Core.Pdf;
using Raffica.Core.Settings;
using Raffica.Core.Viewing;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Hosting;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Settings;
using Raffica.Pdf.Text;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

/// <summary>
/// The window of the PDF app: the shared frame with the drawn title bar (mark, document tabs, search over the ribbon), the ribbon, the
/// pages pane on the left (204 px), a reserved comments pane on the right (300 px, closed), the document slot and the status bar. Without
/// a document tab it shows the start view (no panes, no status bar). The document slot shows the page view of the active tab.
/// </summary>
internal sealed partial class PdfWindow : Window
{
    /// <summary>Width of the pages pane in device-independent pixels.</summary>
    internal const double PagesPaneWidth = 204;

    /// <summary>Width of the (reserved) comments pane.</summary>
    internal const double CommentsPaneWidth = 300;

    /// <summary>How many recent files the start view shows.</summary>
    internal const int RecentOnStartView = 5;

    /// <summary>How long a file given at the start may take to its first sharp page before the listener opens anyway.</summary>
    private static readonly TimeSpan FirstSharpFallbackDelay = TimeSpan.FromSeconds(3);

    // A hand-over of 64 hostile paths must not become 64 cards.
    private const int MaxHandoverErrorCards = 3;
    private const int MaxShownNameLength = 120;

    private readonly IDisposable _fieldCodes;
    private readonly IDisposable _selectedSubscription;
    private readonly CloseGuard _closeGuard;
    private readonly PdfOptions _options;
    private readonly RecentService _recent;
    private readonly Dictionary<PdfTab, PageView> _views = [];
    private readonly Dictionary<PdfTab, TabPanes> _panes = [];
    private readonly Dictionary<PdfTab, SearchSession> _searches = [];
    private readonly Dictionary<PdfTab, TextInteraction> _texts = [];
    private readonly Dictionary<PdfTab, OutlineSource> _outlines = [];
    private readonly Action<Uri>? _launcher;
    private readonly TimeProvider? _time;
    private SearchBar? _searchBar;
    private LeftPaneKind _leftPane = LeftPaneKind.Pages;
    private DispatcherTimer? _firstSharpFallback;
    private bool _firstSharpWorkStarted;
    private SingleInstance? _instance;
    private PdfEngine? _engine;
    private bool _closeConfirmed;

    /// <param name="options">The command line.</param>
    /// <param name="settings">The shell settings, read by the caller at startup.</param>
    /// <param name="appSettings">The settings of the viewer, read once at startup by the caller (null: the defaults; the window reads no file).</param>
    /// <param name="launcher">Opens a confirmed address outside the document (tests give their own; null: the system browser or mail program).</param>
    /// <param name="time">The clock of the arming delay of the link card (tests move it; null: the system clock).</param>
    public PdfWindow(PdfOptions options, ShellSettings settings, AppSettings? appSettings = null, Action<Uri>? launcher = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        _launcher = launcher;
        _time = time;
        _options = options;
        var appDefaults = appSettings ?? AppSettings.Default;
        _recent = new RecentService(new RecentFilesStore(PdfSettingsFiles.RecentPathFor(options.SettingsFile)));
        InitializeComponent();
        Title = PdfStrings.WindowTitle;

        // 2.1 has no command that changes a document, so there is nothing to save yet: a Save choice cannot succeed and keeps the tab.
        _closeGuard = new CloseGuard(PromptAsync, _ => Task.FromResult(false));
        Workspace = new Workspace(_closeGuard);

        Frame = new AppFrame();
        _rules = CreateRules();
        var definition = PdfRibbon.Create(CreateCommands(), _rules);
        Ribbon = new RibbonView { Definition = definition, ActiveTab = definition.Tabs.Single(t => t.Letter == PdfRibbon.HomeLetter) };

        var titleBar = new TitleBar { Mark = CreateAppMark() };
        TitleSearch = titleBar.Search;
        TitleSearch.Source = definition;
        TitleSearch.CommandChosen += command => command.Run?.Invoke();
        TitleSearch.ExtraResults = new PdfPaletteSource(CreatePaletteContext()).Find;
        Tabs = titleBar.Tabs;
        Tabs.CloseRequested += OnCloseRequested;
        Tabs.Items.CollectionChanged += OnTabsChanged;
        _selectedSubscription = Tabs.GetObservable(DocumentTabs.SelectedProperty).Subscribe(new SelectedObserver(this));

        Workspace.TabsChanged += OnWorkspaceTabsChanged;
        Workspace.ActiveChanged += OnWorkspaceActiveChanged;

        NavigationPane = new Pane { Title = PdfStrings.PanePages, Width = PagesPaneWidth };
        DocumentPane = new Pane { IsDocument = true, PaneName = ShellStrings.PaneDocument };
        CommentsPane = new Pane { Title = ShellStrings.PaneComments, Width = CommentsPaneWidth, IsOpen = false };
        FieldCodeBadge.SetCode(NavigationPane, "6");
        FieldCodeBadge.SetCode(CommentsPane, "7");

        Status = new StatusBar();
        Start = new StartView { Tiles = StartTile.Standard(), Recent = [], OwnApp = StartApp.Pdf };

        Frame.TitleBar = titleBar;
        Host = new DialogHost { Content = Frame };
        Content = Host;
        Dialogs = new DialogQueue(Host);
        Settings = new PdfSettingsController(options, Host, settings, appDefaults, SetCompact, SetPageThemeOfAllTabs);
        Flow = new OpenFlow(Workspace, () => _engine ??= new PdfEngine(), AskPasswordAsync, ShowOpenError);
        Start.BrowseRequested += () => _ = GuardAsync(OpenFromDialogAsync);
        Start.OpenRequested += entry => _ = GuardAsync(() => OpenRecentAsync(entry));
        Status.ZoomChanged += (_, percent) => ActiveView?.SetZoom(percent / 100.0);
        _recent.Changed += () => Start.Recent = _recent.ForStartView(RecentOnStartView);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += OnOpened;
        SetCompact(settings.CompactCommandBar);
        Refresh();

        // Window-wide keys: Ctrl+K, F6 / Shift+F6, the shortcuts of the command catalog and the Alt field codes (Q the search field, 6 the pages pane, 7 the comments pane).
        FrameKeys.Attach(this, TitleSearch);
        AttachShortcuts(definition);
        var paneFocus = new PaneFocus(this, [NavigationPane, DocumentPane, CommentsPane], Status);
        FieldCodeTarget[] targets =
        [
            new("Q", TitleSearch.FocusAndSelect),
            new("6", () => paneFocus.Focus(NavigationPane)),
            new("7", () => paneFocus.Focus(CommentsPane)),
        ];
        _fieldCodes = FieldCodeInput.Attach(this, new FieldCodeSession(definition), Ribbon, Status, targets);
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            _fieldCodes.Dispose();
            _selectedSubscription.Dispose();
            _firstSharpFallback?.Stop();

            // The position of every open file is remembered, then the views let go of their sessions, then the sessions are disposed.
            foreach (var tab in Workspace.Tabs)
            {
                RememberView(tab);
            }

            // The panes let go of the sessions and the views first, then the views, then the sessions.
            foreach (var panes in _panes.Values)
            {
                panes.Dispose();
            }

            _panes.Clear();
            foreach (var search in _searches.Values)
            {
                search.Dispose();
            }

            _searches.Clear();
            foreach (var text in _texts.Values)
            {
                text.Dispose();
            }

            _texts.Clear();
            foreach (var (tab, outline) in _outlines)
            {
                if (_views.TryGetValue(tab, out var outlined))
                {
                    outlined.FirstSharpPage -= outline.Start;
                }

                outline.Dispose();
            }

            _outlines.Clear();
            foreach (var view in _views.Values)
            {
                view.FirstSharpPage -= OnFirstSharpPageShown;
                view.Search = null;
                view.Tab = null;
            }

            _views.Clear();
            foreach (var tab in Workspace.Tabs)
            {
                tab.Dispose();
            }
        };
    }

    /// <summary>The recent files and the position per file (loaded after the first sharp page).</summary>
    internal RecentService Recent => _recent;

    /// <summary>The open documents; the title bar tabs and the status bar follow it.</summary>
    internal Workspace Workspace { get; }

    /// <summary>Opens files into <see cref="Workspace"/> (argument, dialog and drop all go through it).</summary>
    internal OpenFlow Flow { get; }

    /// <summary>Shows the cards of the window one at a time.</summary>
    internal DialogQueue Dialogs { get; }

    /// <summary>The settings dialog and the settings in force.</summary>
    internal PdfSettingsController Settings { get; }

    /// <summary>The host of the dialogs of the window.</summary>
    internal DialogHost Host { get; }

    public AppFrame Frame { get; }

    public RibbonView Ribbon { get; }

    public DocumentTabs Tabs { get; }

    public SearchField TitleSearch { get; }

    public StatusBar Status { get; }

    /// <summary>
    /// The left slot of the frame (204 px): the thumbnails of the pages or the outline of the active tab, one at a time. It is closed while
    /// no document is open and after <see cref="CloseLeftPane"/>; it takes part in the F6 ring.
    /// </summary>
    public Pane NavigationPane { get; }

    public Pane DocumentPane { get; }

    public Pane CommentsPane { get; }

    public StartView Start { get; }

    /// <summary>The one place that sets the compact command bar: the frame (band height) and the ribbon (its content) follow together.</summary>
    internal void SetCompact(bool compact)
    {
        Frame.IsCompact = compact;
        Ribbon.IsCompact = compact;
    }

    /// <summary>Opens the files in order; the last one opened is active, a file that cannot be opened ends in an error card.</summary>
    internal Task OpenFilesAsync(IEnumerable<string> paths) => Flow.OpenManyAsync(paths);

    /// <summary>
    /// Opens the file of an entry of the recent list. A file that does not exist any more ends in the error card as usual and leaves the
    /// list, so the entry does not come back.
    /// </summary>
    internal async Task<PdfTab?> OpenRecentAsync(Raffica.Shell.Frame.RecentFile entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = Path.Combine(entry.Folder, entry.Name);
        var tab = await Flow.OpenAsync(path);
        if (tab is null && !File.Exists(path))
        {
            _recent.Remove(path);
        }

        return tab;
    }

    /// <summary>Replaces the system file dialog (tests): returns the chosen paths, none when the user canceled. Null: the system dialog.</summary>
    internal Func<Task<IReadOnlyList<string>>>? FilePicker { get; set; }

    private bool _pickerShowing;

    /// <summary>
    /// Asks for PDF files with the system file dialog and opens them. While a dialog is already showing, another call does nothing
    /// (a held Ctrl+O or a double click must not pile up pickers).
    /// </summary>
    internal async Task OpenFromDialogAsync()
    {
        if (_pickerShowing)
        {
            return;
        }

        IReadOnlyList<string> paths;
        _pickerShowing = true;
        try
        {
            paths = FilePicker is { } picker ? await picker() : await PickWithSystemDialogAsync();
        }
        finally
        {
            _pickerShowing = false;
        }

        await OpenFilesAsync(paths);
    }

    private async Task<IReadOnlyList<string>> PickWithSystemDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = PdfStrings.OpenDialogTitle,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(PdfStrings.OpenFilterPdf) { Patterns = ["*.pdf"] }],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    /// <summary>Opens the files of a drop (every file, whatever its extension: Core names a file that is no PDF).</summary>
    internal Task OpenDroppedAsync(IDataTransfer data) => OpenFilesAsync(FileDrop.Paths(data));

    private static Border CreateAppMark()
    {
        var mark = new Border { Child = new TextBlock { Text = "R" }, VerticalAlignment = VerticalAlignment.Center };
        mark.Classes.Add("app-mark");
        AutomationProperties.SetName(mark, PdfStrings.AppMarkName);
        return mark;
    }

    /// <summary>Runs an open that nobody awaits (argument, drop, dialog): whatever goes wrong must not take the app down.</summary>
    private static async Task GuardAsync(Func<Task> open)
    {
        try
        {
            await open();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // OpenFlow already turns a bad file into an error card; this is the last net for anything else.
        }
    }

    /// <summary>The app listens for the files of later launches on this instance, once the window is open (the pipe opens after the first frame).</summary>
    internal void Listen(SingleInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _instance = instance;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (_options.Files.Count > 0)
        {
            // Not awaited: a password question of the first file must not hold back the work after the first page. The page view of the
            // first tab starts the work when its first sharp page is drawn (OnFirstSharpPageShown). Two safety nets keep the listener from
            // never starting: when no file could be opened there is no page, and when a page never turns sharp the timer starts it.
            _ = GuardAsync(async () =>
            {
                await OpenFilesAsync(_options.Files);
                if (Workspace.Tabs.Count == 0)
                {
                    await OnFirstSharpPageAsync();
                }
            });
            _firstSharpFallback = new DispatcherTimer { Interval = FirstSharpFallbackDelay };
            _firstSharpFallback.Tick += (_, _) =>
            {
                _firstSharpFallback.Stop();
                _ = GuardAsync(OnFirstSharpPageAsync);
            };
            _firstSharpFallback.Start();
            return;
        }

        // Without a file there is no page to wait for: once the window has been shown and the app is idle, the listener opens.
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        await GuardAsync(OnFirstSharpPageAsync);
    }

    private void OnFirstSharpPageShown() => _ = GuardAsync(OnFirstSharpPageAsync);

    /// <summary>
    /// The single seam for everything that waits for the first sharp page: the recent files are read and the pipe for later launches
    /// opens. It runs once, when the page view of the first tab has drawn its first sharp page, or, when there is nothing to wait for (no
    /// file at the start, no file that could be opened, a page that never turns sharp), after the window was shown and went idle.
    /// </summary>
    internal async Task OnFirstSharpPageAsync()
    {
        if (!_firstSharpWorkStarted)
        {
            _firstSharpWorkStarted = true;
            _firstSharpFallback?.Stop();
            _instance?.StartListening(paths => Dispatcher.UIThread.Post(() => OnHandover(paths)));
        }

        // Idempotent: a second call waits for the load of the first.
        await _recent.LoadAsync();
    }

    /// <summary>The files of a later launch: only existing files open, the rest ends in a few cards; the window comes to the front.</summary>
    internal void OnHandover(IReadOnlyList<string> paths)
    {
        if (_closeConfirmed)
        {
            return;
        }

        try
        {
            var cards = 0;
            var accepted = InstanceHandover.Accept(paths, path =>
            {
                if (cards++ < MaxHandoverErrorCards)
                {
                    ShowOpenError(path.Length > MaxShownNameLength ? path[..MaxShownNameLength] : path, PdfOpenError.FileNotFound);
                }
            });
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            if (accepted.Count > 0)
            {
                _ = GuardAsync(() => OpenFilesAsync(accepted));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever a second instance sends must not take the app down.
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = FileDrop.Accepts(e.DataTransfer) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (FileDrop.Accepts(e.DataTransfer))
        {
            await GuardAsync(() => OpenDroppedAsync(e.DataTransfer));
        }
    }

    private Task<string?> AskPasswordAsync(string fileName, bool incorrect) =>
        Dialogs.ShowAsync<string?>(done => PasswordCard.Create(fileName, incorrect, done));

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void ShowOpenError(string path, PdfOpenError error) =>
        _ = Dialogs.ShowAsync<bool>(done => ErrorCard.Create(DisplayName(path), error, () => done(true)));

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private PdfTab? FindDocument(DocumentTab? tab) => tab is null ? null : Workspace.Tabs.FirstOrDefault(t => ReferenceEquals(t.Tab, tab));

    private async void OnCloseRequested(DocumentTab tab)
    {
        try
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception in the close guard keeps the tab; it must not take the app down.
        }
    }

    private void OnWorkspaceTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            var tab = (PdfTab)e.NewItems![0]!;
            tab.DirtyChanged += UpdateSaveState;
            if (tab.Path is { } opened)
            {
                _recent.Touch(opened);
            }

            Tabs.Items.Insert(Math.Min(e.NewStartingIndex, Tabs.Items.Count), tab.Tab);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            var tab = (PdfTab)e.OldItems![0]!;
            tab.DirtyChanged -= UpdateSaveState;
            _printInfo.Remove(tab);
            RememberView(tab);
            if (_panes.Remove(tab, out var panes))
            {
                panes.Dispose();
            }

            if (_texts.Remove(tab, out var text))
            {
                text.Dispose();
            }

            if (_outlines.Remove(tab, out var outline))
            {
                if (_views.TryGetValue(tab, out var outlined))
                {
                    outlined.FirstSharpPage -= outline.Start;
                }

                outline.Dispose();
            }

            if (_searches.Remove(tab, out var search))
            {
                search.Dispose();
            }

            if (_views.Remove(tab, out var view))
            {
                view.FirstSharpPage -= OnFirstSharpPageShown;
                view.Search = null;
                view.Tab = null;
            }

            Tabs.Items.Remove(tab.Tab);
        }
    }

    private void OnWorkspaceActiveChanged()
    {
        Tabs.Selected = Workspace.Active?.Tab;
        UpdateSaveState();
        ShowActiveView();
        RefreshCommandStates();
    }

    /// <summary>The document slot shows the page view of the active tab (made on first use); the others leave the screen and request nothing.</summary>
    private void ShowActiveView()
    {
        if (Workspace.Active is not { } active)
        {
            DocumentPane.Content = null;
            CloseSearchBar();
            UpdateLeftPane();
            return;
        }

        if (!_views.TryGetValue(active, out var view))
        {
            view = new PageView { Tab = active };
            view.FirstSharpPage += OnFirstSharpPageShown;
            var created = view;
            view.ViewChanged += () =>
            {
                if (ReferenceEquals(ActiveView, created))
                {
                    UpdateViewStatus();
                }
            };
            ApplyInitialView(view, active);
            _views[active] = view;
            var search = new SearchSession(active, () => view.CurrentPage);
            _searches[active] = search;
            view.Search = search;
            _texts[active] = new TextInteraction(active, view, Dialogs, _launcher, _time);

            // The outline is read once for the pane and for the palette, after the first sharp page.
            var outline = new OutlineSource(active);
            _outlines[active] = outline;
            view.FirstSharpPage += outline.Start;
        }

        DocumentPane.Content = view;
        LoadPrintInfo(active);
        if (_searchBar is not null && _searches.TryGetValue(active, out var shown))
        {
            _searchBar.Session = shown;
        }

        UpdateLeftPane();
        UpdateViewStatus();
        Dispatcher.UIThread.Post(() => view.Focus(), DispatcherPriority.Input);
    }

    /// <summary>What the left slot shows now (for tests and the commands of the pane group).</summary>
    internal LeftPaneKind LeftPane => _leftPane;

    /// <summary>Opens the left slot with the thumbnails of the pages.</summary>
    internal void ShowPagesPane() => ShowLeftPane(LeftPaneKind.Pages);

    /// <summary>Opens the left slot with the outline of the document.</summary>
    internal void ShowOutlinePane() => ShowLeftPane(LeftPaneKind.Outline);

    /// <summary>Closes the left slot (the pages and the outline); the document takes the room.</summary>
    internal void CloseLeftPane() => ShowLeftPane(LeftPaneKind.None);

    private void ShowLeftPane(LeftPaneKind kind)
    {
        _leftPane = kind;
        UpdateLeftPane();
    }

    /// <summary>
    /// Puts the pane of the active tab for the wanted kind into the slot (made on first use, kept until the tab closes: the outline is read
    /// once per tab); a pane that leaves the slot goes off screen and requests nothing. No tab or no wish: the slot is closed.
    /// </summary>
    private void UpdateLeftPane()
    {
        // A pane that leaves while the keyboard focus is inside it must not take the focus down with it: the document gets it.
        var focusWasInside = FocusIsInNavigationPane();
        var before = NavigationPane.Content;
        PlaceLeftPane();
        if (focusWasInside && (!NavigationPane.IsOpen || !ReferenceEquals(before, NavigationPane.Content)))
        {
            ActiveView?.Focus();
        }
    }

    private void PlaceLeftPane()
    {
        var active = Workspace.Active;
        NavigationPane.IsOpen = Tabs.Items.Count > 0 && _leftPane != LeftPaneKind.None;
        if (active is null || !_views.TryGetValue(active, out var view) || _leftPane == LeftPaneKind.None)
        {
            NavigationPane.Content = null;
            return;
        }

        if (!_panes.TryGetValue(active, out var panes))
        {
            _panes[active] = panes = new TabPanes(active, view, _outlines[active]);
        }

        if (_leftPane == LeftPaneKind.Pages)
        {
            NavigationPane.Title = PdfStrings.PanePages;
            NavigationPane.Content = panes.Pages;
        }
        else
        {
            NavigationPane.Title = PdfStrings.PaneOutline;
            NavigationPane.Content = panes.Outline;
        }
    }

    /// <summary>The panes of one tab; each is made when first shown and disposed with the tab.</summary>
    private sealed class TabPanes(PdfTab tab, PageView view, OutlineSource outline) : IDisposable
    {
        private PagesPane? _pages;
        private OutlinePane? _outline;

        public PagesPane Pages => _pages ??= new PagesPane(tab, view);

        public OutlinePane Outline => _outline ??= new OutlinePane(view, outline.ReadAsync);

        public void Dispose()
        {
            _pages?.Dispose();
            _outline?.Dispose();
        }
    }

    /// <summary>The search of the active tab, or null without a tab.</summary>
    private SearchSession? ActiveSearch => Workspace.Active is { } active ? _searches.GetValueOrDefault(active) : null;

    /// <summary>The search bar while it is shown (for tests), else null.</summary>
    internal SearchBar? ShownSearchBar => _searchBar is not null && ReferenceEquals(Frame.Notification, _searchBar) ? _searchBar : null;

    /// <summary>The search of a tab, once the tab has been shown (for tests); null before.</summary>
    internal SearchSession? SearchOf(PdfTab tab) => _searches.GetValueOrDefault(tab);

    /// <summary>The text interaction (selection and links) of a tab, once the tab has been shown (for tests); null before.</summary>
    internal TextInteraction? TextOf(PdfTab tab) => _texts.GetValueOrDefault(tab);

    /// <summary>Copies the selected text of the active document to the clipboard (plain text and HTML); returns whether something was copied. The command catalog calls this.</summary>
    internal Task<bool> CopySelectionAsync() =>
        Workspace.Active is { } active && _texts.TryGetValue(active, out var text) ? text.Selection.CopyAsync() : Task.FromResult(false);

    /// <summary>Selects the text of the pages that are shown in the active document. The command catalog calls this.</summary>
    internal void SelectAll()
    {
        if (Workspace.Active is { } active && _texts.TryGetValue(active, out var text))
        {
            text.Selection.SelectAll();
        }
    }

    /// <summary>Shows the search bar of the active document in the notification slot and puts the focus into its field. Does nothing without a document.</summary>
    internal void ShowSearchBar()
    {
        if (ActiveSearch is not { } search)
        {
            return;
        }

        _searchBar ??= CreateSearchBar();
        _searchBar.Session = search;
        Frame.Notification = _searchBar;
        _searchBar.FocusQuery();
        var bar = _searchBar;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(Frame.Notification, bar))
            {
                bar.FocusQuery();
            }
        }, DispatcherPriority.Input);
    }

    /// <summary>Closes the search bar and forgets the searches of all documents (the highlights go); the focus returns to the document.</summary>
    internal void CloseSearchBar()
    {
        var wasShown = ShownSearchBar is not null;
        foreach (var search in _searches.Values)
        {
            search.Clear();
        }

        if (_searchBar is not null && ReferenceEquals(Frame.Notification, _searchBar))
        {
            Frame.Notification = null;
        }

        if (wasShown)
        {
            ActiveView?.Focus();
        }
    }

    /// <summary>Goes to the next hit of the search; without a search it opens the bar instead.</summary>
    internal void SearchNext()
    {
        if (ActiveSearch is { Hits.Count: > 0 } search)
        {
            search.Next();
        }
        else
        {
            ShowSearchBar();
        }
    }

    /// <summary>Goes to the previous hit of the search; without a search it opens the bar instead.</summary>
    internal void SearchPrevious()
    {
        if (ActiveSearch is { Hits.Count: > 0 } search)
        {
            search.Previous();
        }
        else
        {
            ShowSearchBar();
        }
    }

    private SearchBar CreateSearchBar()
    {
        var bar = new SearchBar();
        bar.CloseRequested += CloseSearchBar;
        return bar;
    }

    /// <summary>The page view of the active tab, or null.</summary>
    private PageView? ActiveView => Workspace.Active is { } active ? _views.GetValueOrDefault(active) : null;

    /// <summary>
    /// A new tab starts from the position remembered for its file (when the recent list has been loaded and knows the file, which is
    /// not the case for a file given on the command line: that opens before the list is read, at the defaults), else from the default
    /// layout and zoom mode of the settings.
    /// </summary>
    private void ApplyInitialView(PageView view, PdfTab tab)
    {
        view.PageTheme = Settings.PageThemeForNewTab;
        if (tab.Path is { } path && _recent.ViewOf(path) is { } remembered && remembered.IsValid())
        {
            view.RestoreView(remembered.View, remembered.Zoom, remembered.ZoomMode, remembered.Layout);
            return;
        }

        view.SetLayout(Settings.App.DefaultLayout);
        view.SetZoomMode(Settings.App.DefaultZoomMode);
    }

    /// <summary>Stores where the file of a tab was read (on tab close and window close); a tab that never had a page shown stores nothing.</summary>
    private void RememberView(PdfTab tab)
    {
        if (tab.Path is { } path && _views.TryGetValue(tab, out var view) && view.PageCount > 0)
        {
            _recent.Remember(path, view.CaptureRemembered());
        }
    }

    /// <summary>The position, count and zoom cells follow the page view of the active tab (and are empty without one).</summary>
    private void UpdateViewStatus()
    {
        if (ActiveView is not { PageCount: > 0 } view)
        {
            Status.Position = null;
            Status.Count = null;
            return;
        }

        Status.Position = ViewCommands.PositionText(view.CurrentPage + 1, view.PageCount);
        Status.Count = ViewCommands.CountText(view.PageCount, view.CurrentPageSize);
        Status.ZoomText = ViewCommands.ZoomText(view.Zoom);
        Status.Zoom = view.Zoom * 100;
    }

    /// <summary>
    /// Asks for a page number (the card of Ctrl+G) and goes to the page. Does nothing without a document. The command catalog calls this.
    /// </summary>
    internal async Task GoToPageAsync()
    {
        if (ActiveView is not { PageCount: > 0 } view)
        {
            return;
        }

        var count = view.PageCount;
        var page = await Dialogs.ShowAsync<int?>(done => GoToPageCard.Create(count, view.CurrentPage + 1, done));
        if (page is { } chosen)
        {
            view.GoToPage(chosen - 1);
        }
    }

    /// <summary>
    /// Shows the facts about the active document (Core's <see cref="DocumentInfoReader"/>) in a card and returns when it is closed. Does nothing
    /// without a document. The count cell of the status bar is plain text in the shell, so the command catalog (the palette entry
    /// "Document info") is the way in.
    /// </summary>
    internal async Task ShowDocumentInfoAsync()
    {
        if (Workspace.Active is not { Path: { } path } tab || tab.Session.Document is not { } document)
        {
            return;
        }

        var info = await DocumentInfoReader.ReadAsync(document, path);
        var view = ActiveView;
        var size = view is { PageCount: > 0 } && ReferenceEquals(view.Tab, tab) ? view.FirstPageSize : null;
        await Dialogs.ShowAsync<bool>(done => DocumentInfoCard.Create(info, () => done(true), size));
    }

    /// <summary>The outline of a tab, once the tab has been shown (for tests); null before.</summary>
    internal OutlineSource? OutlineOf(PdfTab tab) => _outlines.GetValueOrDefault(tab);

    // What the palette needs, read when it asks (on every keystroke): nothing here reads a file or calls PDFium.
    private PaletteContext CreatePaletteContext() => new()
    {
        Tabs = () => Workspace.Tabs,
        ActiveTab = () => Workspace.Active,
        Select = tab => Workspace.Select(tab),
        Recent = count => _recent.ForPalette(count),
        OpenRecent = entry => _ = GuardAsync(() => OpenRecentAsync(entry)),
        Outline = () => Workspace.Active is { } active ? _outlines.GetValueOrDefault(active)?.Flat : null,
        PageCount = () => ActiveView?.PageCount ?? 0,
        GoToPage = page => ActiveView?.GoToPage(page),
        ShowDocumentInfo = () => _ = GuardAsync(ShowDocumentInfoAsync),
    };

    /// <summary>The page view of a tab, once the tab has been shown (for tests); null before.</summary>
    internal PageView? ViewOf(PdfTab tab) => _views.GetValueOrDefault(tab);

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
        try
        {
            if (await _closeGuard.CanCloseAllAsync(Workspace.Tabs.ToList()))
            {
                // Posted: with no dirty tab the guard completes synchronously, and Close must not run nested inside the Closing event.
                _closeConfirmed = true;
                Dispatcher.UIThread.Post(Close);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception in the close guard keeps the window open; it must not take the app down.
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
        RefreshCommandStates();
        if (Tabs.Items.Count == 0)
        {
            NavigationPane.IsOpen = false;
            DocumentPane.IsOpen = false;
            Frame.LeftPane = null;
            Frame.RightPane = null;
            Frame.StatusBar = null;
            Frame.TabRow = null;
            Frame.Band = null;
            Frame.Notification = null;
            Frame.Document = Start;
            return;
        }

        Frame.TabRow = Ribbon.TabRow;
        Frame.Band = Ribbon.Band;
        Frame.LeftPane = NavigationPane;
        Frame.RightPane = CommentsPane;
        Frame.Document = DocumentPane;
        Frame.StatusBar = Status;
        NavigationPane.IsOpen = _leftPane != LeftPaneKind.None;
        DocumentPane.IsOpen = true;
    }
}
