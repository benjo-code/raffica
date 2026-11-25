using System.Buffers.Binary;

namespace Raffica.Write.Model;

/// <summary>What the header of a picture says: its content type and its size in pixels.</summary>
public readonly record struct ImageHeaderInfo(string ContentType, int Width, int Height);

/// <summary>
/// Reads the content type and the pixel size of a PNG, JPEG, GIF or BMP from its header bytes, without decoding anything and without touching the bytes beyond the header fields. The check
/// is the insert command's first gate: bytes with no recognised header, a header cut short or a size of zero give null. A header that states a size the data does not hold is accepted here (a decode
/// that fails later shows the layout's marked box); the size limits are the caller's. Never throws, never keeps or reports the bytes.
/// </summary>
public static class ImageHeaderProbe
{
    /// <summary>The header facts of <paramref name="bytes"/>, or null when they are not a picture of one of the four kinds or the header is unreadable.</summary>
    public static ImageHeaderInfo? Read(ReadOnlySpan<byte> bytes)
    {
        var info = Png(bytes) ?? Jpeg(bytes) ?? Gif(bytes) ?? Bmp(bytes);
        return info is { Width: > 0, Height: > 0 } ? info : null;
    }

    private static ImageHeaderInfo? Png(ReadOnlySpan<byte> b)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (b.Length < 24 || !b[..8].SequenceEqual(signature) || BinaryPrimitives.ReadUInt32BigEndian(b[8..]) != 13 || !b.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(b[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(b[20..]);
        return width is 0 or > int.MaxValue || height is 0 or > int.MaxValue ? null : new ImageHeaderInfo("image/png", (int)width, (int)height);
    }

    private static ImageHeaderInfo? Jpeg(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8)
        {
            return null;
        }

        var i = 2;
        while (i + 4 <= b.Length)
        {
            if (b[i] != 0xFF)
            {
                return null;
            }

            var marker = b[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            // Markers without a length: standalone ones.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD8))
            {
                i += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
            if (length < 2)
            {
                return null;
            }

            // The start-of-frame markers (not DHT 0xC4, JPG 0xC8, DAC 0xCC) hold the size.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (i + 9 > b.Length || length < 8)
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]);
                return new ImageHeaderInfo("image/jpeg", width, height);
            }

            if (marker == 0xDA)
            {
                return null;
            }

            i += 2 + length;
        }

        return null;
    }

    private static ImageHeaderInfo? Gif(ReadOnlySpan<byte> b)
    {
        if (b.Length < 13 || !(b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        // What follows the header (and a global colour table) is an extension, an image descriptor or the trailer; text that only starts with the letters is not a picture.
        var packed = b[10];
        var next = 13 + ((packed & 0x80) != 0 ? 3 * (1 << ((packed & 7) + 1)) : 0);
        if (next < b.Length && b[next] is not (0x21 or 0x2C or 0x3B))
        {
            return null;
        }

        return new ImageHeaderInfo("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));
    }

    private static ImageHeaderInfo? Bmp(ReadOnlySpan<byte> b)
    {
        if (b.Length < 28 || b[0] != (byte)'B' || b[1] != (byte)'M')
        {
            return null;
        }

        // The size of the info header is one of the kinds that exist and the pixel data starts after it: a file of text that starts with "BM" has neither.
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(b[14..]);
        var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(b[10..]);
        if (headerSize is not (12 or 40 or 52 or 56 or 64 or 108 or 124) || dataOffset < 14 + headerSize || dataOffset > int.MaxValue)
        {
            return null;
        }

        if (headerSize == 12)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(b[22..]) == 1
                ? new ImageHeaderInfo("image/bmp", BinaryPrimitives.ReadUInt16LittleEndian(b[18..]), BinaryPrimitives.ReadUInt16LittleEndian(b[20..]))
                : null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(b[18..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(b[22..]);
        // A negative height is a top-down bitmap; the size is its magnitude.
        return width <= 0 || height == int.MinValue || BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) != 1 ? null : new ImageHeaderInfo("image/bmp", width, Math.Abs(height));
    }
}
