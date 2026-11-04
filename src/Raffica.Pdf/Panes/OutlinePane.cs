using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Panes;

/// <summary>
/// The outline pane of one document tab: the bookmarks as a tree. The tree is kept as a flat list of the rows that are visible (an
/// entry, its depth, whether it is open) in a virtualised list, so a very large outline costs only the rows on screen and opening or
/// closing an entry inserts or removes one block of rows. Up and Down move through the rows, Right opens an entry (or goes to its first
/// child), Left closes it (or goes to its parent), Enter and a click go to the page of an entry. An entry without a page, or whose page the
/// document does not have, does not jump and says so to assistive technology.
/// </summary>
/// <remarks>
/// The outline is read once, after the page view drew its first sharp page; a read that fails shows the same hint as a document without
/// bookmarks. The rows are built iteratively and never follow an entry back into one of its own ancestors, so a cyclic structure (Core
/// cuts the cycles of a file, but this class does not rely on it) ends after one round; the number of rows is limited to what Core
/// returns at most.
/// </remarks>
internal sealed class OutlinePane : UserControl, IDisposable
{
    // The most rows the list ever holds (the most bookmarks Core returns).
    private const int MaxRows = PdfDocument.MaxOutlineItems;

    // Whatever a title holds, only this much is drawn.
    private const int MaxTitleLength = 500;
    private const double IndentPerLevel = 16;

    private readonly PageView _view;
    private readonly Func<CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>> _read;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AvaloniaList<OutlineRow> _rows = [];
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private bool _readStarted;
    private bool _disposed;

    /// <summary>Makes the pane of a tab: the outline comes from the document of its session.</summary>
    public OutlinePane(PdfTab tab, PageView view)
        : this(view, ReaderOf(tab))
    {
    }

