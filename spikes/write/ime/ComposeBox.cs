using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace ImeSpike;

/// <summary>
/// A custom text control (not Avalonia's TextBox): it draws its own text and the preedit, takes committed text from TextInput and
/// implements Avalonia's TextInputMethodClient so an input method can compose into it. Every call the input method makes is logged.
/// </summary>
public sealed class ComposeBox : Control
{
    readonly Action<string> _log;
    readonly Client _client;
    string _text = "";
    int _caret;
    string _preedit = "";
    int _preeditCursor;
    static readonly Typeface s_face = new("Segoe UI, Noto Sans CJK JP, Noto Sans, sans-serif");
    const double FontSize = 20;

    public ComposeBox(Action<string> log)
    {
        _log = log;
        _client = new Client(this);
        Focusable = true;
        Background = Brushes.White;
        TextInputMethodClientRequested += (_, e) => { e.Client = _client; log("client requested"); };
    }

    public IBrush? Background { get; set; }

    internal string Text => _text;

    TextLayout Layout()
    {
        string shown = _text.Insert(_caret, _preedit);
        if (shown.Length == 0) shown = " ";
        var overrides = new List<ValueSpan<TextRunProperties>>();
        if (_preedit.Length > 0)
            overrides.Add(new ValueSpan<TextRunProperties>(_caret, _preedit.Length,
                new GenericTextRunProperties(s_face, FontSize, TextDecorations.Underline, Brushes.Black)));
        return new TextLayout(shown, s_face, FontSize, Brushes.Black, TextAlignment.Left, TextWrapping.NoWrap, TextTrimming.None,
            null, FlowDirection.LeftToRight, double.PositiveInfinity, double.PositiveInfinity, double.NaN, 0, 0, null, overrides);
    }

    /// <summary>The caret rectangle in this control's coordinates; inside a preedit it follows the preedit cursor.</summary>
    internal Rect CaretRect()
    {
        var layout = Layout();
        int index = _caret + (_preedit.Length > 0 ? _preeditCursor : 0);
        var r = layout.HitTestTextPosition(Math.Min(index, _text.Length + _preedit.Length));
        return new Rect(8 + r.X, 4 + r.Y, 1, Math.Max(r.Height, FontSize * 1.3));
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1), new Rect(Bounds.Size));
        Layout().Draw(context, new Point(8, 4));
        if (IsFocused)
        {
            var c = CaretRect();
            context.DrawLine(new Pen(Brushes.Black, 1), c.TopLeft, c.BottomLeft);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(availableSize.Width, 520), 40);

    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); _log("focus"); InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); _log("blur"); InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); Focus(); }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        _preedit = "";
        _text = _text.Insert(_caret, e.Text);
        _caret += e.Text.Length;
        e.Handled = true;
        _log($"commit \"{e.Text}\" -> text \"{_text}\"");
        Changed();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_preedit.Length > 0) return; // the input method owns the keys while composing
        switch (e.Key)
        {
            case Key.Back when _caret > 0: _text = _text.Remove(_caret - 1, 1); _caret--; Changed(); e.Handled = true; break;
            case Key.Left when _caret > 0: _caret--; Changed(); e.Handled = true; break;
            case Key.Right when _caret < _text.Length: _caret++; Changed(); e.Handled = true; break;
        }
    }

    void Changed()
    {
        InvalidateVisual();
        _client.RaiseCursorRectangle();
    }

    void SetPreedit(string? text, int? cursor)
    {
        _preedit = text ?? "";
        _preeditCursor = cursor ?? _preedit.Length;
        _log($"preedit \"{_preedit}\" cursor {(cursor?.ToString() ?? "none")}");
        Changed();
    }

    sealed class Client : TextInputMethodClient
    {
        readonly ComposeBox _box;
        public Client(ComposeBox box) => _box = box;

        public override Visual TextViewVisual => _box;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;
        public override string SurroundingText => _box._text;
        public override Rect CursorRectangle => _box.CaretRect();
        public override TextSelection Selection
        {
            get => new(_box._caret, _box._caret);
            set { _box._caret = Math.Clamp(value.End, 0, _box._text.Length); _box._log($"selection set {value.Start}..{value.End}"); }
        }

        public override void SetPreeditText(string? preeditText) => _box.SetPreedit(preeditText, null);
        public override void SetPreeditText(string? preeditText, int? cursorPos) => _box.SetPreedit(preeditText, cursorPos);

        internal void RaiseCursorRectangle() => RaiseCursorRectangleChanged();
    }
}
