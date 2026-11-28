using System.Globalization;
using System.Text;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>What a cross-reference shows of its target.</summary>
public enum CrossRefKind
{
    /// <summary>The label and the number of a caption ("Figure 3").</summary>
    LabelAndNumber,

    /// <summary>The number alone: the number label of a heading, the number of a caption, the mark of a note.</summary>
    NumberOnly,

    /// <summary>The text of the target: the heading text, the whole caption, the text of a bookmark.</summary>
    Text,

    /// <summary>The printed page number of the target.</summary>
    Page,
}

/// <summary>What a cross-reference points at.</summary>
public enum CrossRefTargetKind
{
    Heading,
    Caption,
    Footnote,
    Endnote,
    Bookmark,
}

/// <summary>
/// A place a cross-reference can point at: its kind, a position (the start of the heading, caption or bookmark; the place of the note reference as the plain projection counts it) and the line the card shows. The editor finds the
/// target again from this: a heading and a caption by their paragraph, a note by the reference at that offset and a bookmark by its name (<see cref="Display"/>).
/// </summary>
public sealed record CrossRefTarget(CrossRefTargetKind Kind, TextPosition Position, string Display);

/// <summary>Why a cross-reference cannot be inserted. The caller words it for the user in the language of the interface.</summary>
public enum CrossRefRefusal
{
    /// <summary>The target does not offer that choice (a note has no text, a heading no label).</summary>
    KindNotOffered,

    /// <summary>There is no paragraph at the place (or it is not in the document).</summary>
    NoPosition,

    /// <summary>The document is opened as a prefix (the first blocks only).</summary>
    Prefix,

    /// <summary>The place is not in the body (a header, a footer, a note or a comment).</summary>
    NotInBody,

    /// <summary>The place is in a content control that holds plain text only.</summary>
    RestrictedControl,

    /// <summary>The place is in a table of contents, which updates itself.</summary>
    InsideToc,

    /// <summary>The target is not there any more, or is no longer what the card listed (the document changed).</summary>
    TargetGone,

    /// <summary>The reference would stand inside the text it shows (a reference to a heading inside that heading).</summary>
    InsideTarget,

    /// <summary>The target has no number to show (a heading that is not numbered, a note the layout has not numbered).</summary>
    NoNumber,

    /// <summary>The text of a bookmark that spans paragraphs cannot be shown in one reference (its page can).</summary>
    SeveralParagraphs,

    /// <summary>The number of a caption cannot be located (its fields and their results cannot be paired): its text and its page can still be referred to.</summary>
    CaptionNumberNotRead,

    /// <summary>No free name or id for the hidden bookmark.</summary>
    IdsUsedUp,
}

/// <summary>The targets of a cross-reference and what each one offers. The lists are read from the model (and the host for the labels and the marks) when the card opens; nothing is read before.</summary>
public static class CrossReferences
{
    /// <summary>The most targets one list holds (a document with more shows the first ones).</summary>
    public const int MaxTargets = 2000;

    private const int MaxDisplay = 80;

    /// <summary>
    /// The targets of a kind, in document order. Headings are the paragraphs of a heading style (<c>heading 1</c> to <c>heading 9</c> by name; the line holds their number label and text); captions the paragraphs that hold a usable <c>SEQ</c> field of their
    /// own (the line is the caption); footnotes and endnotes the references of the body with their mark (the line is the mark and the beginning of the note); bookmarks the ones the user may see, by their name as it is (the editor finds the bookmark again by it; a name that starts with an
    /// underscore is Write's own and never listed). A line holds no control character and at most 80 characters.
    /// </summary>
    public static IReadOnlyList<CrossRefTarget> Targets(WordDocument document, IFieldHost host, CrossRefTargetKind kind)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(host);
        var found = new List<CrossRefTarget>();
        if (kind == CrossRefTargetKind.Bookmark)
        {
            foreach (var bookmark in BookmarkOperations.Visible(document))
            {
                found.Add(new CrossRefTarget(kind, bookmark.Range.Start, bookmark.Name));
                if (found.Count >= MaxTargets)
                {
                    break;
                }
            }

            return found;
        }

        var styles = new FieldStyles(document);
        foreach (var paragraph in BookmarkOperations.ParagraphsOf(document.Body.Blocks))
        {
            if (found.Count >= MaxTargets)
            {
                break;
            }

            switch (kind)
            {
                case CrossRefTargetKind.Heading when styles.HeadingLevel(paragraph) is not null:
                    var text = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;
                    var label = host.NumberLabel(paragraph) is { Length: > 0 } number ? FieldRefresh.TrimLabel(number) : string.Empty;
                    var shown = Line((label.Length > 0 && text.Length > 0 ? label + " " : label) + text);
                    if (shown.Length > 0)
                    {
                        found.Add(new CrossRefTarget(kind, new TextPosition(StoryRef.Body, paragraph, 0), shown));
                    }

                    break;
                case CrossRefTargetKind.Caption when HasCaptionNumber(paragraph):
                    var caption = Line(ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text);
                    if (caption.Length > 0)
                    {
                        found.Add(new CrossRefTarget(kind, new TextPosition(StoryRef.Body, paragraph, 0), caption));
                    }

                    break;
                case CrossRefTargetKind.Footnote or CrossRefTargetKind.Endnote:
                    AddNotes(document, host, paragraph, kind, found);
                    break;
                default:
                    break;
            }
        }

