using System.Buffers.Binary;
using System.Diagnostics;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Model;

/// <summary>The header probe against hostile and damaged input: it never throws, never loops without bound and refuses what is only picture-shaped text.</summary>
public sealed class ImageHeaderProbeTests
{
    public static TheoryData<string> Formats() => ["png", "jpeg", "gif", "bmp"];

    private static byte[] Valid(string format) => format switch
    {
        "png" => TestPictures.Png(30, 20),
        "jpeg" => TestPictures.JpegHeader(30, 20),
        "gif" => TestPictures.GifHeader(30, 20),
        _ => TestPictures.BmpHeader(30, 20),
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void EveryPrefixOfAValidHeaderGivesNullOrAValidResultAndNeverThrows(string format)
    {
        var bytes = Valid(format);
        Assert.NotNull(ImageHeaderProbe.Read(bytes));
        var accepted = 0;
        for (var length = 0; length <= Math.Min(bytes.Length, 2048); length++)
        {
            var result = ImageHeaderProbe.Read(bytes.AsSpan(0, length));
            if (result is { } read)
            {
                Assert.Equal((30, 20), (read.Width, read.Height));
                accepted++;
            }
        }

        Assert.InRange(accepted, 1, bytes.Length);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void EveryByteOfAHeaderSetToEachExtremeNeverThrows(string format)
    {
        var bytes = Valid(format);
        for (var i = 0; i < Math.Min(bytes.Length, 128); i++)
        {
            foreach (var value in new byte[] { 0x00, 0x7F, 0x80, 0xFF })
            {
                var copy = (byte[])bytes.Clone();
                copy[i] = value;
                var result = ImageHeaderProbe.Read(copy);
                Assert.True(result is null || (result.Value.Width > 0 && result.Value.Height > 0), $"{format} byte {i} = {value}");
            }
        }
    }

    [Fact]
    public void AJpegWithLongPaddingOrAChainOfTinySegmentsEndsInBoundedTime()
    {
        var padded = new byte[2 * 1024 * 1024];
        Array.Fill(padded, (byte)0xFF);
        padded[0] = 0xFF;
        padded[1] = 0xD8;
        var chain = new byte[2 * 1024 * 1024];
        chain[0] = 0xFF;
        chain[1] = 0xD8;
        for (var i = 2; i + 4 <= chain.Length; i += 4)
        {
            chain[i] = 0xFF;
            chain[i + 1] = 0xE1;
            chain[i + 2] = 0x00;
            chain[i + 3] = 0x02;
        }

        var watch = Stopwatch.StartNew();
        Assert.Null(ImageHeaderProbe.Read(padded));
        Assert.Null(ImageHeaderProbe.Read(chain));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the two hostile JPEGs took {watch.Elapsed}");

        // Fill bytes before a real frame are skipped (a marker may be preceded by any number of 0xFF).
        var frame = TestPictures.JpegHeader(31, 21);
        var withFill = new byte[] { 0xFF, 0xD8 }.Concat(Enumerable.Repeat((byte)0xFF, 5000)).Concat(frame.Skip(8)).ToArray();
        Assert.Equal(new ImageHeaderInfo("image/jpeg", 31, 21), ImageHeaderProbe.Read(withFill));
    }

    [Fact]
    public void AJpegFrameCutAtTheBoundaryIsRefusedAndOneByteMoreIsRead()
    {
        var bytes = TestPictures.JpegHeader(30, 20);
        var frameAt = Array.IndexOf(bytes, (byte)0xC0) - 1;

        // The size needs the bytes up to frameAt + 8 inclusive.
        Assert.Null(ImageHeaderProbe.Read(bytes.AsSpan(0, frameAt + 8)));
        Assert.Equal(new ImageHeaderInfo("image/jpeg", 30, 20), ImageHeaderProbe.Read(bytes.AsSpan(0, frameAt + 9)));
    }

    // SkiaSharp cannot decode a TIFF, so an inserted one would be an unselectable placeholder: the probe does not know the format (a TIFF already in a document is left as it is).
    [Fact]
    public void ATiffIsNoPictureOfTheProbe()
    {
        var little = TestPictures.TiffHeader(30, 20);
        Assert.Null(ImageHeaderProbe.Read(little));
        var big = (byte[])little.Clone();
        big[0] = (byte)'M';
        big[1] = (byte)'M';
        Assert.Null(ImageHeaderProbe.Read(big));
    }

    private static byte[] BmpWith(Action<byte[]> change)
    {
        var bytes = TestPictures.BmpHeader(30, 20);
        change(bytes);
        return bytes;
    }

    [Fact]
    public void ABmpWithExtremeOrImpossibleFieldsIsRefused()
    {
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), int.MinValue))));
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), int.MinValue))));
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), 0))));
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), 0))));
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), -5))));
        Assert.Equal(new ImageHeaderInfo("image/bmp", 30, 20), ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), -20))));
        foreach (var size in new uint[] { 0, 1, 11, 13, 15, 16, 39, 41, uint.MaxValue })
        {
            Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(14), size))));
        }

        // The info header kinds that exist are read.
        foreach (var size in new uint[] { 40, 52, 56, 64, 108, 124 })
        {
            Assert.NotNull(ImageHeaderProbe.Read(BmpWith(b =>
            {
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(14), size);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), 14 + size);
            })));
        }

        // The core header (12) has 16-bit sizes and its plane count at its own place.
        var core = BmpWith(b =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(14), 12);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), 26);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), 30);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(20), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(22), 1);
        });
        Assert.Equal(new ImageHeaderInfo("image/bmp", 30, 20), ImageHeaderProbe.Read(core));

        // The pixel data cannot start inside the headers, and a bitmap has one plane.
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), 20))));
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), uint.MaxValue))));
        Assert.Null(ImageHeaderProbe.Read(BmpWith(b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26), 2))));
    }

    [Fact]
    public void APngWithZeroOrHugeSizesOrAWrongHeaderChunkIsRefused()
    {
        Assert.Null(ImageHeaderProbe.Read(TestPictures.PngHeaderOnly(0, 5)));
        Assert.Null(ImageHeaderProbe.Read(TestPictures.PngHeaderOnly(5, 0)));
        Assert.Null(ImageHeaderProbe.Read(TestPictures.PngHeaderOnly((uint)int.MaxValue + 1, 5)));
        Assert.Null(ImageHeaderProbe.Read(TestPictures.PngHeaderOnly(5, uint.MaxValue)));
        Assert.Equal(new ImageHeaderInfo("image/png", int.MaxValue, 1), ImageHeaderProbe.Read(TestPictures.PngHeaderOnly(int.MaxValue, 1)));
        var wrongLength = TestPictures.PngHeaderOnly(5, 5);
        wrongLength[11] = 12;
        Assert.Null(ImageHeaderProbe.Read(wrongLength));
    }

    [Theory]
    [InlineData("BM")]
    [InlineData("GIF89a")]
    [InlineData("GIF87a")]
    [InlineData("II*\0")]
    [InlineData("MM\0*")]
    [InlineData("\u0089PNG\r\n\u001a\n")]
    [InlineData("ÿØ")]
    public void TextThatOnlyStartsLikeAPictureIsRefused(string start)
    {
        var text = start + " this is only a text file that happens to start with the letters of a picture header, with plenty more words after it to read.";
        Assert.Null(ImageHeaderProbe.Read(System.Text.Encoding.Latin1.GetBytes(text)));
        var digits = start + new string('1', 80);
        Assert.Null(ImageHeaderProbe.Read(System.Text.Encoding.Latin1.GetBytes(digits)));
    }
}
