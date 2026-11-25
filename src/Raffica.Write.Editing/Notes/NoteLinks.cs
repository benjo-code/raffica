using System.Globalization;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Which note a reference points to, or which note a story is: the part (footnotes or endnotes) and the id in it.</summary>
public readonly record struct NoteTarget(NoteType Type, int Id)
{
    /// <summary>The story of the note (the key is the id for a footnote, <c>endnote:</c> and the id for an endnote).</summary>
    public StoryRef Story => NoteOperations.StoryOf(Type, Id);
}

/// <summary>
/// The links between a note reference in the body and the note it points to, for the page view: which note a piece of a paragraph's text refers to, which note a story is, the first paragraph of a note,
/// where the reference of a note stands in the body and where the text of a note starts (behind its own mark). Nothing changes the document.
/// </summary>
public static class NoteLinks
{
    /// <summary>The note a piece of the text points to when the piece is a reference in the body; null for any other piece.</summary>
    public static NoteTarget? ReferenceOf(ProjectionPiece? piece) => piece switch
    {
        AtomicPiece { Atomic: AtomicKind.NoteReference, Content: FootnoteReference footnote } => new NoteTarget(NoteType.Footnote, footnote.Id),
        AtomicPiece { Atomic: AtomicKind.NoteReference, Content: EndnoteReference endnote } => new NoteTarget(NoteType.Endnote, endnote.Id),
        _ => null,
    };

    /// <summary>Whether the piece is the mark a note starts with (the number in front of its text), not a reference in the body.</summary>
    public static bool IsOwnMark(ProjectionPiece? piece) =>
        piece is AtomicPiece { Atomic: AtomicKind.NoteReference, Content: UnknownRunContent };

    /// <summary>The note a story is, or null for any story that is not a footnote or an endnote (or has a key that names none).</summary>
    public static NoteTarget? NoteOf(StoryRef story)
    {
        if (story.Kind != StoryKind.Note || story.Key is not { } key)
        {
            return null;
        }

        const string Prefix = "endnote:";
        var endnote = key.StartsWith(Prefix, StringComparison.Ordinal);
        return int.TryParse(endnote ? key[Prefix.Length..] : key, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? new NoteTarget(endnote ? NoteType.Endnote : NoteType.Footnote, id)
            : null;
    }

    /// <summary>The first paragraph of the note, or null when the document has no such note (or its content does not begin with a paragraph).</summary>
    public static Paragraph? FirstParagraph(WordDocument document, NoteTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        var collection = target.Type == NoteType.Endnote ? document.Endnotes : document.Footnotes;
        return collection?.Find(target.Id) is { Kind: NoteKind.Normal } note && note.Content.Blocks.Count > 0 ? note.Content.Blocks[0] as Paragraph : null;
    }

    /// <summary>
    /// Where the reference of a note stands in the body: the paragraph (tables and block content controls looked through) and the offset of the reference piece in the text that
    /// <paramref name="project"/> makes of it (the layout's projection, which numbers the mark). Null when the body has no such reference.
    /// </summary>
    public static TextPosition? FindReference(WordDocument document, NoteTarget target, Func<Paragraph, ParagraphProjection> project)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(project);
        foreach (var paragraph in PageNumberField.Paragraphs(document.Body.Blocks))
        {
            var matching = NoteOperations.References([paragraph]).Where(r => NoteOperations.Describe(r) == (target.Type, target.Id)).ToList();
            if (matching.Count == 0)
            {
                continue;
            }

            foreach (var piece in project(paragraph).Pieces)
            {
                if (piece is AtomicPiece { Atomic: AtomicKind.NoteReference } && matching.Any(m => ReferenceEquals(m, piece.Content)))
                {
                    return new TextPosition(StoryRef.Body, paragraph, piece.Start);
                }
            }
        }

        return null;
    }

    /// <summary>The offset where the text of a note starts: behind its own mark and the one space that follows it (0 when the paragraph has no mark).</summary>
    public static int TextStart(ParagraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var mark = projection.Pieces.FirstOrDefault(IsOwnMark);
        if (mark is null)
        {
            return 0;
        }

        var end = mark.End;
        return end < projection.Text.Length && projection.Text[end] == ' ' ? end + 1 : end;
    }
}
