using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Raffica.Write.Tests.Support;

/// <summary>Small deterministic picture files for the picture tests (no random bytes, no files on disk).</summary>
internal static class TestPictures
{
    /// <summary>A valid grey PNG of the size (all pixels black), small enough to decode in a test.</summary>
    public static byte[] Png(int width, int height)
    {
        var raw = new byte[height * (width + 1)];
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>The first 33 bytes of a PNG that states a size, with no pixel data at all (a lying header).</summary>
    public static byte[] PngHeaderOnly(uint width, uint height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        Chunk(output, "IHDR", header);
        return output.ToArray();
    }

    /// <summary>A GIF header (logical screen descriptor) of the size, with no frames.</summary>
    public static byte[] GifHeader(int width, int height)
    {
        var bytes = new byte[13];
        Encoding.ASCII.GetBytes("GIF89a").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)height);
        return bytes;
    }

    /// <summary>A BMP file header and BITMAPINFOHEADER of the size (a negative height is a top-down bitmap), with no pixel data.</summary>
    public static byte[] BmpHeader(int width, int height)
    {
        var bytes = new byte[54];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24);
        return bytes;
    }

    /// <summary>A little-endian TIFF header with one IFD that states the width (SHORT) and the length (LONG).</summary>
    public static byte[] TiffHeader(int width, int height)
    {
        var bytes = new byte[8 + 2 + 24 + 4];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        bytes[2] = 42;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), 256);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 257);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(30), (uint)height);
        return bytes;
    }

    /// <summary>A JPEG that starts with a comment segment and then a baseline start-of-frame of the size (no scan data).</summary>
    public static byte[] JpegHeader(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xFE, 0x00, 0x04, 0x61, 0x62, 0xFF, 0xC0, 0x00, 0x11, 0x08 };
        bytes.Add((byte)(height >> 8));
        bytes.Add((byte)height);
        bytes.Add((byte)(width >> 8));
        bytes.Add((byte)width);
        bytes.AddRange([0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        return [.. bytes];
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        var name = Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(data);
        var crc = Crc(name, data);
        var tail = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        output.Write(tail);
    }

    private static uint Crc(byte[] name, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in name.Concat(data))
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
