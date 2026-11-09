using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The ribbon (section 6 of the design spec) as two parts for the <see cref="AppFrame"/>: <see cref="TabRow"/> (the tab names, the
/// active tab in ink, a 1 px ink rule under the row) goes into <see cref="AppFrame.TabRow"/> and <see cref="Band"/> (the groups of the
/// active tab) into <see cref="AppFrame.Band"/>. The view itself draws nothing; it owns the two controls, builds them in code from the
/// <see cref="RibbonDefinition"/> and rebuilds the band when the tab or <see cref="IsCompact"/> changes. Compact mode (a 40 px band)
/// hides the labels, turns large tiles into small buttons and moves the commands marked <see cref="CompactPlacement.Overflow"/> behind a
/// dropdown at the end of their group.
/// <para>
/// The band never hides a command. <see cref="IsCompact"/> is the user's setting and forces the compact layout at any width. Without it
/// the band switches to the compact layout by itself when the window is narrower than the full band of the active tab
/// (<see cref="IsEffectivelyCompact"/>); the width of the full band is measured once per tab (the band on screen is its own measure, a
/// compact band measures a throw-away full band) and kept until the <see cref="Definition"/> is replaced, so a tab of another language
/// is a new measure. When even the compact band is wider than the window it scrolls, with a chevron button at each end that shows only
/// while there is more in that direction; keyboard focus brings the focused button into view.
/// </para>
/// </summary>
public class RibbonView : TemplatedControl
{
    public static readonly StyledProperty<RibbonDefinition?> DefinitionProperty = AvaloniaProperty.Register<RibbonView, RibbonDefinition?>(nameof(Definition));
    public static readonly StyledProperty<RibbonTabDefinition?> ActiveTabProperty = AvaloniaProperty.Register<RibbonView, RibbonTabDefinition?>(nameof(ActiveTab));
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<RibbonView, bool>(nameof(IsCompact));

    /// <summary>
    /// Whether the field-code badges are shown (set by <see cref="FieldCodeInput"/>): the tab letters on the tab row, and with
    /// <see cref="FieldCodeTabLetter"/> set the group digits and the command codes of the active tab.
    /// </summary>
    public static readonly StyledProperty<bool> ShowFieldCodesProperty = AvaloniaProperty.Register<RibbonView, bool>(nameof(ShowFieldCodes));

    /// <summary>The letter of the tab chosen in the field-code session, or null while the session still waits for a tab letter.</summary>
    public static readonly StyledProperty<string?> FieldCodeTabLetterProperty = AvaloniaProperty.Register<RibbonView, string?>(nameof(FieldCodeTabLetter));

    /// <summary>
    /// The menu command whose items the field-code session shows (set by <see cref="FieldCodeInput"/>), or null. While it is set the
    /// ribbon itself shows no badges: the codes sit on the items of the menu.
    /// </summary>
    public static readonly StyledProperty<CommandDefinition?> FieldCodeMenuProperty = AvaloniaProperty.Register<RibbonView, CommandDefinition?>(nameof(FieldCodeMenu));

    // A chevron slot: the 24 px chevron button and 4 px on each side. The slot keeps its width while its chevron is hidden, so the
    // viewport stays the same whichever chevron shows.
    private const double ChevronSlotWidth = 32;

    private readonly Panel _tabRow = new();
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _band = new() { ClipToBounds = true };
    private readonly Grid _host = new();
    private readonly ScrollViewer _scroller;
    private readonly Button _scrollLeft;
    private readonly Button _scrollRight;
    private readonly Border _leftSlot = new() { IsVisible = false, Width = ChevronSlotWidth, Padding = new Thickness(4, 0) };
    private readonly Border _rightSlot = new() { IsVisible = false, Width = ChevronSlotWidth, Padding = new Thickness(4, 0) };
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<RibbonTabDefinition, double> _fullWidths = [];
    private readonly List<(Control Target, string Digit)> _groupAnchors = [];
    private readonly List<FieldCodeBadge> _badges = [];
    private bool _settingDefinition;
    private bool _effectivelyCompact;
    private bool _builtCompact;
    private RibbonTabDefinition? _builtTab;
    private double _lastWidth;
    private AppFrame? _frame;

    static RibbonView()
    {
        DefinitionProperty.Changed.AddClassHandler<RibbonView>((view, e) => view.OnDefinitionChanged(e.NewValue as RibbonDefinition));
        ActiveTabProperty.Changed.AddClassHandler<RibbonView>((view, _) => view.OnActiveTabChanged());
        IsCompactProperty.Changed.AddClassHandler<RibbonView>((view, _) => view.RebuildBand());
        ShowFieldCodesProperty.Changed.AddClassHandler<RibbonView>((view, _) => view.RefreshBadges());
        FieldCodeTabLetterProperty.Changed.AddClassHandler<RibbonView>((view, _) => view.RefreshBadges());
        FieldCodeMenuProperty.Changed.AddClassHandler<RibbonView>((view, _) => view.RefreshBadges());
    }

    public RibbonView()
    {
        _tabRow.Classes.Add("ribbon-tabrow");
        _tabRow.Children.Add(new Border { Name = "PART_TabRule", Classes = { "ribbon-tabrule" }, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
        _tabRow.Children.Add(_tabStrip);
        _band.Classes.Add("ribbon-band");

        // The band is a scroll viewer between two slots for the chevrons. The slots take room only while the band is wider than the window and
        // then at both ends, so the viewport does not change when a chevron shows or hides (a button that was scrolled into view stays in view).
        _host.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
        _scroller = new ScrollViewer
        {
            Name = "PART_BandScroll",
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _groups,
        };
        Grid.SetColumn(_scroller, 1);
        _scrollLeft = CreateChevron("PART_ScrollLeft", "Raffica.Icon.ChevronLeft", ShellStrings.RibbonScrollLeft);
        _scrollRight = CreateChevron("PART_ScrollRight", "Raffica.Icon.ChevronRight", ShellStrings.RibbonScrollRight);
        Grid.SetColumn(_rightSlot, 2);
        _leftSlot.Child = _scrollLeft;
        _rightSlot.Child = _scrollRight;
        _scrollLeft.Click += (_, _) => ScrollBy(-1);
        _scrollRight.Click += (_, _) => ScrollBy(1);
        _host.Children.Add(_leftSlot);
        _host.Children.Add(_scroller);
        _host.Children.Add(_rightSlot);
        _band.Child = _host;

        _scroller.ScrollChanged += (_, _) => UpdateChevrons();
        _scroller.PointerWheelChanged += OnBandWheel;
        _groups.AddHandler(GotFocusEvent, OnGroupsGotFocus);
        _band.SizeChanged += OnBandSizeChanged;
        _band.AttachedToVisualTree += OnBandAttached;
        _band.DetachedFromVisualTree += OnBandDetached;
    }

    /// <summary>Raised after a command ran (its <see cref="CommandDefinition.Run"/> has been called), also for items of a menu command and of the overflow menu.</summary>
    public event Action<CommandDefinition>? CommandInvoked;

    public RibbonDefinition? Definition { get => GetValue(DefinitionProperty); set => SetValue(DefinitionProperty, value); }

    public RibbonTabDefinition? ActiveTab { get => GetValue(ActiveTabProperty); set => SetValue(ActiveTabProperty, value); }

    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }

    /// <summary>
    /// Whether the band is in the compact layout: <see cref="IsCompact"/> (the user's setting) or the window is narrower than the full band
    /// of the active tab. Changes only with the width, the tab, the definition or <see cref="IsCompact"/>, never with the layout it picks.
    /// </summary>
    public bool IsEffectivelyCompact => _effectivelyCompact;

    /// <summary>How often the band was built (a test reads it to see that a settled layout does not rebuild).</summary>
    internal int BandBuildCount { get; private set; }

    /// <summary>How often a throw-away full band was built only to measure it (a test reads it to see that a measure is kept).</summary>
    internal int FullMeasureCount { get; private set; }

    public bool ShowFieldCodes { get => GetValue(ShowFieldCodesProperty); set => SetValue(ShowFieldCodesProperty, value); }

    public string? FieldCodeTabLetter { get => GetValue(FieldCodeTabLetterProperty); set => SetValue(FieldCodeTabLetterProperty, value); }

    public CommandDefinition? FieldCodeMenu { get => GetValue(FieldCodeMenuProperty); set => SetValue(FieldCodeMenuProperty, value); }

    /// <summary>The tab row, for <see cref="AppFrame.TabRow"/>.</summary>
    public Control TabRow => _tabRow;

    /// <summary>The band with the groups of the active tab, for <see cref="AppFrame.Band"/>.</summary>
    public Control Band => _band;

    private void OnDefinitionChanged(RibbonDefinition? definition)
    {
        // One rebuild: the tab strip is built here, the band once after the active tab is settled. A new definition is new measures
        // (its names may be in another language).
        _fullWidths.Clear();
        _settingDefinition = true;
        try
        {
            var tabs = definition?.Tabs;
            if (ActiveTab is null || tabs is null || !tabs.Contains(ActiveTab))
            {
                SetCurrentValue(ActiveTabProperty, tabs is { Count: > 0 } ? tabs[0] : null);
            }
        }
        finally
        {
            _settingDefinition = false;
        }

        RebuildTabs();
        RebuildBand();
    }

    private void OnActiveTabChanged()
    {
        if (_settingDefinition)
        {
            return;
        }

        // The tab buttons stay (and keep their keyboard focus); only the active mark moves.
        for (var i = 0; i < _tabStrip.Children.Count && Definition is not null; i++)
        {
            ((TabButton)_tabStrip.Children[i]).SetActive(ReferenceEquals(Definition.Tabs[i], ActiveTab));
        }

        RebuildBand();
    }

    /// <summary>The button of a menu command in the band, or null when the command has no button there (for example in the overflow menu of the compact band).</summary>
    internal RibbonButton? FindMenuButton(CommandDefinition command) =>
        _groups.GetVisualDescendants().OfType<RibbonButton>().FirstOrDefault(b => ReferenceEquals(b.Definition, command) && b.Flyout is MenuFlyout);

    /// <summary>Raises <see cref="CommandInvoked"/> for a command the field-code session ran (the session calls <c>Run</c> itself).</summary>
    internal void NotifyInvoked(CommandDefinition command) => CommandInvoked?.Invoke(command);

    /// <summary>
    /// Asks every command of the band again whether it is enabled (see <see cref="CommandDefinition.EnabledWhen"/>) and updates the
    /// buttons, the items of the menus and the tooltip reasons in place; focus and open menus stay. The app calls this when what the
    /// conditions look at has changed.
    /// </summary>
    public void RefreshCommandStates()
    {
        foreach (var button in _groups.GetVisualDescendants().OfType<RibbonButton>())
        {
            button.RefreshState();
        }

        foreach (var more in _groups.GetVisualDescendants().OfType<Button>())
        {
            if (more is not RibbonButton && more.Flyout is MenuFlyout flyout)
            {
                RibbonButton.RefreshItems(flyout.Items);
            }
        }
    }

    private void Invoke(CommandDefinition command)
    {
        if (!command.IsEnabled)
        {
            return;
        }

        command.Run?.Invoke();
        CommandInvoked?.Invoke(command);
    }

    private void RebuildTabs()
    {
        _tabStrip.Children.Clear();
        if (Definition is null)
        {
            return;
        }

        foreach (var tab in Definition.Tabs)
        {
            var button = new TabButton(tab.Name, tab, () => SetCurrentValue(ActiveTabProperty, tab));
            button.SetActive(ReferenceEquals(tab, ActiveTab));
            AutomationProperties.SetName(button, tab.Name);
            button.Click += (_, _) => button.Select();
            _tabStrip.Children.Add(button);
        }

        RefreshBadges();
    }

    private void RebuildBand()
    {
        var compact = WantsCompact();
        _groups.Children.Clear();
        _groupAnchors.Clear();
        _scroller.Offset = default;
        _band.Classes.Set("compact", compact);
        SetEffectivelyCompact(compact);
        _builtTab = ActiveTab;
        _builtCompact = compact;
        BandBuildCount++;
        if (ActiveTab is null)
        {
            RefreshBadges();
            return;
        }

        AddGroups(_groups, ActiveTab, compact, _groupAnchors);
        RefreshBadges();
    }

    private void AddGroups(StackPanel panel, RibbonTabDefinition tab, bool compact, List<(Control Target, string Digit)>? anchors)
    {
        foreach (var group in tab.Groups)
        {
            if (group.Destructive && panel.Children.Count > 0)
            {
                panel.Children.Add(new Border { Classes = { "ribbon-rule" }, Width = 1, Margin = new Thickness(0, 6), IsHitTestVisible = false });
            }

            panel.Children.Add(compact ? BuildCompactGroup(group) : BuildGroup(group, anchors));
        }
    }

    private void SetEffectivelyCompact(bool compact)
    {
        _effectivelyCompact = compact;
        _frame?.SetCurrentValue(AppFrame.IsAutoCompactProperty, compact);
    }

    /// <summary>Whether the band should be compact now: the setting, or a window narrower than the full band of the active tab (not known yet: no).</summary>
    private bool WantsCompact()
    {
        if (IsCompact)
        {
            return true;
        }

        var width = _band.Bounds.Width;
        if (ActiveTab is not { } tab || width <= 0)
        {
            return false;
        }

        return FullWidthOf(tab) is { } full && full > width + 0.5;
    }

    /// <summary>
    /// The width the full band of a tab needs, or null while it cannot be measured yet (the band is not in a window). It is taken once
    /// and kept: from the band on screen when that is the full band of the tab, else from a throw-away full band that is measured inside the
    /// band (the styles apply only there) and removed again. The result never depends on the layout that is showing.
    /// </summary>
    private double? FullWidthOf(RibbonTabDefinition tab)
    {
        if (_fullWidths.TryGetValue(tab, out var known))
        {
            return known;
        }

        if (!_builtCompact && ReferenceEquals(_builtTab, tab) && _groups.IsMeasureValid && _groups.DesiredSize.Width > 0)
        {
            return _fullWidths[tab] = _groups.DesiredSize.Width;
        }

        if (!_band.IsAttachedToVisualTree())
        {
            return null;
        }

        var probe = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Opacity = 0 };
        Grid.SetColumnSpan(probe, 3);
        AddGroups(probe, tab, compact: false, anchors: null);
        _host.Children.Add(probe);
        try
        {
            probe.Measure(Size.Infinity);
            FullMeasureCount++;
            return _fullWidths[tab] = probe.DesiredSize.Width;
        }
        finally
        {
            _host.Children.Remove(probe);
        }
    }

    private void OnBandSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Only the width decides; the height changes with the layout the decision picks and must not start the next decision.
        var width = e.NewSize.Width;
        if (Math.Abs(width - _lastWidth) < 0.5)
        {
            return;
        }

        _lastWidth = width;
        if (WantsCompact() != _effectivelyCompact)
        {
            RebuildBand();
        }
    }

    private void OnBandAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _frame = _band.FindAncestorOfType<AppFrame>();
        _frame?.SetCurrentValue(AppFrame.IsAutoCompactProperty, _effectivelyCompact);
    }

    private void OnBandDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _frame?.SetCurrentValue(AppFrame.IsAutoCompactProperty, false);
        _frame = null;
        _lastWidth = 0;
    }

    private static Button CreateChevron(string name, string iconKey, string label)
    {
        var chevron = new Button { Name = name, Content = RibbonButton.CreateIcon(iconKey, 16), IsVisible = false, IsTabStop = false, Focusable = false };
        chevron.Classes.Add("ribbon-chevron");
        AutomationProperties.SetName(chevron, label);
        ToolTip.SetTip(chevron, new TooltipContent { Title = label });
        return chevron;
    }

    private void UpdateChevrons()
    {
        // The slots follow the extent and the width of the band (not the viewport, which they change themselves), so this cannot loop.
        var overflowing = _host.Bounds.Width > 0 && _scroller.Extent.Width > _host.Bounds.Width + 0.5;
        _leftSlot.IsVisible = overflowing;
        _rightSlot.IsVisible = overflowing;
        var offset = _scroller.Offset.X;
        _scrollLeft.IsVisible = overflowing && offset > 0.5;
        _scrollRight.IsVisible = overflowing && offset + _scroller.Viewport.Width < _scroller.Extent.Width - 0.5;
    }

    private void ScrollBy(int direction)
    {
        var step = Math.Max(96, _scroller.Viewport.Width * 0.6);
        var max = Math.Max(0, _scroller.Extent.Width - _scroller.Viewport.Width);
        _scroller.Offset = new Vector(Math.Clamp(_scroller.Offset.X + (direction * step), 0, max), 0);
    }

    private void OnBandWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_scroller.Extent.Width <= _scroller.Viewport.Width + 0.5 || e.Delta.Y == 0)
        {
            return;
        }

        var max = _scroller.Extent.Width - _scroller.Viewport.Width;
        _scroller.Offset = new Vector(Math.Clamp(_scroller.Offset.X - (e.Delta.Y * 48), 0, max), 0);
        e.Handled = true;
    }

    // Keyboard focus (Tab, arrows, a program) shows the button; a click does not move the band under the pointer.
    private void OnGroupsGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod != NavigationMethod.Pointer && e.Source is Control control)
        {
            control.BringIntoView();
        }
    }

    /// <summary>Rebuilds the badges from the state: nothing at rest; tab letters while the mode is on; plus group digits and command codes once a tab is chosen.</summary>
    private void RefreshBadges()
    {
        FieldCodeBadge.RemoveAll(_badges);
        if (!ShowFieldCodes || FieldCodeMenu is not null)
        {
            return;
        }

        foreach (var child in _tabStrip.Children)
        {
            if (child is Button { Tag: RibbonTabDefinition tab } button)
            {
                Add(button, tab.Letter);
            }
        }

        if (FieldCodeTabLetter is null)
        {
            return;
        }

        foreach (var (target, digit) in _groupAnchors)
        {
            Add(target, digit);
        }

        foreach (var button in _groups.GetVisualDescendants().OfType<RibbonButton>())
        {
            Add(button, button.Definition.FieldCode);
        }

        void Add(Control target, string text)
        {
            if (FieldCodeBadge.Show(target, text) is { } badge)
            {
                _badges.Add(badge);
            }
        }
    }

    private StackPanel BuildGroup(RibbonGroupDefinition group, List<(Control Target, string Digit)>? anchors)
    {
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
        foreach (var command in group.Commands.Where(c => c.Size == CommandSize.Large))
        {
            commands.Children.Add(new RibbonButton(command, RibbonButtonKind.Tile, Invoke));
        }

        // The small commands stack two high next to the large ones (2 x 26 px + 2 px gap = the 54 px of a tile).
        StackPanel? column = null;
        foreach (var command in group.Commands.Where(c => c.Size != CommandSize.Large))
        {
            if (column is null || column.Children.Count == 2)
            {
                column = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                commands.Children.Add(column);
            }

            column.Children.Add(new RibbonButton(command, command.IconOnly ? RibbonButtonKind.IconOnly : RibbonButtonKind.Small, Invoke));
        }

        var label = new TextBlock { Text = group.Label, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        label.Classes.Add("ribbon-group-label");
        anchors?.Add((label, group.Digit));

        var panel = new StackPanel { Margin = new Thickness(8, 0) };
        panel.Children.Add(commands);
        panel.Children.Add(label);
        return panel;
    }

    private StackPanel BuildCompactGroup(RibbonGroupDefinition group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
        foreach (var command in group.Commands.Where(c => c.Compact == CompactPlacement.Inline))
        {
            row.Children.Add(new RibbonButton(command, RibbonButtonKind.IconOnly, Invoke));
        }

        var overflow = group.Commands.Where(c => c.Compact == CompactPlacement.Overflow).ToList();
        if (overflow.Count > 0)
        {
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            foreach (var command in overflow)
            {
                flyout.Items.Add(RibbonButton.CreateMenuItem(command, Invoke));
            }

            var more = new Button { Content = RibbonButton.CreateIcon("Raffica.Icon.ChevronDown", 16), Flyout = flyout };
            more.Classes.Add("ribbon-more");
            AutomationProperties.SetName(more, ShellStrings.RibbonGroupMore(group.Label));
            ToolTip.SetTip(more, new TooltipContent { Title = ShellStrings.RibbonMore });
            row.Children.Add(more);
        }

        return row;
    }

    /// <summary>A tab name in the tab row: a button, and for assistive technology a tab item with the selection item pattern that says whether it is the active tab.</summary>
    private sealed class TabButton : Button
    {
        private readonly Action _select;

        public TabButton(string name, RibbonTabDefinition tab, Action select)
        {
            Content = name;
            Tag = tab;
            _select = select;
            Classes.Add("ribbon-tab");
        }

        public bool IsActive { get; private set; }

        protected override Type StyleKeyOverride => typeof(Button);

        public void Select() => _select();

        public void SetActive(bool active)
        {
            if (active == IsActive)
            {
                return;
            }

            IsActive = active;
            Classes.Set("active", active);
            (ControlAutomationPeer.FromElement(this) as TabAutomationPeer)?.SelectionChanged(!active, active);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new TabAutomationPeer(this);

        private sealed class TabAutomationPeer(TabButton owner) : ControlAutomationPeer(owner), ISelectionItemProvider
        {
            public bool IsSelected => owner.IsActive;

            public ISelectionProvider? SelectionContainer => null;

            public void Select() => owner.Select();

            public void AddToSelection() => Select();

            public void RemoveFromSelection()
            {
                // A ribbon always has one active tab: it cannot be deselected.
            }

            public void SelectionChanged(bool oldValue, bool newValue) =>
                RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, oldValue, newValue);

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;
        }
    }
}
