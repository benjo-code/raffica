using Avalonia.Controls;
using Avalonia.Input;

namespace Raffica.Shell.Controls;

/// <summary>
/// The select (dropdown) of the shell: a <see cref="ComboBox"/> with the shell theme whose list also opens on Down and Up
/// while it is closed. The framework's combo box changes the selection with those keys instead, which is the wrong reading
/// for the keyboard model of the suite (Enter, Space, F4, Down and Up open the list; Esc closes it; Up and Down inside the
/// list move the highlight, Enter takes it). Mouse wheel behaviour is unchanged.
/// </summary>
public class SelectBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && !IsDropDownOpen && !IsEditable && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Down or Key.Up)
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
