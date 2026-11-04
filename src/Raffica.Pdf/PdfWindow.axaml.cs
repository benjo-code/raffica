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
using Raffica.Pdf.Settings;
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
    private DispatcherTimer? _firstSharpFallback;
    private bool _firstSharpWorkStarted;
    private readonly AppSettings _appSettings;
    private SingleInstance? _instance;
    private PdfEngine? _engine;
    private bool _closeConfirmed;

    /// <param name="options">The command line.</param>
    /// <param name="settings">The shell settings, read by the caller at startup.</param>
    /// <param name="appSettings">The settings of the viewer, read once at startup by the caller (null: the defaults; the window reads no file).</param>
    public PdfWindow(PdfOptions options, ShellSettings settings, AppSettings? appSettings = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        _options = options;
        _appSettings = appSettings ?? AppSettings.Default;
        _recent = new RecentService(new RecentFilesStore(PdfSettingsFiles.RecentPathFor(options.SettingsFile)));
        InitializeComponent();
        Title = PdfStrings.WindowTitle;

        Frame = new AppFrame();
        var definition = PdfRibbon.Create(new PdfCommands(Open: () => _ = GuardAsync(OpenFromDialogAsync)));
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
        Dialogs = new DialogQueue(Host);
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
            _firstSharpFallback?.Stop();

            // The position of every open file is remembered, then the views let go of their sessions, then the sessions are disposed.
            foreach (var tab in Workspace.Tabs)
            {
                RememberView(tab);
            }

            foreach (var view in _views.Values)
            {
                view.FirstSharpPage -= OnFirstSharpPageShown;
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

    /// <summary>Asks for PDF files with the system file dialog and opens them.</summary>
    internal async Task OpenFromDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = PdfStrings.OpenDialogTitle,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(PdfStrings.OpenFilterPdf) { Patterns = ["*.pdf"] }],
        });
        await OpenFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
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
            RememberView(tab);
            if (_views.Remove(tab, out var view))
            {
                view.FirstSharpPage -= OnFirstSharpPageShown;
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
    }

    /// <summary>The document slot shows the page view of the active tab (made on first use); the others leave the screen and request nothing.</summary>
    private void ShowActiveView()
    {
        if (Workspace.Active is not { } active)
        {
            DocumentPane.Content = null;
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
        }

        DocumentPane.Content = view;
        UpdateViewStatus();
        Dispatcher.UIThread.Post(() => view.Focus(), DispatcherPriority.Input);
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
        if (tab.Path is { } path && _recent.ViewOf(path) is { } remembered && remembered.IsValid())
        {
            view.RestoreView(remembered.View, remembered.Zoom, remembered.ZoomMode, remembered.Layout);
            return;
        }

        view.SetLayout(_appSettings.DefaultLayout);
        view.SetZoomMode(_appSettings.DefaultZoomMode);
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
