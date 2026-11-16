using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>One choice of a picker: <paramref name="Id"/> is what the app gets back, <paramref name="Label"/> is what the user reads.</summary>
public sealed record PickerItem(string Id, string Label);

/// <summary>
/// What turns a <see cref="CommandDefinition"/> into a picker (font family, size, a style). <paramref name="Items"/> is asked each time the
/// list opens (never while the ribbon is built, so a long list such as the installed fonts costs nothing before first use);
/// <paramref name="Chosen"/> gets the id of a chosen item, or for an <paramref name="Editable"/> picker (the size box) the text the user
/// typed and confirmed with Enter. <paramref name="SelectedId"/> and <paramref name="IsMixed"/> are read again by
/// <see cref="RibbonView.RefreshCommandStates"/>; a mixed picker shows "Mixed" and no selection. <paramref name="SelectedLabel"/> is the
/// text shown while the list is not loaded yet (it defaults to the id; for an editable picker the shown text is the label or the id).
/// </summary>
public sealed record PickerDefinition(
    Func<IReadOnlyList<PickerItem>> Items,
    Action<string> Chosen,
    Func<string?>? SelectedId = null,
    Func<bool>? IsMixed = null,
    Func<string?>? SelectedLabel = null,
    bool Editable = false,
    double Width = 140);

/// <summary>
/// A picker in the ribbon band: the shell's <see cref="SelectBox"/> (26 px, the shell theme) made from the <see cref="CommandDefinition.Picker"/>
/// of a command. It lists the items on first open, shows the selected item or "Mixed", reports a choice through
/// <see cref="PickerDefinition.Chosen"/> and then through the ribbon's <c>CommandInvoked</c>, takes the enabled state and the tooltip reason
/// of the command like <see cref="RibbonButton"/>, and exposes the shown text as its automation value. The editable variant takes digits
/// and one decimal mark only: Enter commits, Esc (or leaving the box) puts back the text of the state.
/// </summary>
public sealed class RibbonPicker : SelectBox
{
    private readonly Action<CommandDefinition> _invoked;
    private IReadOnlyList<PickerItem>? _items;
    private bool _updating;
    private string _display = "";

    public RibbonPicker(CommandDefinition command, Action<CommandDefinition> invoked)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(invoked);
        Command = command;
        Picker = command.Picker ?? throw new ArgumentException("The command has no picker.", nameof(command));
        _invoked = invoked;

        Classes.Add("ribbon-picker");
        SelectionChanged += OnSelected;
        Width = Picker.Width;
        IsEditable = Picker.Editable;
        AutomationProperties.SetName(this, command.Name);
        ToolTip.SetTip(this, new TooltipContent { Title = command.Name, Shortcut = command.Shortcut, Description = command.Description });
        ToolTip.SetShowOnDisabled(this, true);

