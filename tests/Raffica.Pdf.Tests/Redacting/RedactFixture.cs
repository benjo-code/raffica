using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>
/// A temp copy of a test file in a headless window with the text interaction, the redact session of the tab (entered), its pointer and key
/// side and its overlay on the page view: what the window builds when the mode starts, without the window. The view fills the window, so a
/// point of the view is a point of the window.
/// </summary>
internal sealed class RedactFixture : IDisposable
{
    private readonly TempCopy _copy;
    private readonly TextFixture _text;

    private RedactFixture(TempCopy copy, TextFixture text, RedactSession session, RedactInteraction interaction, RedactOverlay overlay, ManualTimers time)
    {
        _copy = copy;
        _text = text;
        Session = session;
        Interaction = interaction;
        Overlay = overlay;
        Time = time;
    }

    public RedactSession Session { get; }

    public RedactInteraction Interaction { get; }

    public RedactOverlay Overlay { get; }

    public ManualTimers Time { get; }

    public TextFixture Text => _text;

    public PageView View => _text.View;

    public PdfTab Tab => _text.Tab;

    public Window Window => _text.Window;

    public string Path => _copy.Path;

    public static async Task<RedactFixture> OpenAsync(string file = "pages-marked.pdf", double width = 800, double height = 900, bool enter = true)
    {
        var copy = new TempCopy(file);
        RedactSession? session = null;
        var text = await TextFixture.OpenAsync(copy.Path, width, height, null, () => session is { IsActive: true, Tool: RedactTool.Area });
        session = text.Tab.Redact;
        var time = new ManualTimers();
        var interaction = new RedactInteraction(text.Tab, text.View, session, text.Text.Selection, time);
        var overlay = new RedactOverlay(session);
        text.View.ModeOverlay = overlay;
        if (enter)
        {
            session.Enter();
            ViewFixture.Pump();
        }

        return new RedactFixture(copy, text, session, interaction, overlay, time);
    }

    /// <summary>The place in the view of a position of a page in points.</summary>
    public Point At(int page, double x, double y) => View.ToView(page, x, y)!.Value;

    public void Down(Point point) => Window.MouseDown(point, MouseButton.Left);

    public void MoveTo(Point point) => Window.MouseMove(point);

    public void Up(Point point) => Window.MouseUp(point, MouseButton.Left);

    public void Drag(Point from, Point to)
    {
        Down(from);
        MoveTo(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        MoveTo(to);
        Up(to);
        ViewFixture.Pump();
    }

    /// <summary>Sends a key to the interaction the way the page view does (the focus is on the view); returns whether it was used.</summary>
    public bool Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = View };
        var used = Interaction.HandleKey(e);
        ViewFixture.Pump();
        return used;
    }

    /// <summary>Selects the text between two characters of a page with the text interaction (the text tool).</summary>
    public async Task<PageText> SelectAsync(int page, int firstChar, int lastChar)
    {
        var text = await _text.TextOfAsync(page);
        var from = _text.CharPoint(text, firstChar, right: false);
        var to = _text.CharPoint(text, lastChar, right: true);
        _text.Text.Selection.Press(from, 1);
        _text.Text.Selection.Drag(to);
        _text.Text.Selection.Release();
        ViewFixture.Pump();
        return text;
    }

    public void Dispose()
    {
        Interaction.Dispose();
        Overlay.Dispose();
        _text.Dispose();
        _copy.Dispose();
    }
}
