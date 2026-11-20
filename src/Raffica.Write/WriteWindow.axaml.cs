using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Raffica.Core.Hosting;
using Raffica.Hosting;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Platform.MacOS;
using Raffica.Write.Settings;
using RecentFilesStore = Raffica.Core.Settings.RecentFilesStore;

namespace Raffica.Write;

/// <summary>
/// The window of the Write app: the shared frame with the drawn title bar (mark, document tabs, command search), the document slot and the status bar. Without a
/// document tab it shows the start view (the one tile "Open document", the recent files, no status bar). The document slot shows the page view of the active tab (<see cref="CreateDocumentArea"/>) and the ribbon sits in <see cref="AppFrame.TabRow"/> and
/// <see cref="AppFrame.Band"/> (see <see cref="Refresh"/> and <c>WriteWindow.View.cs</c>). Nothing of the layout, the fonts or a file exists before the first file is opened.
/// </summary>
internal sealed partial class WriteWindow : Window, IHostedWindow, IWriteMenuWindow, ISaveInteraction
{
    /// <summary>How many recent files the start view shows.</summary>
    internal const int RecentOnStartView = 5;

    /// <summary>The longest the window waits for the settings files and the recent list when it closes or the process exits.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(1);

    // A hand-over of 64 hostile paths must not become 64 cards.
    private const int MaxHandoverErrorCards = 3;
    internal const int MaxShownNameLength = 120;

    private readonly WriteOptions _options;
    private readonly RecentService _recent;
    private readonly EventHandler? _processExit;
    private readonly Dictionary<WriteTab, Control> _areas = [];
    private readonly HashSet<Key> _keysDown = [];
    private readonly List<string> _pendingHandover = [];
    private readonly List<string> _undelivered = [];
    private SingleInstance? _instance;
    private bool _listening;
    private bool _handoverDrainPosted;
    private bool _watchingHost;
    private bool _pickerShowing;
    private bool _closeConfirmed;
    private int _closeRuns;
    private readonly CloseSignal _closing = new();
    private readonly WriteSaveFlow _saves;
    private readonly CloseGuard _closeGuard;
    private readonly Dictionary<WriteTab, Task> _closingTabs = [];

    /// <summary>Ends when the window starts to close: every open of this window watches it (a file that finishes late adds no tab).</summary>
    private readonly CancellationToken _closeToken;

    /// <summary>Whether the ribbon band is held until the first page is on screen.</summary>
    private readonly bool _deferRibbon;

