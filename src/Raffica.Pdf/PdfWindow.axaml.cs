using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Raffica.Core.Hosting;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Settings;
using Raffica.Core.Signatures;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Hosting;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Printing;
using Raffica.Pdf.Settings;
using Raffica.Pdf.Signing;
using Raffica.Pdf.Text;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

/// <summary>
/// The window of the PDF app: the shared frame with the drawn title bar (mark, document tabs, search over the ribbon), the ribbon, the
/// pages pane on the left (204 px), the marks pane on the right (256 px, closed until asked for), the document slot and the status bar. Without
/// a document tab it shows the start view (no panes, no status bar). The document slot shows the page view of the active tab.
/// </summary>
internal sealed partial class PdfWindow : Window
{
    /// <summary>Width of the pages pane in device-independent pixels.</summary>
    internal const double PagesPaneWidth = 204;

    /// <summary>Width of the marks pane (the right slot).</summary>
    internal const double MarksPaneWidth = 256;

    /// <summary>How many recent files the start view shows.</summary>
    internal const int RecentOnStartView = 5;

    /// <summary>How long a file given at the start may take to its first sharp page before the listener opens anyway.</summary>
    private static readonly TimeSpan FirstSharpFallbackDelay = TimeSpan.FromSeconds(3);

    /// <summary>The longest the window waits for the settings files and the recent list when it closes or the process exits.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(1);

    // A hand-over of 64 hostile paths must not become 64 cards.
    private const int MaxHandoverErrorCards = 3;
    private const int MaxShownNameLength = 120;

    private readonly IDisposable _fieldCodes;
    private readonly EventHandler? _processExit;
    private readonly IDisposable _selectedSubscription;
    private readonly CloseGuard _closeGuard;
    private readonly PdfOptions _options;
    private readonly RecentService _recent;
    private SignatureLibrary? _signatures;
    private readonly Dictionary<PdfTab, PageView> _views = [];
    private readonly Dictionary<PdfTab, TabPanes> _panes = [];
    private readonly Dictionary<PdfTab, MarksPane> _marks = [];
    private readonly HashSet<PdfTab> _marksOpen = [];
    private readonly Dictionary<PdfTab, SearchSession> _searches = [];
    private readonly Dictionary<PdfTab, TextInteraction> _texts = [];
    private readonly Dictionary<PdfTab, AnnotateInteraction> _annotates = [];
    private readonly Dictionary<PdfTab, AnnotateBadge> _badges = [];
    private readonly Dictionary<PdfTab, BadgeHost> _hosts = [];
    private readonly Dictionary<PdfTab, FormParts> _formParts = [];
    private readonly Dictionary<PdfTab, OutlineSource> _outlines = [];

    // Where a tab starts when its view is made again after a rewrite (page, zoom, zoom mode, layout); used once by ApplyInitialView.
    private readonly Dictionary<PdfTab, RememberedView> _rebuildViews = [];
    private readonly Action<Uri>? _launcher;
    private readonly TimeProvider? _time;
    private SearchBar? _searchBar;
    private LeftPaneKind _leftPane = LeftPaneKind.Pages;
    private DispatcherTimer? _firstSharpFallback;
    private bool _firstSharpWorkStarted;
    private SingleInstance? _instance;
    private PdfEngine? _engine;
    private bool _closeConfirmed;
    private SaveController _saves = null!; // made in the constructor, after the dialog queue it needs
    private readonly Func<PdfTab, Task<string?>>? _savePicker;

    /// <param name="options">The command line.</param>
    /// <param name="settings">The shell settings, read by the caller at startup.</param>
    /// <param name="appSettings">The settings of the viewer, read once at startup by the caller (null: the defaults; the window reads no file).</param>
    /// <param name="launcher">Opens a confirmed address outside the document (tests give their own; null: the system browser or mail program).</param>
    /// <param name="time">The clock of the arming delay of the link card (tests move it; null: the system clock).</param>
    /// <param name="printService">The print service (tests give a fake; null: the one of the system, made on the first print).</param>
    /// <param name="saveRunner">Replaces the save of Core (tests; null: the real save).</param>
    /// <param name="savePicker">Replaces the save dialog of the system (tests; null: the system dialog).</param>
    public PdfWindow(PdfOptions options, ShellSettings settings, AppSettings? appSettings = null, Action<Uri>? launcher = null, TimeProvider? time = null, IPrintService? printService = null, SaveRunner? saveRunner = null, Func<PdfTab, Task<string?>>? savePicker = null)
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

