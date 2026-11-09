using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
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

    /// <summary>Whether a switch or choice command is on (drawn pressed, exposed as a toggle); always false for a plain command.</summary>
    internal bool IsOn { get; private set; }

    protected override AutomationPeer OnCreateAutomationPeer() => Definition.IsToggle ? new RibbonToggleAutomationPeer(this) : base.OnCreateAutomationPeer();

    /// <summary>Runs the command as a click does (the toggle pattern of assistive technology).</summary>
    internal void Activate()
    {
        if (IsEnabled)
        {
            RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(ClickEvent));
        }
    }

    /// <summary>Takes over the enabled and the checked state of the definition: the button is dimmed and inert while it is disabled, and the tooltip names the reason; a command that is on is drawn pressed.</summary>
    internal void RefreshState()
    {
        var enabled = Definition.IsEnabled;
        IsEnabled = enabled;
        if (Definition.IsToggle)
        {
            var on = Definition.IsChecked;
            if (on != IsOn)
            {
                IsOn = on;
                Classes.Set("checked", on);
                (ControlAutomationPeer.FromElement(this) as RibbonToggleAutomationPeer)?.StateChanged(!on, on);
            }
        }

        if (ToolTip.GetTip(this) is TooltipContent tip)
        {
            tip.Description = enabled ? Definition.Description : Definition.CurrentDisabledReason ?? Definition.Description;
        }

        if (Flyout is MenuFlyout flyout)
        {
            RefreshItems(flyout.Items);
        }
    }

    /// <summary>Updates the enabled and the checked state of menu items made by <see cref="CreateMenuItem"/> (their command rides in the tag).</summary>
    internal static void RefreshItems(IEnumerable<object?> items)
    {
        foreach (var item in items)
        {
            if (item is MenuItem { Tag: CommandDefinition command } menuItem)
            {
                menuItem.IsEnabled = command.IsEnabled;
                if (command.IsToggle)
                {
                    menuItem.IsChecked = command.IsChecked;
                }

                RefreshItems(menuItem.Items.Cast<object?>());
            }
        }
    }

    /// <summary>A menu item for a command (used by the menu of a command with items and by the overflow menu of a group); the command rides in <see cref="Control.Tag"/>.</summary>
    internal static MenuItem CreateMenuItem(CommandDefinition command, Action<CommandDefinition> invoked)
    {
        var item = new MenuItem { Header = command.Name, Tag = command, IsEnabled = command.IsEnabled };
        if (command.IsToggle)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = command.IsChecked;
        }

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

    private static RibbonLabel Label(CommandDefinition definition, TextAlignment alignment, bool wrap)
    {
        // A wrapping (tile) label is capped by RibbonLabel itself so that it can step over the cap for a long word.
        var label = new RibbonLabel(wrap ? TileLabelMaxWidth : double.PositiveInfinity)
        {
            Text = definition.Name,
            FontSize = 12,
            LineHeight = 14,
            TextAlignment = alignment,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = wrap ? 2 : 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!wrap)
        {
            label.MaxWidth = SmallLabelMaxWidth;
        }

        label.Classes.Add("ribbon-label");
        return label;
    }

    /// <summary>The button of a switch or choice command as an automation button with the toggle pattern, so assistive technology hears whether it is on.</summary>
    private sealed class RibbonToggleAutomationPeer(RibbonButton owner) : ControlAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.IsOn ? ToggleState.On : ToggleState.Off;

        public void Toggle() => owner.Activate();

        public void StateChanged(bool oldValue, bool newValue) =>
            RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, oldValue ? ToggleState.On : ToggleState.Off, newValue ? ToggleState.On : ToggleState.Off);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    }

    /// <summary>
    /// The label of a tile: it wraps at spaces and hyphens only. Up to <c>wrapWidth</c> it wraps like any text; a word (or the part
    /// between two hyphens) that is wider than that is never cut in the middle, the label takes the width of that word instead and the
    /// tile grows with it (the text engine would otherwise break the word at any character).
    /// </summary>
    private sealed class RibbonLabel(double wrapWidth) : TextBlock
    {
        private double _widest = -1;
        private string? _measuredText;

        protected override Type StyleKeyOverride => typeof(TextBlock);

        protected override Size MeasureOverride(Size availableSize)
        {
            if (TextWrapping == TextWrapping.NoWrap || string.IsNullOrEmpty(Text))
            {
                return base.MeasureOverride(availableSize);
            }

            var cap = Math.Max(wrapWidth, WidestSegment());
            return base.MeasureOverride(new Size(Math.Min(availableSize.Width, cap), availableSize.Height));
        }

        // The width of the widest piece the text may be broken into; kept for one text and font.
        private double WidestSegment()
        {
            var key = $"{Text}|{FontSize}|{FontFamily}|{FontWeight}";
            if (_widest >= 0 && key == _measuredText)
            {
                return _widest;
            }

            _widest = 0;
            _measuredText = key;
            foreach (var segment in Segments(Text!))
            {
                var probe = new TextBlock { Text = segment, FontFamily = FontFamily, FontSize = FontSize, FontWeight = FontWeight, FontStyle = FontStyle, TextWrapping = TextWrapping.NoWrap };
                probe.Measure(Size.Infinity);
                _widest = Math.Max(_widest, probe.DesiredSize.Width);
            }

            return _widest;
        }

        // The pieces between the break opportunities: split at a space (it is dropped) and after a hyphen (it stays with its piece).
        private static IEnumerable<string> Segments(string text)
        {
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    if (i > start)
                    {
                        yield return text[start..i];
                    }

                    start = i + 1;
                }
                else if (text[i] is '-' or '‐' && i + 1 < text.Length && text[i + 1] != ' ')
                {
                    yield return text[start..(i + 1)];
                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                yield return text[start..];
            }
        }
    }
}