    /// <param name="options">The command line.</param>
    /// <param name="settings">The shell settings, read by the caller at startup.</param>
    /// <param name="appSettings">The Write settings, read once at startup by the caller (null: the defaults; the window reads no file).</param>
    /// <param name="work">The fonts of the process and the counters; the windows of one app share it (null: one of its own).</param>
    /// <param name="deferRibbon">True (the app): the content of the ribbon band is put on screen after the first page of the first file, so the page is not held back by laying out about 40 buttons and pickers (the ribbon appears two frames later; the key tips, the command search and the pickers release it at once). False (the tests): the band is there from the start.</param>
    public WriteWindow(WriteOptions options, ShellSettings settings, WriteSettings? appSettings = null, OpenWork? work = null, bool deferRibbon = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        _options = options;
        _deferRibbon = deferRibbon;
        _closeToken = _closing.Token;
        Work = work ?? new OpenWork();
        _recent = new RecentService(new RecentFilesStore(AppFiles.Write.RecentPathFor(options.SettingsFile)));
        InitializeComponent();
        Title = WriteStrings.WindowTitle;
        Workspace = new WriteWorkspace();
        _saves = new WriteSaveFlow(this);
        _closeGuard = new CloseGuard(AskCloseAsync, SaveForCloseAsync, _saves.WhenIdleAsync);

        Frame = new AppFrame();
        var titleBar = new TitleBar { Mark = CreateAppMark() };
        TitleSearch = titleBar.Search;
        CreateRibbon();
        _saves.Changed += RefreshCommandStates;
        _saves.Changed += FormattingSaveChanged;
        Tabs = titleBar.Tabs;
        Tabs.CloseRequested += OnCloseRequested;
        Tabs.NewButtonLabel = WriteStrings.OpenFileButton;
        Tabs.NewRequested += () => _ = GuardAsync(OpenFromDialogAsync);
        Tabs.Items.CollectionChanged += OnTabsChanged;
        Tabs.GetObservable(DocumentTabs.SelectedProperty).Subscribe(new SelectedObserver(this));
        Workspace.TabsChanged += OnWorkspaceTabsChanged;
        Workspace.ActiveChanged += OnWorkspaceActiveChanged;

        DocumentPane = new Pane { IsDocument = true, PaneName = ShellStrings.PaneDocument };
        Status = new StatusBar();
        _paneFocus = new PaneFocus(this, [DocumentPane], Status);
        Start = new StartView { Tiles = StartTile.OpenDocumentOnly(), Recent = [], OwnApp = StartApp.Write };

        Frame.TitleBar = titleBar;
        Host = new DialogHost { Content = Frame };
        Content = Host;
        Dialogs = new DialogQueue(Host);
        Settings = new WriteSettingsController(options, Host, settings, appSettings ?? WriteSettings.Default, SetCompact);
        Flow = new OpenFlow(Workspace, Work, ShowOpenError, ShowOpenFailures);
        Status.ZoomChanged += (_, percent) => ActiveView?.SetZoom(percent / 100.0);
        Start.NewRequested += OnStartTile;
        Start.BrowseRequested += () => _ = GuardAsync(OpenFromDialogAsync);
        Start.OpenRequested += entry => _ = GuardAsync(() => OpenRecentAsync(entry));
        _recent.Changed += () =>
        {
            Start.Recent = _recent.ForStartView(RecentOnStartView);
            StatesChanged?.Invoke();
        };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += OnOpened;
        SetCompact(settings.CompactCommandBar);

        // The window fits the work area and goes where it closed last; a measurement run keeps the fixed size of the window.
        if (!options.IsMeasuring)
        {
            TitleBar.PrepareWindow(this);
            ApplyPlacement(settings.WindowPlacement);
            Placement.Attach(this);
        }

        Refresh();

        // Window-wide keys: the command search (Alt+Q; Cmd+K on macOS), Open, Close tab and the tab switch.
        FrameKeys.Attach(this, TitleSearch, SearchShortcut.AltQ);
        AttachKeys();
        AttachRecovery();

        Closing += OnWindowClosing;
        Closed += OnWindowClosed;

        // A normal exit of the process also writes what is still queued (a close of the window already did).
        _processExit = (_, _) => FlushWriters();
        AppDomain.CurrentDomain.ProcessExit += _processExit;
    }

    /// <summary>Raised when the files of a later launch arrive in the running instance, before anything is checked or opened.</summary>
    internal event Action? HandoverArrived;

    /// <summary>Raised when the page view of a tab has drawn page 1 (the measurement of the first and the second file listens).</summary>
    internal event Action<WriteTab>? FirstSharpPageDrawn;

    /// <summary>Raised when the states the macOS menu shows may have changed.</summary>
    public event Action? StatesChanged;

    /// <summary>The fonts of the process and the counters of the open path.</summary>
    internal OpenWork Work { get; }

    /// <summary>The open documents; the title bar tabs follow it.</summary>
    internal WriteWorkspace Workspace { get; }

    /// <summary>Opens files into <see cref="Workspace"/> (argument, dialog, drop and hand-over all go through it).</summary>
    internal OpenFlow Flow { get; }

    /// <summary>Shows the cards of the window one at a time.</summary>
    internal DialogQueue Dialogs { get; }

    /// <summary>The settings dialog and the settings in force (the view and zoom a new tab starts with).</summary>
    internal WriteSettingsController Settings { get; }

    /// <summary>The host of the dialogs of the window.</summary>
    internal DialogHost Host { get; }

    /// <summary>The recent files and the position per file (loaded after the first file is shown, or once the idle window has been shown).</summary>
    internal RecentService Recent => _recent;

    public AppFrame Frame { get; }

    public DocumentTabs Tabs { get; }

    public SearchField TitleSearch { get; }

    public StatusBar Status { get; }

    /// <summary>The document slot of the frame: the area of the active tab.</summary>
    public Pane DocumentPane { get; }

