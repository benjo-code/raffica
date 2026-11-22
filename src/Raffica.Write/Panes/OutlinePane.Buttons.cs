using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Write.Commands;

namespace Raffica.Write.Panes;

/// <summary>The header of the outline pane: the four outline commands (move up, move down, promote, demote) as icon buttons that act on the selected row.</summary>
internal sealed partial class OutlinePane
{
    private static readonly string[] ButtonIds = [WriteCommandIds.MoveUp, WriteCommandIds.MoveDown, WriteCommandIds.Promote, WriteCommandIds.Demote];

    private readonly Dictionary<string, Button> _buttons = [];
    private OutlinePaneActions? _actions;

    /// <summary>What the pane asks of its window; set once by the window that holds the pane. The buttons, the Alt and Shift keys and the drag do nothing without it.</summary>
    internal OutlinePaneActions? Actions
    {
        get => _actions;
        set
        {
            _actions = value;
            RefreshButtons();
        }
    }

    /// <summary>The header button of an outline command (for tests).</summary>
    internal Button ButtonOf(string id) => _buttons[id];

    /// <summary>
    /// Sets each button from the rules for the selected row: enabled, or disabled with the reason as its tooltip and its name for assistive technology. The same command, the same words as the ribbon.
    /// The facts behind the rules are remembered per model revision and row, so this is cheap to ask on every selection change and after every read of the list.
    /// </summary>
    internal void RefreshButtons()
    {
        foreach (var id in ButtonIds)
        {
            if (!_buttons.TryGetValue(id, out var button))
            {
                return;
            }

            var label = WriteCommandCatalog.Commands.First(c => c.Id == id).Name;
            string? reason = null;
            var allowed = false;
            if (_disposed || _actions is not { } actions)
            {
                // Not wired (a pane on its own): nothing to run.
            }
            else if (_list.SelectedItem is null)
            {
                reason = WriteStrings.OutlineNoRowSelected;
            }
            else
            {
                allowed = actions.Allows(this, id);
                reason = allowed ? null : actions.ReasonFor(this, id);
            }

            button.IsEnabled = allowed;
            var text = reason ?? label;
            ToolTip.SetTip(button, text);
            AutomationProperties.SetName(button, text);
        }
    }

    private StackPanel CreateHeader()
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4, 4, 4, 2) };
        header.Classes.Add("outline-actions");
        AutomationProperties.SetName(header, WriteStrings.OutlineActionsName);
        foreach (var id in ButtonIds)
        {
            var command = WriteCommandCatalog.Commands.First(c => c.Id == id);
            var icon = new StrokeIcon { Size = 16 };
            icon.Bind(StrokeIcon.DataProperty, icon.GetResourceObservable("Raffica.Icon." + command.Icon, value => value as Geometry));
            var button = new Button { Content = icon, Width = 32 };
            button.Classes.Add("icon");
            ToolTip.SetShowOnDisabled(button, true);
            var commandId = id;
            button.Click += (_, _) => RunButton(commandId);
            _buttons[id] = button;
            header.Children.Add(button);
        }

        return header;
    }

    // A click: the window runs the command for the selected row and shows the reason when the rules refuse it.
    private void RunButton(string id)
    {
        if (_disposed || _actions is not { } actions || _list.SelectedItem is null)
        {
            return;
        }

        if (!actions.Run(this, id))
        {
            RefreshButtons();
        }
    }
}
