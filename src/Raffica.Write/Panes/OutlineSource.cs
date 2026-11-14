using System.Text;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Panes;

/// <summary>One heading of the document as the outline lists it.</summary>
/// <param name="Level">The level, 1 to 9 (the outline level of Word plus one).</param>
/// <param name="Text">The text of the heading as it reads on the page (field results in, deleted text out).</param>
/// <param name="BodyBlockIndex">The index of the heading's top-level block in the body (what <c>DocumentLayout.Locate</c> takes).</param>
internal readonly record struct OutlineEntry(int Level, string Text, int BodyBlockIndex);

/// <summary>
/// Reads the outline of a document: every body-level paragraph whose resolved outline level is set (its own <c>w:outlineLvl</c> or the one of its style, never the
/// name of a style) and whose text is not empty. A pure function of the model, so it may run on a pool thread. A paragraph inside a table is not listed (4.2 lists
/// body-level paragraphs only; a block content control at the top level counts as its blocks and they carry the index of the control).
/// </summary>
internal static class OutlineSource
{
    /// <summary>The longest heading text an entry holds (a longer paragraph is cut; the outline is a list of names).</summary>
    internal const int MaxTextLength = 300;

    private const int MaxControlDepth = 32;

    /// <summary>The headings in document order. Empty headings are left out; tracked changes are read as accepted (insertions in, deletions out).</summary>
    public static IReadOnlyList<OutlineEntry> Read(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var entries = new List<OutlineEntry>();
        var blocks = document.Body.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            Collect(blocks[i], i, resolver, entries, 0);
        }

        return entries;
    }

    private static void Collect(Block block, int bodyIndex, StyleResolver resolver, List<OutlineEntry> entries, int depth)
    {
        if (block is ContentControlBlock control)
        {
            if (depth < MaxControlDepth)
            {
                foreach (var inner in control.Content)
                {
                    Collect(inner, bodyIndex, resolver, entries, depth + 1);
                }
            }

            return;
        }

        if (block is not Paragraph paragraph || resolver.ForParagraph(paragraph).OutlineLevel is not { } level)
        {
            return;
        }

        var text = TextOf(paragraph);
        if (text.Length > 0)
        {
            entries.Add(new OutlineEntry(Math.Clamp(level, 0, 8) + 1, text, bodyIndex));
        }
    }

    // The text of a paragraph as it is shown: the result of a field and not its instruction, an insertion and not a deletion.
    private static string TextOf(Paragraph paragraph)
    {
        var text = new StringBuilder();
        var fields = new List<bool>();
        AddInlines(paragraph.Inlines, text, fields, 0);
        return Normalize(text);
    }

    private static void AddInlines(IEnumerable<Inline> inlines, StringBuilder text, List<bool> fields, int depth)
    {
        if (depth > MaxControlDepth)
        {
            return;
        }

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    AddRun(run, text, fields);
                    break;
                case Hyperlink link:
                    AddInlines(link.Inlines, text, fields, depth + 1);
                    break;
                case SimpleField field:
                    AddInlines(field.Inlines, text, fields, depth + 1);
                    break;
                case ContentControlInline control:
                    AddInlines(control.Inlines, text, fields, depth + 1);
                    break;
                case TrackedChange { Kind: TrackedChangeKind.Insert or TrackedChangeKind.MoveTo } change:
                    AddInlines(change.Inlines, text, fields, depth + 1);
                    break;
                default:
                    break;
            }
        }
    }

    // `fields` holds, per open complex field, whether its result part has begun; text counts only when every open field is in its result.
    private static void AddRun(Run run, StringBuilder text, List<bool> fields)
    {
        foreach (var content in run.Content)
        {
            switch (content)
            {
                case FieldChar { Kind: FieldCharKind.Begin }:
                    fields.Add(false);
                    break;
                case FieldChar { Kind: FieldCharKind.Separate } when fields.Count > 0:
                    fields[^1] = true;
                    break;
                case FieldChar { Kind: FieldCharKind.End } when fields.Count > 0:
                    fields.RemoveAt(fields.Count - 1);
                    break;
                case TextContent t when fields.TrueForAll(static inResult => inResult):
                    text.Append(t.Text);
                    break;
                case TabContent or BreakContent when fields.TrueForAll(static inResult => inResult):
                    text.Append(' ');
                    break;
                default:
                    break;
            }
        }
    }

    // Runs of white space become one space; the ends are trimmed; a very long text is cut.
    private static string Normalize(StringBuilder raw)
    {
        var result = new StringBuilder(Math.Min(raw.Length, MaxTextLength));
        var space = false;
        for (var i = 0; i < raw.Length && result.Length < MaxTextLength; i++)
        {
            var c = raw[i];
            if (char.IsWhiteSpace(c) || c is '\u00A0' or '\u200B')
            {
                space = result.Length > 0;
                continue;
            }

            if (space)
            {
                result.Append(' ');
                space = false;
            }

            result.Append(c);
        }

        return result.ToString();
    }
}
