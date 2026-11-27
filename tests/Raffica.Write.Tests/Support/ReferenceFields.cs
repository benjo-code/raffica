using System.Globalization;
using System.Text;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>One field of the body the oracle has an answer for: the paragraph, the field's index among the fields of that paragraph (nested ones counted, document order), its instruction and the expected result, or null when Write must leave the stored result alone.</summary>
internal sealed record ExpectedField(Paragraph Paragraph, int Ordinal, string Instruction, string? Text);

/// <summary>
/// The results of the order-based fields (<c>SEQ</c>, <c>STYLEREF 1 \s</c>, <c>REF</c>, <c>NOTEREF</c>) of a document's body, counted from the model alone and written from Word's documented behaviour, so that a test can compare
/// them with what the refresh wrote without the refresh checking itself. It shares no code with <c>FieldRefresh</c>, <c>SeqCounter</c> or <c>FieldWriter</c>: it counts the sequences with its own state, finds bookmarks with its own walk and
/// reads heading labels from the host (in the tests, <see cref="ReferenceNumbering"/>). Only the grammar (<see cref="FieldView"/>, <see cref="FieldInstruction"/>) is shared.
/// <para>
/// The rules (spec table of supported fields): a field counts only when it is complete, stands in no tracked change, is not nested in another field and is inside the support table; <c>SEQ</c> numbers per label (ignoring case) in
/// document order; <c>\r n</c> sets the number, a heading of level n makes the next <c>\s n</c> field start again, <c>\c</c> repeats a number above zero; the formats are ARABIC, ROMAN, roman, ALPHABETIC and alphabetic;
/// <c>STYLEREF 1 \s</c> is the label of the last <c>Heading1</c> paragraph up to and including the field's own, without trailing periods; <c>REF</c> is the text of the bookmark's range inside one paragraph (a deleted range left out,
/// the results of the fields inside it as they are expected to be) or with <c>\n</c> or <c>\r</c> the label of its paragraph without trailing periods; <c>NOTEREF</c> is the mark of the first note reference in the range.
/// </para>
/// </summary>
internal static class ReferenceFields
{
    public static IReadOnlyList<ExpectedField> Expected(WordDocument document, IFieldHost host)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(host);
        var paragraphs = ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().ToList();
        var labels = new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ExpectedField>();
        var answers = new Dictionary<(Paragraph, int), string>();
        Paragraph? chapter = null;

        foreach (var paragraph in paragraphs)
        {
            var level = HeadingOf(paragraph);
            if (level is { } heading)
            {
                foreach (var label in labels.Values)
                {
                    label.Pending.Add(heading);
                }

                if (heading == 1)
                {
                    chapter = paragraph;
                }
            }

            var fields = FieldView.InParagraph(paragraph);
            for (var ordinal = 0; ordinal < fields.Count; ordinal++)
            {
                var field = fields[ordinal];
                if (field.Parent is not null || field.Support.Kind is not (FieldKind.Seq or FieldKind.StyleRef))
                {
                    continue;
                }

                string? text = null;
                if (Counts(field))
                {
                    if (field.Support.Kind == FieldKind.Seq)
                    {
                        if (!labels.TryGetValue(field.Instruction.Argument!, out var label))
                        {
                            label = new Label();
                            labels[field.Instruction.Argument!] = label;
                        }

                        text = Sequence(label, field.Instruction);
                    }
                    else if (chapter is not null && host.NumberLabel(chapter) is { Length: > 0 } chapterLabel)
                    {
                        text = chapterLabel.TrimEnd('.');
                    }
                }

                if (text is not null)
                {
                    answers[(paragraph, ordinal)] = text;
                }

                results.Add(new ExpectedField(paragraph, ordinal, InstructionOf(field), text));
            }
        }

