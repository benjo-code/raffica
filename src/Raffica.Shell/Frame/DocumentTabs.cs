using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The document tabs of the title bar (section 6 of the design spec): a strip of <see cref="DocumentTabItem"/>s with the "+" button
/// after it. A tab is 28 px high and between 96 and 240 px wide; with many tabs they share the room down to the minimum and then the
/// strip scrolls (wheel, the arrows before and after it, or the selected tab is brought into view) while the "+" button stays visible;
/// a list button then names every tab and selects the one that is clicked. The control only reports what the user asks for
/// (<see cref="CloseRequested"/>, <see cref="NewRequested"/>); the app changes <see cref="Items"/>. The tab views are kept per item,
/// so a focused tab keeps its keyboard focus when other tabs come and go.
/// </summary>
public class DocumentTabs : TemplatedControl
{
    public static readonly StyledProperty<DocumentTab?> SelectedProperty = AvaloniaProperty.Register<DocumentTabs, DocumentTab?>(nameof(Selected));

    private readonly Dictionary<DocumentTab, DocumentTabItem> _views = [];
    private readonly TabStrip _strip = new();
    private readonly ScrollViewer _scroll;
    private readonly Button _plus;
    private readonly Button _scrollLeft;
    private readonly Button _scrollRight;
    private readonly Button _allTabs;
    private readonly TabsLayout _layout;
    private Border? _host;
    private string _newLabel = string.Empty;

    static DocumentTabs()
    {
        SelectedProperty.Changed.AddClassHandler<DocumentTabs>((tabs, _) => tabs.OnSelectedChanged());
    }

    public DocumentTabs()
    {
        Items.CollectionChanged += OnItemsChanged;

        _scroll = new ScrollViewer
        {
            Name = "PART_Scroll",
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _strip,
        };
        _scroll.PointerWheelChanged += (_, e) =>
        {
            var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            var max = Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width);
            _scroll.Offset = new Vector(Math.Clamp(_scroll.Offset.X - delta * 48, 0, max), 0);
            e.Handled = true;
        };

        _plus = new Button { Name = "PART_New", Content = RibbonButton.CreateIcon("Raffica.Icon.Plus", 16) };
        _plus.Classes.Add("document-tab-new");
        ApplyNewButtonLabel(ShellStrings.NewDocument);
        _plus.Click += (_, _) => NewRequested?.Invoke();
        WindowDecorationProperties.SetElementRole(_plus, WindowDecorationsElementRole.User);

        _scrollLeft = ScrollButton("PART_ScrollLeft", "Raffica.Icon.ChevronLeft", ShellStrings.TabsScrollLeft, () => ScrollToNext(-1));
        _scrollRight = ScrollButton("PART_ScrollRight", "Raffica.Icon.ChevronRight", ShellStrings.TabsScrollRight, () => ScrollToNext(1));
        _allTabs = ScrollButton("PART_AllTabs", "Raffica.Icon.ChevronDown", ShellStrings.TabsAll, null);
        var list = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        list.Opening += (_, _) => RebuildList(list);
        _allTabs.Flyout = list;
        _scroll.ScrollChanged += (_, _) => UpdateScrollButtons();

