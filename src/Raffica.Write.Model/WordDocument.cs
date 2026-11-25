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

    /// <summary>The edit revision and the dirty state of this document.</summary>
    public DocumentEdits Edits { get; } = new();

    public StyleSheet? Styles { get; set; }

    public NumberingDefinitions? Numbering { get; set; }

    public DocumentSettings? Settings { get; set; }

    public NoteCollection? Footnotes { get; set; }

    public NoteCollection? Endnotes { get; set; }

    public CommentCollection? Comments { get; set; }

    private MediaStore? _media;

    /// <summary>Where the package layer says which relationship ids the main part uses; asked once, when <see cref="Media"/> is first used, so an opened file pays nothing for it.</summary>
    public Func<IEnumerable<string>>? MainRelationshipIds { get; set; }

    /// <summary>Whether <see cref="Media"/> was ever used.</summary>
    public bool HasMedia => Volatile.Read(ref _media) is not null;

    /// <summary>The new pictures of the document, made on first use.</summary>
    public MediaStore Media => Volatile.Read(ref _media) ?? InitMedia();

    private MediaStore InitMedia()
    {
        var made = new MediaStore(MainRelationshipIds?.Invoke());
        return Interlocked.CompareExchange(ref _media, made, null) ?? made;
    }

    /// <summary>The header and footer parts, keyed by the relationship id the sections' <see cref="HeaderFooterReference"/>s use.</summary>
    public IDictionary<string, HeaderFooterPart> HeadersFooters { get; } = new Dictionary<string, HeaderFooterPart>(StringComparer.Ordinal);
}
