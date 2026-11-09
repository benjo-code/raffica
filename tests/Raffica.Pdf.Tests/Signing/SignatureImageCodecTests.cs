using System.Buffers.Binary;
using Avalonia.Headless.XUnit;
using Raffica.Core.Imaging;
using Raffica.Core.Signatures;
using Raffica.Pdf.Signing;
using Xunit;

namespace Raffica.Pdf.Tests.Signing;

public sealed class SignatureImageCodecTests
{
    // A 16 x 16 JPEG (724 bytes, quality 90): white with a black bar two pixels high; made once with a system encoder.
    private const string TinyJpeg = "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6K+f/il+wV8CfjT471Pxl4y8Df2z4k1Lyvtd7/a9/B5nlxJEnyRTqgwkaDhRnGTySaPhb+wV8Cfgt470zxl4N8Df2N4k03zfsl7/a9/P5fmRPE/ySzshykjjlTjORyAaAP/2Q==";

    [AvaloniaFact]
    public async Task PngWithAlphaAndJpegBothDecode()
    {
        var bgra = new byte[8 * 6 * 4];
        var ink = ((3 * 8) + 4) * 4;
        bgra[ink] = 20;
        bgra[ink + 1] = 40;
        bgra[ink + 2] = 200;
        bgra[ink + 3] = 255;
        var png = await SignatureImageCodec.DecodeAsync(new MemoryStream(PngEncoder.EncodeWithAlpha(new SignatureImage(8, 6, bgra))));
        var jpeg = await SignatureImageCodec.DecodeAsync(new MemoryStream(Convert.FromBase64String(TinyJpeg)));

        var decodedPng = Assert.IsType<SignatureImageResult.Ok>(png).Image;
        Assert.Equal((8, 6), (decodedPng.Width, decodedPng.Height));
        Assert.Equal(new byte[] { 20, 40, 200, 255 }, decodedPng.Bgra.AsSpan(ink, 4).ToArray());
        Assert.Equal(0, decodedPng.Bgra[3]);
        var decodedJpeg = Assert.IsType<SignatureImageResult.Ok>(jpeg).Image;
        Assert.Equal((16, 16), (decodedJpeg.Width, decodedJpeg.Height));
        Assert.True(decodedJpeg.Bgra[((8 * 16) + 8) * 4] < 80); // the black bar
        Assert.True(decodedJpeg.Bgra[((2 * 16) + 8) * 4] > 200); // the white paper
    }

    [AvaloniaFact]
    public async Task HalfTransparentPixelComesBackStraightNotPremultiplied()
    {
        var bgra = new byte[8 * 6 * 4];
        var pixel = ((2 * 8) + 3) * 4;
        bgra[pixel] = 20;
        bgra[pixel + 1] = 40;
        bgra[pixel + 2] = 200;
        bgra[pixel + 3] = 128;

        var result = await SignatureImageCodec.DecodeAsync(new MemoryStream(PngEncoder.EncodeWithAlpha(new SignatureImage(8, 6, bgra))));

        var image = Assert.IsType<SignatureImageResult.Ok>(result).Image;
        // Premultiplied values would be about 10, 20 and 100; the small tolerance is the rounding of the round trip.
        Assert.InRange(image.Bgra[pixel], 17, 23);
        Assert.InRange(image.Bgra[pixel + 1], 37, 43);
        Assert.InRange(image.Bgra[pixel + 2], 197, 203);
        Assert.InRange(image.Bgra[pixel + 3], 127, 129);
    }

    [AvaloniaFact]
    public async Task NonSeekableStreamOver10MiBIsRefusedAfterAboutTheLimit()
    {
        var length = SignatureLimits.MaxFileBytes + (10 * 1024 * 1024);
        using var stream = new CountingStream(length);

        var result = await SignatureImageCodec.DecodeAsync(stream);

        Assert.Equal(SignatureImageProblem.TooLarge, Assert.IsType<SignatureImageResult.Problem>(result).Reason);
        // The copy reads in blocks, so it may be one block past the limit, but never on towards the end of the stream.
        Assert.True(stream.BytesRead <= SignatureLimits.MaxFileBytes + (128 * 1024), $"read {stream.BytesRead}");
    }

