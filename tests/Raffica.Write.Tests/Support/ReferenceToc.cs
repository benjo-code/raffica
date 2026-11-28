using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// One entry the oracle expects in a table of contents: the paragraph it stands for, its level, the line before the page number (the label, a tab or a space and the text; the text or the label alone), whether it shows a page number and
/// whether a tab stands after the label.
/// </summary>
internal sealed record ExpectedEntry(Paragraph Heading, int Level, string Line, bool Page, bool LabelTab = false);

/// <summary>A table of contents of the body with the entries the oracle expects (the sentence of an empty one is <see cref="Sentence"/>), what stands before their page numbers and the first level it asks for.</summary>
internal sealed record ExpectedToc(IList<Block> Container, int First, int Last, bool TailOnly, bool Links, bool Figures, IReadOnlyList<ExpectedEntry> Entries, string Separator, int FirstLevel);

/// <summary>
/// The entries of the tables of contents of a document's body, counted from the model alone: its own walk over the headings (and captions), its own reading of the visible text and the outline levels, and its own check of what
/// an entry paragraph says. It shares no code with <c>TocBuilder</c>, <c>TocRefresh</c>, <c>TocTabs</c> or <c>TocSpec</c>; only the field grammar (<see cref="FieldInstruction"/>, <see cref="TocRanges"/>) and the number labels of the host are
/// shared. The rules, as Word has them: which paragraphs are listed at which level and which style an entry gets is <see cref="ReferenceStyles"/>, inside the levels of <c>\o</c>; a line is the visible text
/// (line breaks and tabs as spaces, hidden text and tracked deletions left out, trimmed), after the number label and what the list level puts behind it (a tab, or a space) when the host shows a label; a heading with no text is the label
/// alone, and is left out without a label, when its paragraph mark is hidden, when it holds a page break or something else than text (a picture or a symbol, which Word would copy); a list takes the paragraphs that hold a
/// <c>SEQ</c> of its label that Word counts (tracked insertions too, not deleted or moved away) and that is not hidden (<c>\h</c> without a format), with the whole text of the paragraph. Before a page number stands the first character
/// of the value of <c>\p</c> (a space for an empty one), else a tab. Page numbers are not judged (they come with the page results). An entry with a tab after its label must have a left stop, its own or its style's, when it has a right
/// one: else the tab after the label goes to the right edge.
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

                // The first tab of a caption stays, with a stop of the entry's behind what stands before it.
                var whole = VisibleText(paragraph, keepFirstTab: true);
                if (whole.Length > 0)
                {
                    entries.Add(new ExpectedEntry(paragraph, 1, whole, !noPages && !(noRange is { } r && r.From <= 1 && r.To >= 1), whole.Contains('\t', StringComparison.Ordinal)));
                }

                continue;
            }

            // A \u without \o lists the outline levels too.
            var level = ReferenceStyles.Level(document, paragraph, instruction.Has("u"));
            if (level is null || level < from || level > to)
            {
                continue;
            }

            var text = VisibleText(paragraph);
            var number = host.NumberLabel(paragraph);
            var numbered = !string.IsNullOrEmpty(number);
            if (text.Length == 0 && !(numbered && ListedWithoutText(paragraph)))
            {
                continue;
            }

            var tab = numbered && text.Length > 0 && ReferenceNumbering.TabFollows(document, paragraph);
            var line = !numbered ? text : text.Length == 0 ? number! : number + (tab ? "\t" : " ") + text;
            entries.Add(new ExpectedEntry(paragraph, level.Value, line, !noPages && !(noRange is { } n && level >= n.From && level <= n.To), tab));
        }

        var separator = "\t";
        if (instruction.ValueOf("p") is { } value)
        {
            separator = value.Length == 0 ? " " : value[..(char.IsHighSurrogate(value[0]) && value.Length > 1 ? 2 : 1)];
        }

        found.Add(new ExpectedToc(container, first, last, tailOnly, instruction.Has("h"), label is not null, entries, separator, label is null ? from : 1));
    }

    // A paragraph mark that is neither hidden nor a tracked deletion, and in the shown runs no page break and nothing but text, tabs, line and column breaks, references and field marks.
    private static bool ListedWithoutText(Paragraph paragraph)
    {
        var mark = paragraph.Properties.Get(W + "rPr");
        if (mark?.Element(W + "del") is not null || mark?.Element(W + "vanish") is not null)
        {
            return false;
        }

        return !LiveRuns(paragraph.Inlines).Where(r => r.Properties.Get(W + "vanish") is null).SelectMany(r => r.Content)
            .Any(c => c is Picture || (c is BreakContent b && b.Kind == BreakKind.Page)
                || (c is UnknownRunContent u && u.Content.Element.Name.LocalName is not ("cr" or "ptab" or "lastRenderedPageBreak" or "annotationRef")));
    }

    private static IEnumerable<Run> LiveRuns(IEnumerable<Inline> inlines) => inlines.SelectMany(i => i switch
    {
        Run run => [run],
        Hyperlink link => LiveRuns(link.Inlines),
        SimpleField field => LiveRuns(field.Inlines),
        ContentControlInline control => LiveRuns(control.Inlines),
        TrackedChange change when change.Kind is not (TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom) => LiveRuns(change.Inlines),
        _ => [],
    });

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
        FieldView.InParagraph(paragraph).Any(f => f.Parent is null && f.Instruction.Name == "SEQ" && f.IsComplete && !f.InDeletion && f.Instruction.ArgumentCount == 1
            && f.Instruction.Switches.All(s => "chnrs*#".Contains(s.Name, StringComparison.Ordinal)) && !(f.Instruction.Has("h") && !f.Instruction.Has("*"))
            && string.Equals(f.Instruction.Argument, label, StringComparison.OrdinalIgnoreCase));

    /// <summary>The visible text of a paragraph by the entry rules.</summary>
    public static string VisibleText(Paragraph paragraph, bool keepFirstTab = false)
    {
        var text = new StringBuilder();
        Read(paragraph.Inlines, deleted: false, text);
        var parts = text.ToString().Trim().Split('\t');
        return keepFirstTab && parts.Length > 1 ? parts[0] + "\t" + string.Join(' ', parts.Skip(1)) : string.Join(' ', parts);
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
                        else if (content is TabContent)
                        {
                            text.Append('\t');
                        }
                        else if (content is BreakContent)
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

    private static List<string> StopKinds(XElement? tabs) => [.. (tabs?.Elements(W + "tab") ?? []).Select(t => (string?)t.Attribute(W + "val") ?? string.Empty)];

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
    /// starts in the heading, and a page number where one is due (its value too when <paramref name="pageTextOf"/> gives the expected page text of a heading; a heading it gives null for is not judged for its value). Returns how many entries were compared.
    /// </summary>
    public static int AssertEqualToExpected(WordDocument document, IFieldHost host, string label, Func<Paragraph, string?>? pageTextOf = null, IReadOnlySet<Block>? notJudged = null)
    {
        var compared = 0;
        var german = CaptionLabels.DocumentLanguage(document) == "de";
        foreach (var toc in Expected(document, host))
        {
            if (notJudged?.Contains(toc.Container[toc.First]) == true)
            {
                continue;
            }

            var areaLast = toc.TailOnly ? toc.Last - 1 : toc.Last;
            var count = areaLast - toc.First + 1;
            var wanted = Math.Max(1, toc.Entries.Count);
            Assert.True(count == wanted, $"{label}: a table of contents has {count} entry paragraphs, the headings give {wanted}");
            if (toc.Entries.Count == 0)
            {
                var sentence = (Paragraph)toc.Container[toc.First];
                Assert.Equal(german ? GermanSentence : Sentence, Stored(sentence));
                // The sentence names the entry style of the first level when the document defines it, else none: never a style the document lacks.
                var defined = ReferenceStyles.BuiltIn(document, ReferenceStyles.EntryName(toc.FirstLevel, toc.Figures));
                Assert.True(sentence.Properties.StyleId == defined, $"{label}: the sentence of an empty table of contents has the style {sentence.Properties.StyleId ?? "(none)"}, not {defined ?? "(none)"}");
                continue;
            }

            for (var k = 0; k < toc.Entries.Count; k++)
            {
                var entry = toc.Entries[k];
                var paragraph = (Paragraph)toc.Container[toc.First + k];
                var line = Stored(paragraph);
                var page = entry.Page ? pageTextOf?.Invoke(entry.Heading) : null;
                Assert.True(
                    entry.Page ? (page is null ? line.StartsWith(entry.Line + toc.Separator, StringComparison.Ordinal) && line.Length > entry.Line.Length + toc.Separator.Length : line == entry.Line + toc.Separator + page) : line == entry.Line,
                    $"{label}: entry {k} of a table of contents reads another line than the heading (expected length {entry.Line.Length}, stored length {line.Length}, page {entry.Page})");
                var style = ReferenceStyles.EntryStyle(document, entry.Level, toc.Figures);
                Assert.True(style is not null && paragraph.Properties.StyleId == style, $"{label}: entry {k} of a table of contents has the style {paragraph.Properties.StyleId}, not the entry style the document defines ({style ?? "none"})");
                if (entry.LabelTab)
                {
                    var own = StopKinds(paragraph.Properties.Get(W + "tabs"));
                    var inherited = new List<string>();
                    var current = document.Styles?.Find(style!);
                    for (var depth = 0; current is not null && depth < 32; depth++)
                    {
                        inherited.AddRange(StopKinds(current.ParagraphProperties.Get(W + "tabs")));
                        current = current.BasedOn is { } parent ? document.Styles?.Find(parent) : null;
                    }

                    Assert.True(!own.Contains("right") || own.Contains("left") || inherited.Contains("left"), $"{label}: entry {k} has a number, a right stop and no left one");
                }
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
