using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;

namespace Raffica.Gallery.Sections;

/// <summary>
/// The text field and the select in the states of the state table (rest, hover, typing or open, keyboard focus, disabled), one row
/// per control and content. Hover and the keyboard ring are forced with <see cref="StateMatrix"/>; in the row with text the
/// "typing" cell really takes the focus and selects its text, so the selection colour and the caret are on screen.
/// </summary>
internal sealed partial class InputsSection : UserControl
{
    private enum State
    {
        Rest,
        Hover,
        TypingOpen,
        Focus,
        Disabled,
    }

    private int _row;

    public InputsSection()
    {
        InitializeComponent();
        Title.Text = GalleryStrings.SectionInputs;

        AddHeader();
        AddRow(GalleryStrings.RowTextField, state => Apply(Field(GalleryStrings.RowTextField, string.Empty), state, typingFocus: false));
        AddRow(GalleryStrings.RowTextWithValue, state => Apply(Field(GalleryStrings.RowTextWithValue, GalleryStrings.SampleReport), state, typingFocus: true));
        AddRow(GalleryStrings.RowSelect, state => Apply(Select(GalleryStrings.RowSelect, selected: true), state, typingFocus: false));
        AddRow(GalleryStrings.RowSelectPlaceholder, state => Apply(Select(GalleryStrings.RowSelectPlaceholder, selected: false), state, typingFocus: false));
    }

    private static TextBox Field(string name, string text)
    {
        var box = new TextBox { Text = text, PlaceholderText = GalleryStrings.SamplePlaceholder, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static SelectBox Select(string name, bool selected)
    {
        var select = new SelectBox
        {
            ItemsSource = new[] { GalleryStrings.SampleParagraph, GalleryStrings.SampleHeading1, GalleryStrings.SampleHeading2, GalleryStrings.SampleQuote },
            PlaceholderText = GalleryStrings.SampleSelectHint,
            Width = 180,
            SelectedIndex = selected ? 1 : -1,
        };
        AutomationProperties.SetName(select, name);
        return select;
    }

    private static Control Apply(Control control, State state, bool typingFocus)
    {
        switch (state)
        {
            case State.Hover:
                StateMatrix.Force(control, ":pointerover");
                break;
            case State.TypingOpen when control is TextBox box && typingFocus:
                box.Loaded += (_, _) =>
                {
                    box.Focus();
                    box.SelectAll();
                };
                break;
            case State.TypingOpen when control is TextBox:
                StateMatrix.Force(control, ":focus");
                break;
            case State.TypingOpen:
                StateMatrix.Force(control, ":dropdownopen");
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
        string[] states = [GalleryStrings.StateRest, GalleryStrings.StateHover, GalleryStrings.StateTypingOpen, GalleryStrings.StateFocus, GalleryStrings.StateDisabled];
        for (var i = 0; i < states.Length; i++)
        {
            Place(new TextBlock { Text = states[i], Classes = { "label" } }, i + 1, new Thickness(8, 0, 0, 4));
        }

        _row++;
    }

    private void AddRow(string label, Func<State, Control> make)
    {
        Matrix.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Place(new TextBlock { Text = label, Classes = { "ui" }, VerticalAlignment = VerticalAlignment.Center }, 0, new Thickness(0, 8));
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