    public StartView Start { get; }

    /// <summary>Replaces the system file dialog (tests): returns the chosen paths, none when the user canceled. Null: the system dialog.</summary>
    internal Func<Task<IReadOnlyList<string>>>? FilePicker { get; set; }

    /// <summary>The area of a tab that has been shown (tests); null before.</summary>
    internal Control? AreaOf(WriteTab tab) => _areas.GetValueOrDefault(tab);

    /// <summary>The one place that sets the compact command bar.</summary>
    internal void SetCompact(bool compact)
    {
        Frame.IsCompact = compact;
        Ribbon.IsCompact = compact;
    }

    // The mark of the title bar: a small ink tile with the initial.
    private static Border CreateAppMark()
    {
        var mark = new Border { Child = new TextBlock { Text = "W" }, VerticalAlignment = VerticalAlignment.Center };
        mark.Classes.Add("app-mark");
        AutomationProperties.SetName(mark, WriteStrings.AppMarkName);
        return mark;
    }

    // The only tile of the start view is "Open document" (a single `_` lambda parameter would shadow the discard).
    private void OnStartTile(StartApp app) => _ = GuardAsync(OpenFromDialogAsync);

    // Opening.

    /// <summary>The paper a new document gets: A4, or Letter in the US and Canada (tests set their own).</summary>
    internal Func<PaperSize> PaperSource { get; set; } = () => BlankPaper.For(RegionInfo.CurrentRegion);

    /// <summary>
    /// Makes a new untitled document of the paper (the window's <see cref="PaperSource"/> when none is given) and shows it in a tab of its own, as an opened file is. Null when the window is
    /// closing or the blank document could not be made; the latter is told in the open-error card of the app (no path to name, so the card names the untitled document).
    /// </summary>
    internal async Task<WriteTab?> NewDocumentAsync(Func<PaperSize>? paper = null)
    {
        if (IsClosing)
        {
            return null;
        }

        try
        {
            var tab = await Flow.NewDocumentAsync((paper ?? PaperSource)(), _closeToken);
            if (tab is null)
            {
                ShowOpenError(WriteStrings.UntitledName, OpenError.Other);
            }

            return tab;
        }
        catch (OperationCanceledException)
        {
            // The window closed while the document was made: no tab, nothing left behind.
            return null;
        }
    }

    /// <summary>Opens the files in order; the last one opened is active, files that cannot be opened end in a card (one card for two or more).</summary>
    internal Task OpenFilesAsync(IEnumerable<string> paths) => Flow.OpenManyAsync(paths, _closeToken);

    /// <summary>
    /// Opens the file of an entry of the recent list. A file that does not exist any more ends in the error card as usual and leaves the list, so the entry does
    /// not come back; a file that is there but cannot be read now (a share that is offline, a file in use, a damaged file) stays.
    /// </summary>
    internal async Task<WriteTab?> OpenRecentAsync(RecentFile entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = Path.Combine(entry.Folder, entry.Name);
        OpenResult result;
        try
        {
            result = await Flow.OpenAsync(path, _closeToken);
        }
        catch (OperationCanceledException)
        {
            // The window closed while the file was read: no tab, no card, the list stays as it is.
            return null;
        }

        if (result is Failed failed)
        {
            Flow.Report([new OpenFailure(path, failed.Error)]);
            if (failed.Error == OpenError.NotFound)
            {
                _recent.Remove(path);
            }

            return null;
        }

        return ((Opened)result).Tab;
    }

    /// <summary>Asks for documents with the system file dialog and opens them. While a dialog is already showing, another call does nothing.</summary>
    internal async Task OpenFromDialogAsync()
    {
        if (_pickerShowing)
        {
            return;
        }

        IReadOnlyList<string> paths;
        _pickerShowing = true;

        // The picker may take the key up of the O of Ctrl+O: without this the O would stay "down" and the next Ctrl+O would count as a repeat.
        _keysDown.Clear();
        try
        {
            paths = FilePicker is { } picker ? await picker() : await PickWithSystemDialogAsync();
        }
        finally
        {
            _pickerShowing = false;
            _keysDown.Clear();
        }

        await OpenFilesAsync(paths);
    }

