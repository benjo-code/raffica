using DocumentFormat.OpenXml.Packaging;
using Raffica.Write.Model;

namespace Raffica.Write.Docx;

/// <summary>
/// Where the parts the model understands are in a package: found through the relationships of the main part (and, for the
/// comments extension, of the comments part). Relationship ids are stored, not part objects, so the same map finds the parts
/// again in the copy a save edits. Only the package layer is used: no typed content accessor of the SDK is ever called.
/// </summary>
internal sealed class PartMap
{
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public const string HeaderType = Office + "header";
    public const string FooterType = Office + "footer";
    public const string StylesType = Office + "styles";
    public const string NumberingType = Office + "numbering";
    public const string SettingsType = Office + "settings";
    public const string FootnotesType = Office + "footnotes";
    public const string EndnotesType = Office + "endnotes";
    public const string CommentsType = Office + "comments";
    public const string CommentsExtendedType = "http://schemas.microsoft.com/office/2011/relationships/commentsExtended";

    public string? Styles { get; private set; }

    public string? Numbering { get; private set; }

    public string? Settings { get; private set; }

    public string? Footnotes { get; private set; }

    public string? Endnotes { get; private set; }

    public string? Comments { get; private set; }

    /// <summary>The relationship id of the comments extension part, in the main part or (when <see cref="ExtendedInCommentsPart"/>) in the comments part.</summary>
    public string? CommentsExtended { get; private set; }

    public bool ExtendedInCommentsPart { get; private set; }

    /// <summary>Finds the single-instance parts through the relationships of <paramref name="main"/>; the first relationship of a type wins.</summary>
    public static PartMap Locate(MainDocumentPart main)
    {
        var map = new PartMap
        {
            Styles = IdOf(main, StylesType),
            Numbering = IdOf(main, NumberingType),
            Settings = IdOf(main, SettingsType),
            Footnotes = IdOf(main, FootnotesType),
            Endnotes = IdOf(main, EndnotesType),
            Comments = IdOf(main, CommentsType),
            CommentsExtended = IdOf(main, CommentsExtendedType),
        };
        if (map.CommentsExtended is null && map.Comments is not null && ById(main, map.Comments) is { } comments)
        {
            map.CommentsExtended = IdOf(comments, CommentsExtendedType);
            map.ExtendedInCommentsPart = map.CommentsExtended is not null;
        }

        return map;
    }

    /// <summary>The part that relationship <paramref name="id"/> of <paramref name="owner"/> points to, or null (no such id, or the id belongs to an external relationship).</summary>
    public static OpenXmlPart? ById(OpenXmlPartContainer owner, string id)
    {
        foreach (var pair in owner.Parts)
        {
            if (string.Equals(pair.RelationshipId, id, StringComparison.Ordinal))
            {
                return pair.OpenXmlPart;
            }
        }

        return null;
    }

    /// <summary>The part for a header or footer reference: only a part related as a header or footer counts.</summary>
    public static OpenXmlPart? HeaderFooterById(MainDocumentPart main, string id)
    {
        foreach (var pair in main.Parts)
        {
            if (string.Equals(pair.RelationshipId, id, StringComparison.Ordinal))
            {
                var type = pair.OpenXmlPart.RelationshipType;
                return type is HeaderType or FooterType ? pair.OpenXmlPart : null;
            }
        }

        return null;
    }

    /// <summary>Every relationship id used by a header or footer reference in a section of <paramref name="story"/> (body-level and paragraph section breaks, also inside tables and content controls), in order of first use.</summary>
    public static IReadOnlyList<string> HeaderFooterIds(Story story)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(SectionProperties? section)
        {
            if (section is null)
            {
                return;
            }

            foreach (var reference in section.HeaderFooterReferences)
            {
                if (seen.Add(reference.RelationshipId))
                {
                    ids.Add(reference.RelationshipId);
                }
            }
        }

        Walk(story.Blocks, Add);
        Add(story.FinalSection);
        return ids;
    }

    private static void Walk(IEnumerable<Block> blocks, Action<SectionProperties?> add)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    add(paragraph.SectionBreak);
                    break;
                case Table table:
                    WalkRows(table.Rows, add);
                    break;
                case ContentControlBlock control:
                    Walk(control.Content, add);
                    break;
            }
        }
    }

    private static void WalkRows(IEnumerable<RowItem> items, Action<SectionProperties?> add)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case TableRow row:
                    WalkRows(row.Items, add);
                    break;
                case TableCell cell:
                    Walk(cell.Blocks, add);
                    break;
            }
        }
    }

    private static string? IdOf(OpenXmlPartContainer owner, string relationshipType)
    {
        foreach (var pair in owner.Parts)
        {
            if (pair.OpenXmlPart.RelationshipType == relationshipType)
            {
                return pair.RelationshipId;
            }
        }

        return null;
    }
}
