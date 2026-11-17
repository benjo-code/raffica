using SkiaSharp;

namespace Raffica.Write.Layout;

/// <summary>Reads the pixel size of a picture from its header only (<see cref="SKCodec"/>), without decoding a bitmap.</summary>
internal static class ImageHeader
{
    private const int HeaderBytes = 256 * 1024;

    /// <summary>The size, or null for an empty buffer, a metafile, an unreadable header or a zero size. Never throws (but for out of memory).</summary>
    public static ImageInfoPx? Read(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (bytes.IsEmpty || ImageFormats.IsMetafile(bytes.Span))
            {
                return null;
            }

            // The header lies in the first bytes (a long EXIF block of a photo is the longest case).
            using var data = SKData.CreateCopy(bytes.Span[..Math.Min(bytes.Length, HeaderBytes)]);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return null;
            }

            return new ImageInfoPx(codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
