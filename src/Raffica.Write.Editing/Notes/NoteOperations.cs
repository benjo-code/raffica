using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>The pieces of a note insertion that do not depend on the editor's state: the styles, the ids, the note and the reference run, and the references inside blocks.</summary>
internal static class NoteOperations
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The ids of the styles of a notes part: the reference character style and the text paragraph style.</summary>
    public static (string Reference, string Text) StyleIds(NoteType type) =>
        type == NoteType.Footnote ? ("FootnoteReference", "FootnoteText") : ("EndnoteReference", "EndnoteText");

    /// <summary>
    /// Adds the styles a note needs to the style sheet when the document has none of them: the superscript character style of the reference and the small paragraph style of the note text. A style that exists is left as
    /// it is. Returns the change a step records (the first style made, the other as its companion; old and new are equal clones, the styles are made in place), or null when nothing was made or the document has no style sheet.
    /// </summary>
    public static StyleChange? EnsureStyles(WordDocument document, NoteType type)
    {
        var sheet = document.Styles;
        if (sheet is null)
        {
            return null;
        }

        var (referenceId, textId) = StyleIds(type);
        var word = type == NoteType.Footnote ? "footnote" : "endnote";
        var made = new List<StyleChange>();
        if (sheet.Find(referenceId) is null)
        {
            var style = new Style { Id = referenceId, Type = "character", Name = word + " reference", BasedOn = sheet.Find("DefaultParagraphFont") is null ? null : "DefaultParagraphFont" };
            style.RunProperties.Set(W + "vertAlign", new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript")));
            Priority(style);
            sheet.Styles.Add(style);
            made.Add(new StyleChange(style.Clone(), style.Clone()));
        }

        if (sheet.Find(textId) is null)
        {
            var style = new Style { Id = textId, Type = "paragraph", Name = word + " text", BasedOn = sheet.Find("Normal") is null ? null : "Normal" };
            style.ParagraphProperties.Set(W + "spacing", new XElement(W + "spacing", new XAttribute(W + "after", "0"), new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto")));
            style.RunProperties.Set(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", "20")));
            style.RunProperties.Set(W + "szCs", new XElement(W + "szCs", new XAttribute(W + "val", "20")));
            Priority(style);
            sheet.Styles.Add(style);
            made.Add(new StyleChange(style.Clone(), style.Clone()));
        }

        return made.Count == 0 ? null : made[0] with { Companions = made.Skip(1).ToList() };
    }

    private static void Priority(Style style)
    {
        style.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "uiPriority", new XAttribute(W + "val", "99")))));
        style.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(new XElement(W + "unhideWhenUsed"))));
    }

    /// <summary>The id for a new note: one more than the highest id of the part (the separators are -1 and 0, so the first note is 1).</summary>
    public static int NextId(NoteCollection? collection)
    {
        var highest = 0;
        if (collection is not null)
        {
            foreach (var note in collection.Notes)
            {
                highest = Math.Max(highest, note.Id);
            }
        }

        return highest + 1;
    }

    /// <summary>The run that holds the reference mark in the body: the character style of the notes (or a direct superscript when the document has no style sheet) and the reference content.</summary>
    public static Run ReferenceRun(WordDocument document, NoteType type, int id)
    {
        var run = new Run();
        MarkStyle(document, type, run);
        run.Content.Add(type == NoteType.Footnote ? new FootnoteReference(id) : new EndnoteReference(id));
        return run;
    }

    /// <summary>A new note: its first paragraph holds the note's own mark (<c>w:footnoteRef</c> or <c>w:endnoteRef</c>) and a space, in the note text style.</summary>
    public static Note NewNote(WordDocument document, NoteType type, int id)
    {
        var item = type == NoteType.Footnote ? "footnote" : "endnote";
        var note = new Note(id, NoteKind.Normal, new Story(new OoxmlRoot(W + item, [])));
        var paragraph = new Paragraph();
        if (document.Styles?.Find(StyleIds(type).Text) is not null)
        {
            paragraph.Properties.StyleId = StyleIds(type).Text;
        }

        var mark = new Run();
        MarkStyle(document, type, mark);
        mark.Content.Add(new UnknownRunContent(new UnknownContent(new XElement(W + item + "Ref"))));
        paragraph.Inlines.Add(mark);
        var space = new Run();
        space.Content.Add(new TextContent(" "));
        paragraph.Inlines.Add(space);
        note.Content.Blocks.Add(paragraph);
        return note;
    }

    private static void MarkStyle(WordDocument document, NoteType type, Run run)
    {
        if (document.Styles?.Find(StyleIds(type).Reference) is not null)
        {
            run.Properties.StyleId = StyleIds(type).Reference;
        }
        else
        {
            run.Properties.Set(W + "vertAlign", new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript")));
        }
    }

    /// <summary>Every footnote and endnote reference in the blocks (tables, content controls, hyperlinks and tracked changes looked through), in order.</summary>
    public static List<RunContent> References(IEnumerable<Block> blocks)
    {
        var found = new List<RunContent>();
        foreach (var block in blocks)
        {
            Collect(block, found, 0);
        }

        return found;
    }

    private static void Collect(Block block, List<RunContent> into, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (block)
        {
            case Paragraph paragraph:
                CollectInlines(paragraph.Inlines, into, depth);
                break;
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    Collect(inner, into, depth + 1);
                }

                break;
            case Table table:
                foreach (var row in table.Rows)
                {
                    CollectItem(row, into, depth + 1);
                }

                break;
            default:
                break;
        }
    }

    private static void CollectItem(RowItem item, List<RunContent> into, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    CollectItem(child, into, depth + 1);
                }

                break;
            case TableCell cell:
                foreach (var inner in cell.Blocks)
                {
                    Collect(inner, into, depth + 1);
                }

                break;
            default:
                break;
        }
    }

    private static void CollectInlines(IEnumerable<Inline> inlines, List<RunContent> into, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                foreach (var content in run.Content)
                {
                    if (content is FootnoteReference or EndnoteReference)
                    {
                        into.Add(content);
                    }
                }
            }
            else if (ParagraphMap.ChildrenOf(inline) is { } children)
            {
                CollectInlines(children, into, depth + 1);
            }
        }
    }

    /// <summary>The part and the id a reference content points to.</summary>
    public static (NoteType Type, int Id) Describe(RunContent reference) => reference switch
    {
        FootnoteReference footnote => (NoteType.Footnote, footnote.Id),
        EndnoteReference endnote => (NoteType.Endnote, endnote.Id),
        _ => throw new ArgumentException("Not a note reference.", nameof(reference)),
    };

    /// <summary>The story of a note: the key is the id for a footnote and <c>endnote:</c> and the id for an endnote.</summary>
    public static StoryRef StoryOf(NoteType type, int id) =>
        new(StoryKind.Note, (type == NoteType.Endnote ? "endnote:" : string.Empty) + id.ToString(CultureInfo.InvariantCulture));
}
