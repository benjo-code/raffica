using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// The number labels and note marks a field refresh needs, from the model alone and through the layout's own definitions (<see cref="LayoutEffectiveFormat"/> for the resolved paragraph, <see cref="NumberingCounter"/> for the label, the
/// same note counting and number format as the page), so a result written by the refresh equals what the page draws. Both answers are worked out once for a document revision and dropped when the revision changes (a refresh does not change
/// them: it rewrites results, not numbering or notes). Not thread-safe: the owner calls it on one thread (the UI thread, under the edit lease).
/// </summary>
public sealed class FieldLabels(WordDocument document)
{
    private const int MaxDepth = 64;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly WordDocument _document = document ?? throw new ArgumentNullException(nameof(document));
    private int _revision = int.MinValue;
    private LayoutEffectiveFormat? _format;
    private Dictionary<Paragraph, string>? _labels;
    private Dictionary<(NoteType Type, int Id), string>? _marks;
    private readonly HashSet<Paragraph> _missed = new(ReferenceEqualityComparer.Instance);

    /// <summary>How many times the labels of the whole body were worked out (tests: a paragraph without a list never earns one).</summary>
    internal int LabelBuilds { get; private set; }

    /// <summary>The label of a paragraph of the body as the page draws it (for example <c>2.1.</c>), or null when it has none (no list, or a list or level that does not exist).</summary>
    public string? NumberLabel(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var format = Current();

        // A paragraph that is in no list is answered at once: only a numbered paragraph needs the counters of every paragraph before it.
        if (format.ResolveParagraph(StoryRef.Body, paragraph).NumberingId <= 0)
        {
            return null;
        }

        _labels ??= BuildLabels(format);
        if (_labels.TryGetValue(paragraph, out var label))
        {
            return label;
        }

        // A numbered paragraph the counting did not see: the paragraph is not one of the objects the labels were worked out for (an undo within the debounce put a clone back and the revision is the one the labels were made at), or it has no label
        // (a list that does not exist). The first time a paragraph is missing everything is worked out again, on a fresh format; a second miss for the same paragraph is an answer.
        if (!_missed.Add(paragraph))
        {
            return null;
        }

        _format = new LayoutEffectiveFormat(_document);
        _labels = BuildLabels(_format);
        return _labels.GetValueOrDefault(paragraph);
    }

    /// <summary>The number the note reference shows in the body: references count in reading order per kind, in the number format of the document settings (footnotes decimal, endnotes lower roman by default). Null for an id no reference names.</summary>
    public string? NoteMarkText(int noteId, NoteType type)
    {
        _ = Current();
        _marks ??= BuildMarks();
        return _marks.GetValueOrDefault((type, noteId));
    }

    private LayoutEffectiveFormat Current()
    {
        var revision = _document.Edits.Stamp.Revision;
        if (_format is null || revision != _revision)
        {
            _revision = revision;
            _format = new LayoutEffectiveFormat(_document);
            _labels = null;
            _marks = null;
            _missed.Clear();
        }

        return _format;
    }

    private Dictionary<Paragraph, string> BuildLabels(LayoutEffectiveFormat format)
    {
        LabelBuilds++;
        var labels = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
        var counter = new NumberingCounter(_document.Numbering);
        foreach (var paragraph in ParagraphsOf(_document.Body.Blocks, 0))
        {
            if (counter.Next(format.ResolveParagraph(StoryRef.Body, paragraph)) is { } label)
            {
                labels[paragraph] = label.Text;
            }
        }

        return labels;
    }

    private Dictionary<(NoteType Type, int Id), string> BuildMarks()
    {
        var settings = _document.Settings?.Properties;
        var footnoteFormat = (string?)settings?.Get(W + "footnotePr")?.Element(W + "numFmt")?.Attribute(W + "val");
        var endnoteFormat = (string?)settings?.Get(W + "endnotePr")?.Element(W + "numFmt")?.Attribute(W + "val") ?? "lowerRoman";
        var marks = new Dictionary<(NoteType Type, int Id), string>();
        var footnotes = 0;
        var endnotes = 0;
        foreach (var paragraph in ParagraphsOf(_document.Body.Blocks, 0))
        {
            foreach (var content in ReferencesOf(paragraph.Inlines, 0))
            {
                switch (content)
                {
                    case FootnoteReference footnote:
                        footnotes++;
                        marks.TryAdd((NoteType.Footnote, footnote.Id), PageNumbers.Format(footnotes, footnoteFormat));
                        break;
                    case EndnoteReference endnote:
                        endnotes++;
                        marks.TryAdd((NoteType.Endnote, endnote.Id), PageNumbers.Format(endnotes, endnoteFormat));
                        break;
                    default:
                        break;
                }
            }
        }

        return marks;
    }

    // The note references of the inlines in reading order, entered through links, simple fields, content controls and tracked insertions (the projection shows a deletion as nothing).
    private static IEnumerable<RunContent> ReferencesOf(IEnumerable<Inline> inlines, int depth)
    {
        if (depth > MaxDepth)
        {
            yield break;
        }

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        if (content is FootnoteReference or EndnoteReference)
                        {
                            yield return content;
                        }
                    }

                    break;
                case Hyperlink link:
                    foreach (var inner in ReferencesOf(link.Inlines, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                case SimpleField field:
                    foreach (var inner in ReferencesOf(field.Inlines, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                case ContentControlInline control:
                    foreach (var inner in ReferencesOf(control.Inlines, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                case TrackedChange { Kind: TrackedChangeKind.Insert or TrackedChangeKind.MoveTo } change:
                    foreach (var inner in ReferencesOf(change.Inlines, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static IEnumerable<Paragraph> ParagraphsOf(IEnumerable<Block> blocks, int depth)
    {
        if (depth > MaxDepth)
        {
            yield break;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Table table:
                    foreach (var inner in ParagraphsOfRow(table.Rows, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                case ContentControlBlock control:
                    foreach (var inner in ParagraphsOf(control.Content, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static IEnumerable<Paragraph> ParagraphsOfRow(IEnumerable<RowItem> items, int depth)
    {
        if (depth > MaxDepth)
        {
            yield break;
        }

        foreach (var item in items)
        {
            switch (item)
            {
                case TableRow row:
                    foreach (var inner in ParagraphsOfRow(row.Items, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                case TableCell cell:
                    foreach (var inner in ParagraphsOf(cell.Blocks, depth + 1))
                    {
                        yield return inner;
                    }

                    break;
                default:
                    break;
            }
        }
    }
}
