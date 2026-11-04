using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Raffica.Shell;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Gallery.FrameDemo;

/// <summary>What the frame view starts from: the settings file and what it held.</summary>
internal sealed record FrameDemoStartup(SettingsStore? Store, ShellSettings Settings);

/// <summary>Where the frame view keeps its settings and how the stored values combine with the switches.</summary>
internal static class FrameDemoSettings
{
    /// <summary>
    /// The settings file: <c>--settings-file</c> if given, else the settings of the gallery app in the profile of the user, and none at
    /// all for a measurement run without <c>--settings-file</c> (a measurement never reads or writes the settings of the user).
    /// </summary>
    public static string? StorePath(GalleryOptions options) =>
        options.SettingsFile ?? (options.IsMeasuring ? null : SettingsStore.DefaultPath("gallery"));

    public static FrameDemoStartup Open(GalleryOptions options)
    {
        if (StorePath(options) is not { } path)
        {
            return new FrameDemoStartup(null, new ShellSettings());
        }

        var store = new SettingsStore(path);
        return new FrameDemoStartup(store, store.Load());
    }

    /// <summary>The language at start: <c>--lang</c>, then the stored setting, then the UI language of the operating system.</summary>
    public static string ResolveLanguage(GalleryOptions options, ShellSettings stored) =>
        options.LangGiven ? options.Lang : Languages.Resolve(stored.Language, CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;

    /// <summary>The theme at start: <c>--theme</c> if given, else the stored one.</summary>
    public static ThemeMode ResolveTheme(GalleryOptions options, ShellSettings stored) =>
        options.ThemeGiven ? options.Theme : stored.Theme;
}

/// <summary>
/// The demo window of the app frame (<c>--view frame</c>): the shared frame with the drawn title bar (mark, three document tabs, search
/// over the ribbon), the sample Write ribbon, a navigation pane, the document page, a comments pane, the notification bar and the
/// status bar. F6, the Alt field codes (with Q for the search field and 6 and 7 for the side panes) and Ctrl+K work. Without any document tab it shows the start view; the File tab shows it too. The
/// start tiles and the recent files only record their request in the notification bar, nothing is opened.
/// </summary>
internal sealed partial class FrameDemoWindow : Window
{
    private readonly GalleryOptions _options;
    private readonly SettingsStore? _store;
    private readonly bool? _forceHighContrast;
    private readonly IDisposable _fieldCodes;
    private readonly RibbonTabDefinition _homeTab;
    private readonly StartView _start;
    private ShellSettings _settings;
    private bool _startShown;
    private int _untitled;

    public FrameDemoWindow(GalleryOptions options)
        : this(options, FrameDemoSettings.Open(options))
    {
    }

    public FrameDemoWindow(GalleryOptions options, FrameDemoStartup startup)
    {
        InitializeComponent();
        _options = options;
        _store = startup.Store;
        _forceHighContrast = options.HighContrast ? true : null;

        // What the dialog shows is what runs: the switches win over the stored values for this run.
        _settings = startup.Settings with
        {
            Theme = FrameDemoSettings.ResolveTheme(options, startup.Settings),
            CompactCommandBar = options.Compact || startup.Settings.CompactCommandBar,
        };
        Title = GalleryStrings.FrameWindowTitle;

        Frame = new AppFrame();
        var definition = DemoRibbon.Create(name => Notify(GalleryStrings.RanCommand(name)), () => SetCompact(!Frame.IsCompact));
        _homeTab = definition.Tabs.Single(t => t.Letter == "H");
        Ribbon = new RibbonView { Definition = definition, ActiveTab = _homeTab };

        AppMarkButton = CreateAppMark();
        var titleBar = new TitleBar { Mark = AppMarkButton };
        TitleSearch = titleBar.Search;
        TitleSearch.Source = definition;
        TitleSearch.CommandChosen += command => command.Run?.Invoke();
        Tabs = titleBar.Tabs;
        foreach (var title in new[] { GalleryStrings.DocTab1, GalleryStrings.DocTab2, GalleryStrings.DocTab3 })
        {
            Tabs.Items.Add(new DocumentTab(title));
        }

        Tabs.Selected = Tabs.Items[0];
        Tabs.CloseRequested += OnTabCloseRequested;
        Tabs.NewRequested += OnTabNewRequested;
        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.Property == DocumentTabs.SelectedProperty && _startShown && Tabs.Selected is not null)
            {
                ShowDocument();
            }
        };
        Ribbon.PropertyChanged += (_, e) =>
        {
            if (e.Property != RibbonView.ActiveTabProperty)
            {
                return;
            }

            if (Ribbon.ActiveTab?.Letter == DemoRibbon.FileLetter)
            {
                ShowStart(keepRibbon: true);
            }
            else if (_startShown && Tabs.Items.Count > 0)
            {
                ShowDocument();
            }
        };