        // The Save choice of the close question saves through the controller and closes only if that worked.
        _savePicker = savePicker;
        _closeGuard = new CloseGuard(PromptAsync, SaveForCloseAsync, new PlanClose(AskPlanAsync, ApplyForCloseAsync));
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
        CommentsPane = new Pane { Title = PdfStrings.PaneMarks, Width = MarksPaneWidth, IsOpen = false };
        FieldCodeBadge.SetCode(NavigationPane, "6");
        FieldCodeBadge.SetCode(CommentsPane, "7");

        Status = new StatusBar();
        Start = new StartView { Tiles = StartTile.Standard(), Recent = [], OwnApp = StartApp.Pdf };

        Frame.TitleBar = titleBar;
        // The card of a running print job sits under the frame: it does not take the notification slot of the search bar.
        var printSlot = new ContentControl();
        DockPanel.SetDock(printSlot, Dock.Bottom);
        Host = new DialogHost { Content = new DockPanel { Children = { printSlot, Frame } } };
        Content = Host;
        Dialogs = new DialogQueue(Host);
        Settings = new PdfSettingsController(options, Host, settings, appDefaults, SetCompact, SetPageThemeOfAllTabs);
        Tools = new ToolState(() => Settings.App, Settings.Update);
        Tools.Changed += OnToolsChanged;
        _printService = printService;
        _print = new PrintCommand(
            () => _printService ??= PlatformPrintServices.Create(),
            () => Workspace.Active,
            new PrintPresenter(Dialogs, card => printSlot.Content = card),
            () => Settings.App,
            Settings.RememberPrinter,
            () => ActiveView?.CurrentPage ?? 0,
            () => TryGetPlatformHandle()?.Handle ?? 0,
            () => _saves.IsSaving || _applyFlow?.IsRunning == true || _outputFlow?.IsRunning == true || _exportFlow?.IsRunning == true);
        _saves = new SaveController(Dialogs, PickSavePathAsync, () => _engine ??= new PdfEngine(), card => printSlot.Content = card, () => _print.IsRunning, time, saveRunner, AskRequiredAsync);
        _saves.Changed += RefreshCommandStates;
        _saves.PathChanged += OnSavedUnderNewPath;
        _saves.ApplyPlan = (tab, saveAs) => Applying.RunAsync(tab, saveAs ? ApplyTarget.SaveAs : ApplyTarget.Choose);
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

        // Window-wide keys: Ctrl+K, F6 / Shift+F6, the shortcuts of the command catalog and the Alt field codes (Q the search field, 6 the pages pane, 7 the marks pane).
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
        Closed += OnWindowClosed;

