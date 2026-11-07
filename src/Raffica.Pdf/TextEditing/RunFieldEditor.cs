using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;

namespace Raffica.Pdf.TextEditing;

/// <summary>
/// The field over a line of the page: one line of text in the size and the colour of the line on a field that contrasts with it (a light line gets a
/// dark field, a dark one a light field, whichever reads better), the app ink as its edge. The field owns the keys of the draft: Enter writes (unless an
/// IME composition is open, which Enter belongs to), Esc drops, Tab and Shift+Tab write and leave the field for the page (the focus is never trapped);
/// Ctrl+Shift+G (Cmd+Shift+G) turns the whole-line switch on and off; everything else, the arrows, Ctrl+Z, Ctrl+A, AltGr combinations, stays with the <see cref="TextBox"/>. It decides nothing itself:
/// <see cref="TextEditInteraction"/> reacts to the events. Used on the UI thread only.
/// </summary>
internal sealed class RunFieldEditor : UserControl
{
    public RunFieldEditor()
    {
        Field = new TextBox
        {
            Name = "PART_Field",
            AcceptsReturn = false,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(2, 0),
            // One more than a line may have, so a text over the limit still reaches the refusal with its note; a paste is cut at the same place.
            MaxLength = Raffica.Core.TextEdit.TextEditLimits.MaxCharsPerRun + 1,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Field.Classes.Add("textbox-editor");
        AutomationProperties.SetName(Field, PdfStrings.TextEditFieldName);
        Content = Field;
        Field.AddHandler(KeyDownEvent, OnFieldKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Field.TextChanged += OnTextChanged;
        Field.LostFocus += OnLostFocus;
    }

    /// <summary>Raised when the user asks to write the line (Enter).</summary>
    public event Action? CommitRequested;

    /// <summary>Raised when the user asks to drop the draft (Esc).</summary>
    public event Action? DiscardRequested;

    /// <summary>Raised when the user leaves the field with Tab or Shift+Tab: the line is written and the page gets the focus.</summary>
    public event Action? LeaveRequested;

    /// <summary>Raised when the user asks to turn the whole-line switch on or off (Ctrl+Shift+G, Cmd+Shift+G on a Mac).</summary>
    public event Action? WholeRunToggleRequested;

    /// <summary>Raised when the field lost the keyboard focus (to the page, another control or a card).</summary>
    public event Action? FocusLeft;

    /// <summary>Raised when the text changed.</summary>
    public event Action? TextEdited;

    /// <summary>The field.</summary>
    public TextBox Field { get; }

    /// <summary>Sets the text the field starts with (the line) and puts the caret at its end.</summary>
    /// <param name="text">The text.</param>
    public void SetStartText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Field.Text = text;
        Field.CaretIndex = text.Length;
    }

    /// <summary>Sets the colour of the text (the colour of the line) and the field behind it: the one of white and black that contrasts more.</summary>
    /// <param name="color">The fill colour of the line.</param>
    public void SetColor(AnnotationColor color)
    {
        var text = Color.FromRgb(color.R, color.G, color.B);
        var onWhite = Contrast(text, Colors.White);
        var onBlack = Contrast(text, Colors.Black);
        var back = onBlack > onWhite ? Colors.Black : Colors.White;
        Field.Foreground = new SolidColorBrush(text);
        Field.Background = new SolidColorBrush(back);
        Field.CaretBrush = new SolidColorBrush(text);
        Field.SelectionBrush = new SolidColorBrush(text, 0.35);
        Field.SelectionForegroundBrush = new SolidColorBrush(back);
    }

    /// <summary>Sets the size of the field in DIPs: its width, its height and the font size (the points of the line times the zoom).</summary>
    public void SetSize(double width, double height, double fontSize)
    {
        Field.Width = Math.Max(1, width);
        Field.MinHeight = Math.Max(1, height);
        Field.FontSize = Math.Max(1, fontSize);
    }

    /// <summary>The width in DIPs the text wants at the current font size (one line, so the field can grow to the right).</summary>
    public double NeededWidth()
    {
        var width = Field.Width;
        Field.Width = double.NaN;
        Field.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var needed = Field.DesiredSize.Width;
        Field.Width = width;
        return needed;
    }

    // Whether an IME composition is open: Enter then belongs to the composition (it commits the pre-edit text), not to the draft.
    private bool IsComposing() => Field.GetVisualDescendants().OfType<TextPresenter>().Any(p => !string.IsNullOrEmpty(p.PreeditText));

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && !IsComposing())
        {
            e.Handled = true;
            DiscardRequested?.Invoke();
        }
        else if (e.Key == Key.G && e.KeyModifiers == (Raffica.Shell.Localization.KeyNames.CommandModifier | KeyModifiers.Shift))
        {
            e.Handled = true;
            WholeRunToggleRequested?.Invoke();
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && !IsComposing())
        {
            e.Handled = true;
            CommitRequested?.Invoke();
        }
        else if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && !IsComposing())
        {
            e.Handled = true;
            LeaveRequested?.Invoke();
        }
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e) => TextEdited?.Invoke();

    private void OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => FocusLeft?.Invoke();

    // WCAG contrast ratio of two colours.
    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }
}
