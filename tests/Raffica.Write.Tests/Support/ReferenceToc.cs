using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>One entry the oracle expects in a table of contents: the paragraph it stands for, its level, the line (the label, a tab and the text, or the text alone) and whether it shows a page number.</summary>
internal sealed record ExpectedEntry(Paragraph Heading, int Level, string Line, bool Page);

/// <summary>A table of contents of the body with the entries the oracle expects (the sentence of an empty one is <see cref="Sentence"/>).</summary>
internal sealed record ExpectedToc(IList<Block> Container, int First, int Last, bool TailOnly, bool Links, bool Figures, IReadOnlyList<ExpectedEntry> Entries);

/// <summary>
/// The entries of the tables of contents of a document's body, counted from the model alone: its own walk over the headings (and captions), its own reading of the visible text and the outline levels, and its own check of what
/// an entry paragraph says. It shares no code with <c>TocBuilder</c>, <c>TocRefresh</c> or <c>TocSpec</c>; only the field grammar (<see cref="FieldInstruction"/>, <see cref="TocRanges"/>) and the number labels of the host are
/// shared. The rules: a heading is a paragraph of the style Heading1 to Heading9 by id (with <c>\u</c> also one with an outline level, its own or its style's) inside the levels of <c>\o</c>; its line is the visible text
/// (line breaks and tabs as spaces, hidden text and tracked deletions left out, trimmed), after the number label and a tab when the host shows a label; a heading with no text is left out; a list takes the paragraphs that hold a
/// <c>SEQ</c> of its label, with the whole text of the paragraph. Page numbers are not judged (they come with the page results).
/// </summary>
internal static class ReferenceToc
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The sentence of an empty table of contents in English.</summary>
    public const string Sentence = "No table of contents entries found.";

    /// <summary>The sentence of an empty table of contents in German.</summary>
    public const string GermanSentence = "Es wurden keine Einträge für das Inhaltsverzeichnis gefunden.";

    public static IReadOnlyList<ExpectedToc> Expected(WordDocument document, IFieldHost host)
    {
        var found = new List<ExpectedToc>();
        var all = new List<(IList<Block> Container, int First, int Last)>();
        Gather(document.Body.Blocks, all, 0);
        var inside = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var (container, first, last) in all)
        {
            for (var i = first; i <= last; i++)
            {
                if (container[i] is Paragraph paragraph)
                {
                    inside.Add(paragraph);
                }
            }
        }

        Collect(document.Body.Blocks, found, document, host, inside, 0);
        return found;
    }

    private static void Collect(IList<Block> container, List<ExpectedToc> found, WordDocument document, IFieldHost host, HashSet<Paragraph> inside, int depth)
    {
        var covered = new HashSet<int>();
        foreach (var range in TocRanges.In(container) ?? [])
        {
            for (var i = range.First; i <= range.Last; i++)
            {
                covered.Add(i);
            }

            var tailOnly = container[range.Last] is Paragraph last && last.Inlines.All(i => i is BookmarkStart or BookmarkEnd || (i is Run r && r.Content.All(c => c is FieldChar)));
            Add(found, container, range.First, range.Last, tailOnly, range.Instruction, document, host, inside);
        }

        for (var i = 0; i < container.Count; i++)
        {
            if (covered.Contains(i) || container[i] is not Paragraph paragraph)
            {
                continue;
            }

            var toc = FieldView.InParagraph(paragraph).FirstOrDefault(f => f.Parent is null && f.Support.Kind == FieldKind.Toc && f.IsComplete);
            if (toc is not null)
            {
                Add(found, container, i, i, false, toc.Info!.Instruction, document, host, inside);
            }
        }

        if (depth > 8)
        {
            return;
        }

        foreach (var block in container)
        {
            switch (block)
            {
                case ContentControlBlock control:
                    Collect(control.Content, found, document, host, inside, depth + 1);
                    break;
                case Table table:
                    foreach (var cell in table.Rows.OfType<TableRow>().SelectMany(r => r.Items.OfType<TableCell>()))
                    {
                        Collect(cell.Blocks, found, document, host, inside, depth + 1);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static void Add(List<ExpectedToc> found, IList<Block> container, int first, int last, bool tailOnly, string instructionText, WordDocument document, IFieldHost host, HashSet<Paragraph> inside)
    {
        var instruction = FieldInstruction.Parse(instructionText);
        if (instruction.HasUnknownToken)
        {
            return;
        }

        string? label = instruction.ValueOf("c");
        var from = 1;
        var to = 9;
        if (label is null)
        {
            if (instruction.ValueOf("o") is { } levels)
            {
                var parts = levels.Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out from) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out to))
                {
                    return;
                }
            }
            else if (!instruction.Has("u"))
            {
                return;
            }
        }

        var noPages = instruction.Has("n") && instruction.ValueOf("n") is null;
        (int From, int To)? noRange = null;
        if (instruction.ValueOf("n") is { } range)
        {
            var parts = range.Split('-');
            noRange = (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        var entries = new List<ExpectedEntry>();
        foreach (var paragraph in ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>())
        {
            if (inside.Contains(paragraph))
            {
                continue;
            }

            if (label is not null)
            {
                if (!HoldsSeq(paragraph, label))
                {
                    continue;
                }

                var whole = VisibleText(paragraph);
                if (whole.Length > 0)
                {
                    entries.Add(new ExpectedEntry(paragraph, 1, whole, !noPages && !(noRange is { } r && r.From <= 1 && r.To >= 1)));
                }

                continue;
            }

            var level = LevelOf(document, paragraph, instruction.Has("u"));
            if (level is null || level < from || level > to)
            {
                continue;
            }

            var text = VisibleText(paragraph);
            if (text.Length == 0)
            {
                continue;
            }

            var number = host.NumberLabel(paragraph);
            entries.Add(new ExpectedEntry(paragraph, level.Value, string.IsNullOrEmpty(number) ? text : number + "\t" + text, !noPages && !(noRange is { } n && level >= n.From && level <= n.To)));
        }

        found.Add(new ExpectedToc(container, first, last, tailOnly, instruction.Has("h"), label is not null, entries));
    }

    private static void Gather(IList<Block> container, List<(IList<Block>, int, int)> all, int depth)
    {
        foreach (var range in TocRanges.In(container) ?? [])
        {
            all.Add((container, range.First, range.Last));
        }

        if (depth > 8)
        {
            return;
        }

        foreach (var block in container)
        {
            switch (block)
            {
                case ContentControlBlock control:
                    Gather(control.Content, all, depth + 1);
                    break;
                case Table table:
                    foreach (var cell in table.Rows.OfType<TableRow>().SelectMany(r => r.Items.OfType<TableCell>()))
                    {
                        Gather(cell.Blocks, all, depth + 1);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static bool HoldsSeq(Paragraph paragraph, string label) =>
        FieldView.InParagraph(paragraph).Any(f => f.Parent is null && f.Support.Kind == FieldKind.Seq && f.Support.IsSupported && f.IsComplete && !f.InTrackedChange && string.Equals(f.Instruction.Argument, label, StringComparison.OrdinalIgnoreCase));

    private static int? LevelOf(WordDocument document, Paragraph paragraph, bool outline)
    {
        var id = paragraph.Properties.StyleId;
        for (var level = 1; level <= 9; level++)
        {
            if (id == "Heading" + level.ToString(CultureInfo.InvariantCulture))
            {
                return level;
            }
        }

        if (!outline)
        {
            return null;
        }

        if (paragraph.Properties.Get(W + "outlineLvl") is { } own)
        {
            return Outline(own);
        }

        var current = id is null ? null : document.Styles?.Find(id);
        for (var depth = 0; current is not null && depth < 32; depth++)
        {
            if (current.ParagraphProperties.Get(W + "outlineLvl") is { } styled)
            {
                return Outline(styled);
            }

            current = current.BasedOn is { } parent ? document.Styles?.Find(parent) : null;
        }

        return null;
    }

    private static int? Outline(XElement element) =>
        int.TryParse((string?)element.Attribute(W + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 8 ? value + 1 : null;

    /// <summary>The visible text of a paragraph by the entry rules.</summary>
    public static string VisibleText(Paragraph paragraph)
    {
        var text = new StringBuilder();
        Read(paragraph.Inlines, deleted: false, text);
        return text.ToString().Trim();
    }

    private static void Read(IEnumerable<Inline> inlines, bool deleted, StringBuilder text)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    if (deleted || run.Properties.Get(W + "vanish") is not null)
                    {
                        break;
                    }

                    foreach (var content in run.Content)
                    {
                        if (content is TextContent t)
                        {
                            text.Append(t.Text);
                        }
                        else if (content is TabContent or BreakContent)
                        {
                            text.Append(' ');
                        }
                    }

                    break;
                case Hyperlink link:
                    Read(link.Inlines, deleted, text);
                    break;
                case SimpleField field:
                    Read(field.Inlines, deleted, text);
                    break;
                case ContentControlInline control:
                    Read(control.Inlines, deleted, text);
                    break;
                case TrackedChange change:
                    Read(change.Inlines, deleted || change.Kind is TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom, text);
                    break;
                default:
                    break;
            }
        }
    }

    // The text of an entry paragraph as it stands (text and tabs of every run, field instructions left out).
    private static string Stored(Paragraph paragraph)
    {
        var text = new StringBuilder();
        foreach (var run in ModelWalk.Inlines(paragraph.Inlines).OfType<Run>())
        {
            foreach (var content in run.Content)
            {
                if (content is TextContent t)
                {
                    text.Append(t.Text);
                }
                else if (content is TabContent)
                {
                    text.Append('\t');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Asserts that every table of contents of the body says what the oracle expects: as many entry paragraphs as entries (the sentence of an empty one), each in the style of its level with its line, a link to a hidden bookmark that
    /// starts in the heading, and a page number where one is due (not its value). Returns how many entries were compared.
    /// </summary>
    public static int AssertEqualToExpected(WordDocument document, IFieldHost host, string label)
    {
        var compared = 0;
        var german = CaptionLabels.DocumentLanguage(document) == "de";
        foreach (var toc in Expected(document, host))
        {
            var areaLast = toc.TailOnly ? toc.Last - 1 : toc.Last;
            var count = areaLast - toc.First + 1;
            var wanted = Math.Max(1, toc.Entries.Count);
            Assert.True(count == wanted, $"{label}: a table of contents has {count} entry paragraphs, the headings give {wanted}");
            if (toc.Entries.Count == 0)
            {
                Assert.Equal(german ? GermanSentence : Sentence, Stored((Paragraph)toc.Container[toc.First]));
                continue;
            }

            for (var k = 0; k < toc.Entries.Count; k++)
            {
                var entry = toc.Entries[k];
                var paragraph = (Paragraph)toc.Container[toc.First + k];
                var line = Stored(paragraph);
                Assert.True(
                    entry.Page ? line.StartsWith(entry.Line + "\t", StringComparison.Ordinal) && line.Length > entry.Line.Length + 1 : line == entry.Line,
                    $"{label}: entry {k} of a table of contents reads another line than the heading (expected length {entry.Line.Length}, stored length {line.Length}, page {entry.Page})");
                var style = toc.Figures ? "TableofFigures" : "TOC" + entry.Level.ToString(CultureInfo.InvariantCulture);
                Assert.True(paragraph.Properties.StyleId == style, $"{label}: entry {k} of a table of contents has the style {paragraph.Properties.StyleId}, not {style}");
                if (toc.Links)
                {
                    var link = ModelWalk.Inlines(paragraph.Inlines).OfType<Hyperlink>().Single();
                    Assert.StartsWith("_Toc", link.Anchor, StringComparison.Ordinal);
                    var owner = ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().FirstOrDefault(p => ModelWalk.Inlines(p.Inlines).OfType<BookmarkStart>().Any(b => b.Name == link.Anchor));
                    Assert.True(ReferenceEquals(owner, entry.Heading), $"{label}: the link of entry {k} does not point at its heading");
                }

                compared++;
            }
        }

        return compared;
    }
}
