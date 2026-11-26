using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>Hand-made documents with tables of contents and a dump of a document that shows every difference (properties, run content, field marks, bookmarks), for tests that must prove a refused edit left the model as it was.</summary>
internal static class TocDocument
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static Run Mark(FieldCharKind kind)
    {
        var run = new Run();
        run.Content.Add(new FieldChar(kind));
        return run;
    }

    public static Run Code(string text)
    {
        var run = new Run();
        run.Content.Add(new InstructionText(text));
        return run;
    }

    public static Run Text(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    public static Paragraph Plain(string text)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(Text(text));
        return paragraph;
    }

    /// <summary>
    /// The paragraphs of a table of contents: the begin, the instruction and the separate in the first, the end in the last. With <paramref name="nested"/> each entry is a link that holds a complete <c>PAGEREF</c> field, as Word writes it.
    /// </summary>
    public static List<Paragraph> Toc(bool nested, params string[] entries)
    {
        var paragraphs = new List<Paragraph>();
        for (var i = 0; i < entries.Length; i++)
        {
            var paragraph = new Paragraph();
            if (i == 0)
            {
                paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
                paragraph.Inlines.Add(Code(" TOC \\o \"1-3\" \\h \\z \\u "));
                paragraph.Inlines.Add(Mark(FieldCharKind.Separate));
            }

            if (nested)
            {
                var link = new Hyperlink { Anchor = "_Toc" + i };
                link.Inlines.Add(Text(entries[i]));
                link.Inlines.Add(Mark(FieldCharKind.Begin));
                link.Inlines.Add(Code(" PAGEREF _Toc" + i + " \\h "));
                link.Inlines.Add(Mark(FieldCharKind.Separate));
                link.Inlines.Add(Text((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                link.Inlines.Add(Mark(FieldCharKind.End));
                paragraph.Inlines.Add(link);
            }
            else
            {
                paragraph.Inlines.Add(Text(entries[i]));
            }

            if (i == entries.Length - 1)
            {
                paragraph.Inlines.Add(Mark(FieldCharKind.End));
            }

            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    public static WordDocument Document(params Block[] blocks)
    {
        var story = new Story(new OoxmlRoot(W + "document", []));
        foreach (var block in blocks)
        {
            story.Blocks.Add(block);
        }

        return new WordDocument(story);
    }

    /// <summary>Every block of the body as text, with the properties, the run content (text, field marks, instructions) and the other inlines; two equal dumps are equal documents for the purposes of the editor.</summary>
    public static string Dump(WordDocument document)
    {
        var text = new StringBuilder();
        foreach (var block in document.Body.Blocks)
        {
            DumpBlock(text, block);
        }

        return text.ToString();
    }

    private static void DumpBlock(StringBuilder text, Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                text.Append("P[").AppendJoin(",", paragraph.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting))).Append("]sec=").Append(paragraph.SectionBreak is not null);
                text.Append(" attrs=").AppendJoin(",", paragraph.UnknownAttributes.Select(a => a.ToString())).Append(' ');
                DumpInlines(text, paragraph.Inlines);
                text.Append('\n');
                break;
            case Table table:
                text.Append("TABLE\n");
                foreach (var row in table.Rows)
                {
                    DumpRow(text, row);
                }

                text.Append("ENDTABLE\n");
                break;
            case ContentControlBlock control:
                text.Append("SDT\n");
                foreach (var inner in control.Content)
                {
                    DumpBlock(text, inner);
                }

                text.Append("ENDSDT\n");
                break;
            default:
                text.Append(block.GetType().Name).Append('\n');
                break;
        }
    }

    private static void DumpRow(StringBuilder text, RowItem item)
    {
        switch (item)
        {
            case TableRow row:
                text.Append("ROW\n");
                foreach (var child in row.Items)
                {
                    DumpRow(text, child);
                }

                break;
            case TableCell cell:
                text.Append("CELL\n");
                foreach (var block in cell.Blocks)
                {
                    DumpBlock(text, block);
                }

                break;
            default:
                text.Append(item.GetType().Name).Append('\n');
                break;
        }
    }

    private static void DumpInlines(StringBuilder text, IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    text.Append("R[").AppendJoin(",", run.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting))).Append("]{");
                    foreach (var content in run.Content)
                    {
                        text.Append(content switch
                        {
                            TextContent t => "t:" + t.Text,
                            FieldChar f => "f:" + f.Kind,
                            InstructionText i => "i:" + i.Text,
                            _ => content.GetType().Name,
                        }).Append('|');
                    }

                    text.Append('}');
                    break;
                case Hyperlink link:
                    text.Append("H(").Append(link.Anchor).Append(")<");
                    DumpInlines(text, link.Inlines);
                    text.Append('>');
                    break;
                case BookmarkStart start:
                    text.Append("BS:").Append(start.Name);
                    break;
                case BookmarkEnd end:
                    text.Append("BE:").Append(end.Id);
                    break;
                case SimpleField simple:
                    text.Append("SF(").Append(simple.Instruction).Append(")<");
                    DumpInlines(text, simple.Inlines);
                    text.Append('>');
                    break;
                default:
                    text.Append(inline.GetType().Name);
                    break;
            }

            text.Append(';');
        }
    }
}
