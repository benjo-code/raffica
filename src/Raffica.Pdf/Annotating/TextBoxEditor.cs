using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Core.Annotations;
using Raffica.Core.Text;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The editing field of a text box, placed over the box on the page: a multi-line <see cref="TextBox"/> in Geist at the size the text will
/// have on the page (font size times zoom), the colour of the annotation as text on paper white, with the app ink as its edge, and a hint
/// below it that names what is wrong with the text. The field owns the keys of the draft: Ctrl+Enter asks to commit, Esc asks to discard,
/// everything else (Ctrl+Z, Ctrl+A, the arrows) stays with the <see cref="TextBox"/>. It decides nothing itself; the
/// <see cref="TextBoxController"/> reacts to the events. Used on the UI thread only.
/// </summary>
internal sealed class TextBoxEditor : UserControl
{
    // The hint names at most this many offending characters.
    internal const int MaxNamedCharacters = 8;

    private readonly Border _hint;
    private readonly TextBlock _hintText;

    public TextBoxEditor()
    {
        Field = new TextBox
        {
            Name = "PART_Field",
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            NewLine = "\n",
            MaxLength = TextBoxSpec.MaxTextLength,
            Background = Brushes.White,
        };
        Field.Classes.Add("textbox-editor");
        Field.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        AutomationProperties.SetName(Field, PdfStrings.TextBoxFieldName);
        _hintText = new TextBlock();
        _hint = new Border { Child = _hintText, IsVisible = false };
        _hint.Classes.Add("textbox-hint");
        AutomationProperties.SetLiveSetting(_hint, AutomationLiveSetting.Assertive);
        Content = new StackPanel { Children = { Field, _hint } };
        Field.AddHandler(KeyDownEvent, OnFieldKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Field.TextChanged += OnTextChanged;
        Field.LostFocus += OnLostFocus;
    }

    /// <summary>Raised when the user asks to make the text box (Ctrl+Enter).</summary>
    public event Action? CommitRequested;

    /// <summary>Raised when the user asks to drop the draft (Esc).</summary>
    public event Action? DiscardRequested;

    /// <summary>Raised when the field lost the keyboard focus (to the page, another control or a card).</summary>
    public event Action? FocusLeft;

    /// <summary>Raised when the text changed.</summary>
    public event Action? TextEdited;

    /// <summary>The field.</summary>
    public TextBox Field { get; }

    /// <summary>The hint shown below the field; empty while there is none.</summary>
    public string Hint => _hint.IsVisible ? _hintText.Text ?? string.Empty : string.Empty;

    /// <summary>Sets the text the field starts with (an existing text box being edited) and puts the caret at its end.</summary>
    /// <param name="text">The text.</param>
    public void SetStartText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Field.Text = text;
        Field.CaretIndex = text.Length;
        ClearHint();
    }

    /// <summary>Sets the colour of the text (data of the palette) and the paper behind it.</summary>
    /// <param name="color">The colour of the annotation.</param>
    public void SetColor(AnnotationColor color) => Field.Foreground = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

    /// <summary>Sets the size of the field in DIPs: its width, the least height it keeps, the most height it may take and the font size.</summary>
    /// <param name="width">The width.</param>
    /// <param name="minHeight">The height of the box on the page.</param>
    /// <param name="maxHeight">The most height before the text scrolls (the room to the page edge).</param>
    /// <param name="fontSize">The font size (points times zoom).</param>
    public void SetSize(double width, double minHeight, double maxHeight, double fontSize)
    {
        Field.Width = Math.Max(1, width);
        Field.MinHeight = Math.Max(0, minHeight);
        Field.MaxHeight = Math.Max(Field.MinHeight, maxHeight);
        Field.FontSize = Math.Max(1, fontSize);
    }

    /// <summary>The height in DIPs the field wants for its text at the current width and font size.</summary>
    /// <returns>The height.</returns>
    public double NeededHeight()
    {
        var max = Field.MaxHeight;
        Field.MaxHeight = double.PositiveInfinity;
        Field.Measure(new Size(Field.Width, double.PositiveInfinity));
        var needed = Field.DesiredSize.Height;
        Field.MaxHeight = max;
        return needed;
    }

    /// <summary>Shows a hint below the field.</summary>
    /// <param name="message">The text.</param>
    public void ShowHint(string message)
    {
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

    /// <summary>The text of a rejection: the characters (at most eight) or the length limit.</summary>
    /// <param name="check">The result of <see cref="TextBoxSpec.CheckText"/>.</param>
    /// <returns>The message, or an empty string for a text that is fine or empty.</returns>
    public static string MessageOf(TextBoxTextCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.Problem switch
        {
            TextBoxTextProblem.TooLong => PdfStrings.TextBoxTooLong,
            TextBoxTextProblem.TooManyLines => PdfStrings.TextBoxTooManyLines,
            TextBoxTextProblem.UnsupportedCharacters => PdfStrings.TextBoxRejectedFormat.Replace(
                "{0}", string.Join(' ', check.Unsupported.Take(MaxNamedCharacters).Select(Visible)), StringComparison.Ordinal),
            _ => string.Empty,
        };
    }

    // A tab or another control character cannot be seen: it is named by its code.
    private static string Visible(string character) =>
        character.Length > 0 && char.IsControl(character[0]) ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"U+{(int)character[0]:X4}") : character;

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            DiscardRequested?.Invoke();
        }
        else if (e.Key is Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            CommitRequested?.Invoke();
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearHint();
        TextEdited?.Invoke();
    }

    private void OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => FocusLeft?.Invoke();
}
