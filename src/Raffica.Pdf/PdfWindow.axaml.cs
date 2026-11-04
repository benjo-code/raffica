using System.Collections.Specialized;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Pdf.Commands;
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
        Tabs.CloseRequested += tab => Tabs.Items.Remove(tab);
        Tabs.Items.CollectionChanged += OnTabsChanged;

        PagesPane = new Pane { Title = PdfStrings.PanePages, Width = PagesPaneWidth };
        DocumentPane = new Pane { IsDocument = true, PaneName = ShellStrings.PaneDocument };
        CommentsPane = new Pane { Title = ShellStrings.PaneComments, Width = CommentsPaneWidth, IsOpen = false };
        FieldCodeBadge.SetCode(PagesPane, "6");
        FieldCodeBadge.SetCode(CommentsPane, "7");

        Status = new StatusBar();
        Start = new StartView { Tiles = StartTile.Standard(), Recent = [], OwnApp = StartApp.Pdf };

        Frame.TitleBar = titleBar;
        Content = new DialogHost { Content = Frame };
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
        Closed += (_, _) => _fieldCodes.Dispose();
    }

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