    [AvaloniaFact]
    public async Task ExactlyTenMiBPassesTheSizeCheck()
    {
        // Zeros are no image: the size check let it through and it is refused as Unreadable, not as TooLarge.
        var seekable = await SignatureImageCodec.DecodeAsync(new MemoryStream(new byte[SignatureLimits.MaxFileBytes]));
        using var forward = new CountingStream(SignatureLimits.MaxFileBytes);
        var unknownLength = await SignatureImageCodec.DecodeAsync(forward);

        Assert.Equal(SignatureImageProblem.Unreadable, Assert.IsType<SignatureImageResult.Problem>(seekable).Reason);
        Assert.Equal(SignatureImageProblem.Unreadable, Assert.IsType<SignatureImageResult.Problem>(unknownLength).Reason);
        Assert.Equal(SignatureLimits.MaxFileBytes, forward.BytesRead);
    }

    [AvaloniaFact]
    public async Task FileOver10MiBIsTooLargeBeforeDecoding()
    {
        // Zeros are no image at all: were it decoded the answer would be Unreadable.
        var file = new byte[SignatureLimits.MaxFileBytes + 1];

        var result = await SignatureImageCodec.DecodeAsync(new MemoryStream(file));

        Assert.Equal(SignatureImageProblem.TooLarge, Assert.IsType<SignatureImageResult.Problem>(result).Reason);
    }

    [AvaloniaFact]
    public async Task HeaderOver6000PixelsIsRefusedBeforeDecoding()
    {
        // Only a head, no pixel data: decoding it would fail as Unreadable, so TooManyPixels proves the header was read first.
        var head = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(head, 0);
        BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(8), 13);
        "IHDR"u8.CopyTo(head.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(16), SignatureLimits.MaxSourceSide + 1);
        BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(20), 10);
        head[24] = 8;
        head[25] = 6;

        var result = await SignatureImageCodec.DecodeAsync(new MemoryStream(head));

        Assert.Equal(SignatureImageProblem.TooManyPixels, Assert.IsType<SignatureImageResult.Problem>(result).Reason);
    }

    [AvaloniaFact]
    public async Task NotAnImageIsUnreadable()
    {
        var text = new MemoryStream("This is a letter, not a picture."u8.ToArray());
        var cutOff = Convert.FromBase64String(TinyJpeg)[..300];

        Assert.Equal(SignatureImageProblem.Unreadable, Assert.IsType<SignatureImageResult.Problem>(await SignatureImageCodec.DecodeAsync(text)).Reason);
        Assert.Equal(SignatureImageProblem.Unreadable, Assert.IsType<SignatureImageResult.Problem>(await SignatureImageCodec.DecodeAsync(new MemoryStream(cutOff))).Reason);
        Assert.Equal(SignatureImageProblem.Unreadable, Assert.IsType<SignatureImageResult.Problem>(await SignatureImageCodec.DecodeAsync(new MemoryStream())).Reason);
    }

    [AvaloniaFact]
    public async Task FullyTransparentOrBlankImageIsEmpty()
    {
        var transparent = new byte[10 * 10 * 4];
        var white = new byte[10 * 10 * 4];
        Array.Fill(white, (byte)255);

        var first = await SignatureImageCodec.DecodeAsync(new MemoryStream(PngEncoder.EncodeWithAlpha(new SignatureImage(10, 10, transparent))));
        var second = await SignatureImageCodec.DecodeAsync(new MemoryStream(PngEncoder.EncodeWithAlpha(new SignatureImage(10, 10, white))));

        Assert.Equal(SignatureImageProblem.Empty, Assert.IsType<SignatureImageResult.Problem>(first).Reason);
        Assert.Equal(SignatureImageProblem.Empty, Assert.IsType<SignatureImageResult.Problem>(second).Reason);
    }

    [AvaloniaFact]
    public async Task LargeImageIsShrunkTo1200()
    {
        var width = 2400;
        var height = 600;
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i + 3] = 255;
            bgra[i] = (byte)((i / 4) % 251);
        }

        var result = await SignatureImageCodec.DecodeAsync(new MemoryStream(PngEncoder.EncodeWithAlpha(new SignatureImage(width, height, bgra))));

        var image = Assert.IsType<SignatureImageResult.Ok>(result).Image;
        Assert.Equal((1200, 300), (image.Width, image.Height));
    }

    /// <summary>A forward-only stream of zeros that counts what was read from it.</summary>
    private sealed class CountingStream(long length) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, length - BytesRead);
            buffer[..n].Clear();
            BytesRead += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Read(buffer.Span));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
