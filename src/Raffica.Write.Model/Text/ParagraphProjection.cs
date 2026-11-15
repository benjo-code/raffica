using System.Text;

namespace Raffica.Write.Model;

public enum AtomicKind
{
    FieldResult,
    Picture,
    NoteReference,
    CommentReference,
    Placeholder,
}

/// <summary>
/// One piece of a paragraph's projected text, with its source. <see cref="ContentOffset"/> is the character offset inside the text of
/// <see cref="Content"/> (0 for everything that is not a text piece). A piece of a hidden run has <see cref="Length"/> 0.
/// </summary>
public abstract record ProjectionPiece(int Start, int Length, Run? Run, RunContent? Content, int ContentOffset)
{
    public int End => Start + Length;
}

public sealed record TextPiece(int Start, int Length, Run? Run, RunContent? Content, int ContentOffset) : ProjectionPiece(Start, Length, Run, Content, ContentOffset);

public sealed record TabPiece(int Start, int Length, Run? Run, RunContent? Content, int ContentOffset) : ProjectionPiece(Start, Length, Run, Content, ContentOffset);

public sealed record BreakPiece(BreakKind Break, int Start, int Length, Run? Run, RunContent? Content, int ContentOffset) : ProjectionPiece(Start, Length, Run, Content, ContentOffset);

public sealed record AtomicPiece(AtomicKind Atomic, int Start, int Length, Run? Run, RunContent? Content, int ContentOffset) : ProjectionPiece(Start, Length, Run, Content, ContentOffset);

/// <summary>
/// The one definition of a paragraph's visible text. Offsets are the coordinates of the layout's flat paragraph text: hidden text has no
/// length, a field result is one atomic range of the length of its value, a picture, a break and a placeholder have length 1, and the
/// list label is not part of the text. The rules (what is visible, how complex fields are framed, which tracked changes show) are the
/// layout's: insertions in, deletions out.
/// </summary>
public sealed class ParagraphProjection
{
    /// <summary>The character of a picture, note or comment reference, or placeholder (U+FFFC).</summary>
    public const char ObjectChar = '￼';

    private readonly ProjectionPiece[] _visible;
    private readonly (int Start, int End)[] _atomic;

    private ParagraphProjection(string text, ProjectionPiece[] pieces, (int Start, int End)[] atomic)
    {
        Text = text;
        Pieces = pieces;
        _visible = [.. pieces.Where(p => p.Length > 0)];
        _atomic = atomic;
    }

    public string Text { get; }

    public IReadOnlyList<ProjectionPiece> Pieces { get; }

