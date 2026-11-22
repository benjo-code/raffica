using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;

namespace Raffica.Write.Panes;

/// <summary>
/// The outline pane of one document tab: the headings of the document as an indented list. It is made when the user asks for the outline and kept until the tab closes.
/// The headings are first read from the whole model (the pane shows "reading" until the full open has replaced the prefix model, and a document whose full open failed
/// is read from what the tab has). Every read runs on the UI thread, where the model is edited (the read of the thesis is a few milliseconds; a pool thread would race the
/// edits, which change the model only on the UI thread under the edit lease). After an edit the list is read again once the edits pause (<see cref="RefreshDelay"/>, a
/// timer that is made at the first edit after the pane exists); the list is rebuilt only when a heading, its level or its text changed, and the selected heading stays
/// selected (by its paragraph). Up, Down, Home and End move through the list (the list's own keys), Enter and a click raise <see cref="Navigate"/> with the body block index
/// the heading has at that moment (found from its paragraph, not from the index of the last read, which an edit shifts); a heading whose paragraph is gone raises nothing.
/// Every row has a name for assistive technology that says the level.
/// </summary>
internal sealed partial class OutlinePane : UserControl, IDisposable
{
    private const double IndentPerLevel = 14;

    private readonly WriteTab _tab;
    private readonly PageView _view;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private readonly Grid _body;
    private List<OutlineRow> _rows = [];
    private bool _restoreFocus;
    private DispatcherTimer? _refresh;
    private bool _readStarted;
    private bool _disposed;

