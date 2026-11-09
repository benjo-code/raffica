using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Raffica.Pdf.Forms;

/// <summary>
/// What hangs over the top middle of the page area of a tab with a form while no annotate tool is active: the notice line (what the app does
/// not do for this document, dismissible, shown once per tab), the counter of the empty required fields (a button that runs the Next required
/// field command) and the switch of the field highlight. State is told in words, never by colour alone. It follows the
/// <see cref="FormSession"/> (<see cref="FormSession.Changed"/>) and the highlight of the <see cref="FormMarkers"/>, which both the switch and
/// the ribbon command change through <see cref="FormMarkers.ApplyHighlight"/>. UI thread only; one per tab, built after the first sharp page.
/// </summary>
internal sealed class FormBadge : UserControl, IDisposable
{
    private readonly FormSession _session;
    private readonly FormMarkers _markers;
    private readonly TextBlock _notice;
    private readonly Border _noticeRow;
    private readonly Button _dismiss;
    private readonly Button _counter;
    private readonly TextBlock _counterText;
    private readonly ToggleButton _highlight;
    private readonly Func<Task> _load;
    private bool _dismissed;
    private bool _loading;
    private bool _syncing;
    private bool _disposed;

    /// <param name="session">The form state of the tab.</param>
    /// <param name="markers">The overlay, the one place that applies the highlight.</param>
    /// <param name="nextRequired">Runs the Next required field command of the window (the counter button calls it).</param>
    /// <param name="load">A test seam: reads the form info and the required list instead of the session (default: the session).</param>
    public FormBadge(FormSession session, FormMarkers markers, Action nextRequired, Func<Task>? load = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(nextRequired);
        _session = session;
        _markers = markers;
        _load = load ?? LoadAsync;

        _notice = new TextBlock { Name = "PART_Notice", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _notice.Bind(TextBlock.ForegroundProperty, _notice.GetResourceObservable("Raffica.Brush.Text.Primary"));
        _dismiss = new Button { Name = "PART_Dismiss", Content = "×", Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Top };
        var dismissName = PdfStrings.FormNoticeDismiss;
        AutomationProperties.SetName(_dismiss, dismissName);
        ToolTip.SetTip(_dismiss, dismissName);
        _dismiss.Click += (_, _) => Dismiss();
        var noticeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        noticeGrid.Children.Add(_notice);
        noticeGrid.Children.Add(_dismiss);
        Grid.SetColumn(_dismiss, 1);
        _noticeRow = new Border { Child = noticeGrid, Padding = new Thickness(0, 0, 0, 4), IsVisible = false };

        _counterText = new TextBlock { Name = "PART_CounterText" };
        _counter = new Button { Name = "PART_Counter", Content = _counterText, IsVisible = false };
        _counter.Click += (_, _) => nextRequired();

        _highlight = new ToggleButton { Name = "PART_Highlight", Content = PdfStrings.FormHighlightToggle };
        AutomationProperties.SetName(_highlight, PdfStrings.FormHighlightToggle);
        _highlight.IsCheckedChanged += (_, _) => OnHighlightChanged();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        actions.Children.Add(_counter);
        actions.Children.Add(_highlight);
        var column = new StackPanel { Orientation = Orientation.Vertical };
        column.Children.Add(_noticeRow);
        column.Children.Add(actions);

        var frame = new Border { Child = column, Padding = new Thickness(12, 6), BorderThickness = new Thickness(1), MaxWidth = 520 };
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Raffica.Brush.Surface.Band"));
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Raffica.Brush.Rule"));
        Content = frame;

        // A press on the badge (also on its padding, the gaps and the notice) is not a press on the page under it: no selection, no link, no pointer capture.
        frame.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);
        frame.AddHandler(PointerReleasedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);

        // The badge never takes the keyboard from the page: a click on a button leaves the focus on the page view, so Tab and Enter still reach the
        // form keys. The counter and the switch are out of the tab order for that reason; their keyboard path is the ribbon (Next required field,
        // Highlight fields), and they stay reachable and invokable for assistive technology. The dismiss button of the notice is the
        // exception: it can take the focus (a keyboard user could not close the notice otherwise), Esc on it closes the notice, and the notice
        // hands the focus back to the page when it closes.
        _counter.Focusable = false;
        _highlight.Focusable = false;
        _dismiss.AddHandler(KeyDownEvent, OnDismissKeyDown);

        _session.Changed += Update;
        _markers.HighlightChanged += Update;
        Update();
    }

