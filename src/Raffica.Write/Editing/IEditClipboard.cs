using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The clipboard as the editing commands see it: one write of a <see cref="ClipboardContent"/> and one read. Both are asynchronous (the system clipboard is) and neither throws:
/// a clipboard that is busy, remote or broken reads as "nothing" and writes as "not done". The Avalonia one is <see cref="AvaloniaEditClipboard"/>, tests use a double.
/// </summary>
internal interface IEditClipboard
{
    /// <summary>What a paste would take: the fragment (with the source it came from) and the plain text, or null when the clipboard holds neither or cannot be read.</summary>
    Task<ClipboardContent?> ReadAsync();

    /// <summary>Replaces the clipboard with the content in one data object; false when that failed (nothing is then promised about the old content).</summary>
    Task<bool> WriteAsync(ClipboardContent content);
}

/// <summary>
/// The system clipboard through Avalonia. A copy is one data object with one item that holds <c>text/plain</c> (what every other program reads), the fragment as the platform
/// format <c>application/x-raffica-write-fragment</c> and, beside it, the platform format <c>application/x-raffica-write-source</c>: the id of the editor that made the copy and the
/// package part of the story (a small UTF-8 text, the part after a line feed). The source is outside the fragment bytes, so a fragment that came from another process or window
/// can never claim to come from this document: a paste in the editor that made it keeps styles, numbering and relationship ids, every other paste does not
/// (see <see cref="TextEditor.Paste"/>). Neither call throws: a failing clipboard reads as nothing and writes as false (the exception is not kept, it could hold text).
/// </summary>
internal sealed class AvaloniaEditClipboard(Func<IClipboard?> clipboard) : IEditClipboard
{
    /// <summary>The fragment format.</summary>
    internal static readonly DataFormat<byte[]> Fragment = DataFormat.CreateBytesPlatformFormat("application/x-raffica-write-fragment");

    /// <summary>The raw PNG bytes of a picture on the clipboard (what browsers, screenshot tools and Office put there).</summary>
    internal static readonly DataFormat<byte[]> Png = DataFormat.CreateBytesPlatformFormat("image/png");

    /// <summary>The format of the source (editor id and package part) of a fragment.</summary>
    internal static readonly DataFormat<byte[]> Source = DataFormat.CreateBytesPlatformFormat("application/x-raffica-write-source");

    public async Task<ClipboardContent?> ReadAsync()
    {
        try
        {
            if (clipboard() is not { } system || await system.TryGetDataAsync() is not { } transfer)
            {
                return null;
            }

            using (transfer)
            {
                var text = await transfer.TryGetTextAsync();
                var fragment = await transfer.TryGetValueAsync(Fragment);

                // A picture counts only when there is neither a fragment nor text (own fragment, then text, then picture), so a clipboard with text never pays for the conversion of its bitmap.
                var read = fragment is null && string.IsNullOrEmpty(text) ? await ReadImageAsync(transfer) : default;
                var image = read.Bytes;
                if (text is null && fragment is null && image is null && read.Refusal is null)
                {
                    return null;
                }

                string? id = null;
                string? part = null;
                if (fragment is not null && await transfer.TryGetValueAsync(Source) is { } source)
                {
                    (id, part) = ParseSource(source);
                }

                return new ClipboardContent(text ?? string.Empty, fragment, id, part, image is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(image), image is null ? read.Refusal : null);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public async Task<bool> WriteAsync(ClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            if (clipboard() is not { } system)
            {
                return false;
            }

            var item = new DataTransferItem();
            item.SetText(content.PlainText ?? string.Empty);
            if (content.Fragment is { Length: > 0 } fragment)
            {
                item.Set(Fragment, fragment);
                if (content.SourceId is { } id)
                {
                    item.Set(Source, Encoding.UTF8.GetBytes(id + "\n" + content.SourcePart));
                }
            }

            var transfer = new DataTransfer();
            transfer.Add(item);
            await system.SetDataAsync(transfer);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>What <see cref="ReadImageAsync"/> found: the bytes of an image file, or the reason there are none although the transfer offered a picture, or neither (no picture offered).</summary>
    internal readonly record struct ClipboardImage(byte[]? Bytes, PictureRefusal? Refusal);

    /// <summary>
    /// The picture on the clipboard as the bytes of an image file. The format <c>image/png</c> is read first and its bytes are taken as they are (no re-encoding); the system bitmap is the second choice and is
    /// converted to PNG on a pool thread (never the UI thread). A bitmap above <see cref="PictureRules.MaxPixels"/> is not converted at all: the result carries <see cref="PictureRefusal.TooManyPixels"/>. A picture that was
    /// offered but could not be read or converted (a clipboard that fails, a conversion that throws) carries <see cref="PictureRefusal.NotAnImage"/>; a transfer that offers no picture gives neither bytes nor reason. A PNG of zero
    /// bytes comes back as an empty array (the editor refuses it with its own reason); bytes above the size limit are returned unchanged (reading them was already done and the editor refuses them). Nothing of the picture is kept
    /// or logged.
    /// </summary>
    internal static async Task<ClipboardImage> ReadImageAsync(IAsyncDataTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        var offered = false;
        try
        {
            if (transfer.Formats.Contains(Png))
            {
                offered = true;
                if (await transfer.TryGetValueAsync(Png) is { } png)
                {
                    return new ClipboardImage(png, null);
                }
            }

            if (!transfer.Formats.Contains(DataFormat.Bitmap))
            {
                return new ClipboardImage(null, offered ? PictureRefusal.NotAnImage : null);
            }

            offered = true;
            if (await transfer.TryGetBitmapAsync() is not { } bitmap)
            {
                return new ClipboardImage(null, PictureRefusal.NotAnImage);
            }

            using (bitmap)
            {
                if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > PictureRules.MaxPixels)
                {
                    return new ClipboardImage(null, PictureRefusal.TooManyPixels);
                }

                var converted = await Task.Run(() =>
                {
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    return stream.ToArray();
                });
                return new ClipboardImage(converted, null);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ClipboardImage(null, offered ? PictureRefusal.NotAnImage : null);
        }
    }

    // The id and the part of the source format; a source that is not text of this shape is no source (the fragment is then foreign).
    internal static (string? Id, string? Part) ParseSource(byte[] bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return (null, null);
        }

        var split = text.IndexOf('\n', StringComparison.Ordinal);
        if (split <= 0)
        {
            return (null, null);
        }

        var part = text[(split + 1)..];
        return (text[..split], part.Length == 0 ? null : part);
    }
}
