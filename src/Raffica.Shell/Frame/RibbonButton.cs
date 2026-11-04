using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Shell.Frame;

/// <summary>How a <see cref="RibbonButton"/> is laid out.</summary>
public enum RibbonButtonKind
{
    /// <summary>A large tile: 54 px high, a 20 px icon above a label of up to two lines, at least 48 px wide.</summary>
    Tile,

    /// <summary>A small button: 26 px high, a 16 px icon and the label.</summary>
    Small,

    /// <summary>A small button without a label (the label stays as automation name and tooltip).</summary>
    IconOnly,
}

/// <summary>
/// A ribbon button built from a <see cref="CommandDefinition"/> (section 6 of the design spec). It brings the visuals of the
/// plain button, a tooltip with name, shortcut and description and the accessible name from the command. A command with
/// <see cref="CommandDefinition.Items"/> opens a menu of them instead of running. The controls are built in code, without
/// reflection, so the control stays friendly to Native AOT. The definition is exposed as <see cref="Definition"/> because
/// <c>Command</c> is already the <c>ICommand</c> of <see cref="Button"/>.
/// </summary>
public class RibbonButton : Button
{
    // The widest label: a tile label wraps (two lines) inside 96 px, which keeps a long German word such as "Rechtschreibung" whole;
    // a small button label stays on one line and may take 160 px.
    private const double TileLabelMaxWidth = 96;
    private const double SmallLabelMaxWidth = 160;

    public RibbonButton(CommandDefinition definition, RibbonButtonKind kind, Action<CommandDefinition> invoked)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(invoked);
        Definition = definition;
        Kind = kind;

        Classes.Add(kind switch
        {
            RibbonButtonKind.Tile => "ribbon-tile",
            RibbonButtonKind.Small => "ribbon-small",
            _ => "ribbon-iconly",
        });
        AutomationProperties.SetName(this, definition.Name);
        ToolTip.SetTip(this, new TooltipContent { Title = definition.Name, Shortcut = definition.Shortcut, Description = definition.Description });
        ToolTip.SetShowOnDisabled(this, true);
        Content = BuildContent(definition, kind);

        if (definition.Items is { Count: > 0 } items)
        {
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            foreach (var item in items)
            {
                flyout.Items.Add(CreateMenuItem(item, invoked));
            }

            Flyout = flyout;
        }
        else
        {
            Click += (_, _) => invoked(definition);
        }

        RefreshState();
    }

    public CommandDefinition Definition { get; }

    public RibbonButtonKind Kind { get; }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>Takes over the enabled state of the definition: the button is dimmed and inert while it is disabled, and the tooltip names the reason.</summary>
    internal void RefreshState()
    {
        var enabled = Definition.IsEnabled;
        IsEnabled = enabled;
        if (ToolTip.GetTip(this) is TooltipContent tip)
        {
            tip.Description = enabled ? Definition.Description : Definition.CurrentDisabledReason ?? Definition.Description;
        }

        if (Flyout is MenuFlyout flyout)
        {
            RefreshItems(flyout.Items);
        }
    }

    /// <summary>Updates the enabled state of menu items made by <see cref="CreateMenuItem"/> (their command rides in the tag).</summary>
    internal static void RefreshItems(IEnumerable<object?> items)
    {
        foreach (var item in items)
        {
            if (item is MenuItem { Tag: CommandDefinition command } menuItem)
            {
                menuItem.IsEnabled = command.IsEnabled;
                RefreshItems(menuItem.Items.Cast<object?>());
            }
        }
    }

    /// <summary>A menu item for a command (used by the menu of a command with items and by the overflow menu of a group); the command rides in <see cref="Control.Tag"/>.</summary>
    internal static MenuItem CreateMenuItem(CommandDefinition command, Action<CommandDefinition> invoked)
    {
        var item = new MenuItem { Header = command.Name, Tag = command, IsEnabled = command.IsEnabled };
        AutomationProperties.SetName(item, command.Name);
        if (!string.IsNullOrEmpty(command.Shortcut))
        {
            try
            {
                item.InputGesture = KeyGesture.Parse(command.Shortcut);
            }
            catch (FormatException)
            {
                // A shortcut that is not a key gesture stays visible in the tooltip only.
            }
        }

        if (command.Items is { Count: > 0 } children)
        {
            foreach (var child in children)
            {
                item.Items.Add(CreateMenuItem(child, invoked));
            }
        }
        else
        {
            item.Click += (_, _) => invoked(command);
        }

        return item;
    }

    internal static StrokeIcon CreateIcon(string key, int size)
    {
        var icon = new StrokeIcon { Size = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(StrokeIcon.DataProperty, icon.GetResourceObservable(key, value => value as Geometry));
        return icon;
    }

    private static Control BuildContent(CommandDefinition definition, RibbonButtonKind kind)
    {
        switch (kind)
        {
            case RibbonButtonKind.Tile:
                var tile = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                tile.Children.Add(CreateIcon(definition.IconKey, 20));
                tile.Children.Add(Label(definition, TextAlignment.Center, wrap: true));
                return tile;

            case RibbonButtonKind.Small:
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(CreateIcon(definition.IconKey, 16));
                row.Children.Add(Label(definition, TextAlignment.Left, wrap: false));
                if (definition.Items is { Count: > 0 })
                {
                    row.Children.Add(CreateIcon("Raffica.Icon.ChevronDown", 16));
                }

                return row;

            default:
                return CreateIcon(definition.IconKey, 16);
        }
    }

    private static TextBlock Label(CommandDefinition definition, TextAlignment alignment, bool wrap)
    {
        var label = new TextBlock
        {
            Text = definition.Name,
            FontSize = 12,
            LineHeight = 14,
            TextAlignment = alignment,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = wrap ? 2 : 1,
            MaxWidth = wrap ? TileLabelMaxWidth : SmallLabelMaxWidth,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Classes.Add("ribbon-label");
        return label;
    }
}
