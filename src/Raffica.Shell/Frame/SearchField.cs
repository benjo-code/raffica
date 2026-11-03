using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The command search of the title bar (section 6 of the design spec): 28 px high with a magnifier and the placeholder, no keycap (the
/// shortcut is named in the tooltip). Typing lists the commands of <see cref="Source"/> whose name matches (<see cref="CommandMatcher"/>)
/// in a popup of 32 px rows, name on the left and shortcut on the right. Up and Down move the choice, Enter or a click raises
/// <see cref="CommandChosen"/>, Escape closes the list and keeps the text. Search finds commands; it is never the only path to one.
/// </summary>
public class SearchField : TemplatedControl
{
    private const int MaxVisibleRows = 8;

    public static readonly StyledProperty<RibbonDefinition?> SourceProperty = AvaloniaProperty.Register<SearchField, RibbonDefinition?>(nameof(Source));

    private readonly StackPanel _list = new();
    private readonly Border _results;
    private readonly TextBlock _noResults;
    private readonly List<SearchResultRow> _rows = [];
    private TextBox? _input;
    private Popup? _popup;
    private int _active;
    private bool _clearing;

    public SearchField()
    {
        _noResults = new TextBlock { Text = ShellStrings.SearchNoResults, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        _noResults.Classes.Add("search-empty");
        _results = new Border { Name = "PART_Results", Child = new ScrollViewer { Name = "PART_ResultsScroll", MaxHeight = MaxVisibleRows * 32, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new StackPanel { Children = { _list, _noResults } } } };
        _results.Classes.Add("search-results");
        ToolTip.SetTip(this, CreateTooltip());
        WindowDecorationProperties.SetElementRole(this, WindowDecorationsElementRole.User);
        FieldCodeBadge.SetCode(this, "Q");
    }

    /// <summary>Raised when the user picks a command from the list; the app runs it.</summary>
    public event Action<CommandDefinition>? CommandChosen;

    /// <summary>The ribbon whose commands are searched.</summary>
    public RibbonDefinition? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>The tooltip: "Search (Ctrl+K)" / "Suchen (Strg+K)", the shortcut written by <see cref="KeyNames"/> (Cmd+K on macOS).</summary>
    internal static TooltipContent CreateTooltip() => new() { Title = ShellStrings.SearchTooltip(KeyNames.Format(OperatingSystem.IsMacOS() ? "Meta+K" : "Ctrl+K", ShellStrings.Culture)) };

    /// <summary>Puts the keyboard focus into the field and selects its text.</summary>
    public void FocusAndSelect()
    {
        ApplyTemplate();
        if (_input is null)
        {
            return;
        }

        _input.Focus(NavigationMethod.Tab);
        _input.SelectAll();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_input is not null)
        {
            _input.RemoveHandler(KeyDownEvent, OnInputKeyDown);
            _input.TextChanged -= OnTextChanged;
            _input.LostFocus -= OnLostFocus;
        }

        _input = e.NameScope.Find<TextBox>("PART_Input");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        if (_input is null || _popup is null)
        {
            return;
        }

        _input.PlaceholderText = ShellStrings.Search;
        AutomationProperties.SetName(_input, ShellStrings.Search);
        _input.InnerLeftContent = new Border
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = RibbonButton.CreateIcon("Raffica.Icon.Search", 16),
        };
        _input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        _input.TextChanged += OnTextChanged;
        _input.LostFocus += OnLostFocus;
        _popup.PlacementTarget = this;
        _popup.Child = _results;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_clearing)
        {
            UpdateResults();
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        // A click on a row must still reach the row, so the list stays while the pointer is over it.
        if (_popup is { IsOpen: true } && !_results.IsPointerOver)
        {
            _popup.IsOpen = false;
        }
    }

    private void UpdateResults()
    {
        if (_input is null || _popup is null)
        {
            return;
        }

        _rows.Clear();
        _list.Children.Clear();
        var query = _input.Text ?? string.Empty;
        if (Source is null || string.IsNullOrWhiteSpace(query))
        {
            _popup.IsOpen = false;
            return;
        }

        foreach (var command in CommandMatcher.Find(Source, query, ShellStrings.Culture))
        {
            var row = new SearchResultRow(command);
            var index = _rows.Count;
            row.PointerEntered += (_, _) => SetActive(index);
            row.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                {
                    Choose(command);
                    e.Handled = true;
                }
            };
            _rows.Add(row);
            _list.Children.Add(row);
        }

        _noResults.IsVisible = _rows.Count == 0;
        _active = 0;
        SetActive(0);
        _results.MinWidth = Math.Max(Bounds.Width, 200);
        _popup.IsOpen = true;
    }

    private void SetActive(int index)
    {
        _active = index;
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].IsActive = i == index;
        }

        // The list scrolls after eight rows: the active row must stay visible while the arrow keys move it.
        if (index >= 0 && index < _rows.Count)
        {
            _rows[index].BringIntoView();
        }
    }

    private void Choose(CommandDefinition command)
    {
        if (_popup is not null)
        {
            _popup.IsOpen = false;
        }

        if (_input is not null)
        {
            _clearing = true;
            try
            {
                _input.Text = string.Empty;
            }
            finally
            {
                _clearing = false;
            }
        }

        _rows.Clear();
        _list.Children.Clear();
        CommandChosen?.Invoke(command);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_popup is not { IsOpen: true })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                SetActive(Math.Min(_active + 1, _rows.Count - 1));
                e.Handled = true;
                break;
            case Key.Up:
                SetActive(Math.Max(_active - 1, 0));
                e.Handled = true;
                break;
            case Key.Enter:
                if (_rows.Count > 0)
                {
                    Choose(_rows[_active].Command);
                }

                e.Handled = true;
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }
}

/// <summary>One command in the results of the <see cref="SearchField"/>: 32 px high, the name on the left, the shortcut on the right.</summary>
internal sealed class SearchResultRow : Border
{
    public SearchResultRow(CommandDefinition command)
    {
        Command = command;
        ShortcutText = command.Shortcut;
        Classes.Add("search-row");
        AutomationProperties.SetName(this, command.Name);

        var name = new TextBlock { Text = command.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 0) };
        grid.Children.Add(name);
        if (!string.IsNullOrEmpty(command.Shortcut))
        {
            var shortcut = new TextBlock { Text = KeyNames.Format(command.Shortcut, ShellStrings.Culture), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            shortcut.Classes.Add("search-shortcut");
            Grid.SetColumn(shortcut, 1);
            grid.Children.Add(shortcut);
        }

        Child = grid;
    }

    public CommandDefinition Command { get; }

    protected override Type StyleKeyOverride => typeof(Border);

    public string? ShortcutText { get; }

    public bool IsActive
    {
        get => PseudoClasses.Contains(":active");
        set => PseudoClasses.Set(":active", value);
    }
}
