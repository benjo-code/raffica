using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    private readonly Panel _tabRow = new();
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _band = new() { ClipToBounds = true };
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<(Control Target, string Digit)> _groupAnchors = [];
    private readonly List<FieldCodeBadge> _badges = [];
    private bool _settingDefinition;

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
        _band.Child = _groups;
    }

    /// <summary>Raised after a command ran (its <see cref="CommandDefinition.Run"/> has been called), also for items of a menu command and of the overflow menu.</summary>
    public event Action<CommandDefinition>? CommandInvoked;

    public RibbonDefinition? Definition { get => GetValue(DefinitionProperty); set => SetValue(DefinitionProperty, value); }

    public RibbonTabDefinition? ActiveTab { get => GetValue(ActiveTabProperty); set => SetValue(ActiveTabProperty, value); }

    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }

    public bool ShowFieldCodes { get => GetValue(ShowFieldCodesProperty); set => SetValue(ShowFieldCodesProperty, value); }

    public string? FieldCodeTabLetter { get => GetValue(FieldCodeTabLetterProperty); set => SetValue(FieldCodeTabLetterProperty, value); }

    public CommandDefinition? FieldCodeMenu { get => GetValue(FieldCodeMenuProperty); set => SetValue(FieldCodeMenuProperty, value); }

    /// <summary>The tab row, for <see cref="AppFrame.TabRow"/>.</summary>
    public Control TabRow => _tabRow;

    /// <summary>The band with the groups of the active tab, for <see cref="AppFrame.Band"/>.</summary>
    public Control Band => _band;

    private void OnDefinitionChanged(RibbonDefinition? definition)
    {
        // One rebuild: the tab strip is built here, the band once after the active tab is settled.
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
            ((Button)_tabStrip.Children[i]).Classes.Set("active", ReferenceEquals(Definition.Tabs[i], ActiveTab));
        }

        RebuildBand();
    }

    /// <summary>The button of a menu command in the band, or null when the command has no button there (for example in the overflow menu of the compact band).</summary>
    internal RibbonButton? FindMenuButton(CommandDefinition command) =>
        _groups.GetVisualDescendants().OfType<RibbonButton>().FirstOrDefault(b => ReferenceEquals(b.Definition, command) && b.Flyout is MenuFlyout);

    /// <summary>Raises <see cref="CommandInvoked"/> for a command the field-code session ran (the session calls <c>Run</c> itself).</summary>
    internal void NotifyInvoked(CommandDefinition command) => CommandInvoked?.Invoke(command);

    private void Invoke(CommandDefinition command)
    {
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
            var button = new Button { Content = tab.Name, Tag = tab };
            button.Classes.Add("ribbon-tab");
            button.Classes.Set("active", ReferenceEquals(tab, ActiveTab));
            AutomationProperties.SetName(button, tab.Name);
            button.Click += (_, _) => SetCurrentValue(ActiveTabProperty, tab);
            _tabStrip.Children.Add(button);
        }

        RefreshBadges();
    }

    private void RebuildBand()
    {
        _groups.Children.Clear();
        _groupAnchors.Clear();
        _band.Classes.Set("compact", IsCompact);
        if (ActiveTab is null)
        {
            RefreshBadges();
            return;
        }

        foreach (var group in ActiveTab.Groups)
        {
            if (group.Destructive && _groups.Children.Count > 0)
            {
                _groups.Children.Add(new Border { Classes = { "ribbon-rule" }, Width = 1, Margin = new Thickness(0, 6), IsHitTestVisible = false });
            }

            _groups.Children.Add(IsCompact ? BuildCompactGroup(group) : BuildGroup(group));
        }

        RefreshBadges();
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

    private StackPanel BuildGroup(RibbonGroupDefinition group)
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
        _groupAnchors.Add((label, group.Digit));

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
}
