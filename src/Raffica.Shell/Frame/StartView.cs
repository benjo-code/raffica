using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The start view every app shows while no file is open (section 6 of the design spec), under the title bar and without a status
/// bar (the host collapses <see cref="AppFrame.StatusBar"/>). Left a 336 px column: "New" with the blank tiles, two per row, and
/// "Open" with the Browse button; right the "Recent" files. The view only reports what the user asked for; a tile of the own app
/// opens the blank document in place, a tile of another app starts that app, and the host decides which of the two it is.
/// </summary>
public class StartView : TemplatedControl
{
    public static readonly StyledProperty<IReadOnlyList<StartTile>> TilesProperty =
        AvaloniaProperty.Register<StartView, IReadOnlyList<StartTile>>(nameof(Tiles), []);

    public static readonly StyledProperty<IReadOnlyList<RecentFile>> RecentProperty =
        AvaloniaProperty.Register<StartView, IReadOnlyList<RecentFile>>(nameof(Recent), []);

    /// <summary>The app that shows this view; its tile is the one that opens in place.</summary>
    public static readonly StyledProperty<StartApp> OwnAppProperty =
        AvaloniaProperty.Register<StartView, StartApp>(nameof(OwnApp));

    private Grid? _tiles;
    private StackPanel? _recent;
    private TextBlock? _empty;
    private Button? _browse;

    /// <summary>Raised when a tile is clicked, with the app of the tile (the own app included).</summary>
    public event Action<StartApp>? NewRequested;

    /// <summary>Raised when a recent file is clicked.</summary>
    public event Action<RecentFile>? OpenRequested;

    /// <summary>Raised by the Browse button; the host shows the system file dialog.</summary>
    public event Action? BrowseRequested;

    public IReadOnlyList<StartTile> Tiles { get => GetValue(TilesProperty); set => SetValue(TilesProperty, value); }

    public IReadOnlyList<RecentFile> Recent { get => GetValue(RecentProperty); set => SetValue(RecentProperty, value); }

    public StartApp OwnApp { get => GetValue(OwnAppProperty); set => SetValue(OwnAppProperty, value); }

    /// <summary>Moves the keyboard focus to the first tile (for a host that just showed the view). Returns false when there is none to focus.</summary>
    public bool FocusFirstTile()
    {
        // The template parts are laid out first: a tile that is not in the window yet cannot take the focus.
        ApplyTemplate();
        UpdateLayout();
        return _tiles?.Children.OfType<Button>().FirstOrDefault()?.Focus(NavigationMethod.Tab) ?? false;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_browse is not null)
        {
            _browse.Click -= OnBrowseClick;
        }

        _tiles = e.NameScope.Find<Grid>("PART_Tiles");
        _recent = e.NameScope.Find<StackPanel>("PART_Recent");
        _empty = e.NameScope.Find<TextBlock>("PART_RecentEmpty");
        _browse = e.NameScope.Find<Button>("PART_Browse");

        SetText(e, "PART_NewHeading", ShellStrings.StartNew);
        SetText(e, "PART_OpenHeading", ShellStrings.StartOpen);
        SetText(e, "PART_RecentHeading", ShellStrings.StartRecent);
        if (_empty is not null)
        {
            _empty.Text = ShellStrings.StartRecentEmpty;
        }

        if (_browse is not null)
        {
            _browse.Content = ShellStrings.StartBrowse;
            AutomationProperties.SetName(_browse, ShellStrings.StartBrowse);
            _browse.Click += OnBrowseClick;
        }

        RebuildTiles();
        RebuildRecent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TilesProperty)
        {
            RebuildTiles();
        }
        else if (change.Property == RecentProperty)
        {
            RebuildRecent();
        }
    }

    private static void SetText(TemplateAppliedEventArgs e, string name, string text)
    {
        if (e.NameScope.Find<TextBlock>(name) is { } block)
        {
            block.Text = text;
        }
    }

    private void OnBrowseClick(object? sender, RoutedEventArgs e) => BrowseRequested?.Invoke();

    private void RebuildTiles()
    {
        if (_tiles is null)
        {
            return;
        }

        _tiles.Children.Clear();
        _tiles.RowDefinitions.Clear();
        var tiles = Tiles;
        for (var i = 0; i < tiles.Count; i++)
        {
            if (i % 2 == 0)
            {
                _tiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var button = CreateTile(tiles[i]);
            Grid.SetRow(button, i / 2);
            Grid.SetColumn(button, i % 2);
            _tiles.Children.Add(button);
        }
    }

    private Button CreateTile(StartTile tile)
    {
        // The preview area is a quiet desk-coloured box (the app draws the real blank page later); the mark sits in its corner.
        var mark = new TextBlock { Text = tile.Mark, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 6, 0, 0) };
        mark.Classes.Add("start-mark");
        mark.Classes.Add(tile.App.ToString().ToLowerInvariant());
        var preview = new Border { Child = mark };
        preview.Classes.Add("start-preview");
        var label = new TextBlock { Text = tile.Label, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0) };
        label.Classes.Add("start-tile-label");
        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(preview);
        Grid.SetRow(label, 1);
        content.Children.Add(label);

        var button = new Button { Content = content };
        button.Classes.Add("start-tile");
        AutomationProperties.SetName(button, tile.Label);
        button.Click += (_, _) => NewRequested?.Invoke(tile.App);
        return button;
    }

    private void RebuildRecent()
    {
        if (_recent is null || _empty is null)
        {
            return;
        }

        _recent.Children.Clear();
        var files = Recent;
        _empty.IsVisible = files.Count == 0;
        foreach (var file in files)
        {
            var dateText = file.DateText(ShellStrings.Culture);
            var name = new TextBlock { Text = file.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var folder = new TextBlock { Text = file.Folder, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            folder.Classes.Add("start-row-secondary");
            var date = new TextBlock { Text = dateText, VerticalAlignment = VerticalAlignment.Center };
            date.Classes.Add("start-row-secondary");
            date.Classes.Add("tnum");
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2.4*,1.7*,96"), ColumnSpacing = 12 };
            grid.Children.Add(name);
            Grid.SetColumn(folder, 1);
            grid.Children.Add(folder);
            Grid.SetColumn(date, 2);
            grid.Children.Add(date);

            var row = new Button { Content = grid };
            row.Classes.Add("start-row");
            AutomationProperties.SetName(row, file.Name);
            AutomationProperties.SetHelpText(row, file.Folder + " " + dateText);
            row.Click += (_, _) => OpenRequested?.Invoke(file);
            _recent.Children.Add(row);
        }
    }
}
