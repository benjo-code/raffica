using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Raffica.Core.Forms;

namespace Raffica.Pdf.Forms;

/// <summary>
/// The Avalonia element over a form widget: a single-line or multi-line <see cref="TextBox"/> (masked for a password field; the whole text of
/// a comb field), an editable or fixed <see cref="ComboBox"/>, or a <see cref="ListBox"/> that takes several choices when the field does. It
/// is light paper with dark text whatever the theme and the page theme are (it sits over a form that is drawn on paper), carries the display
/// name of the field as its automation name and has a live hint below it that names what is wrong with the text. It owns the keys of the
/// draft (Enter in a single-line field, Ctrl+Enter, Esc), decides nothing else and writes nothing: <see cref="FormInteraction"/> reacts to
/// the events. The text box is not given a <c>MaxLength</c>: the control would shorten a pasted text silently, and a refused text is refused
/// as a whole (the rules are Core's <c>FormValueRules</c>, the hint says what they found). UI thread only.
/// </summary>
internal sealed class FormFieldEditor : UserControl
{
    private readonly Border _hint;
    private readonly TextBlock _hintText;
    private readonly List<string> _labels;
    private readonly bool _multiline;
    private bool _initializing = true;

    public FormFieldEditor(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Field = field;
        _multiline = field.Traits.HasFlag(FormFieldTraits.Multiline);
        _labels = [.. field.Options.Select(o => o.Label)];
        Input = field.Kind switch
        {
            FormFieldKind.Text => BuildText(field),
            FormFieldKind.ComboBox => BuildCombo(field),
            FormFieldKind.ListBox => BuildList(field),
            _ => throw new ArgumentException("A check box, a radio button and the kinds Raffica does not write have no editor.", nameof(field)),
        };
        Input.Name = "PART_Input";
        Input.MinWidth = 0;
        Input.MinHeight = 0;
        AutomationProperties.SetName(Input, field.DisplayName);
        AutomationProperties.SetName(this, field.DisplayName);

        _hintText = new TextBlock();
        _hint = new Border { Child = _hintText, IsVisible = false };
        _hint.Classes.Add("textbox-hint");
        AutomationProperties.SetLiveSetting(_hint, AutomationLiveSetting.Assertive);
        Content = new StackPanel { Children = { Input, _hint } };

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Input.LostFocus += OnLostFocus;
        _initializing = false;
    }

    /// <summary>Raised when the user asks to write the draft: Enter in a single-line field (the argument is <see langword="true"/>: go on to the next field), Ctrl+Enter or Cmd+Enter anywhere (<see langword="false"/>).</summary>
    public event Action<bool>? CommitRequested;

    /// <summary>Raised when the user asks to drop the draft (Esc).</summary>
    public event Action? DiscardRequested;

    /// <summary>Raised when the keyboard focus left the element (to the page, another control or a card).</summary>
    public event Action? FocusLeft;

    /// <summary>Raised when the text of a text field or the typed text of an editable combo box changed.</summary>
    public event Action? TextEdited;

    /// <summary>Raised when a choice was made in a combo box or a list box (the choice is to be written at once).</summary>
    public event Action? ChoiceMade;

    /// <summary>The widget as it was when the editor was made.</summary>
    public FormField Field { get; }

    /// <summary>The control that takes the input: a <see cref="TextBox"/>, a <see cref="ComboBox"/> or a <see cref="ListBox"/>.</summary>
    public Control Input { get; }

    /// <summary>Whether the draft is text (a text field or the typed text of an editable combo box) and so is written when it is left.</summary>
    public bool IsTextDraft => Input is TextBox || (Input is ComboBox { IsEditable: true });

    /// <summary>Whether a choice is written the moment it is made (combo box, list box).</summary>
    public bool CommitsOnChoice => Input is ComboBox or ListBox;

    /// <summary>The hint shown below the input; empty while there is none.</summary>
    public string Hint => _hint.IsVisible ? _hintText.Text ?? string.Empty : string.Empty;

    /// <summary>The text of the draft with line breaks as LF, or <see langword="null"/> for an input that has none (a list box, a combo box that is not editable).</summary>
    public string? DraftText => Input switch
    {
        TextBox box => Normalize(box.Text ?? string.Empty),
        ComboBox { IsEditable: true } combo => Normalize(combo.Text ?? string.Empty),
        _ => null,
    };

    /// <summary>
    /// The value the draft stands for: the text, the chosen option of a combo box (as text when it is editable and the text is no option) or
    /// the chosen options of a list box; <see langword="null"/> for a combo box with nothing chosen and nothing typed.
    /// </summary>
    public FormFieldState? ReadState()
    {
        switch (Input)
        {
            case TextBox box:
                return FormFieldState.OfText(Normalize(box.Text ?? string.Empty));
            case ComboBox combo:
                var typed = combo.IsEditable ? Normalize(combo.Text ?? string.Empty) : null;
                if (combo.SelectedIndex >= 0 && (typed is null || string.Equals(typed, _labels[combo.SelectedIndex], StringComparison.Ordinal)))
                {
                    return FormFieldState.OfSelection([combo.SelectedIndex]);
                }

                return string.IsNullOrEmpty(typed) ? null : FormFieldState.OfText(typed);
            case ListBox list:
                return FormFieldState.OfSelection([.. list.Selection.SelectedIndexes.Where(i => i >= 0 && i < _labels.Count).Order()]);
            default:
                return null;
        }
    }

    /// <summary>Sets the size of the input in DIPs and the font size (the page points times the zoom).</summary>
    public void Place(double width, double height, double fontSize)
    {
        Input.Width = Math.Max(1, width);
        Input.Height = Math.Max(1, height);
        if (Input is TemplatedControl templated)
        {
            templated.FontSize = Math.Max(1, fontSize);
        }
    }

