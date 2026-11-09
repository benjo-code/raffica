using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Raffica.Pdf.Text;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

/// <summary>
/// Copy goes to the clipboard of the headless platform (Avalonia.Headless: an in-process clipboard of the window), not to the one of the
/// machine; a paste into another application is a manual check.
/// </summary>
public sealed class CopyTests
{
    [AvaloniaFact]
    public async Task CopyPutsPlainAndHtmlOnTheClipboard()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf", 800, 900, zoom: 0.5);
        var clipboard = f.Window.Clipboard!;
        await clipboard.ClearAsync();
        f.Text.Selection.SelectAll();

        var copied = await f.Text.Selection.CopyAsync(clipboard);

        Assert.True(copied);
        var plain = await clipboard.TryGetTextAsync();
        var html = await clipboard.TryGetValueAsync(PageClipboard.Html);
        Assert.NotNull(plain);
        Assert.Contains("Handbuch", plain, StringComparison.Ordinal);
        Assert.Equal((await f.Text.Selection.GetTextAsync())!.Plain, plain);
        Assert.NotNull(html);
        Assert.Contains("<p>", html, StringComparison.Ordinal);
        Assert.Contains("Handbuch", html, StringComparison.Ordinal);
        Assert.Equal((await f.Text.Selection.GetTextAsync())!.Html, html);
    }

    [AvaloniaFact]
    public async Task CopyWithoutASelectionLeavesTheClipboardAlone()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var clipboard = f.Window.Clipboard!;
        await clipboard.SetTextAsync("untouched");

        Assert.False(await f.Text.Selection.CopyAsync(clipboard));

        Assert.Equal("untouched", await clipboard.TryGetTextAsync());
    }

    [AvaloniaFact]
    public async Task TheHtmlIsEscaped()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        f.Text.Selection.SelectAll();
        var html = (await f.Text.Selection.GetTextAsync())!.Html;
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowsHtmlFormatCarriesTheOffsetsOfTheFragment()
    {
        var bytes = PageClipboard.HtmlFormatBytes("<p>Grüße</p>\n");
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("Version:0.9\r\n", text, StringComparison.Ordinal);
        int Value(string key)
        {
            var at = text.IndexOf(key + ":", StringComparison.Ordinal) + key.Length + 1;
            return int.Parse(text.AsSpan(at, 10), System.Globalization.CultureInfo.InvariantCulture);
        }

        // The offsets count bytes of the UTF-8 text.
        Assert.Equal("<p>Grüße</p>\n", System.Text.Encoding.UTF8.GetString(bytes, Value("StartFragment"), Value("EndFragment") - Value("StartFragment")));
        Assert.Equal(bytes.Length, Value("EndHTML"));
        Assert.StartsWith("<html>", System.Text.Encoding.UTF8.GetString(bytes, Value("StartHTML"), 6), StringComparison.Ordinal);
    }
}