    public static ParagraphProjection Build(Paragraph paragraph, IProjectionHost host)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(host);
        return new Builder(host).Run(paragraph);
    }

    /// <summary>The piece that starts at the offset or contains it; at the end of the text the last piece with length; null when the text is empty.</summary>
    public ProjectionPiece? PieceAt(int offset)
    {
        if (_visible.Length == 0)
        {
            return null;
        }

        if (offset >= Text.Length)
        {
            return _visible[^1];
        }

        var low = 0;
        var high = _visible.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (_visible[mid].Start <= offset)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return _visible[low];
    }

    /// <summary>The atomic unit (a field result, picture, reference or placeholder) that starts at the offset or contains it, as a half-open range; null for plain text.</summary>
    public (int Start, int End)? AtomicAt(int offset)
    {
        foreach (var range in _atomic)
        {
            if (offset >= range.Start && offset < range.End)
            {
                return range;
            }
        }

        return null;
    }

    private sealed class Frame
    {
        public StringBuilder Instruction { get; } = new();

        public bool InResult { get; set; }

        public bool Decided { get; set; }

        public string? Value { get; set; }

        public bool Emitted { get; set; }
    }

    private sealed class Builder(IProjectionHost host)
    {
        private readonly List<ProjectionPiece> _pieces = [];
        private readonly List<(int Start, int End)> _atomic = [];
        private readonly List<Frame> _frames = [];
        private readonly StringBuilder _text = new();
        private int _simpleDepth;
        private int _groupStart = -1;

        public ParagraphProjection Run(Paragraph paragraph)
        {
            AddInlines(paragraph.Inlines);

            // A complex field without an End mark is closed with the paragraph: its text is one atomic range.
            _frames.Clear();
            _simpleDepth = 0;
            CloseGroup();
            return new ParagraphProjection(_text.ToString(), [.. _pieces], [.. _atomic]);
        }

        private void AddInlines(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Model.Run run:
                        AddRun(run);
                        break;
                    case Hyperlink link:
                        AddInlines(link.Inlines);
                        break;
                    case SimpleField field:
                        AddSimpleField(field);
                        break;
                    case ContentControlInline control:
                        AddInlines(control.Inlines);
                        break;
                    case TrackedChange { Kind: TrackedChangeKind.Insert or TrackedChangeKind.MoveTo } change:
                        AddInlines(change.Inlines);
                        break;
                    default:
                        break;
                }
            }
        }

        private void AddSimpleField(SimpleField field)
        {
            if (!Visible(_frames.Count))
            {
                return;
            }

            _simpleDepth++;
            try
            {
                if (host.FieldValue(field.Instruction) is { } value)
                {
                    var first = field.Inlines.OfType<Model.Run>().FirstOrDefault();
                    Emit(new AtomicPiece(AtomicKind.FieldResult, 0, 0, first, null, 0), value, first is not null && host.IsHidden(first));
                }
                else
                {
                    AddInlines(field.Inlines);
                }
            }
            finally
            {
                _simpleDepth--;
                CloseGroup();
            }
        }

        private void AddRun(Model.Run run)
        {
            var hidden = host.IsHidden(run);
            foreach (var content in run.Content)
            {
                if (content is FieldChar fieldChar)
                {
                    FieldChange(fieldChar, run, hidden);
                    continue;
                }

                if (content is InstructionText instruction)
                {
                    if (_frames.Count > 0 && !_frames[^1].InResult)
                    {
                        _frames[^1].Instruction.Append(instruction.Text);
                    }

                    continue;
                }

                if (!Visible(_frames.Count))
                {
                    // Inside an instruction, or in the result of a field that has a value: the first text of such a result is replaced by the value.
                    if (content is TextContent && _frames.Count > 0 && _frames[^1] is { Value: not null, Emitted: false } top && Visible(_frames.Count - 1))
                    {
                        top.Emitted = true;
                        Emit(new AtomicPiece(AtomicKind.FieldResult, 0, 0, run, content, 0), top.Value, hidden, inField: true);
                    }

                    continue;
                }

                AddContent(run, content, hidden);
            }
        }

        private void AddContent(Model.Run run, RunContent content, bool hidden)
        {
            hidden |= !host.HasText(content);
            switch (content)
            {
                case FootnoteReference or EndnoteReference:
                    EmitObject(new AtomicPiece(AtomicKind.NoteReference, 0, 0, run, content, 0), host.NoteMarkText(content), hidden);
                    break;
                case CommentReference:
                    EmitObject(new AtomicPiece(AtomicKind.CommentReference, 0, 0, run, content, 0), null, hidden);
                    break;
                case TextContent t:
                    Emit(new TextPiece(0, 0, run, content, 0), t.Text, hidden);
                    break;
                case TabContent:
                    Emit(new TabPiece(0, 0, run, content, 0), "\t", hidden);
                    break;
                case BreakContent b:
                    Emit(new BreakPiece(b.Kind, 0, 0, run, content, 0), "\n", hidden);
                    break;
                case Picture:
                    EmitObject(new AtomicPiece(AtomicKind.Picture, 0, 0, run, content, 0), null, hidden);
                    break;
                case UnknownRunContent { Content.Element.Name.LocalName: "footnoteRef" or "endnoteRef" }:
                    if (host.NoteMarkText(content) is { } own)
                    {
                        Emit(new AtomicPiece(AtomicKind.NoteReference, 0, 0, run, content, 0), own, hidden);
                    }

                    break;
                case UnknownRunContent unknown:
                    AddUnknown(run, unknown, hidden);
                    break;
                default:
                    break;
            }
        }

        private void AddUnknown(Model.Run run, UnknownRunContent unknown, bool hidden)
        {
            switch (unknown.Content.Element.Name.LocalName)
            {
                case "cr":
                    Emit(new BreakPiece(BreakKind.TextWrapping, 0, 0, run, unknown, 0), "\n", hidden);
                    break;
                case "noBreakHyphen":
                    Emit(new TextPiece(0, 0, run, unknown, 0), "‑", hidden);
                    break;
                case "ptab":
                    Emit(new TabPiece(0, 0, run, unknown, 0), "\t", hidden);
                    break;
                case "AlternateContent" or "pict" or "object":
                    EmitObject(new AtomicPiece(AtomicKind.Placeholder, 0, 0, run, unknown, 0), null, hidden);
                    break;
                default:
                    break;
            }
        }

        // A reference, picture or placeholder is one character; a note reference with a mark text is that text.
        private void EmitObject(AtomicPiece template, string? markText, bool hidden) =>
            Emit(template, string.IsNullOrEmpty(markText) ? ObjectChar.ToString() : markText, hidden);

        private bool Visible(int depth)
        {
            for (var i = 0; i < depth; i++)
            {
                if (!_frames[i].InResult || _frames[i].Value is not null)
                {
                    return false;
                }
            }

            return true;
        }

        private void FieldChange(FieldChar fieldChar, Model.Run run, bool hidden)
        {
            switch (fieldChar.Kind)
            {
                case FieldCharKind.Begin:
                    _frames.Add(new Frame());
                    break;
                case FieldCharKind.Separate when _frames.Count > 0:
                    _frames[^1].InResult = true;
                    Decide(_frames[^1]);
                    break;
                case FieldCharKind.End when _frames.Count > 0:
                    var closed = _frames[^1];
                    _frames.RemoveAt(_frames.Count - 1);
                    Decide(closed);
                    if (closed is { Value: not null, Emitted: false } && Visible(_frames.Count))
                    {
                        Emit(new AtomicPiece(AtomicKind.FieldResult, 0, 0, run, fieldChar, 0), closed.Value, hidden, inField: true);
                    }

                    CloseGroup();
                    break;
                default:
                    break;
            }
        }

        private void Decide(Frame frame)
        {
            if (frame.Decided)
            {
                return;
            }

            frame.Decided = true;
            frame.Value = host.FieldValue(frame.Instruction.ToString());
        }

        private void CloseGroup()
        {
            if (_frames.Count == 0 && _simpleDepth == 0 && _groupStart >= 0)
            {
                if (_text.Length > _groupStart)
                {
                    _atomic.Add((_groupStart, _text.Length));
                }

                _groupStart = -1;
            }
        }

        // Adds one piece; hidden content keeps its piece with no length. Everything produced inside a field belongs to the field's result.
        private void Emit(ProjectionPiece template, string value, bool hidden, bool inField = false)
        {
            var start = _text.Length;
            var length = hidden ? 0 : value.Length;
            var inGroup = inField || _frames.Count > 0 || _simpleDepth > 0;
            var piece = template with { Start = start, Length = length };
            if (inGroup && length > 0 && _groupStart < 0)
            {
                _groupStart = start;
            }

            if (inGroup && piece is TextPiece text)
            {
                // Text in a field result is part of that result.
                piece = new AtomicPiece(AtomicKind.FieldResult, text.Start, text.Length, text.Run, text.Content, text.ContentOffset);
            }

            _pieces.Add(piece);
            if (length > 0)
            {
                _text.Append(value);
                if (!inGroup && piece is AtomicPiece)
                {
                    _atomic.Add((start, start + length));
                }
            }
        }
    }
}
