namespace Raffica.Write.Model;

/// <summary>
/// Turns paragraphs into the bytes of the private clipboard format <see cref="FormatId"/> and back. The editor knows only this interface
/// (the DOCX project implements it), so the editing library needs no file-format code. The bytes of a fragment are untrusted input.
/// </summary>
public interface IFragmentCodec
{
    /// <summary>The clipboard format id of a fragment.</summary>
    public const string FormatId = "application/x-raffica-write-fragment";

    /// <summary>The bytes of the paragraphs. Section breaks and paragraph ids are never written.</summary>
    byte[] Write(IReadOnlyList<Paragraph> paragraphs);

    /// <summary>The paragraphs of a fragment. Throws a <see cref="FormatException"/> (and nothing else) when the bytes are not a readable fragment or exceed a limit.</summary>
    IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes);
}