        var bookmarks = FindBookmarks(paragraphs);
        foreach (var paragraph in paragraphs)
        {
            var fields = FieldView.InParagraph(paragraph);
            for (var ordinal = 0; ordinal < fields.Count; ordinal++)
            {
                var field = fields[ordinal];
                if (field.Parent is not null || field.Support.Kind is not (FieldKind.Ref or FieldKind.NoteRef))
                {
                    continue;
                }

                string? text = null;
                if (Counts(field) && bookmarks.TryGetValue(field.Instruction.Argument!, out var mark))
                {
                    text = field.Support.Kind == FieldKind.Ref ? ReferenceText(mark, field.Instruction, host, answers, (paragraph, ordinal)) : NoteMark(mark, host);
                }

                results.Add(new ExpectedField(paragraph, ordinal, InstructionOf(field), text));
            }
        }

        return results;
    }

    /// <summary>The stored result of the field with that ordinal in the paragraph (the text between its separate and end characters, or the content of a simple field), or null when it has none.</summary>
    public static string? Stored(Paragraph paragraph, int ordinal)
    {
        var info = FieldScanner.Scan(paragraph.Inlines, intoContentControls: true)[ordinal];
        var text = new StringBuilder();
        if (info.Simple is not null)
        {
            Append(text, info.Simple.Inlines);
            return text.ToString();
        }

        if (info.ResultContainer is not { } list || info.SeparateRun is null || info.EndRun is null)
        {
            return null;
        }

        var from = list.IndexOf(info.SeparateRun);
        var to = list.IndexOf(info.EndRun);
        if (from < 0 || to < from)
        {
            return null;
        }

        Append(text, list.Skip(from + 1).Take(to - from - 1));
        return text.ToString();
    }

    /// <summary>Asserts that every field the oracle has a text for stores exactly that text, and (<paramref name="includeTocs"/>) that every table of contents lists what the oracle expects. Returns how many results were compared.</summary>
    public static int AssertStoredEqualsExpected(WordDocument document, IFieldHost host, string label, bool includeTocs = true)
    {
        var compared = 0;
        foreach (var field in Expected(document, host).Where(f => f.Text is not null))
        {
            Assert.True(
                string.Equals(field.Text, Stored(field.Paragraph, field.Ordinal), StringComparison.Ordinal),
                $"{label}: the result of a {FieldInstruction.Parse(field.Instruction).Name} field is not the expected one (expected length {field.Text!.Length}, stored length {Stored(field.Paragraph, field.Ordinal)?.Length})");
            compared++;
        }

        // The entries of the tables of contents are judged here too (their own oracle, ReferenceToc), so every test that asks for the results of the fields also asks for the lists.
        return includeTocs ? compared + ReferenceToc.AssertEqualToExpected(document, host, label) : compared;
    }

    private sealed class Label
    {
        public int Value { get; set; }

        public HashSet<int> Pending { get; } = [];
    }

    private static string InstructionOf(FieldSpan field) => field.Instruction.Name;

    private static bool Counts(FieldSpan field) => field.Support.IsSupported && field.IsComplete && !field.InTrackedChange;

    private static int? HeadingOf(Paragraph paragraph)
    {
        var id = paragraph.Properties.StyleId;
        for (var level = 1; level <= 9; level++)
        {
            if (id == "Heading" + level.ToString(CultureInfo.InvariantCulture))
            {
                return level;
            }
        }

        return null;
    }

    private static string? Sequence(Label label, FieldInstruction instruction)
    {
        // The format comes first: a field with a format Write does not read does not move the count.
        var format = "ARABIC";
        foreach (var (_, value) in instruction.Switches.Where(s => s.Name == "*" && s.Value is not null))
        {
            if (value!.ToUpperInvariant() is "MERGEFORMAT" or "CHARFORMAT" or "ARABIC")
            {
                continue;
            }

            if (value is "ROMAN" or "roman" or "ALPHABETIC" or "alphabetic")
            {
                format = value;
                continue;
            }

            return null;
        }

        if (instruction.Has("r"))
        {
            if (!int.TryParse(instruction.ValueOf("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var set))
            {
                return null;
            }

            label.Value = set;
            return Render(label.Value, format);
        }

        var restartLevel = 0;
        if (instruction.Has("s"))
        {
            if (!int.TryParse(instruction.ValueOf("s"), NumberStyles.None, CultureInfo.InvariantCulture, out restartLevel) || restartLevel is < 1 or > 9)
            {
                return null;
            }
        }

        if (restartLevel > 0 && label.Pending.Remove(restartLevel))
        {
            label.Value = 0;
        }

        if (!(instruction.Has("c") && label.Value > 0))
        {
            label.Value += 1;
        }

        return Render(label.Value, format);
    }

    private static string Render(int value, string format)
    {
        var plain = value.ToString(CultureInfo.InvariantCulture);
        if (value < 1 || format == "ARABIC")
        {
            return plain;
        }

        if (format is "ROMAN" or "roman")
        {
            if (value >= 4000)
            {
                return plain;
            }

            var remaining = value;
            var roman = new StringBuilder();
            string[] symbols = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];
            int[] values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
            for (var i = 0; i < values.Length; i++)
            {
                for (; remaining >= values[i]; remaining -= values[i])
                {
                    roman.Append(symbols[i]);
                }
            }

            return format == "ROMAN" ? roman.ToString().ToUpperInvariant() : roman.ToString();
        }

        var repeat = ((value - 1) / 26) + 1;
        if (repeat > 64)
        {
            return plain;
        }

        var letter = (char)((format == "ALPHABETIC" ? 'A' : 'a') + ((value - 1) % 26));
        return new string(letter, repeat);
    }

    private sealed record Mark(Paragraph Paragraph, int Id);

    private static Dictionary<string, Mark> FindBookmarks(List<Paragraph> paragraphs)
    {
        var found = new Dictionary<string, Mark>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in paragraphs)
        {
            foreach (var inline in ModelWalk.Inlines(paragraph.Inlines).OfType<BookmarkStart>())
            {
                found.TryAdd(inline.Name, new Mark(paragraph, inline.Id));
            }
        }

        return found;
    }

    private static string? ReferenceText(Mark mark, FieldInstruction instruction, IFieldHost host, Dictionary<(Paragraph, int), string> answers, (Paragraph Paragraph, int Ordinal) own)
    {
        if (instruction.Has("n") || instruction.Has("r"))
        {
            return host.NumberLabel(mark.Paragraph) is { Length: > 0 } label ? label.TrimEnd('.') : null;
        }

        var reader = new RangeReader(mark, answers);
        reader.Visit(mark.Paragraph.Inlines, deleted: false);

        // A reference to a bookmark that holds the reference itself is invalid to Word ("Error! Not a valid bookmark self-reference.", Word 16.0, checked 2026-11-27): there is no text for it, Write keeps the stored result.
        var self = ReferenceEquals(own.Paragraph, mark.Paragraph) && reader.Begun.Contains(own.Ordinal);
        return reader.Complete && !self ? reader.Text.ToString() : null;
    }

    private static string? NoteMark(Mark mark, IFieldHost host)
    {
        var reader = new RangeReader(mark, new Dictionary<(Paragraph, int), string>());
        reader.Visit(mark.Paragraph.Inlines, deleted: false);
        if (!reader.Complete)
        {
            return null;
        }

        return reader.FirstNote switch
        {
            FootnoteReference f => host.NoteMarkText(f.Id, NoteType.Footnote),
            EndnoteReference e => host.NoteMarkText(e.Id, NoteType.Endnote),
            _ => null,
        };
    }

    // Reads the text between the markers of one bookmark in one paragraph: its own field counter (every field start, nested ones too, in document order) lets it put the expected result in place of the stored one.
    private sealed class RangeReader(Mark mark, Dictionary<(Paragraph, int), string> answers)
    {
        private readonly List<(int Ordinal, bool Replaced)> _fields = [];
        private bool _inside;
        private bool _done;
        private int _ordinal;
        private int _silent;

        public StringBuilder Text { get; } = new();

        public bool Complete { get; private set; }

        public RunContent? FirstNote { get; private set; }

        /// <summary>The ordinals of the fields that begin between the markers.</summary>
        public HashSet<int> Begun { get; } = [];

        public void Visit(IEnumerable<Inline> inlines, bool deleted)
        {
            foreach (var inline in inlines)
            {
                if (_done)
                {
                    return;
                }

                switch (inline)
                {
                    case BookmarkStart start when start.Id == mark.Id:
                        _inside = true;
                        break;
                    case BookmarkEnd end when end.Id == mark.Id:
                        Complete = _inside;
                        _inside = false;
                        _done = true;
                        return;
                    case Run run:
                        Content(run, deleted);
                        break;
                    case SimpleField simple:
                        var ordinal = _ordinal++;
                        if (_inside)
                        {
                            Begun.Add(ordinal);
                        }

                        if (_inside && !deleted && _silent == 0 && answers.TryGetValue((mark.Paragraph, ordinal), out var expected))
                        {
                            Text.Append(expected);
                            _silent++;
                            Visit(simple.Inlines, deleted);
                            _silent--;
                        }
                        else
                        {
                            Visit(simple.Inlines, deleted);
                        }

                        break;
                    case Hyperlink link:
                        Visit(link.Inlines, deleted);
                        break;
                    case ContentControlInline control:
                        Visit(control.Inlines, deleted);
                        break;
                    case TrackedChange change:
                        Visit(change.Inlines, deleted || change.Kind is TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom);
                        break;
                    default:
                        break;
                }
            }
        }

        private void Content(Run run, bool deleted)
        {
            foreach (var content in run.Content)
            {
                switch (content)
                {
                    case FieldChar { Kind: FieldCharKind.Begin }:
                        if (_inside)
                        {
                            Begun.Add(_ordinal);
                        }

                        _fields.Add((_ordinal++, false));
                        break;
                    case FieldChar { Kind: FieldCharKind.Separate } when _fields.Count > 0:
                        string? expected = null;
                        var replace = _inside && !deleted && _silent == 0 && answers.TryGetValue((mark.Paragraph, _fields[^1].Ordinal), out expected);
                        _fields[^1] = (_fields[^1].Ordinal, replace);
                        if (replace)
                        {
                            Text.Append(expected);
                            _silent++;
                        }

                        break;
                    case FieldChar { Kind: FieldCharKind.End } when _fields.Count > 0:
                        if (_fields[^1].Replaced)
                        {
                            _silent--;
                        }

                        _fields.RemoveAt(_fields.Count - 1);
                        break;
                    case TextContent t when _inside && !deleted && _silent == 0:
                        Text.Append(t.Text);
                        break;
                    case TabContent when _inside && !deleted && _silent == 0:
                        Text.Append('\t');
                        break;
                    case BreakContent when _inside && !deleted && _silent == 0:
                        Text.Append(' ');
                        break;
                    case FootnoteReference or EndnoteReference when _inside && !deleted && FirstNote is null:
                        FirstNote = content;
                        break;
                    default:
                        break;
                }
            }
        }

    }

    private static void Append(StringBuilder text, IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        switch (content)
                        {
                            case TextContent t:
                                text.Append(t.Text);
                                break;
                            case TabContent:
                                text.Append('\t');
                                break;
                            case BreakContent:
                                text.Append(' ');
                                break;
                            default:
                                break;
                        }
                    }

                    break;
                case Hyperlink link:
                    Append(text, link.Inlines);
                    break;
                case SimpleField simple:
                    Append(text, simple.Inlines);
                    break;
                case ContentControlInline control:
                    Append(text, control.Inlines);
                    break;
                case TrackedChange change when change.Kind is not (TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom):
                    Append(text, change.Inlines);
                    break;
                default:
                    break;
            }
        }
    }
}
