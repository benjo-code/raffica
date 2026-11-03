using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Gallery.Sections;

/// <summary>
/// Buttons and choices in the states of the state table (rest, hover, pressed or on, keyboard focus, disabled), one row per
/// control and variant. Hover, pressed and focus are forced with <see cref="StateMatrix"/>.
/// </summary>
internal sealed partial class ButtonsSection : UserControl
{
    private enum State
    {
        Rest,
        Hover,
        PressedOn,
        Focus,
        Disabled,
    }

    private int _row;

    public ButtonsSection()
    {
        InitializeComponent();
        Title.Text = GalleryStrings.SectionButtons;

        AddHeader();
        AddRow(GalleryStrings.RowButton, state => Apply(new Button { Content = GalleryStrings.SampleInsert }, state));
        AddRow(GalleryStrings.RowBoxed, state => Apply(Text(new Button { Content = GalleryStrings.SampleBrowse }, "boxed"), state));
        AddRow(GalleryStrings.RowPrimary, state => Apply(Text(new Button { Content = GalleryStrings.SampleInsert }, "primary"), state));
        AddRow(GalleryStrings.RowDestructive, state => Apply(Text(new Button { Content = GalleryStrings.SampleApply }, "destructive"), state));
        AddRow(GalleryStrings.RowIcon, state => Apply(IconButton(), state));
        AddRow(GalleryStrings.RowToggle, state => Apply(Toggle(state), state));
        AddRow(GalleryStrings.RowCheckBox, state => Apply(new CheckBox { Content = GalleryStrings.SampleTrackChanges, IsChecked = state == State.PressedOn }, state));
        AddRow(GalleryStrings.RowRadio, state => Apply(new RadioButton { Content = GalleryStrings.SamplePortrait, IsChecked = state == State.PressedOn }, state));
        AddRow(GalleryStrings.RowSegmented, Segmented);
    }

    private static Button Text(Button button, string variant)
    {
        button.Classes.Add(variant);
        return button;
    }

    private static Button IconButton()
    {
        var button = new Button { Content = Icon("Raffica.Icon.Bold") };
        button.Classes.Add("icon");
        AutomationProperties.SetName(button, GalleryStrings.IconBold);
        return button;
    }

    private static ToggleButton Toggle(State state)
    {
        var toggle = new ToggleButton { Content = Icon("Raffica.Icon.Bold"), IsChecked = state == State.PressedOn };
        toggle.Classes.Add("icon");
        AutomationProperties.SetName(toggle, GalleryStrings.IconBold);
        return toggle;
    }

    private static StrokeIcon Icon(string key)
    {
        Application.Current!.TryGetResource(key, null, out var geometry);
        return new StrokeIcon { Size = 16, Data = geometry as Geometry };
    }

    private static Control Segmented(State current)
    {
        var control = new SegmentedControl
        {
            ItemsSource = new[] { GalleryStrings.SamplePages, GalleryStrings.SampleMarks, GalleryStrings.SampleResults },
            SelectedIndex = current == State.PressedOn ? 1 : 0,
        };
        control.ContainerPrepared += (_, e) =>
        {
            if (current == State.Hover && e.Index == 1)
            {
                StateMatrix.Force(e.Container, ":pointerover");
            }
            else if (current == State.Focus && e.Index == 0)
            {
                StateMatrix.Force(e.Container, ":focus-visible");
            }
        };
        control.IsEnabled = current != State.Disabled;
        return control;
    }

    private static Control Apply(Control control, State state)
    {
        switch (state)
        {
            case State.Hover:
                StateMatrix.Force(control, ":pointerover");
                break;
            case State.PressedOn when control is Button and not ToggleButton:
                StateMatrix.Force(control, ":pointerover", ":pressed");
                break;
            case State.Focus:
                StateMatrix.Force(control, ":focus-visible");
                break;
            case State.Disabled:
                control.IsEnabled = false;
                break;
        }

        return control;
    }

    private void AddHeader()
    {
        Matrix.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] states = [GalleryStrings.StateRest, GalleryStrings.StateHover, GalleryStrings.StatePressedOn, GalleryStrings.StateFocus, GalleryStrings.StateDisabled];
        for (var i = 0; i < states.Length; i++)
        {
            Place(new TextBlock { Text = states[i], Classes = { "label" } }, i + 1, new Thickness(8, 0, 0, 4));
        }

        _row++;
    }

    private void AddRow(string label, Func<State, Control> make)
    {
        Matrix.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Place(new TextBlock { Text = label, Classes = { "ui" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, 0, new Thickness(0, 8));
        foreach (var state in Enum.GetValues<State>())
        {
            // 12 px of air around a cell so the 4 px ring and the neighbours do not touch.
            Place(make(state), (int)state + 1, new Thickness(8, 8, 12, 8));
        }

        _row++;
    }

    private void Place(Control control, int column, Thickness margin)
    {
        control.Margin = margin;
        Grid.SetRow(control, _row);
        Grid.SetColumn(control, column);
        Matrix.Children.Add(control);
    }

}