        if (Picker.Editable)
        {
            AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, OnEditKeyDown, RoutingStrategies.Tunnel);
            LostFocus += (_, _) =>
            {
                if (!IsDropDownOpen && !IsKeyboardFocusWithin)
                {
                    RefreshState();
                }
            };
        }

        RefreshState();
    }

    public CommandDefinition Command { get; }

    public PickerDefinition Picker { get; }

    /// <summary>Whether the list has been read (it is read each time it opens, never before the first open).</summary>
    internal bool ItemsLoaded => _items is not null;

    /// <summary>The text the picker shows now (the label of the selection, "Mixed", or the typed size); its automation value.</summary>
    internal string DisplayText => _display;

    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new RibbonPickerPeer(this);

    /// <summary>Opens the list (a list picker) or focuses the text box (the size box): what a field code of the command does.</summary>
    internal void Open()
    {
        if (!IsEnabled)
        {
            return;
        }

        Focus(NavigationMethod.Tab);
        if (!IsEditable)
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
        }
    }

    /// <summary>Takes over the enabled state, the tooltip reason and the shown selection (or "Mixed") from the command.</summary>
    internal void RefreshState()
    {
        var enabled = Command.IsEnabled;
        IsEnabled = enabled;
        if (ToolTip.GetTip(this) is TooltipContent tip)
        {
            tip.Description = enabled ? Command.Description : Command.CurrentDisabledReason ?? Command.Description;
        }

        var mixed = Picker.IsMixed?.Invoke() ?? false;
        var id = mixed ? null : Picker.SelectedId?.Invoke();
        string? label = null;
        var index = -1;
        if (id is not null)
        {
            if (_items is not null)
            {
                index = IndexOf(_items, id);
            }

            label = index >= 0 ? _items![index].Label : Picker.SelectedLabel?.Invoke() ?? id;
        }

        var old = _display;
        _updating = true;
        try
        {
            Classes.Set("mixed", mixed);
            PlaceholderText = mixed ? ShellStrings.PickerMixed : IsEditable ? null : label ?? "";
            if (IsEditable)
            {
                SetCurrentValue(TextProperty, mixed ? "" : label ?? "");
            }
            else
            {
                SetCurrentValue(SelectedIndexProperty, index);
            }
        }
        finally
        {
            _updating = false;
        }

        _display = mixed ? ShellStrings.PickerMixed : label ?? "";
        if (old != _display)
        {
            (ControlAutomationPeer.FromElement(this) as RibbonPickerPeer)?.ValueChanged(old, _display);
        }
    }

    /// <summary>Builds the choices of the overflow menu of the compact band: a radio item per picker item, checked for the selection.</summary>
    internal static IEnumerable<MenuItem> CreateChoiceItems(CommandDefinition command, Action<CommandDefinition> invoked)
    {
        var picker = command.Picker!;
        var items = picker.Items();
        var mixed = picker.IsMixed?.Invoke() ?? false;
        var selected = mixed ? null : picker.SelectedId?.Invoke();
        foreach (var item in items)
        {
            var choice = new MenuItem { Header = item.Label, ToggleType = MenuItemToggleType.Radio, IsChecked = item.Id == selected };
            AutomationProperties.SetName(choice, item.Label);
            var id = item.Id;
            choice.Click += (_, _) =>
            {
                picker.Chosen(id);
                invoked(command);
            };
            yield return choice;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        // The list is read just before it shows (on every open, so a changed list is current).
        if (change.Property == IsDropDownOpenProperty && change.NewValue is true)
        {
            LoadItems();
        }

        base.OnPropertyChanged(change);
    }

    private void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating || _items is null || SelectedIndex < 0 || SelectedIndex >= _items.Count)
        {
            return;
        }

        Commit(_items[SelectedIndex].Id);
    }

    private static int IndexOf(IReadOnlyList<PickerItem> items, string id)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void LoadItems()
    {
        _items = Picker.Items();
        var labels = new List<string>(_items.Count);
        foreach (var item in _items)
        {
            labels.Add(item.Label);
        }

        _updating = true;
        try
        {
            ItemsSource = labels;
        }
        finally
        {
            _updating = false;
        }

        RefreshState();
    }

    private void Commit(string value)
    {
        Picker.Chosen(value);
        _invoked(Command);
        RefreshState();
    }

    // The size box takes digits and one decimal mark only.
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var box = e.Source as TextBox;
        var current = box?.Text ?? "";
        var start = box is null ? current.Length : Math.Min(box.SelectionStart, box.SelectionEnd);
        var end = box is null ? current.Length : Math.Max(box.SelectionStart, box.SelectionEnd);
        start = Math.Min(start, current.Length);
        end = Math.Min(end, current.Length);
        var candidate = string.Concat(current.AsSpan(0, start), e.Text, current.AsSpan(end));
        if (!IsNumberText(candidate, allowEmpty: true))
        {
            e.Handled = true;
        }
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || IsDropDownOpen || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var typed = Text?.Trim() ?? "";
            if (IsNumberText(typed, allowEmpty: false))
            {
                Commit(typed);
            }
            else
            {
                RefreshState();
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RefreshState();
        }
    }

    private static bool IsNumberText(string text, bool allowEmpty)
    {
        if (text.Length == 0)
        {
            return allowEmpty;
        }

        var marks = 0;
        var digits = 0;
        foreach (var c in text)
        {
            if (c is '.' or ',')
            {
                marks++;
            }
            else if (c is >= '0' and <= '9')
            {
                digits++;
            }
            else
            {
                return false;
            }
        }

        return marks <= 1 && (digits > 0 || allowEmpty);
    }

    /// <summary>The picker as an automation combo box whose value is the shown text (also "Mixed"), editable or not.</summary>
    private sealed class RibbonPickerPeer(RibbonPicker owner) : ComboBoxAutomationPeer(owner), IValueProvider
    {
        public bool IsReadOnly => !owner.IsEditable;

        public string? Value => owner.DisplayText;

        public void SetValue(string? value)
        {
            if (owner.IsEditable && value is not null && IsNumberText(value.Trim(), allowEmpty: false))
            {
                owner.Commit(value.Trim());
            }
        }

        public void ValueChanged(string oldValue, string newValue) =>
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, oldValue, newValue);
    }
}
