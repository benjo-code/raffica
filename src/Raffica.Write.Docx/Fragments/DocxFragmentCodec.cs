using Raffica.Write.Model;

namespace Raffica.Write.Docx;

/// <summary>The <see cref="IFragmentCodec"/> of Write: <see cref="DocxFragment"/> behind the interface the editing library knows.</summary>
public sealed class DocxFragmentCodec : IFragmentCodec
{
    public byte[] Write(IReadOnlyList<Paragraph> paragraphs) => DocxFragment.Write(paragraphs);

    public IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes) => DocxFragment.Read(bytes);
}
