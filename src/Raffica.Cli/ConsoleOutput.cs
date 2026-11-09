using System.Text;

namespace Raffica.Cli;

/// <summary>
/// Chooses where the command writes its output. Redirected output (a file or a pipe) is the raw UTF-8 stream of the process, byte for byte.
/// On Windows a console that is not redirected is written through <see cref="Console.Out"/> after the console's output encoding was set to
/// UTF-8 (without BOM), because the raw bytes of a stream would show as mojibake in a console with an ANSI or OEM code page.
/// </summary>
internal static class ConsoleOutput
{
    /// <summary>Whether the output goes through the console's text writer: on Windows, and only when it is not redirected.</summary>
    /// <param name="isWindows">Whether the process runs on Windows.</param>
    /// <param name="isOutputRedirected">Whether standard output is redirected (<see cref="Console.IsOutputRedirected"/>).</param>
    /// <returns><see langword="true"/> for the console writer, <see langword="false"/> for the raw stream.</returns>
    public static bool UsesConsoleWriter(bool isWindows, bool isOutputRedirected) => isWindows && !isOutputRedirected;

    /// <summary>The stream the command writes its UTF-8 output to.</summary>
    /// <param name="isWindows">Whether the process runs on Windows.</param>
    /// <param name="isOutputRedirected">Whether standard output is redirected.</param>
    /// <param name="openRaw">Opens the raw standard output stream.</param>
    /// <param name="openConsole">Sets the console's output encoding to UTF-8 without BOM and returns the writer of the console.</param>
    /// <returns>The raw stream, or a stream that decodes the UTF-8 bytes into the console writer.</returns>
    public static Stream Open(bool isWindows, bool isOutputRedirected, Func<Stream> openRaw, Func<TextWriter> openConsole)
    {
        ArgumentNullException.ThrowIfNull(openRaw);
        ArgumentNullException.ThrowIfNull(openConsole);
        return UsesConsoleWriter(isWindows, isOutputRedirected) ? new TextWriterStream(openConsole()) : openRaw();
    }

    /// <summary>Sets the output encoding of the console to UTF-8 without BOM and returns <see cref="Console.Out"/> (which follows the encoding).</summary>
    /// <returns>The writer of the console.</returns>
    public static TextWriter OpenConsoleWriter()
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return Console.Out;
    }

    /// <summary>A write-only stream of UTF-8 bytes that a decoder turns into text for a writer; a character split between two writes is kept until its end arrives.</summary>
    private sealed class TextWriterStream(TextWriter writer) : Stream
    {
        private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var chars = new char[_decoder.GetCharCount(buffer, flush: false)];
            var written = _decoder.GetChars(buffer, chars, flush: false);
            writer.Write(chars, 0, written);
        }

        public override void Flush() => writer.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
