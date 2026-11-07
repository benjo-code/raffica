using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The title bar of the app frame (section 6 of the design spec), 36 px: the app <see cref="Mark"/>, the <see cref="Tabs"/> with the
/// open documents, the <see cref="Search"/> field (300 px, down to 200 when the window gets narrow) and, on Windows and Linux, the
/// three caption buttons of 46 x 36 (<see cref="ShowCaptionButtons"/>; the close button has the same neutral hover as the others, never
/// red). On macOS the system draws the window buttons, so the bar keeps <see cref="LeadingInset"/> = 80 px free on the left instead.
/// The bar draws the window chrome itself: once it is in a window it sets <c>Window.WindowDecorations</c> to
/// <see cref="WindowDecorations.BorderOnly"/> (no system title bar, but the border with its resize grips; macOS keeps the system
/// title bar behind the content) and marks its parts with <see cref="WindowDecorationProperties.ElementRoleProperty"/>: the bar itself
/// is the drag area (<see cref="WindowDecorationsElementRole.TitleBar"/>), the tabs and the search field are
/// <see cref="WindowDecorationsElementRole.User"/> elements that receive input, the caption buttons carry their button roles.
/// </summary>
public class TitleBar : TemplatedControl
{
    private const double SearchMax = 300;
    private const double SearchMin = 200;
    private const double TabsRoom = 240;
    private const double MacInset = 80;

