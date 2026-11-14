using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Write.Documents;
using Raffica.Write.Viewing;

namespace Raffica.Write.Panes;

/// <summary>
/// The outline pane of one document tab: the headings of the document as an indented list. It is made when the user asks for the outline and kept until the tab closes;
/// the headings are read once, from the whole model (the pane shows "reading" until the full open has replaced the prefix model, and a document whose full open failed
/// is read from what the tab has). The read runs on a pool thread. Up, Down, Home and End move through the list (the list's own keys), Enter and a click raise
/// <see cref="Navigate"/> with the body block index of the heading; the window answers by scrolling there. Every row has a name for assistive technology that says the level.
/// </summary>
internal sealed class OutlinePane : UserControl, IDisposable
{
    private const double IndentPerLevel = 14;

    private readonly WriteTab _tab;
    private readonly PageView _view;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private List<OutlineRow> _rows = [];
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
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

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

        var grid = new Grid();
        grid.Children.Add(_list);
        grid.Children.Add(_hint);
        Content = grid;

        view.Runner.Changed += OnRunnerChanged;
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

    /// <summary>How many times the model was read (once per pane at most).</summary>
    internal int ReadCount { get; private set; }

    /// <summary>Completes when the headings were read and the list is built (also when the pane was disposed first).</summary>
    internal Task LoadTask => _loaded.Task;

    internal bool IsDisposed => _disposed;

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
        if (row >= 0 && row < _rows.Count)
        {
            Navigate?.Invoke(_rows[row].Entry.BodyBlockIndex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.Runner.Changed -= OnRunnerChanged;
        _tab.Disposing -= Dispose;
        _cancel.Cancel();
        _cancel.Dispose();
        _loaded.TrySetResult();
    }

    // The headings come from the whole model: wait for the full open (or for its failure, then the model the tab has).
    private void OnRunnerChanged()
    {
        if (!_readStarted && !_disposed && (_view.Runner.FullModelReady || _view.Runner.IsFaulted))
        {
            _readStarted = true;
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        IReadOnlyList<OutlineEntry> entries;
        ReadCount++;
        var model = _tab.Docx.Model;
        try
        {
            var token = _cancel.Token;
            entries = await Task.Run(() => OutlineSource.Read(model), token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A cancelled read (the tab closed) or a model the reader cannot take ends as "no headings".
            entries = [];
        }

        if (!_disposed)
        {
            _rows = [.. entries.Select(e => new OutlineRow(e))];
            _list.ItemsSource = _rows;
            _list.IsVisible = _rows.Count > 0;
            _hint.IsVisible = _rows.Count == 0;
            _hint.Text = WriteStrings.OutlineEmpty;
            AutomationProperties.SetName(_hint, WriteStrings.OutlineEmpty);
        }

        _loaded.TrySetResult();
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
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        if (source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { } item && _list.IndexFromContainer(item) is >= 0 and var index)
        {
            Choose(index);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _list.SelectedIndex >= 0)
        {
            e.Handled = true;
            Choose(_list.SelectedIndex);
        }
    }

    /// <summary>One heading of the list.</summary>
    internal sealed class OutlineRow(OutlineEntry entry)
    {
        public OutlineEntry Entry { get; } = entry;
    }
}
