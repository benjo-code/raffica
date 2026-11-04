using Avalonia;
using Avalonia.Controls;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Text;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

/// <summary>A clock the test moves by hand (the arming delay of the link card).</summary>
internal sealed class ManualTime : TimeProvider
{
    private long _milliseconds;

    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _milliseconds;

    public void Advance(int milliseconds) => _milliseconds += milliseconds;
}

/// <summary>
/// An opened document in a headless window inside a dialog host, with the text interaction of the app on its page view: a launcher that
/// only records the addresses it was given and a clock the test moves. The view fills the window, so a point of the view is a point of the window.
/// </summary>
internal sealed class TextFixture : IDisposable
{
    private readonly ViewFixture _inner;

    private TextFixture(ViewFixture inner, DialogHost host, TextInteraction text, ManualTime time, List<Uri> launched)
    {
        _inner = inner;
        Host = host;
        Text = text;
        Time = time;
        Launched = launched;
    }

    public DialogHost Host { get; }

    public TextInteraction Text { get; }

    public ManualTime Time { get; }

    public List<Uri> Launched { get; }

    public PageView View => _inner.View;

    public PdfTab Tab => _inner.Tab;

    public Window Window => _inner.Window;

    public static async Task<TextFixture> OpenAsync(string file, double width = 800, double height = 900, double? zoom = null)
    {
        var inner = await ViewFixture.OpenAsync(file, width, height, show: false);
        var host = new DialogHost { Content = inner.View };
        inner.Window.Content = host;
        inner.Window.Show();
        ViewFixture.Pump();
        if (zoom is { } z)
        {
            inner.View.SetZoom(z);
        }

        await inner.SettleAsync();
        var time = new ManualTime();
        var launched = new List<Uri>();
        var text = new TextInteraction(inner.Tab, inner.View, new DialogQueue(host), launched.Add, time);
        await text.Selection.EnsureVisibleTextAsync();
        return new TextFixture(inner, host, text, time, launched);
    }

    /// <summary>The text of a page, loaded.</summary>
    public async Task<PageText> TextOfAsync(int page) =>
        await Tab.Session.EnsurePageTexts()!.GetAsync(page, cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>The point of the view at the left (or right) edge of a character, in the middle of its line.</summary>
    public Point CharPoint(PageText text, int index, bool right)
    {
        var box = text.Chars[index].Box;
        return View.ToView(text.PageIndex, right ? box.Right - 0.5 : box.Left + 0.5, (box.Top + box.Bottom) / 2)!.Value;
    }

    public (byte[] Pixels, int Stride) CaptureView() => _inner.Capture();

    public void Dispose()
    {
        Text.Dispose();
        _inner.Dispose();
    }
}