        // A normal exit of the process also writes what is still queued (a close of the window already did).
        _processExit = (_, _) => FlushWriters();
        AppDomain.CurrentDomain.ProcessExit += _processExit;
    }

    // Every step runs even when one before it throws: a failing Dispose must not leave the sessions open or the positions unremembered.
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

    private static void EachStep<T>(IEnumerable<T> items, Action<T> step)
    {
        foreach (var item in items.ToList())
        {
            Step(() => step(item));
        }
    }

    private void FlushWriters()
    {
        _recent.Flush(FlushTimeout);
        Settings.Flush(FlushTimeout);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Step(_print.Cancel);
        Step(_saves.Cancel);
        Step(() => _applyFlow?.Cancel());
        Step(() => _outputFlow?.Cancel());
        Step(() => _exportFlow?.Cancel());
        Step(_fieldCodes.Dispose);
        Step(_selectedSubscription.Dispose);
        Step(() => _firstSharpFallback?.Stop());
        Step(() => AppDomain.CurrentDomain.ProcessExit -= _processExit);

        // The position of every open file is remembered, then the panes, the views and the texts let go of the sessions, then the sessions are disposed.
        EachStep(Workspace.Tabs, RememberView);
        EachStep(_panes.Values, panes => panes.Dispose());
        _panes.Clear();
        EachStep(_marks.Values, DisposeMarks);
        _marks.Clear();
        _marksOpen.Clear();
        EachStep(_searches.Values, search => search.Dispose());
        _searches.Clear();
        EachStep(_annotates.Values, annotate => annotate.Dispose());
        _annotates.Clear();
        EachStep(_hosts.Values, host => host.Forms?.Dispose());
        EachStep(_hosts.Values, host => host.Dispose());
        _hosts.Clear();
        EachStep(_badges.Values, badge => badge.Dispose());
        _badges.Clear();
        EachStep(_formParts, pair => DisposeFormParts(pair.Key, pair.Value));
        _formParts.Clear();
        EachStep(_texts.Values, text => text.Dispose());
        _texts.Clear();
        EachStep(_outlines, pair =>
        {
            if (_views.TryGetValue(pair.Key, out var outlined))
            {
                outlined.FirstSharpPage -= pair.Value.Start;
            }

            pair.Value.Dispose();
        });
        _outlines.Clear();
        EachStep(_views.Values, view =>
        {
            view.FirstSharpPage -= OnFirstSharpPageShown;
            view.Search = null;
            view.Tab = null;
        });
        _views.Clear();
        EachStep(Workspace.Tabs, tab => tab.Dispose());

        // Last: the files get what is queued (positions of the tabs, the last printer), waiting a moment at most.
        Step(FlushWriters);
    }

    /// <summary>Raised when the page view of a tab has painted its first sharp tile (the measurement of the first and the second file listens).</summary>
    internal event Action<PdfTab>? FirstSharpPageDrawn;

    /// <summary>Raised when the files of a later launch arrive in the running instance, before anything is checked or opened.</summary>
    internal event Action? HandoverArrived;

    /// <summary>The recent files and the position per file (loaded after the first sharp page).</summary>
    internal RecentService Recent => _recent;

    /// <summary>
    /// The saved signatures. Made on the first use (the folder next to <c>--settings-file</c> or the one of the user); neither the
    /// library nor its folder exists before that, so the start and the measurement runs are unchanged.
    /// </summary>
    internal SignatureLibrary Signatures => _signatures ??= new SignatureLibrary(() => PdfSettingsFiles.SignaturesDirectoryFor(_options.SettingsFile));

    /// <summary>Replaces the library (tests: one over a folder that cannot be read).</summary>
    internal void UseSignatureLibrary(SignatureLibrary library) => _signatures = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>
    /// Shows the new-signature card (through the dialog queue) and gives the signature it saved, or <see langword="null"/> when it was cancelled.
    /// The card picks its image with the system file dialog unless <paramref name="pickImage"/> is given (tests).
    /// </summary>
    internal Task<SavedSignature?> ShowNewSignatureAsync(Func<Task<Stream?>>? pickImage = null) =>
        Dialogs.ShowAsync<SavedSignature?>(done => NewSignatureCard.Create(Signatures, pickImage ?? PickSignatureImageAsync, done));

    /// <summary>The file dialog of the card: one PNG or JPG; the stream is read by the card, the path is never kept.</summary>
    private async Task<Stream?> PickSignatureImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = PdfStrings.SignatureNewPickTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(PdfStrings.SignatureNewPickFilter)
                {
                    Patterns = ["*.png", "*.PNG", "*.jpg", "*.JPG", "*.jpeg", "*.JPEG"],
                    MimeTypes = ["image/png", "image/jpeg"],
                    AppleUniformTypeIdentifiers = ["public.png", "public.jpeg"],
                },
            ],
        });
        return files.Count == 0 ? null : await files[0].OpenReadAsync();
    }

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

    /// <summary>The right slot of the frame (256 px): the marks pane of the active tab. Closed until the marks command opens it; it takes part in the F6 ring (field code 7).</summary>
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

        // The picker may take the key up of the O of Ctrl+O (a portal or a sheet does not always deactivate the window): without this the
        // O would stay "down" and the next Ctrl+O would count as a repeat.
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
            Title = PdfStrings.OpenDialogTitle,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(PdfStrings.OpenFilterPdf)
                {
                    // Both spellings (a Linux file dialog matches the case), the MIME type for the portals and the type of macOS.
                    Patterns = ["*.pdf", "*.PDF"],
                    MimeTypes = ["application/pdf"],
                    AppleUniformTypeIdentifiers = ["com.adobe.pdf"],
                },
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    /// <summary>Saves the active tab into its file (what Ctrl+S does); false without a tab or when nothing was saved.</summary>
    internal Task<bool> SaveActiveAsync() => Workspace.Active is { } tab ? _saves.SaveAsync(tab) : Task.FromResult(false);

    /// <summary>Saves the active tab under a path the user picks (what Ctrl+Shift+S does).</summary>
    internal Task<bool> SaveActiveAsAsync() => Workspace.Active is { } tab ? _saves.SaveAsAsync(tab) : Task.FromResult(false);

    /// <summary>The save and save as logic of the window (for tests).</summary>
    internal SaveController Saves => _saves;

    private void OnSavedUnderNewPath(PdfTab tab)
    {
        if (tab.Path is { } path)
        {
            _recent.Touch(path);
        }
    }

    private Task<string?> PickSavePathAsync(PdfTab tab) => PickPathAsync(tab, tab.Title);

    // The target of an applied page plan: never the name of the open file, so that a quick Enter cannot replace it.
    private Task<string?> PickApplyPathAsync(PdfTab tab) =>
        PickPathAsync(tab, PdfStrings.Fill(PdfStrings.ApplySuggestedNameFormat, Path.GetFileNameWithoutExtension(tab.Title)));

    private async Task<string?> PickPathAsync(PdfTab tab, string suggestedName, string? title = null)
    {
        if (_savePicker is { } picker)
        {
            return await picker(tab);
        }

        // As with the open dialog: a picker may take the key up of the S of Ctrl+S.
        _keysDown.Clear();
        try
        {
            var start = tab.Path is { } path && Path.GetDirectoryName(path) is { Length: > 0 } folder ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title ?? PdfStrings.SaveDialogTitle,
                SuggestedFileName = suggestedName,
                DefaultExtension = "pdf",
                ShowOverwritePrompt = true,
                SuggestedStartLocation = start,
                FileTypeChoices =
                [
                    new FilePickerFileType(PdfStrings.OpenFilterPdf)
                    {
                        Patterns = ["*.pdf", "*.PDF"],
                        MimeTypes = ["application/pdf"],
                        AppleUniformTypeIdentifiers = ["com.adobe.pdf"],
                    },
                ],
            });
            return file?.TryGetLocalPath();
        }
        finally
        {
            _keysDown.Clear();
        }
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
        // The load starts first and runs off the UI thread: whatever the rest does, a failing step cannot skip it. Idempotent: a second
        // call waits for the load of the first.
        var load = _recent.LoadAsync();
        if (!_firstSharpWorkStarted)
        {
            _firstSharpWorkStarted = true;
            _firstSharpFallback?.Stop();
            try
            {
                _instance?.StartListening(OnHandover);
            }
            finally
            {
                ProbePrint();
            }
        }

        await load;
    }

    /// <summary>
    /// The files of a later launch (called on the thread of the pipe, never the UI thread: asking for a file on a dead network share can
    /// stall for a long time). The check of the paths happens here; the UI thread only gets the answer: only existing files open, the
    /// rest ends in a few cards, and the window comes to the front.
    /// </summary>
    internal void OnHandover(IReadOnlyList<string> paths)
    {
        try
        {
            Dispatcher.UIThread.Post(() => HandoverArrived?.Invoke());
            if (_closeConfirmed)
            {
                return;
            }

            var rejected = new List<string>();
            var accepted = InstanceHandover.Accept(paths, path =>
            {
                if (rejected.Count < MaxHandoverErrorCards)
                {
                    rejected.Add(path.Length > MaxShownNameLength ? path[..MaxShownNameLength] : path);
                }
            });
            Dispatcher.UIThread.Post(() => ShowHandover(accepted, rejected));
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
            return;
        }

        try
        {
            foreach (var name in rejected)
            {
                ShowOpenError(name, PdfOpenError.FileNotFound);
            }

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
        var data = e.DataTransfer;
        await GuardAsync(async () =>
        {
            if (FileDrop.Accepts(data))
            {
                await OpenDroppedAsync(data);
            }
        });
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
            tab.Annotations.StateChanged += RefreshCommandStates;
            tab.OrganizeCreated += OnOrganizeCreated;
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
            tab.Annotations.StateChanged -= RefreshCommandStates;
            tab.OrganizeCreated -= OnOrganizeCreated;
            _printInfo.Remove(tab);
            _annotateAllowed.Remove(tab);
            _fillAllowed.Remove(tab);
            RememberView(tab);
            _marksOpen.Remove(tab);
            _rebuildViews.Remove(tab);
            DisposeTabPieces(tab);

            Tabs.Items.Remove(tab.Tab);
        }
    }

    // The one cleanup of the per-tab pieces (panes, marks, interactions, badges, form parts, text, outline, search, view), for the close of a tab
    // and for the rebuild after a rewrite. It leaves the dictionaries without the tab and the tab itself alone.
    private void DisposeTabPieces(PdfTab tab)
    {
        if (_panes.Remove(tab, out var panes))
        {
            panes.Dispose();
        }

        // The marks pane listens to the select controller of the tab, so it goes first.
        if (_marks.Remove(tab, out var marks))
        {
            DisposeMarks(marks);
        }

        if (_annotates.Remove(tab, out var annotate))
        {
            annotate.Dispose();
        }

        if (_hosts.Remove(tab, out var host))
        {
            host.Forms?.Dispose();
            host.Dispose();
        }

        if (_badges.Remove(tab, out var badge))
        {
            badge.Dispose();
        }

        if (_formParts.Remove(tab, out var formParts))
        {
            DisposeFormParts(tab, formParts);
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
            view.FormOverlay = null;
            view.Tab = null;
        }
    }

    /// <summary>
    /// The document of the tab was rewritten (page changes applied) and the session adopted the new file: every piece that is bound to the old
    /// page list or the old page sizes goes (the same cleanup as the close of a tab) and is made anew over the adopted document, at
    /// <paramref name="page"/> (0-based, limited to the new count) with the zoom, zoom mode and layout the tab had. The annotation and form
    /// sessions are made anew too (the editor of a rewritten document starts clean) and the permissions are read again. A tab in the background
    /// shows nothing until it is selected. UI thread; no edit may be open (the Apply flow asks for that first).
    /// </summary>
    internal void RebuildTab(PdfTab tab, int page)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!Workspace.Tabs.Contains(tab))
        {
            return;
        }

        Debug.Assert(tab.AnnotationsIfCreated is not { IsDirty: true }, "A rewritten document has no open edit.");
        if (_views.TryGetValue(tab, out var view))
        {
            var kept = view.CaptureRemembered();
            _rebuildViews[tab] = kept with { View = new ViewState(new PageAnchor(Math.Max(0, page), 0), 0) };
        }

        if (tab.AnnotationsIfCreated is { } old)
        {
            old.StateChanged -= RefreshCommandStates;
        }

        DisposeTabPieces(tab);
        tab.ResetForRewrittenDocument();
        tab.Annotations.StateChanged += RefreshCommandStates;
        _printInfo.Remove(tab);
        _annotateAllowed.Remove(tab);
        _fillAllowed.Remove(tab);
        if (ReferenceEquals(Workspace.Active, tab))
        {
            ShowActiveView();
            EnforceAnnotatePermission(showCard: false);
        }

        UpdateSaveState();
        RefreshCommandStates();
    }

    private void OnWorkspaceActiveChanged()
    {
        Tabs.Selected = Workspace.Active?.Tab;
        if (OpenPlan is not null)
        {
            // The tool is the window's, not the tab's: a tool left over from another tab does not run over an open plan.
            Tools.End();
        }

        UpdateSaveState();
        ShowActiveView();
        EnforceAnnotatePermission(showCard: false);
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
            UpdateMarksPane();
            return;
        }

        if (!_views.TryGetValue(active, out var view))
        {
            view = new PageView { Tab = active };
            view.FirstSharpPage += OnFirstSharpPageShown;
            var shownTab = active;
            view.FirstSharpPage += () => FirstSharpPageDrawn?.Invoke(shownTab);

            // The permissions are read only once this tab has its first sharp page: nothing for the print command is asked before.
            view.FirstSharpPage += () => LoadPrintInfo(shownTab);

            // The Pages commands wait for the first sharp page (nothing of the mode is read before it).
            view.FirstSharpPage += RefreshCommandStates;
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
            var text = new TextInteraction(active, view, Dialogs, _launcher, _time, () => Tools.Tool != AnnotateTool.None);
            _texts[active] = text;
            var annotate = new AnnotateInteraction(active, view, Tools, text.Selection, Dialogs, _time);
            _annotates[active] = annotate;
            var annotated = active;
            annotate.SelectionChanged += () =>
            {
                if (ReferenceEquals(Workspace.Active, annotated))
                {
                    RefreshCommandStates();
                }
            };
            var badge = new AnnotateBadge(Tools, annotate.Select);
            var host = new BadgeHost(badge, null, Tools);
            view.Badge = host;
            _badges[active] = badge;
            _hosts[active] = host;
            AttachForm(active, view);

            // The outline is read once for the pane and for the palette, after the first sharp page.
            var outline = new OutlineSource(active);
            _outlines[active] = outline;
            view.FirstSharpPage += outline.Start;
        }

        DocumentPane.Content = view;
        if (_searchBar is not null && _searches.TryGetValue(active, out var shown))
        {
            _searchBar.Session = shown;
        }

        UpdateLeftPane();
        UpdateMarksPane();
        UpdateViewStatus();
        Dispatcher.UIThread.Post(() => view.Focus(), DispatcherPriority.Input);
    }

    // A document without a form gets no form session and no overlay: nothing is created, drawn or loaded for it.
    private void AttachForm(PdfTab tab, PageView view)
    {
        bool hasForm;
        try
        {
            hasForm = tab.Session.Document is { HasAcroForm: true };
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (!hasForm)
        {
            return;
        }

        var focus = new FormFocus();
        var markers = new FormMarkers(tab.Forms, focus) { Suppressed = () => Tools.Tool != AnnotateTool.None };
        markers.Attach(view);
        view.FormOverlay = markers;
        var interaction = new FormInteraction(tab, view, Tools, tab.Forms, focus, Dialogs, () => FillAllowedOf(tab), () => PlanOpenOf(tab));
        var keys = new FormKeys(view, Tools, tab.Forms, focus, interaction, Dialogs, () => FillAllowedOf(tab), () => PlanOpenOf(tab));
        _formParts[tab] = new FormParts(markers, focus, interaction, keys);

        // The commands of the Forms tab follow what the session knows: the fields, the required list, the highlight.
        tab.Forms.Changed += RefreshCommandStates;

        // The badge (notice, counter, highlight switch) is built once the first sharp page is there, and only for a document with a form.
        void BuildBadge()
        {
            if (_hosts.TryGetValue(tab, out var host) && host.Forms is null && _formParts.ContainsKey(tab))
            {
                host.Forms = new FormBadge(tab.Forms, markers, RunNextRequired);
            }
        }

        if (view.FirstSharpPageShown)
        {
            BuildBadge();
        }
        else
        {
            view.FirstSharpPage += BuildBadge;
        }
    }

    // The one cleanup of the form pieces of a tab, for the close of the tab and of the window: the commands stop following the session, then the
    // keys, the interaction and the markers let go.
    private void DisposeFormParts(PdfTab tab, FormParts parts)
    {
        tab.FormsIfCreated!.Changed -= RefreshCommandStates;
        parts.Keys.Dispose();
        parts.Interaction.Dispose();
        parts.Markers.Dispose();
    }

    /// <summary>The form markers and the form focus of a tab once the tab has been shown (for tests and the form interaction); null for a document without a form.</summary>
    internal FormParts? FormsOf(PdfTab tab) => _formParts.GetValueOrDefault(tab);

    /// <summary>The host of the badges of a tab once the tab has been shown (for tests); its form badge exists after the first sharp page of a document with a form.</summary>
    internal BadgeHost? BadgeHostOf(PdfTab tab) => _hosts.GetValueOrDefault(tab);

    /// <summary>The marks pane of a tab once it was opened (for tests); null before.</summary>
    internal MarksPane? MarksOf(PdfTab tab) => _marks.GetValueOrDefault(tab);

    /// <summary>Shows the marks pane of the active tab, or hides it when it is shown. The state belongs to the tab: another tab keeps its own.</summary>
    internal void ToggleMarksPane()
    {
        if (Workspace.Active is not { } active || !_views.ContainsKey(active))
        {
            return;
        }

        if (!_marksOpen.Remove(active))
        {
            _marksOpen.Add(active);
        }

        UpdateMarksPane();
    }

    /// <summary>
    /// Puts the marks pane of the active tab into the right slot when the tab has it open (made on first use and read after the first
    /// sharp page, kept until the tab closes); else the slot is closed. A pane that closes under the keyboard focus hands the focus to the
    /// document (the F6 ring does that when the slot closes).
    /// </summary>
    private void UpdateMarksPane()
    {
        var active = Workspace.Active;
        if (active is null || Tabs.Items.Count == 0 || !_marksOpen.Contains(active) || !_views.TryGetValue(active, out var view) || !_annotates.TryGetValue(active, out var annotate))
        {
            CommentsPane.IsOpen = false;
            CommentsPane.Content = null;
            return;
        }

        if (!_marks.TryGetValue(active, out var marks))
        {
            var shown = active;
            _marks[active] = marks = new MarksPane(new MarksModel(active, view), active, view, Tools, annotate.Select, Dialogs, () => AnnotateAllowedOf(shown) != false);
        }

        CommentsPane.Content = marks;
        CommentsPane.IsOpen = true;
    }

    private static void DisposeMarks(MarksPane marks)
    {
        marks.Dispose();
        marks.Model.Dispose();
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

    /// <summary>The annotate interaction (tool routing and markup) of a tab, once the tab has been shown (for tests); null before.</summary>
    internal AnnotateInteraction? AnnotateOf(PdfTab tab) => _annotates.GetValueOrDefault(tab);

    /// <summary>The sign of the annotate mode over the page area of a tab, once the tab has been shown (for tests); null before.</summary>
    internal ModeIndicator? ModeIndicatorOf(PdfTab tab) => _badges.GetValueOrDefault(tab)?.Indicator;

    /// <summary>The colour and width choice over the page area of a tab, once the tab has been shown (for tests); null before.</summary>
    internal StyleStrip? StyleStripOf(PdfTab tab) => _badges.GetValueOrDefault(tab)?.Strip;

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
    internal PageView? ActiveView => Workspace.Active is { } active ? _views.GetValueOrDefault(active) : null;

    /// <summary>
    /// A new tab starts from the position remembered for its file (when the recent list has been loaded and knows the file, which is
    /// not the case for a file given on the command line: that opens before the list is read, at the defaults), else from the default
    /// layout and zoom mode of the settings.
    /// </summary>
    private void ApplyInitialView(PageView view, PdfTab tab)
    {
        view.PageTheme = Settings.PageThemeForNewTab;
        if (_rebuildViews.Remove(tab, out var rebuilt))
        {
            view.RestoreView(rebuilt.View, rebuilt.Zoom, rebuilt.ZoomMode, rebuilt.Layout);
            return;
        }

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

    // The card about empty required fields; "go to first empty field" takes the same path as the ribbon command and the badge counter.
    private async Task<RequiredChoice> AskRequiredAsync(PdfTab tab, IReadOnlyList<string> names)
    {
        var choice = await Dialogs.ShowAsync<RequiredChoice>(done => RequiredFieldsCard.Create(names, done));
        if (choice == RequiredChoice.GoToFirst && ReferenceEquals(Workspace.Active, tab))
        {
            GoToFirstRequired(tab);
        }

        return choice;
    }

    // "Save" of the close prompt: when a refused field draft keeps the tab open after the save, the draft is shown so the user sees why.
    private async Task<bool> SaveForCloseAsync(PdfTab tab)
    {
        var saved = await _saves.SaveAsync(tab);
        if (tab.AnnotationsIfCreated is { HasUnresolved: true } && FormsOf(tab) is { } parts)
        {
            parts.Interaction.RevealDraft();
        }

        return saved;
    }

    /// <summary>"Go to first empty field": the walk starts at the beginning, not after the field that had the focus; a draft that is still open stops it and is shown.</summary>
    internal void GoToFirstRequired(PdfTab tab)
    {
        if (FormsOf(tab) is { } parts && parts.Interaction.EndOrReveal())
        {
            parts.Focus.Set(null);
            RunNextRequired();
        }
    }

    private Task<CloseChoice> PromptAsync(PdfTab tab) => AskAsync(tab, UnsavedKind.Save);

    /// <summary>"Discard the page changes?" for a tab with an open plan: Save is Apply, DontSave is Discard. A card that is already open answers Cancel.</summary>
    internal Task<CloseChoice> AskPlanAsync(PdfTab tab) => AskAsync(tab, UnsavedKind.Plan);

    private Task<CloseChoice> AskAsync(PdfTab tab, UnsavedKind kind)
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
        }, kind, _time));
        return answer.Task;
    }

    // "Apply…" of the close question: the Apply card flow; the tab closes only if the plan was written.
    private Task<bool> ApplyForCloseAsync(PdfTab tab) => Applying.RunAsync(tab);

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
