using System.Buffers.Binary;

namespace Raffica.Write.Model;

/// <summary>What the header of a picture says: its content type and its size in pixels.</summary>
public readonly record struct ImageHeaderInfo(string ContentType, int Width, int Height);

/// <summary>
/// Reads the content type and the pixel size of a PNG, JPEG, GIF, BMP or TIFF from its header bytes, without decoding anything and without touching the bytes beyond the header fields. The check
/// is the insert command's first gate: bytes with no recognised header, a header cut short or a size of zero give null. A header that states a size the data does not hold is accepted here (a decode
/// that fails later shows the layout's marked box); the size limits are the caller's. Never throws, never keeps or reports the bytes.
/// </summary>
public static class ImageHeaderProbe
{
    /// <summary>The header facts of <paramref name="bytes"/>, or null when they are not a picture of one of the five kinds or the header is unreadable.</summary>
    public static ImageHeaderInfo? Read(ReadOnlySpan<byte> bytes)
    {
        var info = Png(bytes) ?? Jpeg(bytes) ?? Gif(bytes) ?? Bmp(bytes) ?? Tiff(bytes);
        return info is { Width: > 0, Height: > 0 } ? info : null;
    }

    private static ImageHeaderInfo? Png(ReadOnlySpan<byte> b)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (b.Length < 24 || !b[..8].SequenceEqual(signature) || !b.Slice(12, 4).SequenceEqual("IHDR"u8))
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
        if (b.Length < 10 || !(b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        return new ImageHeaderInfo("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));
    }

    private static ImageHeaderInfo? Bmp(ReadOnlySpan<byte> b)
    {
        if (b.Length < 26 || b[0] != (byte)'B' || b[1] != (byte)'M')
        {
            return null;
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(b[14..]);
        if (headerSize == 12)
        {
            return new ImageHeaderInfo("image/bmp", BinaryPrimitives.ReadUInt16LittleEndian(b[18..]), BinaryPrimitives.ReadUInt16LittleEndian(b[20..]));
        }

        if (headerSize < 16 || b.Length < 26)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(b[18..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(b[22..]);
        // A negative height is a top-down bitmap; the size is its magnitude.
        return width <= 0 || height == int.MinValue ? null : new ImageHeaderInfo("image/bmp", width, Math.Abs(height));
    }

    private static ImageHeaderInfo? Tiff(ReadOnlySpan<byte> b)
    {
        if (b.Length < 8)
        {
            return null;
        }

        bool little;
        if (b[0] == (byte)'I' && b[1] == (byte)'I' && b[2] == 42 && b[3] == 0)
        {
            little = true;
        }
        else if (b[0] == (byte)'M' && b[1] == (byte)'M' && b[2] == 0 && b[3] == 42)
        {
            little = false;
        }
        else
        {
            return null;
        }

        var offset = U32(b, 4, little);
        if (offset < 8 || (long)offset + 2 > b.Length)
        {
            return null;
        }

        var count = U16(b, (int)offset, little);
        long width = 0;
        long height = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = (long)offset + 2 + (12L * i);
            if (entry + 12 > b.Length)
            {
                return null;
            }

            var tag = U16(b, (int)entry, little);
            if (tag is not (256 or 257))
            {
                continue;
            }

            var type = U16(b, (int)entry + 2, little);
            long value;
            if (type == 3)
            {
                value = U16(b, (int)entry + 8, little);
            }
            else if (type == 4)
            {
                value = U32(b, (int)entry + 8, little);
            }
            else
            {
                return null;
            }

            if (tag == 256)
            {
                width = value;
            }
            else
            {
                height = value;
            }
        }

        return width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue ? null : new ImageHeaderInfo("image/tiff", (int)width, (int)height);
    }

    private static ushort U16(ReadOnlySpan<byte> b, int at, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt16BigEndian(b[at..]);

    private static uint U32(ReadOnlySpan<byte> b, int at, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b[at..]) : BinaryPrimitives.ReadUInt32BigEndian(b[at..]);
}
