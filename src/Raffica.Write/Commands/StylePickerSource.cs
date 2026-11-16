using Raffica.Shell.Frame;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>
/// The lists of the style pickers: the styles of the document a person may choose. A style is listed when it is marked <c>qFormat</c> or is in use (a paragraph or run of the file names it),
/// and it is not <c>semiHidden</c> or <c>hidden</c>. The order is the default style of the kind first, then <c>uiPriority</c> (a style without one counts as 99, as in Word), then the name. A style id a
/// paragraph or run uses that the file does not define is listed as its id and never dropped. A file without a styles part has no list: the picker is disabled with a reason
/// (<see cref="WriteStrings.ReasonNoStyles"/>). Lists are read each time the picker opens, never earlier.
/// </summary>
internal static class StylePickerSource
{
    private const int DefaultPriority = 99;

    /// <summary>The paragraph styles of the document, in picker order; empty when the file has no styles part.</summary>
    public static IReadOnlyList<PickerItem> Paragraph(WordDocument document) => List(document, "paragraph");

    /// <summary>The character styles of the document, in picker order; empty when the file has no styles part.</summary>
    public static IReadOnlyList<PickerItem> Character(WordDocument document) => List(document, "character");

    private static List<PickerItem> List(WordDocument document, string kind)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Styles is not { } sheet)
        {
            return [];
        }

        var used = UsedStyleIds(document, kind == "paragraph");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var listed = new List<Style>();
        foreach (var style in sheet.Styles)
        {
            if ((style.Type ?? "paragraph") != kind)
            {
                continue;
            }

            defined.Add(style.Id);
            if (IsMarked(style, "hidden") || IsMarked(style, "semiHidden"))
            {
                continue;
            }

            if (IsMarked(style, "qFormat") || used.Contains(style.Id) || style.IsDefault)
            {
                listed.Add(style);
            }
        }

        var items = listed
            .OrderBy(s => s.IsDefault ? 0 : 1)
            .ThenBy(PriorityOf)
            .ThenBy(s => s.Name ?? s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(s => new PickerItem(s.Id, s.Name ?? s.Id))
            .ToList();

        // A style the file uses but does not define: its id is all there is to show.
        foreach (var id in used.Where(id => !defined.Contains(id)).Order(StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new PickerItem(id, id));
        }

        return items;
    }

    private static bool IsMarked(Style style, string local)
    {
        foreach (var kept in style.KeptChildren)
        {
            var element = kept.Content.Element;
            if (element.Name.LocalName != local)
            {
                continue;
            }

            // A flag element is on when it has no value or a true one.
            var value = element.Attribute(element.Name.Namespace + "val")?.Value;
            return value is null or "1" or "true" or "on";
        }

        return false;
    }

    private static int PriorityOf(Style style)
    {
        foreach (var kept in style.KeptChildren)
        {
            var element = kept.Content.Element;
            if (element.Name.LocalName == "uiPriority" && int.TryParse(element.Attribute(element.Name.Namespace + "val")?.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var priority))
            {
                return priority;
            }
        }

        return DefaultPriority;
    }

    // The style ids a paragraph (paragraph kind) or run (character kind) names, over every story of the file.
    private static HashSet<string> UsedStyleIds(WordDocument document, bool paragraphs)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        Walk(document.Body.Blocks, paragraphs, used);
        foreach (var part in document.HeadersFooters.Values)
        {
            Walk(part.Content.Blocks, paragraphs, used);
        }

        foreach (var note in document.Footnotes?.Notes ?? [])
        {
            Walk(note.Content.Blocks, paragraphs, used);
        }

        foreach (var note in document.Endnotes?.Notes ?? [])
        {
            Walk(note.Content.Blocks, paragraphs, used);
        }

        return used;
    }

    private static void Walk(IList<Block> blocks, bool paragraphs, HashSet<string> used)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    if (paragraphs)
                    {
                        AddIfSet(paragraph.Properties.StyleId, used);
                    }
                    else
                    {
                        WalkInlines(paragraph.Inlines, used);
                    }

                    break;
                case ContentControlBlock control:
                    Walk(control.Content, paragraphs, used);
                    break;
                case Table table:
                    foreach (var row in table.Rows.OfType<TableRow>())
                    {
                        foreach (var cell in row.Items.OfType<TableCell>())
                        {
                            Walk(cell.Blocks, paragraphs, used);
                        }
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static void WalkInlines(IList<Inline> inlines, HashSet<string> used)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    AddIfSet(run.Properties.StyleId, used);
                    break;
                case Hyperlink link:
                    WalkInlines(link.Inlines, used);
                    break;
                case SimpleField field:
                    WalkInlines(field.Inlines, used);
                    break;
                case TrackedChange change:
                    WalkInlines(change.Inlines, used);
                    break;
                case ContentControlInline control:
                    WalkInlines(control.Inlines, used);
                    break;
                default:
                    break;
            }
        }
    }

    private static void AddIfSet(string? id, HashSet<string> used)
    {
        if (!string.IsNullOrEmpty(id))
        {
            used.Add(id);
        }
    }
}