    public static readonly StyledProperty<object?> MarkProperty = AvaloniaProperty.Register<TitleBar, object?>(nameof(Mark));
    public static readonly StyledProperty<object?> TrailingProperty = AvaloniaProperty.Register<TitleBar, object?>(nameof(Trailing));
    public static readonly StyledProperty<bool> ShowCaptionButtonsProperty = AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowCaptionButtons), !OperatingSystem.IsMacOS());
    public static readonly StyledProperty<double> LeadingInsetProperty = AvaloniaProperty.Register<TitleBar, double>(nameof(LeadingInset), OperatingSystem.IsMacOS() ? MacInset : 0);

    private static readonly Geometry MaximizeGlyph = Geometry.Parse("M5 5h14v14H5z");
    private static readonly Geometry RestoreGlyph = Geometry.Parse("M8 8V4h12v12h-4 M4 8h12v12H4z");

    private readonly ColumnDefinition _insetColumn = new(0, GridUnitType.Pixel);
    private readonly ColumnDefinition _searchColumn = new(SearchMax, GridUnitType.Pixel);
    private readonly ContentPresenter _mark = new() { Name = "PART_Mark", VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentPresenter _trailing = new() { Name = "PART_Trailing", VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _caption = new() { Name = "PART_Caption", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
    private readonly Button _minimize;
    private readonly Button _maximize;
    private readonly Button _close;
    private readonly StrokeIcon _maximizeIcon = new() { Size = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Data = MaximizeGlyph };
    private Border? _host;
    private Window? _window;

    static TitleBar()
    {
        MarkProperty.Changed.AddClassHandler<TitleBar>((bar, e) => bar._mark.Content = e.NewValue);
        TrailingProperty.Changed.AddClassHandler<TitleBar>((bar, e) =>
        {
            bar._trailing.Content = e.NewValue;
            bar.InvalidateMeasure();
        });
        ShowCaptionButtonsProperty.Changed.AddClassHandler<TitleBar>((bar, e) =>
        {
            bar._caption.IsVisible = e.GetNewValue<bool>();
            bar.InvalidateMeasure();
        });
        LeadingInsetProperty.Changed.AddClassHandler<TitleBar>((bar, e) => bar._insetColumn.Width = new GridLength(e.GetNewValue<double>()));
    }

    public TitleBar()
    {
        Tabs = new DocumentTabs { Name = "PART_Tabs", VerticalAlignment = VerticalAlignment.Bottom };
        Search = new SearchField { Name = "PART_Search", VerticalAlignment = VerticalAlignment.Center };

        _minimize = CaptionButton("PART_CaptionMinimize", RibbonButton.CreateIcon("Raffica.Icon.Minus", 16), ShellStrings.Minimize, WindowDecorationsElementRole.MinimizeButton);
        _maximize = CaptionButton("PART_CaptionMaximize", _maximizeIcon, ShellStrings.Maximize, WindowDecorationsElementRole.MaximizeButton);
        _close = CaptionButton("PART_CaptionClose", RibbonButton.CreateIcon("Raffica.Icon.X", 16), ShellStrings.Close, WindowDecorationsElementRole.CloseButton);
        _caption.Children.Add(_minimize);
        _caption.Children.Add(_maximize);
        _caption.Children.Add(_close);
        _caption.IsVisible = ShowCaptionButtons;
        _insetColumn.Width = new GridLength(LeadingInset);

        _minimize.Click += (_, _) =>
        {
            if (_window is not null)
            {
                _window.WindowState = WindowState.Minimized;
            }
        };
        _maximize.Click += (_, _) =>
        {
            if (_window is not null)
            {
                _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
        };
        _close.Click += (_, _) => _window?.Close();
    }

    /// <summary>The app mark at the left edge (an icon or a logo).</summary>
    public object? Mark { get => GetValue(MarkProperty); set => SetValue(MarkProperty, value); }

    /// <summary>
    /// Optional content between the search field and the caption buttons (an app's status button, for example). Empty by default,
    /// in which case the slot takes no room and the bar looks as it did without it.
    /// </summary>
    public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

    /// <summary>Whether the minimize, maximize and close buttons are drawn: yes on Windows and Linux, no on macOS.</summary>
    public bool ShowCaptionButtons { get => GetValue(ShowCaptionButtonsProperty); set => SetValue(ShowCaptionButtonsProperty, value); }

    /// <summary>Free space at the left for the system window buttons: 80 px on macOS, none elsewhere.</summary>
    public double LeadingInset { get => GetValue(LeadingInsetProperty); set => SetValue(LeadingInsetProperty, value); }

    public DocumentTabs Tabs { get; }

    public SearchField Search { get; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_host is not null)
        {
            _host.Child = null;
        }

        _host = e.NameScope.Find<Border>("PART_Host");
        if (_host is null)
        {
            return;
        }

        var grid = new Grid { ColumnDefinitions = [_insetColumn, new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), _searchColumn, new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)] };
        _mark.Margin = new Thickness(12, 0, 8, 0);
        _mark.Content = Mark;
        Place(grid, _mark, 1);
        Tabs.Margin = new Thickness(0, 0, 8, 0);
        Place(grid, Tabs, 2);
        Search.Margin = new Thickness(0, 0, 8, 0);
        Place(grid, Search, 3);
        _trailing.Content = Trailing;
        Place(grid, _trailing, 4);
        Place(grid, _caption, 5);
        _host.Child = grid;
        WindowDecorationProperties.SetElementRole(_host, WindowDecorationsElementRole.TitleBar);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // The search field is flex 0 1 300 with a minimum of 200: it gives way only after the tabs have kept a little room.
        if (_host?.Child is not null && !double.IsInfinity(availableSize.Width))
        {
            _mark.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            _trailing.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var taken = LeadingInset + _mark.DesiredSize.Width + _trailing.DesiredSize.Width + (ShowCaptionButtons ? 3 * 46 : 0) + 28 + TabsRoom + Search.Margin.Right + Tabs.Margin.Right;
            _searchColumn.Width = new GridLength(Math.Clamp(availableSize.Width - taken, SearchMin, SearchMax));
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            if (ShowCaptionButtons)
            {
                window.WindowDecorations = WindowDecorations.BorderOnly;
            }

            window.ExtendClientAreaToDecorationsHint = true;
            window.PropertyChanged += OnWindowPropertyChanged;
            UpdateMaximizeButton();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaximizeButton();
        }
    }

    private void UpdateMaximizeButton()
    {
        var maximized = _window?.WindowState == WindowState.Maximized;
        var name = maximized ? ShellStrings.Restore : ShellStrings.Maximize;
        _maximizeIcon.Data = maximized ? RestoreGlyph : MaximizeGlyph;
        AutomationProperties.SetName(_maximize, name);
        ToolTip.SetTip(_maximize, name);
    }

    private static void Place(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static Button CaptionButton(string name, Control icon, string label, WindowDecorationsElementRole role)
    {
        var button = new Button { Name = name, Content = icon };
        button.Classes.Add("caption-button");
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        WindowDecorationProperties.SetElementRole(button, role);
        return button;
    }
}