        _layout = new TabsLayout(_scroll, _plus, _strip, _scrollLeft, _scrollRight, _allTabs, UpdateScrollButtons);
    }

    /// <summary>Raised when the user closes a tab (close button or middle click); the app removes it from <see cref="Items"/>.</summary>
    public event Action<DocumentTab>? CloseRequested;

    /// <summary>Raised when the user presses the "+" button.</summary>
    public event Action? NewRequested;

    /// <summary>The name and tooltip of the "+" button; "New document" until an app that opens files instead sets its own.</summary>
    public string NewButtonLabel
    {
        get => _newLabel;
        set => ApplyNewButtonLabel(value);
    }

    public ObservableCollection<DocumentTab> Items { get; } = [];

    public DocumentTab? Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }

    private static Button ScrollButton(string name, string icon, string label, Action? click)
    {
        var button = new Button { Name = name, Content = RibbonButton.CreateIcon(icon, 16), IsVisible = false };
        button.Classes.Add("document-tab-scroll");
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, new TooltipContent { Title = label });
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        if (click is not null)
        {
            button.Click += (_, _) => click();
        }

        return button;
    }

    // One click brings the next tab on that side fully into view, so no tab is skipped.
    private void ScrollToNext(int direction)
    {
        var offset = _scroll.Offset.X;
        var viewport = _scroll.Viewport.Width;
        var max = Math.Max(0, _scroll.Extent.Width - viewport);
        double target;
        if (direction < 0)
        {
            var hidden = _strip.Children.LastOrDefault(c => c.Bounds.X < offset - 0.5);
            target = hidden is null ? 0 : hidden.Bounds.X;
        }
        else
        {
            var hidden = _strip.Children.FirstOrDefault(c => c.Bounds.Right > offset + viewport + 0.5);
            target = hidden is null ? max : hidden.Bounds.Right - viewport;
        }

        _scroll.Offset = new Vector(Math.Clamp(target, 0, max), 0);
        UpdateScrollButtons();
    }

    // The arrows are dim at the ends of the strip, so the user sees there is nothing more on that side.
    private void UpdateScrollButtons()
    {
        var max = Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width);
        _scrollLeft.IsEnabled = _scroll.Offset.X > 0.5;
        _scrollRight.IsEnabled = _scroll.Offset.X < max - 0.5;
    }

    // Built when it opens: the names are the titles of the open tabs now, the selected one is marked.
    private void RebuildList(MenuFlyout list)
    {
        list.Items.Clear();
        foreach (var tab in Items)
        {
            var item = new MenuItem
            {
                Header = tab.IsDirty ? tab.Title + " •" : tab.Title,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "document-tabs",
                IsChecked = ReferenceEquals(tab, Selected),
            };
            AutomationProperties.SetName(item, tab.IsDirty ? $"{tab.Title}, {ShellStrings.TabUnsavedChanges}" : tab.Title);
            var chosen = tab;
            item.Click += (_, _) =>
            {
                SetCurrentValue(SelectedProperty, chosen);
                if (_views.TryGetValue(chosen, out var view))
                {
                    Dispatcher.UIThread.Post(() => view.BringIntoView(), DispatcherPriority.Loaded);
                }
            };
            list.Items.Add(item);
        }
    }

    private void ApplyNewButtonLabel(string label)
    {
        _newLabel = label;
        AutomationProperties.SetName(_plus, label);
        ToolTip.SetTip(_plus, new TooltipContent { Title = label });
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_host is not null)
        {
            _host.Child = null;
        }

        _host = e.NameScope.Find<Border>("PART_Host");
        if (_host is not null)
        {
            _host.Child = _layout;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var selected = Selected;
        if (selected is not null && !Items.Contains(selected))
        {
            // The selected tab went away: the neighbour at the same position takes over.
            var index = e.OldStartingIndex >= 0 ? e.OldStartingIndex : 0;
            SetCurrentValue(SelectedProperty, Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)]);
        }

        Reconcile();
    }

    private void Reconcile()
    {
        foreach (var stale in _views.Keys.Where(k => !Items.Contains(k)).ToList())
        {
            _strip.Children.Remove(_views[stale]);
            _views.Remove(stale);
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (!_views.TryGetValue(Items[i], out var view))
            {
                view = new DocumentTabItem(Items[i], SelectTab, tab => CloseRequested?.Invoke(tab));
                WindowDecorationProperties.SetElementRole(view, WindowDecorationsElementRole.User);
                _views[Items[i]] = view;
            }

            if (i < _strip.Children.Count && ReferenceEquals(_strip.Children[i], view))
            {
                continue;
            }

            _strip.Children.Remove(view);
            _strip.Children.Insert(i, view);
        }

        UpdateSelection();
    }

    private void SelectTab(DocumentTab tab) => SetCurrentValue(SelectedProperty, tab);

    private void OnSelectedChanged()
    {
        UpdateSelection();
        if (Selected is { } selected && _views.TryGetValue(selected, out var view))
        {
            Dispatcher.UIThread.Post(() => view.BringIntoView(), DispatcherPriority.Loaded);
        }
    }

    private void UpdateSelection()
    {
        foreach (var (tab, view) in _views)
        {
            view.IsSelected = ReferenceEquals(tab, Selected);
        }
    }

    /// <summary>The row of tabs: each tab is as wide as its title wants, between 96 px and the cap the layout gives.</summary>
    private sealed class TabStrip : Panel
    {
        /// <summary>The widest a tab may be (240 px, or less when many tabs share the room); set by <see cref="TabsLayout"/> before measuring.</summary>
        public double Cap
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;
                    InvalidateMeasure();
                }
            }
        } = 240;

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = 0;
            double height = 0;
            foreach (var child in Children)
            {
                child.MaxWidth = Cap;
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0;
            foreach (var child in Children)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width;
            }

            return finalSize;
        }
    }

    /// <summary>
    /// The scrolling strip with the "+" button after it: the button is measured first, the strip gets what is left. When the tabs do not fit at their minimum
    /// width the left arrow comes before the strip and the right arrow and the list button after it, each as wide as the "+" button, and the strip gets the
    /// rest, so nothing else moves.
    /// </summary>
    private sealed class TabsLayout : Panel
    {
        private const double MinTab = 96;
        private const double MaxTab = 240;
        private readonly ScrollViewer _scroll;
        private readonly Button _plus;
        private readonly TabStrip _strip;
        private readonly Button _left;
        private readonly Button _right;
        private readonly Button _all;
        private readonly Action _arranged;
        private bool _overflow;

        public TabsLayout(ScrollViewer scroll, Button plus, TabStrip strip, Button left, Button right, Button all, Action arranged)
        {
            _scroll = scroll;
            _plus = plus;
            _strip = strip;
            _left = left;
            _right = right;
            _all = all;
            _arranged = arranged;
            Children.Add(left);
            Children.Add(scroll);
            Children.Add(right);
            Children.Add(all);
            Children.Add(plus);
        }

        private double ControlsWidth => _overflow ? _plus.DesiredSize.Width * 3 : 0;

        protected override Size MeasureOverride(Size availableSize)
        {
            _plus.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var room = Math.Max(0, availableSize.Width - _plus.DesiredSize.Width);
            var count = Math.Max(1, _strip.Children.Count);

            // The tabs share the room first as if there were no arrows; only when they are still wider than it (at the minimum width) the arrows come.
            _strip.Cap = double.IsInfinity(room) ? MaxTab : Math.Clamp(room / count, MinTab, MaxTab);
            _strip.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var overflow = !double.IsInfinity(room) && _strip.DesiredSize.Width > room + 0.5;
            if (overflow != _overflow)
            {
                _overflow = overflow;
                _left.IsVisible = _right.IsVisible = _all.IsVisible = overflow;
            }

            var stripRoom = Math.Max(0, room - ControlsWidth);
            if (overflow)
            {
                _strip.Cap = Math.Clamp(stripRoom / count, MinTab, MaxTab);
            }

            var buttonSize = new Size(_plus.DesiredSize.Width, availableSize.Height);
            _left.Measure(buttonSize);
            _right.Measure(buttonSize);
            _all.Measure(buttonSize);
            _scroll.Measure(new Size(stripRoom, availableSize.Height));
            return new Size(_scroll.DesiredSize.Width + ControlsWidth + _plus.DesiredSize.Width, Math.Max(_scroll.DesiredSize.Height, _plus.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var button = _plus.DesiredSize.Width;
            var x = 0.0;
            if (_overflow)
            {
                _left.Arrange(new Rect(0, 0, button, finalSize.Height));
                x = button;
            }

            var stripWidth = _scroll.DesiredSize.Width;
            _scroll.Arrange(new Rect(x, 0, stripWidth, finalSize.Height));
            x += stripWidth;
            if (_overflow)
            {
                _right.Arrange(new Rect(x, 0, button, finalSize.Height));
                _all.Arrange(new Rect(x + button, 0, button, finalSize.Height));
                x += button * 2;
            }

            _plus.Arrange(new Rect(x, 0, button, finalSize.Height));
            _arranged();
            return finalSize;
        }
    }
}
