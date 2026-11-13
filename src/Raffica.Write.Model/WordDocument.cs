namespace Raffica.Write.Model;

/// <summary>
/// The loaded parts of a Word document. The body is always there; every other part is <see langword="null"/> (or absent
/// from <see cref="HeadersFooters"/>) when the file has no such part or the caller did not ask for it.
/// </summary>
public sealed class WordDocument
{
    public WordDocument(Story body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Body = body;
    }

    public Story Body { get; }

    public StyleSheet? Styles { get; set; }

    public NumberingDefinitions? Numbering { get; set; }

    public DocumentSettings? Settings { get; set; }

    public NoteCollection? Footnotes { get; set; }

    public NoteCollection? Endnotes { get; set; }

    public CommentCollection? Comments { get; set; }

    /// <summary>The header and footer parts, keyed by the relationship id the sections' <see cref="HeaderFooterReference"/>s use.</summary>
    public IDictionary<string, HeaderFooterPart> HeadersFooters { get; } = new Dictionary<string, HeaderFooterPart>(StringComparer.Ordinal);
}
