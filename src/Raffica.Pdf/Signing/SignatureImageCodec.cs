using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Raffica.Core.Signatures;

namespace Raffica.Pdf.Signing;

/// <summary>Why an image could not be taken as a signature.</summary>
internal enum SignatureImageProblem
{
    /// <summary>The file is larger than <see cref="SignatureLimits.MaxFileBytes"/>.</summary>
    TooLarge,

    /// <summary>The header says a side is longer than <see cref="SignatureLimits.MaxSourceSide"/> pixels.</summary>
    TooManyPixels,

    /// <summary>Not a PNG or JPEG, cut off, or damaged.</summary>
    Unreadable,

    /// <summary>The image shows nothing: fully transparent or of one single colour.</summary>
    Empty,
}

/// <summary>The outcome of <see cref="SignatureImageCodec.DecodeAsync"/>: the image or the problem.</summary>
internal abstract record SignatureImageResult
{
    private SignatureImageResult()
    {
    }

    /// <summary>The image was decoded (at most <see cref="SignatureLimits.MaxStoredSide"/> pixels on its longer side).</summary>
    /// <param name="Image">The pixels, BGRA, not premultiplied.</param>
    internal sealed record Ok(SignatureImage Image) : SignatureImageResult;

    /// <summary>The image cannot be used.</summary>
    /// <param name="Reason">Why.</param>
    internal sealed record Problem(SignatureImageProblem Reason) : SignatureImageResult;
}

/// <summary>
/// Reads the file of an imported signature (PNG or JPEG) into BGRA pixels with Avalonia's own decoder (no extra package). The size
/// of the file and the size in its header are checked before anything is decoded, and the decoding runs off the UI thread. Nothing
/// about the file (its path, its bytes) is kept or reported.
/// </summary>
internal static class SignatureImageCodec
{
    /// <summary>Reads, checks and decodes <paramref name="file"/>; the stream is read to its end (at most the limit plus one byte) and stays open.</summary>
    public static async Task<SignatureImageResult> DecodeAsync(Stream file)
    {
        ArgumentNullException.ThrowIfNull(file);
        byte[] bytes;
        try
        {
            if (file.CanSeek && file.Length > SignatureLimits.MaxFileBytes)
            {
                return new SignatureImageResult.Problem(SignatureImageProblem.TooLarge);
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(new LimitedStream(buffer, SignatureLimits.MaxFileBytes)).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }
        catch (FileTooLargeException)
        {
            return new SignatureImageResult.Problem(SignatureImageProblem.TooLarge);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
        {
            return new SignatureImageResult.Problem(SignatureImageProblem.Unreadable);
        }

        return await Task.Run(() => Decode(bytes)).ConfigureAwait(false);
    }

    private static SignatureImageResult Decode(byte[] bytes)
    {
        if (!ImageHeader.TryReadSize(bytes, out var width, out var height))
        {
            return new SignatureImageResult.Problem(SignatureImageProblem.Unreadable);
        }

        if (width > SignatureLimits.MaxSourceSide || height > SignatureLimits.MaxSourceSide)
        {
            return new SignatureImageResult.Problem(SignatureImageProblem.TooManyPixels);
        }

        if (!ImageHeader.IsComplete(bytes))
        {
            return new SignatureImageResult.Problem(SignatureImageProblem.Unreadable);
        }

        SignatureImage image;
        try
        {
            image = ToBgra(bytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The platform decoder reports damage with a range of exception types; whichever it is, the file is not usable.
            return new SignatureImageResult.Problem(SignatureImageProblem.Unreadable);
        }

        if (image.IsEmpty || IsOneColor(image.Bgra))
        {
            return new SignatureImageResult.Problem(SignatureImageProblem.Empty);
        }

        return new SignatureImageResult.Ok(SignatureCutout.FitWithin(image, SignatureLimits.MaxStoredSide));
    }

    /// <summary>Decodes to a bitmap and copies it, un-premultiplied, into a BGRA buffer (the framework converts the alpha format).</summary>
    private static unsafe SignatureImage ToBgra(byte[] bytes)
    {
        using var decoded = new Bitmap(new MemoryStream(bytes, writable: false));
        var size = decoded.PixelSize;
        using var target = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var frame = target.Lock();
        decoded.CopyPixels(frame);
        var rowBytes = size.Width * 4;
        var bgra = new byte[rowBytes * size.Height];
        for (var y = 0; y < size.Height; y++)
        {
            new ReadOnlySpan<byte>((byte*)frame.Address + ((long)y * frame.RowBytes), rowBytes).CopyTo(bgra.AsSpan(y * rowBytes, rowBytes));
        }

        return new SignatureImage(size.Width, size.Height, bgra);
    }

    /// <summary>Whether every pixel equals the first (a blank sheet: nothing to separate ink from paper).</summary>
    private static bool IsOneColor(byte[] bgra)
    {
        var first = BitConverter.ToUInt32(bgra, 0);
        for (var i = 4; i < bgra.Length; i += 4)
        {
            if (BitConverter.ToUInt32(bgra, i) != first)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class FileTooLargeException : Exception;

    /// <summary>Writes into a buffer but refuses more than the limit, so a stream of unknown length cannot fill the memory.</summary>
    private sealed class LimitedStream(MemoryStream target, int limit) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (target.Length + buffer.Length > limit)
            {
                throw new FileTooLargeException();
            }

            target.Write(buffer);
        }
    }
}
