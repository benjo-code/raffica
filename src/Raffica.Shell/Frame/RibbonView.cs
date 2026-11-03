using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
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

    /// <summary>Whether the field-code badges are shown (wired by the field-code session; the ribbon only holds the state).</summary>
    public static readonly StyledProperty<bool> ShowFieldCodesProperty = AvaloniaProperty.Register<RibbonView, bool>(nameof(ShowFieldCodes));

    /// <summary>The letter typed so far in a field-code session, or null when no tab letter is highlighted.</summary>
    public static readonly StyledProperty<string?> FieldCodeTabLetterProperty = AvaloniaProperty.Register<RibbonView, string?>(nameof(FieldCodeTabLetter));

    private readonly Panel _tabRow = new();
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _band = new() { ClipToBounds = true };
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };

    static RibbonView()
    {
        DefinitionProperty.Changed.AddClassHandler<RibbonView>((view, e) =>
        {
            var tabs = (e.NewValue as RibbonDefinition)?.Tabs;
            view.SetCurrentValue(ActiveTabProperty, tabs is { Count: > 0 } ? tabs[0] : null);
            view.Rebuild();
        });
        ActiveTabProperty.Changed.AddClassHandler<RibbonView>((view, _) => view.Rebuild());
        IsCompactProperty.Changed.AddClassHandler<RibbonView>((view, _) => view.Rebuild());
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

    /// <summary>The tab row, for <see cref="AppFrame.TabRow"/>.</summary>
    public Control TabRow => _tabRow;

    /// <summary>The band with the groups of the active tab, for <see cref="AppFrame.Band"/>.</summary>
    public Control Band => _band;

    private void Rebuild()
    {
        RebuildTabs();
        RebuildBand();
    }

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
            var button = new Button { Content = tab.Name };
            button.Classes.Add("ribbon-tab");
            button.Classes.Set("active", ReferenceEquals(tab, ActiveTab));
            AutomationProperties.SetName(button, tab.Name);
            button.Click += (_, _) => SetCurrentValue(ActiveTabProperty, tab);
            _tabStrip.Children.Add(button);
        }
    }

    private void RebuildBand()
    {
        _groups.Children.Clear();
        _band.Classes.Set("compact", IsCompact);
        if (ActiveTab is null)
        {
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
