using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Raffica.Pdf.Text;

/// <summary>What the user copied: the text as prose and the same text as an HTML fragment.</summary>
/// <param name="Plain">Core's <c>TextFlow.Plain</c>.</param>
/// <param name="Html">Core's <c>TextFlow.Html</c>.</param>
internal sealed record SelectedText(string Plain, string Html);

/// <summary>
/// Puts copied text on the clipboard as <c>text/plain</c> and <c>text/html</c> in one item. The HTML goes under the name the platform
/// uses (the MIME type, the uniform type identifier on macOS); on Windows it also goes in the form other programs read there, the
/// "HTML Format" with its header of byte offsets.
/// </summary>
internal static class PageClipboard
{
    private const string WindowsHtmlName = "HTML Format";

    // Wraps the fragment; the markers tell a reader where the fragment starts and ends.
    private const string HtmlPrefix = "<html><body>\r\n<!--StartFragment-->";
    private const string HtmlSuffix = "<!--EndFragment-->\r\n</body></html>";

    /// <summary>The HTML fragment as a string (<c>text/html</c>; <c>public.html</c> on macOS).</summary>
    public static readonly DataFormat<string> Html = DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "text/html");

    private static readonly DataFormat<byte[]> WindowsHtml = DataFormat.CreateBytesPlatformFormat(WindowsHtmlName);

    /// <summary>Replaces the clipboard with the text in both forms.</summary>
    public static async Task SetAsync(IClipboard clipboard, SelectedText text)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(text);
        var item = new DataTransferItem();
        item.SetText(text.Plain);
        item.Set(Html, text.Html);
        if (OperatingSystem.IsWindows())
        {
            item.Set(WindowsHtml, HtmlFormatBytes(text.Html));
        }

        var transfer = new DataTransfer();
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
    }

    /// <summary>
    /// The clipboard form of Windows for HTML: a header with the byte offsets (ten digits each) of the HTML text and of the fragment in
    /// it, then the HTML in UTF-8.
    /// </summary>
    internal static byte[] HtmlFormatBytes(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        var prefix = Encoding.UTF8.GetByteCount(HtmlPrefix);
        var body = Encoding.UTF8.GetByteCount(fragment);
        var suffix = Encoding.UTF8.GetByteCount(HtmlSuffix);

        // The header has a fixed length: every number takes ten digits.
        var headerLength = Header(0, 0, 0, 0).Length;
        var startHtml = headerLength;
        var startFragment = headerLength + prefix;
        var endFragment = startFragment + body;
        var endHtml = endFragment + suffix;
        return Encoding.UTF8.GetBytes(Header(startHtml, endHtml, startFragment, endFragment) + HtmlPrefix + fragment + HtmlSuffix);
    }

    private static string Header(int startHtml, int endHtml, int startFragment, int endFragment) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Version:0.9\r\nStartHTML:{startHtml:D10}\r\nEndHTML:{endHtml:D10}\r\nStartFragment:{startFragment:D10}\r\nEndFragment:{endFragment:D10}\r\n");
}
