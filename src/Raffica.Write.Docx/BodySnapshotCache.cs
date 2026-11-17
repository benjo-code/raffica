using System.Xml;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Docx;

/// <summary>
/// The serialized form of the top-level blocks of the body, kept between recovery snapshots so that a snapshot writes only the blocks that changed since the last one. The bytes it returns
/// are exactly the bytes of a full write of the main part: a block's XML does not depend on its siblings (only on the namespaces the root and the body declare, which the one writer of a
/// pass has in scope exactly as the full write has), so a block that did not change is written once and its bytes are reused.
/// </summary>
/// <remarks>
/// <b>What keeps it right.</b> A block is reused only when (1) the block object is the one the cache wrote, (2) the editor did not report its place in a changed range of the body
/// (<see cref="EditTracker"/>) and (3) every revision change since the last pass was attributed (otherwise the whole body is written again); a nested change (a table cell, a content
/// control) writes again every block that is not a paragraph. A pass that fails leaves the cache invalid, so the next one starts from nothing. It is used by the snapshot path only (a user
/// save never reads it), and it is read and changed under the edit lease, one pass at a time (the snapshot's in-flight mark). The oracle test compares it with a full write across long
/// random edit sequences.
/// </remarks>
internal sealed class BodySnapshotCache : IDisposable
{
    private readonly Dictionary<Block, byte[]> _fragments = new(ReferenceEqualityComparer.Instance);
    private readonly MemoryStream _stream = new(1 << 20);
    private EditTracker? _tracker;
    private bool _valid;

    public void Dispose() => _stream.Dispose();

    /// <summary>How many blocks the last pass wrote again, and how many it reused (for the tests and the measurement).</summary>
    public (int Written, int Reused) LastPass { get; private set; }

    /// <summary>The bytes of <c>word/document.xml</c> for the model as it is now.</summary>
    public byte[] Serialise(WordDocument model)
    {
        var story = model.Body;
        var blocks = story.Blocks;
        var everything = true;
        IReadOnlySet<Block>? dirty = null;
        var nested = false;
        if (_tracker is null)
        {
            _tracker = model.Edits.Track(blocks);
        }
        else
        {
            var changes = _tracker.Take();
            everything = !_valid || !changes.Complete;
            dirty = changes.Dirty;
            nested = changes.NestedChanged;
        }

        _valid = false;
        var need = new bool[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            need[i] = everything || !_fragments.ContainsKey(block) || dirty!.Contains(block) || (nested && block is not Paragraph);
        }

        // One buffer for every pass (a copy every few seconds must not put a megabyte or two on the large object heap each time): reused, its capacity kept.
        var stream = _stream;
        stream.SetLength(0);
        long prefixEnd = -1;
        long suffixStart = -1;
        var spans = new List<(int Index, long Start, long End)>();
        using (var writer = XmlWriter.Create(stream, DocumentXml.WriterSettings()))
        {
            writer.WriteStartDocument(true);
            StoryXml.WriteWith(story, writer, w =>
            {
                // The ">" of the body's start tag is written with its first child: a stand-in child marks where it is and is cut out again.
                w.Flush();
                var before = stream.Length;
                XmlKit.Start(w, Names.W + "p");
                w.WriteEndElement();
                w.Flush();
                prefixEnd = before + 1;
                suffixStart = stream.Length;
                for (var i = 0; i < blocks.Count; i++)
                {
                    if (!need[i])
                    {
                        continue;
                    }

                    var start = stream.Length;
                    StoryXml.WriteBlocks([blocks[i]], w);
                    w.Flush();
                    spans.Add((i, start, stream.Length));
                    suffixStart = stream.Length;
                }
            });
            writer.WriteEndDocument();
        }

        var written = stream.GetBuffer();
        var length = (int)stream.Length;
        if (prefixEnd < 1 || written[prefixEnd - 1] != (byte)'>')
        {
            // Not the shape this assumes: write the whole body in one pass, as a user save does, and keep nothing.
            using var whole = new MemoryStream();
            DocumentXml.Write(story, whole);
            _fragments.Clear();
            LastPass = (blocks.Count, 0);
            return whole.ToArray();
        }

        var parts = new byte[blocks.Count][];
        foreach (var (index, start, end) in spans)
        {
            parts[index] = written.AsSpan((int)start, (int)(end - start)).ToArray();
        }

        long total = prefixEnd + (length - suffixStart);
        for (var i = 0; i < blocks.Count; i++)
        {
            parts[i] ??= _fragments[blocks[i]];
            total += parts[i].Length;
        }

        var result = new byte[total];
        written.AsSpan(0, (int)prefixEnd).CopyTo(result);
        var at = (int)prefixEnd;
        foreach (var part in parts)
        {
            part.CopyTo(result, at);
            at += part.Length;
        }

        written.AsSpan((int)suffixStart, length - (int)suffixStart).CopyTo(result.AsSpan(at));

        _fragments.Clear();
        for (var i = 0; i < blocks.Count; i++)
        {
            _fragments[blocks[i]] = parts[i];
        }

        LastPass = (spans.Count, blocks.Count - spans.Count);
        _valid = true;
        return result;
    }
}