        return found;
    }

    /// <summary>The choices a target kind offers, in the order the card shows them.</summary>
    public static IReadOnlyList<CrossRefKind> Offered(CrossRefTargetKind kind) => kind switch
    {
        CrossRefTargetKind.Heading => [CrossRefKind.NumberOnly, CrossRefKind.Text, CrossRefKind.Page],
        CrossRefTargetKind.Caption => [CrossRefKind.LabelAndNumber, CrossRefKind.NumberOnly, CrossRefKind.Text, CrossRefKind.Page],
        CrossRefTargetKind.Footnote or CrossRefTargetKind.Endnote => [CrossRefKind.NumberOnly, CrossRefKind.Page],
        CrossRefTargetKind.Bookmark => [CrossRefKind.Text, CrossRefKind.Page],
        _ => [],
    };

    /// <summary>The reason the target does not offer <paramref name="kind"/> (<see cref="CrossRefRefusal.KindNotOffered"/>), or null. A footnote or an endnote offers its number or its page; a heading its number, text or page; a caption its label and number, number, text or page; a bookmark its text or page.</summary>
    public static CrossRefRefusal? Refusal(CrossRefKind kind, CrossRefTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Enum.IsDefined(kind) && Offered(target.Kind).Contains(kind) ? null : CrossRefRefusal.KindNotOffered;
    }

    /// <summary>Whether the name is one of the hidden bookmarks Write makes for references: <c>_Ref</c> and digits.</summary>
    internal static bool IsRefName(string name)
    {
        const string prefix = "_Ref";
        if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = prefix.Length; i < name.Length; i++)
        {
            if (name[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A name for a new hidden bookmark: <c>_Ref</c> and the next number after the highest one among <paramref name="existing"/> (the names the document holds or points at, see <see cref="BookmarkOperations.TakenNames"/>; nine digits at the least, as Word writes them). Null when the digits are used up.</summary>
    internal static string? NextRefName(IReadOnlyCollection<string> existing)
    {
        var highest = 100_000_000L;
        foreach (var name in existing)
        {
            if (IsRefName(name) && name.Length <= 4 + 17 && long.TryParse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                highest = Math.Max(highest, value);
            }
        }

        return highest >= 99_999_999_999_999_999L ? null : "_Ref" + (highest + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the paragraph holds a top-level <c>SEQ</c> field the refresh counts (a caption).</summary>
    internal static bool HasCaptionNumber(Paragraph paragraph) =>
        FieldRefresh.HasFieldMarkup(paragraph) && FieldView.InParagraph(paragraph).Any(f => f.Parent is null && f.Support.Kind == FieldKind.Seq && FieldRefresh.IsUsable(f));

    private static void AddNotes(WordDocument document, IFieldHost host, Paragraph paragraph, CrossRefTargetKind kind, List<CrossRefTarget> found)
    {
        var type = kind == CrossRefTargetKind.Footnote ? NoteType.Footnote : NoteType.Endnote;
        foreach (var piece in ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Pieces.OfType<AtomicPiece>())
        {
            if (piece.Atomic != AtomicKind.NoteReference || piece.Length == 0 || found.Count >= MaxTargets)
            {
                continue;
            }

            var id = (type, piece.Content) switch
            {
                (NoteType.Footnote, FootnoteReference f) => f.Id,
                (NoteType.Endnote, EndnoteReference e) => e.Id,
                _ => (int?)null,
            };
            if (id is not { } noteId || host.NoteMarkText(noteId, type) is not { Length: > 0 } mark)
            {
                continue;
            }

            var note = (type == NoteType.Footnote ? document.Footnotes : document.Endnotes)?.Find(noteId);
            var text = note is null ? string.Empty : BookmarkOperations.ParagraphsOf(note.Content.Blocks).Select(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text).FirstOrDefault(t => t.Length > 0) ?? string.Empty;
            found.Add(new CrossRefTarget(kind, new TextPosition(StoryRef.Body, paragraph, piece.Start), Line(text.Length > 0 ? mark + " " + text : mark)));
        }
    }

    // One line for a list: control characters and the object character become spaces, runs of spaces one, the ends are trimmed and the line is cut at 80 characters (never inside a surrogate pair).
    private static string Line(string text)
    {
        var line = new StringBuilder(Math.Min(text.Length, MaxDisplay + 1));
        var space = true;
        foreach (var c in text)
        {
            if (char.IsControl(c) || c == ParagraphProjection.ObjectChar || char.IsWhiteSpace(c))
            {
                if (!space)
                {
                    line.Append(' ');
                    space = true;
                }

                continue;
            }

            line.Append(c);
            space = false;
            if (line.Length >= MaxDisplay)
            {
                break;
            }
        }

        var result = line.ToString().TrimEnd();
        if (result.Length > 0 && char.IsHighSurrogate(result[^1]))
        {
            result = result[..^1];
        }

        return result;
    }
}