    /// <summary>Makes a pane that reads its outline with <paramref name="read"/> (for tests: an outline no file has).</summary>
    internal OutlinePane(PageView view, Func<CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>> read)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(read);
        _view = view;
        _read = read;

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _rows,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<OutlineRow>((row, _) => row is null ? null : new OutlineRowView(row, IsJumpable(row))),
            IsVisible = false,
        };
        _list.Classes.Add("nav-list");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, PdfStrings.PaneOutline);
        _list.ContainerPrepared += (_, e) => Decorate(e.Container, e.Index);
        _list.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        _hint = new TextBlock
        {
            Text = PdfStrings.OutlineEmpty,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false,
            Focusable = true,
            IsTabStop = true,
            Margin = new Thickness(4),
        };
        _hint.Classes.Add("nav-hint");
        AutomationProperties.SetName(_hint, PdfStrings.OutlineEmpty);

        var grid = new Grid();
        grid.Children.Add(_list);
        grid.Children.Add(_hint);
        Content = grid;

        view.FirstSharpPage += OnFirstSharpPage;
        if (view.FirstSharpPageShown)
        {
            StartReading();
        }
    }

    /// <summary>The list of entries (for tests).</summary>
    internal ListBox List => _list;

    /// <summary>The hint shown when there is no outline (for tests).</summary>
    internal TextBlock Hint => _hint;

    /// <summary>Whether the hint "no outline" is shown instead of the list.</summary>
    internal bool IsEmptyHintShown => _hint.IsVisible;

    /// <summary>The number of rows that are visible in the tree (open entries count their children).</summary>
    internal int RowCount => _rows.Count;

    /// <summary>How many list items exist at the moment (the realised ones; for tests of the virtualisation).</summary>
    internal int RealizedCount => _list.GetRealizedContainers().Count();

    /// <summary>How many times the outline was read (once per pane at most).</summary>
    internal int ReadCount { get; private set; }

    /// <summary>Completes when the outline was read and the rows were built (also when the read failed or found nothing).</summary>
    internal Task LoadTask => _loaded.Task;

    /// <summary>Whether <see cref="Dispose"/> has run.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>The title of a row (for tests).</summary>
    internal string RowTitle(int row) => _rows[row].Title;

    /// <summary>The list item of a row, or <see langword="null"/> when it is not realised.</summary>
    internal ListBoxItem? ContainerOf(int row) => _list.ContainerFromIndex(row) as ListBoxItem;

    /// <summary>Whether Enter or a click on the row goes to a page.</summary>
    internal bool CanJump(int row) => row >= 0 && row < _rows.Count && IsJumpable(_rows[row]);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.FirstSharpPage -= OnFirstSharpPage;
        _cancel.Cancel();
        _cancel.Dispose();
        _loaded.TrySetResult();
    }

    private static Func<CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>> ReaderOf(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return token => tab.Session.Document is { } document
            ? document.GetOutlineAsync(token)
            : Task.FromResult<IReadOnlyList<PdfOutlineItem>>([]);
    }

    private bool IsJumpable(OutlineRow row) => row.Item.PageIndex is { } page && page >= 0 && page < _view.PageCount;

    private void OnFirstSharpPage() => StartReading();

    // The outline is read once, after the first sharp page; the work is heavy on a large file.
    private void StartReading()
    {
        if (_readStarted || _disposed)
        {
            return;
        }

        _readStarted = true;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IReadOnlyList<PdfOutlineItem> items;
        ReadCount++;
        try
        {
            items = await _read(_cancel.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A bad file, a cancelled read or a failing reader all end in the hint, never in an exception.
            items = [];
        }

        if (!_disposed)
        {
            _rows.AddRange(Visible(items, parent: null, depth: 0));
            _hint.IsVisible = _rows.Count == 0;
            _list.IsVisible = _rows.Count > 0;
        }

        _loaded.TrySetResult();
    }

    // The rows of a list of entries as they are shown: an open entry is followed by its children. Iterative, with the entries on the way
    // down known, so that an entry that is its own ancestor is left out.
    private static List<OutlineRow> Visible(IReadOnlyList<PdfOutlineItem> items, OutlineRow? parent, int depth)
    {
        var rows = new List<OutlineRow>();
        var stack = new Stack<(IReadOnlyList<PdfOutlineItem> Items, int Next, OutlineRow? Parent, int Depth)>();
        stack.Push((items, 0, parent, depth));
        while (stack.Count > 0 && rows.Count < MaxRows)
        {
            var (list, next, owner, level) = stack.Pop();
            while (next < list.Count && rows.Count < MaxRows)
            {
                var item = list[next++];
                if (item is null || owner?.IsSelfOrAncestor(item) == true)
                {
                    continue;
                }

                var row = new OutlineRow(item, owner, level) { Expanded = item.IsOpen && level + 1 < PdfDocument.MaxOutlineDepth };
                rows.Add(row);
                if (row.Expanded && item.Children.Count > 0)
                {
                    // Continue with this level after the children of the entry have been listed.
                    stack.Push((list, next, owner, level));
                    stack.Push((item.Children, 0, row, level + 1));
                    break;
                }
            }
        }

        return rows;
    }

    private void Expand(int index)
    {
        var row = _rows[index];
        if (row.Item.Children.Count == 0 || HasVisibleChildren(index) || row.Depth + 1 >= PdfDocument.MaxOutlineDepth)
        {
            return;
        }

        row.Expanded = true;
        var children = Visible(row.Item.Children, row, row.Depth + 1);
        var room = MaxRows - _rows.Count;
        if (children.Count > room)
        {
            children.RemoveRange(room, children.Count - room);
        }

        if (children.Count > 0)
        {
            _rows.InsertRange(index + 1, children);
        }

        Refresh(index);
    }

    private void Collapse(int index)
    {
        var row = _rows[index];
        row.Expanded = false;
        var end = index + 1;
        while (end < _rows.Count && _rows[end].Depth > row.Depth)
        {
            end++;
        }

        if (end > index + 1)
        {
            _rows.RemoveRange(index + 1, end - index - 1);
        }

        Refresh(index);
    }

    private bool HasVisibleChildren(int index) => index + 1 < _rows.Count && _rows[index + 1].Depth > _rows[index].Depth;

    // Redraws the name and the arrow of a row whose state changed.
    private void Refresh(int index)
    {
        if (_list.ContainerFromIndex(index) is { } container)
        {
            Decorate(container, index);
        }
    }

    private void Decorate(Control container, int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            return;
        }

        var row = _rows[index];
        AutomationProperties.SetName(container, PdfStrings.OutlineEntryName(row.Item.Title, row.Item.Children.Count > 0, row.Expanded, IsJumpable(row)));
        if (container.GetVisualDescendants().OfType<OutlineRowView>().FirstOrDefault() is { } view)
        {
            view.Update();
        }
    }

    private void Jump(int index)
    {
        if (index >= 0 && index < _rows.Count && IsJumpable(_rows[index]))
        {
            _view.GoToPage(_rows[index].Item.PageIndex!.Value);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        var ancestors = source.GetSelfAndVisualAncestors().ToList();
        if (ancestors.OfType<ListBoxItem>().FirstOrDefault() is not { } item || _list.IndexFromContainer(item) is not (>= 0 and var index))
        {
            return;
        }

        if (ancestors.OfType<OutlineRowView>().FirstOrDefault() is { } view && ancestors.Contains(view.Arrow) && _rows[index].Item.Children.Count > 0)
        {
            Toggle(index);
            return;
        }

        Jump(index);
    }

    private void Toggle(int index)
    {
        if (_rows[index].Expanded && HasVisibleChildren(index))
        {
            Collapse(index);
        }
        else
        {
            Expand(index);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || _list.SelectedIndex is not (>= 0 and var index) || index >= _rows.Count)
        {
            return;
        }

        var row = _rows[index];
        switch (e.Key)
        {
            case Key.Right:
                e.Handled = true;
                if (row.Item.Children.Count == 0)
                {
                    return;
                }

                if (row.Expanded && HasVisibleChildren(index))
                {
                    _list.SelectedIndex = index + 1;
                }
                else
                {
                    Expand(index);
                }

                break;
            case Key.Left:
                e.Handled = true;
                if (row.Expanded && HasVisibleChildren(index))
                {
                    Collapse(index);
                }
                else if (row.Parent is { } parent)
                {
                    var at = index - 1;
                    while (at >= 0 && !ReferenceEquals(_rows[at], parent))
                    {
                        at--;
                    }

                    if (at >= 0)
                    {
                        _list.SelectedIndex = at;
                    }
                }

                break;
            case Key.Enter:
                e.Handled = true;
                Jump(index);
                break;
        }
    }

    /// <summary>One visible entry of the tree.</summary>
    internal sealed class OutlineRow(PdfOutlineItem item, OutlineRow? parent, int depth)
    {
        public PdfOutlineItem Item { get; } = item;

        public OutlineRow? Parent { get; } = parent;

        public int Depth { get; } = depth;

        public bool Expanded { get; set; }

        public string Title => Item.Title is { Length: > MaxTitleLength } text ? text[..MaxTitleLength] : Item.Title ?? string.Empty;

        // Whether the entry is the given one or one of the entries above it (by identity: an outline is a tree of records).
        public bool IsSelfOrAncestor(PdfOutlineItem other)
        {
            for (var row = this; row is not null; row = row.Parent)
            {
                if (ReferenceEquals(row.Item, other))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The look of a row: the indent, the arrow of an entry with children, the title.</summary>
    private sealed class OutlineRowView : StackPanel
    {
        private readonly OutlineRow _row;
        private readonly StrokeIcon _icon = new() { Size = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        public OutlineRowView(OutlineRow row, bool jumpable)
        {
            _row = row;
            Orientation = Orientation.Horizontal;
            Margin = new Thickness(row.Depth * IndentPerLevel, 0, 0, 0);
            Arrow = new Border { Width = 20, Height = 20, Background = Avalonia.Media.Brushes.Transparent, Child = _icon, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(Arrow);
            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(row.Title) ? PdfStrings.OutlineUntitled : row.Title,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
            };
            title.Classes.Add("outline-title");
            if (!jumpable)
            {
                title.Classes.Add("no-page");
            }

            Children.Add(title);
            Update();
        }

        /// <summary>The hit area of the arrow.</summary>
        public Border Arrow { get; }

        /// <summary>The arrow follows the state of the entry.</summary>
        public void Update()
        {
            _icon.IsVisible = _row.Item.Children.Count > 0;
            var key = _row.Expanded ? "Raffica.Icon.ChevronDown" : "Raffica.Icon.ChevronRight";
            _icon.Bind(StrokeIcon.DataProperty, _icon.GetResourceObservable(key, value => value as Avalonia.Media.Geometry));
        }
    }
}