    /// <summary>Whether the notice line is shown.</summary>
    public bool NoticeVisible => _noticeRow.IsVisible;

    /// <summary>The notice line (empty while it is not shown).</summary>
    public string NoticeText => _notice.Text ?? string.Empty;

    /// <summary>The text of the notice line (for tests).</summary>
    internal TextBlock NoticeBlock => _notice;

    /// <summary>The button that closes the notice.</summary>
    public Button DismissButton => _dismiss;

    /// <summary>Whether the counter of the empty required fields is shown (not at zero).</summary>
    public bool CounterVisible => _counter.IsVisible;

    /// <summary>The text of the counter, for example "2 required fields empty".</summary>
    public string CounterText => _counterText.Text ?? string.Empty;

    /// <summary>The counter; pressing it goes to the next required field.</summary>
    public Button CounterButton => _counter;

    /// <summary>The switch of the field highlight.</summary>
    public ToggleButton HighlightToggle => _highlight;

    /// <summary>Reads what the badge shows from the document (the info for the notice, the empty required fields for the counter).</summary>
    public async Task LoadAsync()
    {
        await _session.LoadInfoAsync();
        await _session.RefreshRequiredAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= Update;
        _markers.HighlightChanged -= Update;
    }

    private void Dismiss()
    {
        var hadFocus = _dismiss.IsFocused;
        _dismissed = true;
        Update();
        if (hadFocus)
        {
            ReturnFocusToPage();
        }
    }

    private void OnDismissKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _noticeRow.IsVisible)
        {
            e.Handled = true;
            Dismiss();
        }
    }

    // The button that had the focus leaves with the notice: the page view (the nearest focusable ancestor) gets the keyboard back.
    private void ReturnFocusToPage()
    {
        for (var visual = this.GetVisualParent(); visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is InputElement { Focusable: true, IsEffectivelyEnabled: true } input)
            {
                input.Focus();
                return;
            }
        }
    }

    private void OnHighlightChanged()
    {
        if (_syncing)
        {
            return;
        }

        // The switch asks; the session decides (a document that cannot be highlighted stays as it is), and the switch shows the outcome.
        _markers.ApplyHighlight(_highlight.IsChecked == true);
        Update();
    }

    private void Update()
    {
        if (_disposed)
        {
            return;
        }

        // A saved document replaces the old one and the session forgets what it read: read it again, once at a time.
        if (!_loading && _session.HasForm && !_session.RequiredKnown)
        {
            _loading = true;
            _ = ReloadAsync();
        }

        var lines = _session.Info is { } info ? FormNotices.LinesFor(info.Hints) : [];
        var showNotice = !_dismissed && lines.Count > 0;
        _noticeRow.IsVisible = showNotice;
        _notice.Text = showNotice ? string.Join(' ', lines) : string.Empty;

        var empty = _session.RequiredKnown ? _session.EmptyRequired.Count : 0;
        _counter.IsVisible = empty > 0;
        _counterText.Text = empty switch
        {
            0 => string.Empty,
            1 => PdfStrings.FormRequiredOne,
            _ => PdfStrings.FormRequiredManyFormat.Replace("{0}", empty.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
        };
        AutomationProperties.SetName(_counter, _counterText.Text);

        _syncing = true;
        try
        {
            _highlight.IsChecked = _session.Highlight;
        }
        finally
        {
            _syncing = false;
        }
    }

    private async Task ReloadAsync()
    {
        try
        {
            await _load();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The last net, as for the other jobs nobody awaits: the counter stays as it was, the next change of the form asks again.
        }
        finally
        {
            _loading = false;
        }
    }
}