        NavigationPane = new Pane { Title = ShellStrings.PaneNavigation, Width = 240, Content = CreateOutline() };
        DocumentPane = new Pane { IsDocument = true, PaneName = ShellStrings.PaneDocument, Content = DemoDocument.Create(out var input) };
        CommentsPane = new Pane { Title = ShellStrings.PaneComments, Width = 280, Content = CreateComments() };
        InputTarget = input;
        FieldCodeBadge.SetCode(NavigationPane, "6");
        FieldCodeBadge.SetCode(CommentsPane, "7");

        Status = new StatusBar
        {
            SaveState = GalleryStrings.StatusSaved,
            Position = GalleryStrings.StatusPosition,
            Count = GalleryStrings.StatusCount,
            ZoomText = GalleryStrings.StatusZoom,
        };
        Status.ZoomChanged += (_, zoom) => Status.ZoomText = string.Create(CultureInfo.InvariantCulture, $"{zoom:0}%");

        _start = new StartView { Tiles = StartTile.Standard(), Recent = CreateRecent(), OwnApp = StartApp.Write };
        _start.NewRequested += app => Notify(app == StartApp.Pdf ? GalleryStrings.RequestedOpenPdf : GalleryStrings.RequestedNew(app.ToString()));
        _start.BrowseRequested += () => Notify(GalleryStrings.RequestedBrowse);
        _start.OpenRequested += file => Notify(GalleryStrings.RequestedOpen(file.Name));

        Frame.TitleBar = titleBar;
        Host = new DialogHost { Content = Frame };
        Content = Host;
        ShowDocument();
        SetCompact(_settings.CompactCommandBar);

