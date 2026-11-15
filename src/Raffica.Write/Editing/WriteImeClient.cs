using Avalonia;
using Avalonia.Input.TextInput;
using Avalonia.VisualTree;
using Raffica.Write.Viewing;

namespace Raffica.Write.Editing;

/// <summary>
/// What the platform's input method sees of the page view: a <see cref="TextInputMethodClient"/> that supports a preedit and surrounding text. The view answers
/// <c>TextInputMethodClientRequested</c> with it when the platform asks (on focus); nothing of it exists before that. It holds no state of its own: every member asks
/// the view, which owns the caret, the layout and the composition.
/// <list type="bullet">
/// <item><see cref="SurroundingText"/> is the text of the caret's paragraph around the caret (at most <see cref="PageView.SurroundingLimit"/> UTF-16 code units, never
/// the whole document, never splitting a surrogate pair); <see cref="Selection"/> is relative to it. The preedit is not part of it.</item>
/// <item><see cref="CursorRectangle"/> is the caret bar in the coordinates of the view (<see cref="TextViewVisual"/>), inside the preedit while composing, so that the
/// candidate window stands at the cursor; it is read from the layout every time and announced after every change of the caret, the layout, the scroll position and the zoom.</item>
/// <item><c>SetPreeditText</c> shows the text inside the paragraph (underlined, reflowing the line) and never writes it to the document; the committed text arrives as text
/// input and is typed like any text. The cursor of the preedit is drawn by Write itself, because the platforms report it differently (or not at all: the end of the text then).</item>
/// </list>
/// </summary>
internal sealed class WriteImeClient : TextInputMethodClient
{
    private readonly PageView _view;

    public WriteImeClient(PageView view, ImeLog log)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _view = view;
        Log = log;
    }

    /// <summary>The log of this client (lengths only; see <see cref="ImeLog"/>).</summary>
    internal ImeLog Log { get; }

    public override Visual TextViewVisual => _view;

    public override bool SupportsPreedit => true;

    public override bool SupportsSurroundingText => true;

    public override string SurroundingText => _view.ImeSurrounding().Text;

    public override Rect CursorRectangle => _view.ImeCursorRectangle();

    public override TextSelection Selection
    {
        get
        {
            var surrounding = _view.ImeSurrounding();
            return new TextSelection(surrounding.Start, surrounding.End);
        }
        set => _view.ImeSetSelection(value.Start, value.End);
    }

    public override void SetPreeditText(string? preeditText) => _view.ImePreedit(preeditText, null);

    public override void SetPreeditText(string? preeditText, int? cursorPos) => _view.ImePreedit(preeditText, cursorPos);

    /// <summary>Tells the platform that the cursor rectangle changed (it asks for it again).</summary>
    internal void NotifyCursor() => RaiseCursorRectangleChanged();

    /// <summary>Tells the platform that the surrounding text and the selection may have changed, and the cursor with them.</summary>
    internal void NotifyText()
    {
        RaiseSurroundingTextChanged();
        RaiseSelectionChanged();
        RaiseCursorRectangleChanged();
    }

    /// <summary>Asks the platform to end its composition (a click moved the caret away).</summary>
    internal void RequestPlatformReset() => RequestReset();
}
