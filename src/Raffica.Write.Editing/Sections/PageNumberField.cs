using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public enum PageNumberLook
{
    /// <summary>The number alone.</summary>
    Plain,

    /// <summary>"Page 3".</summary>
    Page,

    /// <summary>"Page 3 of 12".</summary>
    PageOfTotal,

    /// <summary>"3 / 12".</summary>
    Slash,
}

public enum PageNumberAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Builds and recognizes the paragraph of a page number: complex fields (begin, instruction, separate, result, end), so the layout and Word both evaluate them.</summary>
internal static class PageNumberField
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// A paragraph with a <c>PAGE</c> field (and a <c>NUMPAGES</c> field for the looks with a total). <paramref name="prefix"/> is a plain run just before the first field
    /// ("A-" gives "A-1"); the word of the look ("Page", "of") comes from the caller, so it can be localized. The cached results are what a reader that does not evaluate fields shows.
    /// </summary>
    public static Paragraph Build(PageNumberLook look, PageNumberAlign align, string prefix, string pageWord, string ofWord, string cachedPage, string cachedTotal)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(pageWord);
        ArgumentNullException.ThrowIfNull(ofWord);
        ArgumentNullException.ThrowIfNull(cachedPage);
        ArgumentNullException.ThrowIfNull(cachedTotal);
        var paragraph = new Paragraph();
        var value = align switch
        {
            PageNumberAlign.Center => "center",
            PageNumberAlign.Right => "right",
            _ => "left",
        };
        paragraph.Properties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", value)));

        if (look is PageNumberLook.Page or PageNumberLook.PageOfTotal && pageWord.Length > 0)
        {
            paragraph.Inlines.Add(TextRun(pageWord + " "));
        }

        if (prefix.Length > 0)
        {
            paragraph.Inlines.Add(TextRun(prefix));
        }

        AddField(paragraph, "PAGE", cachedPage);
        switch (look)
        {
            case PageNumberLook.PageOfTotal:
                paragraph.Inlines.Add(TextRun(" " + ofWord + " "));
                AddField(paragraph, "NUMPAGES", cachedTotal);
                break;
            case PageNumberLook.Slash:
                paragraph.Inlines.Add(TextRun(" / "));
                AddField(paragraph, "NUMPAGES", cachedTotal);
                break;
        }

        return paragraph;
    }

    /// <summary>True when the instruction (the text of a field's code) is the <c>PAGE</c> field: the one definition every reader and writer here uses (not <c>NUMPAGES</c>, <c>SECTIONPAGES</c> or <c>SECTION</c>).</summary>
    public static bool IsPageInstruction(ReadOnlySpan<char> instruction)
    {
        var text = instruction.TrimStart();
        var end = text.IndexOfAny(' ', '\t');
        return (end < 0 ? text : text[..end]).Equals("PAGE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the paragraph holds a <c>PAGE</c> field, wherever it sits in the paragraph (a hyperlink or tracked change included).</summary>
    public static bool HoldsPage(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        foreach (var field in FieldScanner.Scan(paragraph.Inlines))
        {
            if (IsPageInstruction(field.Instruction))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a story's blocks hold a <c>PAGE</c> field in any paragraph, looking through tables and block content controls (Word's built-in page-number footer is a control around its paragraph).</summary>
    public static bool HoldsPage(IList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return Paragraphs(blocks).Any(HoldsPage);
    }

    /// <summary>The paragraphs of a list of blocks, tables and block content controls included (to a depth of 32).</summary>
    public static IEnumerable<Paragraph> Paragraphs(IList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var found = new List<Paragraph>();
        Collect(blocks, found, 0);
        return found;
    }

    /// <summary>
    /// Puts <paramref name="prefix"/> as a plain run just before every <c>PAGE</c> field of the paragraph that does not have it directly in front already (the run takes the formatting of the field's first run).
    /// With <paramref name="dry"/> nothing changes. <paramref name="found"/> counts the <c>PAGE</c> fields the walk reached (hyperlinks and tracked changes included), <paramref name="needed"/> the ones that lack the prefix.
    /// </summary>
    public static void PrefixPageFields(Paragraph paragraph, string prefix, bool dry, out int found, out int needed)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(prefix);
        found = 0;
        needed = 0;
        Prefix(paragraph.Inlines, prefix, dry, ref found, ref needed);
    }

    private static void Prefix(IList<Inline> inlines, string prefix, bool dry, ref int found, ref int needed)
    {
        for (var i = 0; i < inlines.Count; i++)
        {
            Run? template = null;
            switch (inlines[i])
            {
                case SimpleField simple when IsPageInstruction(simple.Instruction):
                    template = new Run();
                    break;
                case SimpleField simple:
                    Prefix(simple.Inlines, prefix, dry, ref found, ref needed);
                    break;
                case Hyperlink link:
                    Prefix(link.Inlines, prefix, dry, ref found, ref needed);
                    break;
                case TrackedChange change:
                    Prefix(change.Inlines, prefix, dry, ref found, ref needed);
                    break;
                case Run run when run.Content.Any(c => c is FieldChar { Kind: FieldCharKind.Begin }) && IsPageInstruction(InstructionAfter(inlines, i)):
                    template = run.Clone();
                    template.Content.Clear();
                    break;
                default:
                    break;
            }

            if (template is null)
            {
                continue;
            }

            found++;
            if (i > 0 && inlines[i - 1] is Run { Content.Count: > 0 } previous && previous.Content[^1] is TextContent text && text.Text.EndsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            needed++;
            if (!dry)
            {
                template.Content.Add(new TextContent(prefix));
                inlines.Insert(i, template);
                i++;
            }
        }
    }

    // The instruction text of the complex field that begins in inlines[begin]: the instruction runs up to its separator or end.
    private static string InstructionAfter(IList<Inline> inlines, int begin)
    {
        var text = new System.Text.StringBuilder();
        for (var j = begin; j < inlines.Count && inlines[j] is Run run; j++)
        {
            foreach (var content in run.Content)
            {
                switch (content)
                {
                    case InstructionText instruction:
                        text.Append(instruction.Text);
                        break;
                    case FieldChar { Kind: FieldCharKind.Separate or FieldCharKind.End }:
                        return text.ToString();
                    default:
                        break;
                }
            }
        }

        return text.ToString();
    }

    private static void Collect(IList<Block> blocks, List<Paragraph> into, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    into.Add(paragraph);
                    break;
                case ContentControlBlock control:
                    Collect(control.Content, into, depth + 1);
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
    }

    private static void CollectItem(RowItem item, List<Paragraph> into, int depth)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    CollectItem(child, into, depth + 1);
                }

                break;
            case TableCell cell:
                Collect(cell.Blocks, into, depth + 1);
                break;
            default:
                break;
        }
    }

    private static Run TextRun(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Run Mark(FieldCharKind kind)
    {
        var run = new Run();
        run.Content.Add(new FieldChar(kind));
        return run;
    }

    private static void AddField(Paragraph paragraph, string instruction, string cached)
    {
        paragraph.Inlines.Add(Mark(FieldCharKind.Begin));
        var code = new Run();
        code.Content.Add(new InstructionText(" " + instruction + " "));
        paragraph.Inlines.Add(code);
        paragraph.Inlines.Add(Mark(FieldCharKind.Separate));
        paragraph.Inlines.Add(TextRun(cached));
        paragraph.Inlines.Add(Mark(FieldCharKind.End));
    }
}
