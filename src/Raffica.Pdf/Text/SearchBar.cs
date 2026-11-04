using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Text;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Text;

/// <summary>
/// The search bar of the document (Ctrl+F), shown in the notification slot of the frame: a field, the toggles "match case" and "whole
/// word", the count of hits, previous and next, and close. Typing starts the search of the bound <see cref="SearchSession"/> at once
/// (the run is cheap to cancel), Enter goes to the next hit, Shift+Enter to the previous one and Esc asks to close the bar. The
/// session belongs to a tab; the bar shows the query, the options and the count of whichever session is bound.
/// </summary>
internal sealed class SearchBar : Border
{
    private readonly TextBox _input;
    private readonly ToggleButton _matchCase;
    private readonly ToggleButton _wholeWord;
    private readonly TextBlock _summary;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _close;
    private SearchSession? _session;
    private bool _binding;

    public SearchBar()
    {
        Classes.Add("search-bar");
        AutomationProperties.SetName(this, PdfStrings.SearchBarName);

        _input = new TextBox { Name = "PART_Query", MinWidth = 220, Width = 320, VerticalAlignment = VerticalAlignment.Center, PlaceholderText = PdfStrings.SearchBarName };
        AutomationProperties.SetName(_input, PdfStrings.SearchBarName);
        _matchCase = CreateToggle("PART_MatchCase", "Aa", PdfStrings.SearchMatchCase);
        _wholeWord = CreateToggle("PART_WholeWord", "ab", PdfStrings.SearchWholeWord);
        _summary = new TextBlock { Name = "PART_Summary", VerticalAlignment = VerticalAlignment.Center, MinWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis };
        _summary.Classes.Add("search-summary");
        AutomationProperties.SetName(_summary, PdfStrings.SearchBarName);
        AutomationProperties.SetLiveSetting(_summary, AutomationLiveSetting.Polite);
        _previous = CreateIconButton("PART_Previous", "Raffica.Icon.ChevronDown", PdfStrings.SearchPreviousHit, rotate: true);
        _next = CreateIconButton("PART_Next", "Raffica.Icon.ChevronDown", PdfStrings.SearchNextHit, rotate: false);
        _close = CreateIconButton("PART_Close", "Raffica.Icon.X", PdfStrings.SearchCloseBar, rotate: false);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_input);
        row.Children.Add(_matchCase);
        row.Children.Add(_wholeWord);
        row.Children.Add(_summary);
        row.Children.Add(_previous);
        row.Children.Add(_next);
        row.Children.Add(_close);
        Child = row;

        _input.TextChanged += (_, _) => OnQueryChanged();
        _matchCase.IsCheckedChanged += (_, _) => OnQueryChanged();
        _wholeWord.IsCheckedChanged += (_, _) => OnQueryChanged();
        _previous.Click += (_, _) => _session?.Previous();
        _next.Click += (_, _) => _session?.Next();
        _close.Click += (_, _) => CloseRequested?.Invoke();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Refresh();
    }

    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>Raised when the user asks to close the bar (Esc, the close button); the window removes it and clears the search.</summary>
    public event Action? CloseRequested;

    /// <summary>The text of the field; setting it starts a search.</summary>
    public string Query
    {
        get => _input.Text ?? string.Empty;
        set => _input.Text = value;
    }

    /// <summary>The field (for tests).</summary>
    internal TextBox Input => _input;

    /// <summary>The count text on the bar (for tests).</summary>
    internal string SummaryText => _summary.Text ?? string.Empty;

    /// <summary>The toggle for upper and lower case (for tests).</summary>
    internal ToggleButton MatchCaseToggle => _matchCase;

    /// <summary>The toggle for whole words (for tests).</summary>
    internal ToggleButton WholeWordToggle => _wholeWord;

    /// <summary>The search the bar shows and drives; the field, the toggles and the count follow it without starting a new run.</summary>
    public SearchSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(value, _session))
            {
                return;
            }

            if (_session is not null)
            {
                _session.Changed -= Refresh;
            }

            _session = value;
            _binding = true;
            try
            {
                _input.Text = value?.Query ?? string.Empty;
                _matchCase.IsChecked = value?.Options.MatchCase ?? false;
                _wholeWord.IsChecked = value?.Options.WholeWord ?? false;
            }
            finally
            {
                _binding = false;
            }

            if (value is not null)
            {
                value.Changed += Refresh;
            }

            Refresh();
        }
    }

    /// <summary>Puts the keyboard focus into the field and selects its text.</summary>
    public void FocusQuery()
    {
        _input.Focus(NavigationMethod.Tab);
        _input.SelectAll();
    }

    private SearchOptions Options => new(_matchCase.IsChecked == true, _wholeWord.IsChecked == true);

    private void OnQueryChanged()
    {
        if (_binding || _session is null)
        {
            return;
        }

        _session.Start(Query, Options);
    }

    private void Refresh()
    {
        _summary.Text = _session?.Summary ?? string.Empty;
        var hasHits = _session is { Hits.Count: > 0 };
        _previous.IsEnabled = hasHits;
        _next.IsEnabled = hasHits;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        }
        else if (e.Key == Key.Enter && e.Source == _input)
        {
            if (e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                _session?.Next();
            }
            else if (e.KeyModifiers == KeyModifiers.Shift)
            {
                e.Handled = true;
                _session?.Previous();
            }
        }
    }

    private static ToggleButton CreateToggle(string name, string text, string accessibleName)
    {
        var toggle = new ToggleButton { Name = name, Content = text, MinWidth = 32, VerticalAlignment = VerticalAlignment.Center };
        toggle.Classes.Add("boxed");
        AutomationProperties.SetName(toggle, accessibleName);
        ToolTip.SetTip(toggle, accessibleName);
        return toggle;
    }

    private static Button CreateIconButton(string name, string iconKey, string accessibleName, bool rotate)
    {
        var icon = new StrokeIcon { Size = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(StrokeIcon.DataProperty, icon.GetResourceObservable(iconKey, value => value as Geometry));
        if (rotate)
        {
            icon.RenderTransform = new RotateTransform(180);
        }

        var button = new Button { Name = name, Content = icon, VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("icon");
        AutomationProperties.SetName(button, accessibleName);
        ToolTip.SetTip(button, accessibleName);
        return button;
    }
}