        // Window-wide keys: Ctrl+K, F6 / Shift+F6 and the Alt field codes.
        FrameKeys.Attach(this, TitleSearch);
        var paneFocus = new PaneFocus(this, [NavigationPane, DocumentPane, CommentsPane], Status);
        FieldCodeTarget[] targets =
        [
            new("Q", TitleSearch.FocusAndSelect),
            new("6", () => paneFocus.Focus(NavigationPane)),
            new("7", () => paneFocus.Focus(CommentsPane)),
        ];
        _fieldCodes = FieldCodeInput.Attach(this, new FieldCodeSession(definition), Ribbon, Status, targets);
        Closed += (_, _) => _fieldCodes.Dispose();
    }

    public AppFrame Frame { get; }

    public DialogHost Host { get; }

    public RibbonView Ribbon { get; }

    public DocumentTabs Tabs { get; }

    public SearchField TitleSearch { get; }

    public Button AppMarkButton { get; }

    public StatusBar Status { get; }

    public Pane NavigationPane { get; }

    public Pane DocumentPane { get; }

    public Pane CommentsPane { get; }

    /// <summary>The text box on the page that the input measurement types into.</summary>
    public TextBox InputTarget { get; }

    /// <summary>The recent files of the start view; their dates carry the local offset, so they read as local dates.</summary>
    internal static IReadOnlyList<RecentFile> CreateRecent()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new RecentFile(GalleryStrings.RecentName1, GalleryStrings.RecentFolder1, now.AddDays(-1)),
            new RecentFile(GalleryStrings.RecentName2, GalleryStrings.RecentFolder2, now.AddDays(-4)),
            new RecentFile(GalleryStrings.RecentName3, GalleryStrings.RecentFolder3, now.AddDays(-12)),
            new RecentFile(GalleryStrings.RecentName4, GalleryStrings.RecentFolder4, now.AddDays(-40)),
        ];
    }

    /// <summary>The one place that sets the compact command bar: the frame (band height) and the ribbon (its content) follow together.</summary>
    internal void SetCompact(bool compact)
    {
        Frame.IsCompact = compact;
        Ribbon.IsCompact = compact;
    }

    /// <summary>Shows the settings dialog (from the menu of the app mark).</summary>
    internal void OpenSettings()
    {
        if (!Host.IsOpen)
        {
            Host.Show(SettingsDialog.Create(_settings, OnSettingsOk, Host.Close));
        }
    }

    /// <summary>Shows a one-sentence notification under the ribbon.</summary>
    internal void Notify(string message) =>
        Frame.Notification = new NotificationBar { Message = message };

    private static ListBox CreateOutline()
    {
        var outline = new ListBox
        {
            ItemsSource = new[] { GalleryStrings.Outline1, GalleryStrings.Outline2, GalleryStrings.Outline3, GalleryStrings.Outline4 },
            SelectedIndex = 0,
        };
        outline.Classes.Add("outline");
        AutomationProperties.SetName(outline, GalleryStrings.OutlineName);
        return outline;
    }

    private StackPanel CreateComments()
    {
        static Border Comment(string author, string text)
        {
            var name = new TextBlock { Text = author };
            name.Classes.Add("comment-author");
            var body = new TextBlock { Text = text };
            body.Classes.Add("comment-text");
            var card = new Border { Child = new StackPanel { Spacing = 2, Children = { name, body } } };
            card.Classes.Add("comment");
            return card;
        }

        // The button gives F6 something to focus in this pane.
        var add = new Button { Content = GalleryStrings.CmdNewComment, HorizontalAlignment = HorizontalAlignment.Left };
        add.Classes.Add("boxed");
        add.Click += (_, _) => Notify(GalleryStrings.RanCommand(GalleryStrings.CmdNewComment));

        return new StackPanel
        {
            Children =
            {
                Comment(GalleryStrings.CommentAuthor1, GalleryStrings.Comment1),
                Comment(GalleryStrings.CommentAuthor2, GalleryStrings.Comment2),
                add,
            },
        };
    }

    private void OnSettingsOk(ShellSettings settings)
    {
        Host.Close();
        _settings = settings;

        // Theme and compact bar apply now; the language is only stored and applies at the next start; autosave is only stored.
        ShellTheme.Apply(Application.Current!, settings.Theme, _options.Ink, _forceHighContrast);
        SetCompact(settings.CompactCommandBar);
        _store?.TrySave(settings);
    }

    private Button CreateAppMark()
    {
        var item = new MenuItem { Header = ShellStrings.SettingsTitle };
        item.Click += (_, _) => OpenSettings();
        var mark = new Border { Child = new TextBlock { Text = "R" }, IsHitTestVisible = false };
        mark.Classes.Add("app-mark");
        var button = new Button { Content = mark, Flyout = new MenuFlyout { Items = { item } }, VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("icon");
        button.Classes.Add("app-mark-button");
        AutomationProperties.SetName(button, GalleryStrings.AppMenuName);
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        return button;
    }

    private void OnTabCloseRequested(DocumentTab tab)
    {
        Tabs.Items.Remove(tab);
        if (Tabs.Items.Count == 0)
        {
            ShowStart(keepRibbon: false);
        }
    }

    private void OnTabNewRequested()
    {
        var tab = new DocumentTab(GalleryStrings.DocTabNew(++_untitled));
        Tabs.Items.Add(tab);
        Tabs.Selected = tab;
        ShowDocument();
    }

    /// <summary>The start view: no panes, no status bar; with <paramref name="keepRibbon"/> the tab row and the band stay (File tab).</summary>
    private void ShowStart(bool keepRibbon)
    {
        _startShown = true;
        NavigationPane.IsOpen = false;
        CommentsPane.IsOpen = false;
        DocumentPane.IsOpen = false;
        Frame.LeftPane = null;
        Frame.RightPane = null;
        Frame.StatusBar = null;
        Frame.Document = _start;
        if (!keepRibbon)
        {
            Frame.TabRow = null;
            Frame.Band = null;
        }
    }

    /// <summary>The document view with every frame part.</summary>
    private void ShowDocument()
    {
        _startShown = false;
        if (Ribbon.ActiveTab?.Letter == DemoRibbon.FileLetter)
        {
            Ribbon.ActiveTab = _homeTab;
        }

        Frame.TabRow = Ribbon.TabRow;
        Frame.Band = Ribbon.Band;
        Frame.LeftPane = NavigationPane;
        Frame.RightPane = CommentsPane;
        Frame.Document = DocumentPane;
        Frame.StatusBar = Status;
        NavigationPane.IsOpen = true;
        CommentsPane.IsOpen = true;
        DocumentPane.IsOpen = true;
    }
}