    /// <summary>Gives the input the keyboard focus (a combo box that is not editable also opens its list).</summary>
    public void FocusInput()
    {
        Input.Focus();
        if (Input is ComboBox { IsEditable: false } combo)
        {
            combo.IsDropDownOpen = true;
        }
        else if (Input is TextBox box)
        {
            box.CaretIndex = (box.Text ?? string.Empty).Length;
        }
    }

    /// <summary>Shows a hint below the input.</summary>
    public void ShowHint(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _hintText.Text = message;
        _hint.IsVisible = true;
        InvalidateMeasure();
    }

    /// <summary>Hides the hint.</summary>
    public void ClearHint()
    {
        if (_hint.IsVisible)
        {
            _hint.IsVisible = false;
            _hintText.Text = string.Empty;
            InvalidateMeasure();
        }
    }

    /// <summary>The text of a refusal: names the characters, the limit or the reason, in the language of the app.</summary>
    public static string MessageOf(FormEditResult.Refused refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        return refused.Problem switch
        {
            FormEditProblem.UnsupportedCharacters => Format(PdfStrings.FormRejectedFormat, refused.Detail),
            FormEditProblem.ControlCharacter => Format(PdfStrings.FormControlCharacterFormat, refused.Detail),
            FormEditProblem.TooLong => Format(PdfStrings.FormTooLongFormat, refused.Detail),
            FormEditProblem.LineBreakNotAllowed => PdfStrings.FormOneLineOnly,
            FormEditProblem.NotKept => PdfStrings.FormNotKept,
            FormEditProblem.NotKeptNotRestored => PdfStrings.FormNotKeptNotRestored,
            FormEditProblem.Locked => PdfStrings.FormLocked,
            FormEditProblem.NotFound => PdfStrings.FormNotFound,
            FormEditProblem.HistoryFull => PdfStrings.FormHistoryFull,
            _ => PdfStrings.FormInvalid,
        };
    }

    /// <summary>The line breaks of a text as LF (the editor works with LF; the form engine writes its own).</summary>
    internal static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Format(string pattern, string detail) => pattern.Replace("{0}", detail, StringComparison.Ordinal);

    // Light paper, dark text, the app ink as the edge: set here and not by theme, the element sits over a page.
    private static void Dress(TemplatedControl control)
    {
        control.Background = Brushes.White;
        control.Foreground = Brushes.Black;
        control.Classes.Add("form-editor");
    }

    private TextBox BuildText(FormField field)
    {
        var box = new TextBox
        {
            AcceptsReturn = _multiline,
            AcceptsTab = false,
            TextWrapping = _multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            NewLine = "\n",
            Text = Normalize(field.State.Text ?? string.Empty),
        };
        if (field.Traits.HasFlag(FormFieldTraits.Password))
        {
            box.PasswordChar = '•';
        }

        if (_multiline)
        {
            box.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        }
        else
        {
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Padding = new Thickness(3, 0);
        }

        box.Classes.Add("textbox-editor");
        Dress(box);
        box.CaretIndex = (box.Text ?? string.Empty).Length;
        box.TextChanged += OnTextChanged;
        return box;
    }

    private ComboBox BuildCombo(FormField field)
    {
        var combo = new ComboBox
        {
            ItemsSource = _labels,
            IsEditable = field.Traits.HasFlag(FormFieldTraits.Edit),
            Padding = new Thickness(3, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (field.State.SelectedOptions.Count > 0 && field.State.SelectedOptions[0] is var chosen && chosen >= 0 && chosen < _labels.Count)
        {
            combo.SelectedIndex = chosen;
        }
        else if (combo.IsEditable)
        {
            combo.Text = Normalize(field.State.Text ?? string.Empty);
        }

        Dress(combo);
        combo.SelectionChanged += OnSelectionChanged;
        if (combo.IsEditable)
        {
            combo.PropertyChanged += OnComboPropertyChanged;
        }

        return combo;
    }

    private ListBox BuildList(FormField field)
    {
        var multi = field.Traits.HasFlag(FormFieldTraits.MultiSelect);
        var list = new ListBox
        {
            ItemsSource = _labels,
            SelectionMode = multi ? SelectionMode.Multiple | SelectionMode.Toggle : SelectionMode.Single,
        };
        foreach (var index in field.State.SelectedOptions.Where(i => i >= 0 && i < _labels.Count).Distinct())
        {
            list.Selection.Select(index);
        }

        Dress(list);
        list.SelectionChanged += OnSelectionChanged;
        return list;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            DiscardRequested?.Invoke();
        }
        else if (e.Key == Key.Enter && e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta && IsTextDraft)
        {
            e.Handled = true;
            CommitRequested?.Invoke(false);
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && IsTextDraft && !_multiline)
        {
            e.Handled = true;
            CommitRequested?.Invoke(true);
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        ClearHint();
        TextEdited?.Invoke();
    }

    private void OnComboPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ComboBox.TextProperty && !_initializing)
        {
            ClearHint();
            TextEdited?.Invoke();
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initializing)
        {
            ChoiceMade?.Invoke();
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (Input is TextBox)
        {
            FocusLeft?.Invoke();
            return;
        }

        // The focus of a combo box moves into its list and back: it has left when it is neither in the element nor in an open list.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!Input.IsKeyboardFocusWithin && Input is not ComboBox { IsDropDownOpen: true })
                {
                    FocusLeft?.Invoke();
                }
            },
            DispatcherPriority.Input);
    }
}