    private async Task<IReadOnlyList<string>> PickWithSystemDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = WriteStrings.OpenDialogTitle,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(WriteStrings.OpenFilterDocx)
                {
                    // Both spellings (a Linux file dialog matches the case), the MIME type for the portals and the type of macOS.
                    Patterns = ["*.docx", "*.DOCX"],
                    MimeTypes = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
                    AppleUniformTypeIdentifiers = ["org.openxmlformats.wordprocessingml.document"],
                },
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void ShowOpenError(string path, OpenError error) => ShowOpenErrorCard(path, error, recoveryCopy: false);

    private void ShowOpenErrorCard(string path, OpenError error, bool recoveryCopy) =>
        _ = Dialogs.ShowAsync<bool>(done => OpenErrorCard.Create(ShortName(DisplayName(path)), error, () => done(true), recoveryCopy));

    /// <summary>A name shown in a card, cut to <see cref="MaxShownNameLength"/> characters without splitting a surrogate pair (a name comes from a file, a sidecar or a hand-over: untrusted).</summary>
    internal static string ShortName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length <= MaxShownNameLength)
        {
            return name;
        }

        var end = MaxShownNameLength;
        if (char.IsHighSurrogate(name[end - 1]) && char.IsLowSurrogate(name[end]))
        {
            end--;
        }

        return name[..end];
    }

    // Two or more files of one open that failed: one card with the names (up to five, then "and N more"), not one card each.
    private void ShowOpenFailures(IReadOnlyList<OpenFailure> failures) => ShowOpenFailuresCard(failures, recoveryCopies: false);

    private void ShowOpenFailuresCard(IReadOnlyList<OpenFailure> failures, bool recoveryCopies)
    {
        var names = failures.Select(failure => ShortName(DisplayName(failure.Path))).ToList();
        _ = Dialogs.ShowAsync<bool>(done => OpenFailuresCard.Create(names, () => done(true), recoveryCopies));
    }

    /// <summary>Runs an open that nobody awaits (argument, drop, dialog): whatever goes wrong must not take the app down.</summary>
    internal static async Task GuardAsync(Func<Task> open)
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

    /// <summary>Opens the files of a drop (every file, whatever its extension: the open names a file that is no document).</summary>
    internal Task OpenDroppedAsync(IDataTransfer data) => OpenFilesAsync(FileDropPaths(data));

    private static List<string> FileDropPaths(IDataTransfer data) =>
        data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var data = e.DataTransfer;
        await GuardAsync(async () =>
        {
            if (data.Contains(DataFormat.File))
            {
                await OpenDroppedAsync(data);
            }
        });
    }

    // The window opens: the pipe for later launches starts at once (a socket only), the files of the command line open without holding back the first frame,
    // and the recent list loads once the first file is shown (or, with no file, once the idle window has been shown).
    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        StartListening();
        if (_options.Files.Count > 0)
        {
            await GuardAsync(async () =>
            {
                await OpenFilesAsync(_options.Files);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                await LoadRecentAsync();

                // A file that did not open leaves a window with no tab: it is shown like an idle one, so it offers the copies (once per process).
                if (Workspace.Tabs.Count == 0)
                {
                    RequestRecoveryOffer();
                }
            });
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        await GuardAsync(LoadRecentAsync);
        RequestRecoveryOffer();
    }

    /// <summary>Reads the recent files off the UI thread (once; a second call waits for the first). Nothing of it runs before the first file or the first idle moment.</summary>
    internal Task LoadRecentAsync() => _recent.LoadAsync();

    /// <summary>The app listens for the files of later launches on this instance, once the window is open (the pipe opens after the first frame).</summary>
    internal void Listen(SingleInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _instance = instance;
    }

    // Once: the pipe loop of the single-instance lock hands each message to OnHandover on a pool thread. A listener that cannot start is no reason for the window to fail.
    private void StartListening()
    {
        if (_listening || _instance is null)
        {
            return;
        }

        _listening = true;
        try
        {
            _instance.StartListening(OnHandover);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Without the listener a later launch opens a window of its own.
        }
    }

    // Hand-over.

    /// <summary>
    /// The files of a later launch (called on the thread of the pipe, never the UI thread: asking for a file on a dead network share can stall for a long time).
    /// The check of the paths happens here; the UI thread only gets the answer: only existing files open, the rest ends in a few cards, and the window comes to the front.
    /// </summary>
    internal void OnHandover(IReadOnlyList<string> paths)
    {
        try
        {
            Dispatcher.Post(() => HandoverArrived?.Invoke());
            if (_closeConfirmed)
            {
                Undeliver(paths);
                return;
            }

            var rejected = new List<string>();
            var accepted = InstanceHandover.Accept(paths, path =>
            {
                if (rejected.Count < MaxHandoverErrorCards)
                {
                    rejected.Add(ShortName(path));
                }
            });
            Dispatcher.Post(() => ShowHandover(accepted, rejected));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever a second instance sends must not take the app down.
        }
    }

    private void ShowHandover(IReadOnlyList<string> accepted, IReadOnlyList<string> rejected)
    {
        if (_closeConfirmed)
        {
            Undeliver(accepted);
            return;
        }

        try
        {
            // Decided before the error cards of this very hand-over open: they do not hold back the good files next to them.
            var busy = Host.IsOpen || IsCloseRunning;
            foreach (var name in rejected)
            {
                ShowOpenError(name, OpenError.NotFound);
            }

            BringToFront();
            if (accepted.Count == 0)
            {
                return;
            }

            // While a card is open or the close questions run, the files wait: opening a tab now would move the focus and the active tab under the card.
            _pendingHandover.AddRange(accepted);
            if (busy)
            {
                WatchHostForHandover();
            }
            else
            {
                OpenPendingHandover();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever a second instance sends must not take the app down.
        }
    }

    // The queued files are opened as soon as the host is free; watched once, by the host's own property.
    private void WatchHostForHandover()
    {
        if (_watchingHost)
        {
            return;
        }

        _watchingHost = true;
        Host.GetObservable(DialogHost.ActiveDialogProperty).Subscribe(new HostObserver(this));
    }

    private sealed class HostObserver(WriteWindow window) : IObserver<Dialog?>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(Dialog? value)
        {
            if (value is null)
            {
                window.PostDrainHandover();
            }
            else
            {
                window.StatesChanged?.Invoke();
            }
        }
    }

    // Posted: the host is still in the middle of its Close, and the queue of the cards may show the next card in the same turn (that one goes first).
    private void PostDrainHandover()
    {
        StatesChanged?.Invoke();
        if (_pendingHandover.Count == 0 || _handoverDrainPosted)
        {
            return;
        }

        _handoverDrainPosted = true;
        Dispatcher.Post(() =>
        {
            _handoverDrainPosted = false;
            DrainHandover();
        }, DispatcherPriority.Background);
    }

    /// <summary>Opens the files that arrived while a card was open, once the host is free again. Call it on the UI thread.</summary>
    internal void DrainHandover()
    {
        if (!Host.IsOpen)
        {
            OpenPendingHandover();
        }
    }

    private void OpenPendingHandover()
    {
        if (_pendingHandover.Count == 0 || IsCloseRunning || _closeConfirmed)
        {
            return;
        }

        var files = _pendingHandover.ToList();
        _pendingHandover.Clear();
        _ = GuardAsync(() => OpenFilesAsync(files));
    }

    // Files this window will not open because it is going away: the host gives them to the next window (TakeUndelivered). Called on any thread.
    private void Undeliver(IReadOnlyList<string> paths)
    {
        lock (_undelivered)
        {
            _undelivered.AddRange(paths);
        }
    }

    /// <summary>The files this window was handed but will not open: the ones queued behind a card and the ones that arrived once the close was confirmed. Cleared by the call.</summary>
    public IReadOnlyList<string> TakeUndelivered()
    {
        List<string> taken;
        lock (_undelivered)
        {
            taken = [.. _pendingHandover, .. _undelivered];
            _pendingHandover.Clear();
            _undelivered.Clear();
        }

        return taken;
    }

    /// <summary>The files of a hand-over that wait for a card to end (tests).</summary>
    internal IReadOnlyList<string> PendingHandover => _pendingHandover;

    /// <summary>True while a close that came from a quit (the application or the system shuts down) is under way: the host ends the app once this window has closed.</summary>
    public bool QuitRequested { get; private set; }

    /// <summary>True once the close is confirmed and the window has not closed yet.</summary>
    public bool IsClosing => _closeConfirmed;

    /// <summary>The files of the host (macOS) go through the same check off the UI thread as the files of the pipe.</summary>
    public void Handover(IReadOnlyList<string> paths) => _ = Task.Run(() => OnHandover(paths));

    // Restores a minimized window and brings it to the front (a hand-over).
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    // Tabs.

    private WriteTab? FindDocument(DocumentTab? tab) => tab is null ? null : Workspace.Tabs.FirstOrDefault(t => ReferenceEquals(t.Tab, tab));

    private void OnCloseRequested(DocumentTab tab)
    {
        if (FindDocument(tab) is { } document)
        {
            _ = GuardAsync(() => CloseTabAsync(document));
        }
        else
        {
            Tabs.Items.Remove(tab);
        }
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    /// <summary>The user selected a tab in the strip: the workspace follows.</summary>
    private void OnTabSelected(DocumentTab? tab)
    {
        if (FindDocument(tab) is { } document)
        {
            Workspace.Select(document);
        }
    }

    private void OnWorkspaceTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            var tab = (WriteTab)e.NewItems![0]!;
            if (tab.Path is { } opened)
            {
                _recent.Touch(opened);
            }

            WatchRecovery(tab);
            Tabs.Items.Insert(Math.Min(e.NewStartingIndex, Tabs.Items.Count), tab.Tab);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            var tab = (WriteTab)e.OldItems![0]!;
            _areas.Remove(tab);
            _faultShown.Remove(tab);
            ForgetOutline(tab);
            ForgetFormatting(tab);
            ForgetRecovery(tab);
            Tabs.Items.Remove(tab.Tab);
        }

        StatesChanged?.Invoke();
    }

    private void OnWorkspaceActiveChanged()
    {
        Tabs.Selected = Workspace.Active?.Tab;
        ShowActiveDocument();
        PlacePanes();
        UpdateViewStatus();
        FocusActiveView();
        StatesChanged?.Invoke();
    }

    /// <summary>The document slot shows the area of the active tab (made on first use); the others leave the screen.</summary>
    private void ShowActiveDocument()
    {
        if (Workspace.Active is not { } active)
        {
            DocumentPane.Content = null;
            return;
        }

        if (!_areas.TryGetValue(active, out var area))
        {
            area = CreateDocumentArea(active);
            _areas[active] = area;
        }

        DocumentPane.Content = area;
    }

    /// <summary>The start view while no tab is open (no status bar), else the document view with the frame parts that exist.</summary>
    private void Refresh()
    {
        if (Tabs.Items.Count == 0)
        {
            DocumentPane.IsOpen = false;
            Frame.StatusBar = null;
            Frame.TabRow = null;
            Frame.Band = null;
            Frame.Notification = null;
            PlacePanes();
            Frame.Document = Start;
            return;
        }

        Frame.TabRow = Ribbon.TabRow;
        Frame.Band = Ribbon.Band;
        StartRibbonReleaseTimer();
        Frame.Document = DocumentPane;
        Frame.StatusBar = Status;
        PlacePanes();
        DocumentPane.IsOpen = true;
    }

    private sealed class SelectedObserver(WriteWindow window) : IObserver<DocumentTab?>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DocumentTab? value) => window.OnTabSelected(value);
    }

    // Keys.

    // Open, Close tab and the tab switch. A key held down repeats; a one-shot command ignores the repeats. A card that is open makes the window modal.
    private void AttachKeys()
    {
        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers
            ?? (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        AddHandler(KeyUpEvent, (_, e) => _keysDown.Remove(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => _keysDown.Clear();
        Activated += (_, _) => FocusActiveView(2);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var repeat = !_keysDown.Add(e.Key);
            if (Host.IsOpen)
            {
                return;
            }

            if (ForwardNavigationKey(e))
            {
                // The page view does not have the focus (it was lost or never taken): the keys that scroll still scroll it.
                e.Handled = true;
            }
            else if (WriteCommandCatalog.Find(e.Key, e.KeyModifiers, command) is { } found)
            {
                // The shortcuts of the catalog (Open, Close tab, Settings, zoom). A key held down repeats only where the shortcut says so; a disabled command still takes its key.
                e.Handled = true;
                if (!repeat || found.Shortcut?.AllowsRepeat == true)
                {
                    if (found.Id == WriteCommandIds.CloseTab && Workspace.Active is null && OperatingSystem.IsMacOS())
                    {
                        Close();
                    }
                    else
                    {
                        RunCommand(found.Id);
                    }
                }
            }
            else if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Control)
            {
                e.Handled = true;
                Workspace.SelectNext();
            }
            else if (e.Key == Key.Tab && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                e.Handled = true;
                Workspace.SelectPrevious();
            }
        }, RoutingStrategies.Tunnel);
    }

    // IWriteMenuWindow (the macOS menu bar).

    bool IWriteMenuWindow.DialogOpen => Host.IsOpen;

    bool IWriteMenuWindow.HasTab => Workspace.Active is not null;

    IReadOnlyList<RecentFile> IWriteMenuWindow.RecentFiles => Start.Recent;

    void IWriteMenuWindow.NewDocument() => _ = GuardAsync(() => NewDocumentAsync());

    void IWriteMenuWindow.Open() => _ = GuardAsync(OpenFromDialogAsync);

    /// <summary>Closes the active tab after the question about its unsaved changes, if it has any (a clean tab closes at once, before this returns).</summary>
    public void CloseActiveTab()
    {
        if (Workspace.Active is { } tab)
        {
            _ = GuardAsync(() => CloseTabAsync(tab));
        }
    }

    /// <summary>Closes the active tab; with no tab open closes the window (Cmd+W on a Mac).</summary>
    public void CloseTabOrWindow()
    {
        if (Workspace.Active is null)
        {
            Close();
        }
        else
        {
            CloseActiveTab();
        }
    }

    // The edit keys of the menu (Cmd+C and the others) are key equivalents of the whole window: while a text box (the command search) has the focus they are its own, so the items step aside.
    bool IWriteMenuWindow.CanRun(string commandId) =>
        _rules.Allows(commandId) && _actions.ContainsKey(commandId) && (!WriteCommandIds.IsEditing(commandId) || FocusedElement() is not TextBox);

    void IWriteMenuWindow.RunCommand(string commandId) => RunCommand(commandId);

    bool IWriteMenuWindow.IsOn(string commandId) => IsCommandOn(commandId);

    void IWriteMenuWindow.OpenRecent(RecentFile file) => _ = GuardAsync(() => OpenRecentAsync(file));

    void IWriteMenuWindow.OpenSettings() => Settings.Open();

    void IWriteMenuWindow.Minimize() => WindowState = WindowState.Minimized;

    void IWriteMenuWindow.ToggleWindowZoom() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void IWriteMenuWindow.BringAllToFront() => Activate();

    // Placement: the window fits the work area and goes where it closed last.

    private void ApplyPlacement(WindowPlacement? stored)
    {
        try
        {
            var screens = Screens;
            var primary = screens.Primary ?? (screens.All.Count > 0 ? screens.All[0] : null);
            if (primary is null)
            {
                return;
            }

            var target = primary;
            if (stored is { IsUsable: true })
            {
                // The screen under the top left corner of the stored window (a little inside it); the primary one when that screen is gone.
                var corner = new PixelPoint((int)Math.Clamp(stored.X + 40, int.MinValue, int.MaxValue), (int)Math.Clamp(stored.Y + 40, int.MinValue, int.MaxValue));
                target = screens.ScreenFromPoint(corner) ?? primary;
            }

            var fit = WindowPlacement.FitTo(target.WorkingArea, target.Scaling, stored, new Size(Width, Height));
            Width = fit.Width;
            Height = fit.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)fit.X, (int)fit.Y);
            Placement.Start(fit);
            if (fit.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The window stays where the system puts it.
        }
    }

    /// <summary>The placement the window stores when it closes; the shared logic of <see cref="WindowPlacementTracker"/> (maximize and minimize transitions included).</summary>
    internal WindowPlacementTracker Placement { get; } = new();

    // Closing.

    // The window closes at once when nothing is unsaved and no save runs. Otherwise the close is held back (Cancel), every tab with unsaved changes is asked in order (a running
    // save is waited for first) and the window closes itself when all are answered; a quit (Cmd+Q, a log out) says so for the host and the flag goes again when a question is answered Cancel.
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        var quit = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (!IsCloseRunning && !Workspace.Tabs.Any(t => t.IsDirty) && !_saves.IsAnyRunning && _closingTabs.Count == 0)
        {
            QuitRequested = quit;
            ReleaseRecovery(Workspace.Tabs);
            ConfirmClose();
            return;
        }

        e.Cancel = true;
        _ = GuardAsync(() => RunCloseAsync(quit));
    }

    // Before anything is flushed or disposed: an open that is still reading a file ends without a tab, a recent entry or a card, and the files queued behind it never start.
    private void ConfirmClose()
    {
        _closeConfirmed = true;
        Workspace.Seal();
        Step(_closing.Cancel);
    }

    private bool IsCloseRunning => _closeRuns > 0;

    /// <summary>
    /// The close questions and, when all are answered, the close itself (the seam of <see cref="OnWindowClosing"/>; <c>Window.Close()</c> cannot carry a reason headless).
    /// <paramref name="quit"/> arms <see cref="QuitRequested"/> for this attempt; a Cancel answer or an exception disarms it again. A second request while the questions run is the same
    /// wish and does not stack a second set (a quit upgrades a plain close, never the other way round).
    /// </summary>
    internal async Task RunCloseAsync(bool quit)
    {
        if (_closeConfirmed)
        {
            return;
        }

        if (IsCloseRunning)
        {
            QuitRequested |= quit;
            return;
        }

        QuitRequested = quit;
        _closeRuns++;
        try
        {
            // A question about one tab (its close button) that is open answers first; the tab it closes is not asked again.
            while (_closingTabs.Count > 0)
            {
                await Task.WhenAll(_closingTabs.Values);
            }

            if (await _closeGuard.CanCloseAllAsync(Workspace.Tabs.ToList()))
            {
                // Posted: with no dirty tab the guard completes synchronously, and Close must not run nested inside the Closing event.
                ReleaseRecovery(Workspace.Tabs);
                ConfirmClose();
                Dispatcher.UIThread.Post(Close);
            }
            else
            {
                QuitRequested = false;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception in the close guard keeps the window open; it must not take the app down.
            QuitRequested = false;
        }
        finally
        {
            _closeRuns--;
            DrainHandover();
        }
    }

    private void FlushWriters()
    {
        _recent.Flush(FlushTimeout);
        Settings.Flush(FlushTimeout);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Step(() => AppDomain.CurrentDomain.ProcessExit -= _processExit);
        Step(() =>
        {
            // A session event after the close (a tab disposing) must not start a timer again.
            _formatClosed = true;
            _formatRefresh?.Dispose();
            _viewStatesRefresh?.Dispose();
        });
        Step(() =>
        {
            if (!_options.IsMeasuring && Placement.ToStore() is { } placement)
            {
                Settings.RememberWindow(placement);
            }
        });

        // A snapshot in flight ends silently and no copy is deleted here: the close questions did that where they were answered.
        Step(StopRecovery);

        // Every tab cancels what runs on its document and lets go of its file; then the files get what is queued, waiting a moment at most.
        foreach (var tab in Workspace.Tabs.ToList())
        {
            Step(tab.Dispose);
        }

        Step(FlushWriters);
    }

    // The signal of a closing window: cancelled once and then disposed (the token of a cancelled source stays usable), so the window itself needs no dispose.
    private sealed class CloseSignal
    {
        private Action? _cancel;

        public CloseSignal()
        {
            var source = new CancellationTokenSource();
            Token = source.Token;
            _cancel = () =>
            {
                try
                {
                    source.Cancel();
                }
                finally
                {
                    source.Dispose();
                }
            };
        }

        public CancellationToken Token { get; }

        public void Cancel() => Interlocked.Exchange(ref _cancel, null)?.Invoke();
    }

    // Every step runs even when one before it throws: a failing Dispose must not leave the other tabs open or the positions unremembered.
    private static void Step(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The window is closing; there is nobody to tell.
        }
    }
}
