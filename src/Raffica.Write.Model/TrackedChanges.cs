using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Model;

public enum RevisionKind
{
    Insert,
    Delete,
    MoveFrom,
    MoveTo,
    ParagraphMark,
    PropertyChange,
}

/// <summary>One revision found in a story. <see cref="Change"/> says whether a <see cref="RevisionKind.ParagraphMark"/> is an insertion, deletion or move; it is null for the other kinds.</summary>
public sealed record RevisionRecord(RevisionKind Kind, int Id, string? Author, DateTimeOffset? Date, TrackedChangeKind? Change);

/// <summary>A list of the revisions of a story in document order. A view: it reads the kept structure and changes nothing.</summary>
public static class TrackedChanges
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IReadOnlyList<RevisionRecord> List(Story story)
    {
        ArgumentNullException.ThrowIfNull(story);
        var records = new List<RevisionRecord>();
        foreach (var block in story.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                // Document order inside a paragraph: its properties come first.
                Properties(paragraph.Properties, records);
                Inlines(paragraph.Inlines, records);
            }
        }

        return records;
    }

    private static void Properties(PropertyBag bag, List<RevisionRecord> records)
    {
        foreach (var child in bag.Children)
        {
            if (child.Name == W + "rPr")
            {
                foreach (var mark in child.Elements())
                {
                    if (MarkKind(mark.Name) is TrackedChangeKind kind)
                    {
                        records.Add(Record(RevisionKind.ParagraphMark, mark, kind));
                    }
                    else if (mark.Name == W + "rPrChange")
                    {
                        records.Add(Record(RevisionKind.PropertyChange, mark, null));
                    }
                }
            }
            else if (child.Name == W + "pPrChange" || child.Name == W + "rPrChange")
            {
                records.Add(Record(RevisionKind.PropertyChange, child, null));
            }
        }
    }

    private static void Inlines(IEnumerable<Inline> inlines, List<RevisionRecord> records)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    Properties(run.Properties, records);
                    break;
                case TrackedChange change:
                    records.Add(new RevisionRecord(
                        change.Kind switch
                        {
                            TrackedChangeKind.Insert => RevisionKind.Insert,
                            TrackedChangeKind.Delete => RevisionKind.Delete,
                            TrackedChangeKind.MoveFrom => RevisionKind.MoveFrom,
                            _ => RevisionKind.MoveTo,
                        },
                        change.Info.Id,
                        change.Info.Author,
                        change.Info.Date,
                        null));
                    Inlines(change.Inlines, records);
                    break;
                case Hyperlink link:
                    Inlines(link.Inlines, records);
                    break;
                case SimpleField field:
                    Inlines(field.Inlines, records);
                    break;
            }
        }
    }

    private static TrackedChangeKind? MarkKind(XName name) =>
        name == W + "ins" ? TrackedChangeKind.Insert
        : name == W + "del" ? TrackedChangeKind.Delete
        : name == W + "moveFrom" ? TrackedChangeKind.MoveFrom
        : name == W + "moveTo" ? TrackedChangeKind.MoveTo
        : null;

    private static RevisionRecord Record(RevisionKind kind, XElement element, TrackedChangeKind? change)
    {
        _ = int.TryParse((string?)element.Attribute(W + "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
        return new RevisionRecord(kind, id, (string?)element.Attribute(W + "author"), RevisionInfo.Parse((string?)element.Attribute(W + "date")), change);
    }
}