    /// <summary>Makes the pane of a tab and starts waiting for the whole model (a model that is there already is read at once).</summary>
    public OutlinePane(WriteTab tab, PageView view)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        _tab = tab;
        _view = view;

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<OutlineRow>((row, _) => row is null ? null : CreateRow(row)),
            IsVisible = false,
        };
        _list.Classes.Add("outline-list");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, WriteStrings.PaneOutline);
        _list.ContainerPrepared += (_, e) => Decorate(e.Container, e.Index);
        _list.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.PointerCaptureLost += OnPointerCaptureLost;
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _list.SelectionChanged += (_, _) => RefreshButtons();
        _list.GotFocus += OnListFocused;
        view.GotFocus += OnViewFocused;
        DetachedFromVisualTree += (_, _) => CancelDrag();

        _hint = new TextBlock
        {
            Text = WriteStrings.OutlineLoading,
            TextWrapping = TextWrapping.Wrap,
            Focusable = true,
            IsTabStop = true,
            Margin = new Thickness(8),
        };
        _hint.Classes.Add("outline-hint");
        AutomationProperties.SetName(_hint, WriteStrings.OutlineLoading);

        _body = new Grid();
        _body.Children.Add(_list);
        _body.Children.Add(_hint);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(CreateHeader());
        Grid.SetRow(_body, 1);
        root.Children.Add(_body);
        Content = root;

        view.Runner.Changed += OnRunnerChanged;
        tab.ModelEdited += OnModelEdited;
        tab.Disposing += Dispose;
        view.Runner.Start();
        OnRunnerChanged();
    }

    /// <summary>Raised when the user chooses a heading (Enter or a click): the body block index of its paragraph.</summary>
    public event Action<int>? Navigate;

    /// <summary>The list of headings (for tests).</summary>
    internal ListBox List => _list;

    /// <summary>The text shown instead of the list while the headings are read or when there are none (for tests).</summary>
    internal TextBlock Hint => _hint;

    /// <summary>The headings, once read (empty before).</summary>
    internal IReadOnlyList<OutlineEntry> Entries => [.. _rows.Select(r => r.Entry)];

    /// <summary>How many times the model was read (the first read, then once per pause in the edits).</summary>
    internal int ReadCount { get; private set; }

    /// <summary>How long the edits must pause before the list is read again (tests shorten it).</summary>
    internal TimeSpan RefreshDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The read of the headings; the UI thread calls it (a test replaces it to see that).</summary>
    internal Func<WordDocument, IReadOnlyList<OutlineHeading>> Reader { get; set; } = OutlineSource.ReadHeadings;

    /// <summary>Whether the refresh timer exists: it is made at the first edit after the pane was made, not before (for tests).</summary>
    internal bool RefreshTimerCreated => _refresh is not null;

    /// <summary>Completes when the headings were read and the list is built (also when the pane was disposed first).</summary>
    internal Task LoadTask => _loaded.Task;

    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Whether the user is working in this pane: its list took the keyboard focus and the page view has not taken it back since. While it is, the outline commands act on <see cref="SelectedParagraph"/>
    /// and not on the heading at the caret. A click on a ribbon button changes neither.
    /// </summary>
    internal bool Engaged { get; private set; }

    /// <summary>The paragraph of the selected row, or null when no row is selected.</summary>
    internal Paragraph? SelectedParagraph => _list.SelectedItem is OutlineRow row ? row.Paragraph : null;

    /// <summary>Puts the keyboard focus on the list (its selected row, or the first), or on the hint while there is no list.</summary>
    public void FocusContent()
    {
        if (!_list.IsVisible)
        {
            _hint.Focus();
            return;
        }

        if (_list.SelectedIndex < 0)
        {
            _list.SelectedIndex = 0;
        }

        _list.ScrollIntoView(_list.SelectedIndex);
        if (_list.ContainerFromIndex(_list.SelectedIndex) is null)
        {
            _list.UpdateLayout();
        }

        (_list.ContainerFromIndex(_list.SelectedIndex) ?? _list).Focus(NavigationMethod.Tab);
    }

    /// <summary>Raises <see cref="Navigate"/> for the row (a no-op for a row that does not exist).</summary>
    internal void Choose(int row)
    {
        if (row < 0 || row >= _rows.Count || _disposed)
        {
            return;
        }

        // The index of the last read is stale after an edit: the heading's paragraph says where the heading is now.
        var index = TextUnits.BlocksOf(_tab.Docx.Model, StoryRef.Body) is { } blocks ? TextUnits.IndexUnder(blocks, _rows[row].Paragraph) : -1;
        if (index >= 0)
        {
            Navigate?.Invoke(index);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CancelDrag();
        _disposed = true;
        _view.Runner.Changed -= OnRunnerChanged;
        _list.GotFocus -= OnListFocused;
        _view.GotFocus -= OnViewFocused;
        _tab.ModelEdited -= OnModelEdited;
        _tab.Disposing -= Dispose;
        _refresh?.Stop();
        _refresh = null;
        _loaded.TrySetResult();
    }

    // The headings come from the whole model: wait for the full open (or for its failure, then the model the tab has).
    private void OnRunnerChanged()
    {
        if (!_readStarted && !_disposed && (_view.Runner.FullModelReady || _view.Runner.IsFaulted))
        {
            _readStarted = true;
            LoadAsync();
        }
    }

    // The first read: the whole model is there (or its open failed and the tab's model is all there is).
    private void LoadAsync()
    {
        Reload();
        _loaded.TrySetResult();
    }

    // An edit, undo, redo or replay changed the model: read again when the edits pause. The timer is made here, at the first edit, and restarted by every later one.
    private void OnModelEdited()
    {
        if (!_readStarted || _disposed)
        {
            return;
        }

        if (_refresh is null)
        {
            _refresh = new DispatcherTimer(DispatcherPriority.Background);
            _refresh.Tick += (_, _) =>
            {
                _refresh?.Stop();
                if (!_disposed)
                {
                    Reload();
                }
            };
        }

        _refresh.Stop();
        _refresh.Interval = RefreshDelay;
        _refresh.Start();
    }

    private void Reload()
    {
        IReadOnlyList<OutlineHeading> headings;
        ReadCount++;
        try
        {
            headings = Reader(_tab.Docx.Model);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A model the reader cannot take ends as "no headings" the first time and leaves the list as it was later.
            if (ReadCount > 1)
            {
                return;
            }

            headings = [];
        }

        if (_disposed || (ReadCount > 1 && SameHeadings(headings)))
        {
            return;
        }

        // The rows are replaced: a drag over the old ones ends, and a list that had the keyboard takes it again on the rebuilt row (the rebuild drops the focused container).
        CancelDrag();
        var restoreFocus = _restoreFocus || _list.IsKeyboardFocusWithin;
        _restoreFocus = false;
        var selected = _list.SelectedItem is OutlineRow row ? row.Paragraph : null;
        var scroll = _list.Scroll as ScrollViewer;
        var offset = scroll?.Offset ?? default;
        _rows = [.. headings.Select(h => new OutlineRow(h.Entry, h.Paragraph))];
        _list.ItemsSource = _rows;
        _list.IsVisible = _rows.Count > 0;
        _hint.IsVisible = _rows.Count == 0;
        _hint.Text = WriteStrings.OutlineEmpty;
        AutomationProperties.SetName(_hint, WriteStrings.OutlineEmpty);
        if (selected is not null)
        {
            _list.SelectedIndex = _rows.FindIndex(r => ReferenceEquals(r.Paragraph, selected));
        }

        if (scroll is not null && offset != default)
        {
            Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Loaded);
        }

        RefreshButtons();
        if (restoreFocus && _rows.Count > 0)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!_disposed && _list.IsVisible && _list.SelectedIndex >= 0)
                    {
                        FocusContent();
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    // True when the new read lists the same headings (same paragraphs, levels and texts) as the list shows.
    private bool SameHeadings(IReadOnlyList<OutlineHeading> headings)
    {
        if (headings.Count != _rows.Count)
        {
            return false;
        }

        for (var i = 0; i < headings.Count; i++)
        {
            var old = _rows[i];
            var next = headings[i];
            if (!ReferenceEquals(old.Paragraph, next.Paragraph) || old.Entry.Level != next.Entry.Level || old.Entry.Text != next.Entry.Text)
            {
                return false;
            }

            // The block index may have moved without a change of heading: keep the entry current.
            if (old.Entry.BodyBlockIndex != next.Entry.BodyBlockIndex)
            {
                _rows[i] = new OutlineRow(next.Entry, next.Paragraph);
            }
        }

        return true;
    }

    private static TextBlock CreateRow(OutlineRow row)
    {
        var text = new TextBlock
        {
            Text = row.Entry.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness((row.Entry.Level - 1) * IndentPerLevel, 0, 0, 0),
            FontSize = 13,
        };
        text.Classes.Add("outline-title");
        text.Classes.Add("level" + Math.Min(row.Entry.Level, 3));
        return text;
    }

    private void Decorate(Control container, int index)
    {
        if (index >= 0 && index < _rows.Count)
        {
            var entry = _rows[index].Entry;
            AutomationProperties.SetName(container, WriteStrings.Fill(WriteStrings.OutlineEntryNameFormat, entry.Text, entry.Level));
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressRow = -1;
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        if (source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { } item && _list.IndexFromContainer(item) is >= 0 and var index)
        {
            // A press may turn into a drag of this row once the pointer has moved far enough.
            _pressRow = index;
            _pressPoint = e.GetPosition(_list);
            Choose(index);
        }
    }

    private void OnListFocused(object? sender, RoutedEventArgs e) => Engaged = true;

    private void OnViewFocused(object? sender, RoutedEventArgs e) => Engaged = false;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // The outline keys (Alt and Shift with an arrow) are tunnelled: the list would take the arrows for its own selection before the window's handler saw them. Every other key stays the list's.
        if (Actions is { } actions && !_dragging && EditingKeys.StructureCommandFor(e.Key, e.KeyModifiers, OperatingSystem.IsMacOS()) is not null && actions.Key(e))
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _list.SelectedIndex >= 0)
        {
            e.Handled = true;
            Choose(_list.SelectedIndex);
        }
    }

    /// <summary>One heading of the list.</summary>
    internal sealed class OutlineRow(OutlineEntry entry, Paragraph paragraph)
    {
        public OutlineEntry Entry { get; } = entry;

        /// <summary>The heading's paragraph: its identity survives an edit elsewhere, its block index does not.</summary>
        public Paragraph Paragraph { get; } = paragraph;
    }
}
