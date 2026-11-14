using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// Makes a <see cref="BlockPlan"/> of each body block, in document order (list numbers depend on that order). Paragraphs become
/// paragraph plans; a table or an element the layout cannot draw becomes a placeholder until its own task replaces it. This is where
/// a table (Task 10) plugs in: return its plan from <see cref="Plan"/>.
/// </summary>
internal sealed class BlockPlanner
{
    internal const float PlaceholderHeight = 24f;
    private const float MaximumBoxSize = 2000f;
    private const float EmuPerPoint = 12700f;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly HashSet<string> s_invisibleBlocks = new(StringComparer.Ordinal)
    {
        "bookmarkStart", "bookmarkEnd", "commentRangeStart", "commentRangeEnd", "permStart", "permEnd", "proofErr",
        "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd", "sectPr",
    };

    private readonly StyleResolver _styles;
    private readonly NumberingCounter _numbering;
    private readonly ParagraphLayouter _layouter;
    private readonly float _defaultTabStop;
    private readonly bool _expandShiftReturn;
    private readonly bool _autoHyphenation;
    private readonly Dictionary<ResolvedParagraph, ResolvedRun> _markRuns = new(ReferenceEqualityComparer.Instance);
    private readonly List<FieldFrame> _frames = [];
    private readonly ImageProbe _probe;
    private readonly bool _flowMode;
    private bool _placeable;
    private bool _planningBox;
    private List<NoteMark>? _marks;
    private List<FloatSpec>? _floats;
    private List<InlineBox>? _flowBoxes;

    public BlockPlanner(StyleResolver styles, NumberingCounter numbering, ParagraphLayouter layouter, DocumentSettings? settings, ImageProbe? probe = null, bool flowMode = false)
    {
        _probe = probe ?? new ImageProbe(null);
        _flowMode = flowMode;
        _styles = styles;
        _numbering = numbering;
        _layouter = layouter;
        _autoHyphenation = styles.AutoHyphenation;
        _defaultTabStop = 36f;
        _expandShiftReturn = true;
        TableTextAtIndent = true;
        if (settings is not null)
        {
            var tab = (string?)settings.Properties.Get(W + "defaultTabStop")?.Attribute(W + "val");
            if (double.TryParse(tab, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) && twips >= 20)
            {
                _defaultTabStop = (float)(twips / 20.0);
            }

            var compat = settings.Properties.Get(W + "compat");
            _expandShiftReturn = compat?.Element(W + "doNotExpandShiftReturn") is null;
            var mode = compat?.Elements(W + "compatSetting").FirstOrDefault(e => (string?)e.Attribute(W + "name") == "compatibilityMode");
            TableTextAtIndent = !int.TryParse((string?)mode?.Attribute(W + "val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level < 15;
        }
    }

    /// <summary>The height of the text area of the section being filled: an inline picture taller than this is shrunk to it (aspect kept). Set by the page filler.</summary>
    internal float TextAreaHeight { get; set; } = float.MaxValue;

    /// <summary>The story the planner reads (<see cref="ImageRef"/> says how it qualifies picture ids): the body unless a header, footer or note planner sets it.</summary>
    internal string StoryKey { get; set; } = ImageRef.DocumentStory;

    /// <summary>
    /// True for the planner of the body in the page view, outside table cells: only there does the page filler place floating objects. A
    /// planner that cannot place them (headers, footers, notes) shows each as an inline box of its size, as the writing view does, so
    /// nothing is lost; the table planner switches it off for the cells of a table.
    /// </summary>
    internal bool CanPlaceFloats { get; set; } = true;

    /// <summary>The styles of the document: the one resolver every planner of a layout shares.</summary>
    internal StyleResolver Styles => _styles;

    /// <summary>True when the table border sits one cell margin left of the indent (a document in a compatibility mode below 15 aligns the text of the first cell, not its border).</summary>
    internal bool TableTextAtIndent { get; }

    /// <summary>What the stories planned next are planned for (fields, note references); the default evaluates nothing and numbers nothing.</summary>
    internal PlanContext Context { get; set; } = new();

    /// <summary>True for a block that takes no room (a bookmark mark at block level).</summary>
    internal static bool TakesNoRoom(Block block) =>
        block is UnknownBlock unknown && s_invisibleBlocks.Contains(unknown.Content.Element.Name.LocalName);

    /// <summary>The flows of a header, footer or note: its blocks laid out at <paramref name="width"/>, in order. Never throws on content.</summary>
    internal List<IBlockFlow> PlanStory(IList<Block> blocks, float width)
    {
        var flows = new List<IBlockFlow>();
        foreach (var flat in FlatBlocks.From(blocks, -1))
        {
            if (Plan(flat) is { } plan)
            {
                flows.Add(plan.Layout(width));
            }
        }

        return flows;
    }

    /// <summary>The plan of a block, or null for a block that takes no room (a bookmark mark at block level).</summary>
    public BlockPlan? Plan(FlatBlock flat) => PlanBlock(flat, null, 0);

    /// <summary>The plan of a block of the body (<paramref name="cell"/> null) or of a table cell, where the paragraph properties of the table style apply.</summary>
    internal BlockPlan? PlanBlock(FlatBlock flat, ResolvedCellFormat? cell, int depth)
    {
        switch (flat.Block)
        {
            case Paragraph paragraph:
                return new ParagraphPlan(flat.BodyIndex, Input(paragraph, cell), _layouter, isContinuation: false);
            case Table table when depth < TableLimits.MaximumDepth:
                return TableReader.Read(this, table, flat.BodyIndex, depth);
            case var block when TakesNoRoom(block):
                return null;
            default:
                // An element the layout cannot draw: a marked box, the model keeps the content.
                return new PlaceholderPlan(flat.BodyIndex, PlaceholderHeight);
        }
    }

    private ParagraphInput Input(Paragraph paragraph, ResolvedCellFormat? cell)
    {
        var resolved = _styles.ForParagraph(paragraph, cell);
        var label = _numbering.Next(resolved);
        var items = new List<ParagraphItem>();
        var text = new StringBuilder();
        ResolvedRun? textRun = null;
        _frames.Clear();
        _marks = null;
        _floats = null;
        _flowBoxes = null;
        _placeable = CanPlaceFloats && cell is null && !_planningBox && !_flowMode;
        AddInlines(paragraph.Inlines, resolved, items, text, ref textRun);
        Flush(items, text, ref textRun);
        _frames.Clear();
        IReadOnlyList<NoteMark>? marks = _marks;
        var floats = _floats;
        if (_flowBoxes is { Count: > 0 } lead)
        {
            // The writing view does not wrap text around objects: each one is a block of its own above the paragraph (a line of its own, then the text).
            var front = new List<ParagraphItem>(lead.Count * 2);
            foreach (var box in lead)
            {
                front.Add(box);
                if (items.Count > 0 || lead.Count > 1)
                {
                    front.Add(new BreakItem(BreakKind.Line));
                }
            }

            items.InsertRange(0, front);
            marks = marks?.Select(m => m with { ItemIndex = m.ItemIndex + front.Count }).ToList();
        }

        var input = new ParagraphInput(resolved, items, MarkRun(resolved), label)
        {
            DefaultTabStop = _defaultTabStop,
            ExpandShiftReturn = _expandShiftReturn,
            AutoHyphenation = _autoHyphenation,
            NoteMarks = marks,
            Floats = floats,
        };

        // The paragraphs of a text box are planned after the paragraph that holds it: planning one is not re-entrant.
        if (floats is not null)
        {
            _planningBox = true;
            try
            {
                foreach (var spec in floats)
                {
                    if (spec is { Kind: FloatKind.TextBox, Content: { } content })
                    {
                        spec.Plans = [.. content.Select(p => new ParagraphPlan(-1, Input(p, null), _layouter, isContinuation: false))];
                    }
                }
            }
            finally
            {
                _planningBox = false;
            }
        }

        return input;
    }

    // A floating object read from a drawing: kept for the page layout when this paragraph is placed by the page filler; otherwise (writing view,
    // header, footer, note, table cell, text box) made an inline box above the paragraph, a picture or a marked box of its size, so nothing vanishes.
    private void AddFloat(FloatSpec? spec, List<ParagraphItem> items)
    {
        if (spec is null)
        {
            return;
        }

        if (!_placeable)
        {
            var cap = Math.Min(1f, MaximumBoxSize / Math.Max(spec.Width, spec.Height));
            var width = spec.Width * cap;
            var height = spec.Height * cap;
            (_flowBoxes ??= []).Add(new InlineBox(width, height, spec.Kind == FloatKind.Image ? spec.Image : null));
            return;
        }

        (_floats ??= []).Add(spec.WithItemIndex(items.Count));
    }

    private ResolvedRun MarkRun(ResolvedParagraph resolved)
    {
        if (resolved.MarkRunProperties is not null)
        {
            return _styles.ForParagraphMark(resolved);
        }

        if (!_markRuns.TryGetValue(resolved, out var run))
        {
            run = _styles.ForParagraphMark(resolved);
            _markRuns[resolved] = run;
        }

        return run;
    }

    private static void Flush(List<ParagraphItem> items, StringBuilder text, ref ResolvedRun? run)
    {
        if (text.Length > 0 && run is not null)
        {
            items.Add(new TextSpan(text.ToString(), run));
        }

        text.Clear();
        run = null;
    }

    private void AddInlines(IEnumerable<Inline> inlines, ResolvedParagraph owner, List<ParagraphItem> items, StringBuilder text, ref ResolvedRun? textRun)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    AddRun(run, owner, items, text, ref textRun);
                    break;
                case Hyperlink link:
                    AddInlines(link.Inlines, owner, items, text, ref textRun);
                    break;
                case SimpleField field:
                    if (!Visible(_frames.Count))
                    {
                        break;
                    }

                    if (Context.Fields?.Evaluate(field.Instruction) is { } value)
                    {
                        Append(value, FieldFormat(owner, field.Inlines), items, text, ref textRun);
                        break;
                    }

                    AddInlines(field.Inlines, owner, items, text, ref textRun);
                    break;
                case ContentControlInline control:
                    AddInlines(control.Inlines, owner, items, text, ref textRun);
                    break;
                case TrackedChange { Kind: TrackedChangeKind.Insert or TrackedChangeKind.MoveTo } change:
                    // Changes are shown as if accepted: insertions in, deletions out.
                    AddInlines(change.Inlines, owner, items, text, ref textRun);
                    break;
                default:
                    break;
            }
        }
    }

    private void AddRun(Run run, ResolvedParagraph owner, List<ParagraphItem> items, StringBuilder text, ref ResolvedRun? textRun)
    {
        var format = _styles.ForRun(owner, run);
        foreach (var content in run.Content)
        {
            if (content is FieldChar fieldChar)
            {
                FieldChange(fieldChar.Kind, format, items, text, ref textRun);
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
                // Inside the instruction of a field, or in the result of a field the layout evaluates: the first text of such a result
                // is replaced by the value, the rest is dropped.
                if (content is TextContent && _frames.Count > 0 && _frames[^1] is { Value: not null, Emitted: false } top && Visible(_frames.Count - 1))
                {
                    top.Emitted = true;
                    Append(top.Value, format, items, text, ref textRun);
                }

                continue;
            }

            switch (content)
            {
                case FootnoteReference reference when Context.Notes is { } notes:
                    Flush(items, text, ref textRun);
                    var mark = notes.Register(false, reference.Id);
                    items.Add(new TextSpan(mark.Text, format with { VerticalAlignment = RunVerticalAlignment.Superscript }));
                    (_marks ??= []).Add(new NoteMark(items.Count - 1, mark));
                    break;
                case EndnoteReference reference when Context.Notes is { } notes:
                    Flush(items, text, ref textRun);
                    var endnote = notes.Register(true, reference.Id);
                    items.Add(new TextSpan(endnote.Text, format with { VerticalAlignment = RunVerticalAlignment.Superscript }));
                    break;
                case TextContent t:
                    Append(t.Text, format, items, text, ref textRun);
                    break;
                case TabContent:
                    Append("\t", format, items, text, ref textRun);
                    break;
                case BreakContent b:
                    Flush(items, text, ref textRun);
                    items.Add(new BreakItem(b.Kind switch
                    {
                        Model.BreakKind.Page => BreakKind.Page,
                        Model.BreakKind.Column => BreakKind.Column,
                        _ => BreakKind.Line,
                    }));
                    break;
                case Picture picture:
                    if (picture.Inline)
                    {
                        Flush(items, text, ref textRun);
                        items.Add(Box(picture, format));
                    }
                    else
                    {
                        Flush(items, text, ref textRun);
                        AddFloat(FloatReader.FromDrawing(picture.Raw, _probe, StoryKey), items);
                    }

                    break;
                case UnknownRunContent { Content.Element.Name.LocalName: "footnoteRef" or "endnoteRef" } when Context.NoteMark is { } own:
                    Flush(items, text, ref textRun);
                    items.Add(new TextSpan(own, format with { VerticalAlignment = RunVerticalAlignment.Superscript }));
                    break;
                case UnknownRunContent unknown:
                    AddUnknown(unknown.Content.Element, format, items, text, ref textRun);
                    break;
                default:
                    break;
            }
        }
    }

    // One open complex field while a paragraph is read.
    private sealed class FieldFrame
    {
        public StringBuilder Instruction { get; } = new();

        public bool InResult { get; set; }

        public bool Decided { get; set; }

        /// <summary>The text of a field the layout evaluates; null for every other field (its stored result shows).</summary>
        public string? Value { get; set; }

        public bool Emitted { get; set; }
    }

    // True when the content at this depth is shown: no enclosing field is in its instruction part and none is replaced by a value.
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

    private void FieldChange(FieldCharKind kind, ResolvedRun format, List<ParagraphItem> items, StringBuilder text, ref ResolvedRun? textRun)
    {
        switch (kind)
        {
            case FieldCharKind.Begin:
                _frames.Add(new FieldFrame());
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
                    Append(closed.Value, format, items, text, ref textRun);
                }

                break;
            default:
                break;
        }
    }

    private void Decide(FieldFrame frame)
    {
        if (frame.Decided)
        {
            return;
        }

        frame.Decided = true;
        frame.Value = Context.Fields?.Evaluate(frame.Instruction.ToString());
    }

    // A simple field is drawn in the format of its first run, or of the paragraph mark when it has none.
    private ResolvedRun FieldFormat(ResolvedParagraph owner, IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                return _styles.ForRun(owner, run);
            }
        }

        return MarkRun(owner) with { ParagraphMarkOnly = false };
    }

    private void AddUnknown(XElement element, ResolvedRun format, List<ParagraphItem> items, StringBuilder text, ref ResolvedRun? textRun)
    {
        switch (element.Name.LocalName)
        {
            case "cr":
                Flush(items, text, ref textRun);
                items.Add(new BreakItem(BreakKind.Line));
                break;
            case "noBreakHyphen":
                Append("‑", format, items, text, ref textRun);
                break;
            case "ptab":
                Append("\t", format, items, text, ref textRun);
                break;
            case "AlternateContent" or "pict":
                Flush(items, text, ref textRun);
                var spec = element.Name.LocalName == "pict" ? FloatReader.FromVml(element, _probe, StoryKey) : FloatReader.FromAlternate(element, _probe, StoryKey);
                if (spec is null)
                {
                    items.Add(InlineBox.Placeholder(format));
                }
                else
                {
                    AddFloat(spec, items);
                }

                break;
            case "object":
                Flush(items, text, ref textRun);
                items.Add(InlineBox.Placeholder(format));
                break;
            default:
                break;
        }
    }

    private static void Append(string value, ResolvedRun format, List<ParagraphItem> items, StringBuilder text, ref ResolvedRun? textRun)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (textRun is not null && !ReferenceEquals(textRun, format) && !textRun.Equals(format))
        {
            Flush(items, text, ref textRun);
        }

        textRun ??= format;
        text.Append(value);
    }

    private InlineBox Box(Picture picture, ResolvedRun format)
    {
        var width = picture.WidthEmu / EmuPerPoint;
        var height = picture.HeightEmu / EmuPerPoint;
        if (picture.EmbedRelationshipId is not { } id)
        {
            var cap = width >= 1f && height >= 1f ? Math.Min(1f, MaximumBoxSize / Math.Max(width, height)) : 0f;
            return cap > 0f ? new InlineBox(width * cap, height * cap, null) : InlineBox.Placeholder(format);
        }

        // Only the header of the picture is read, and only to see whether it can be drawn and, when the file states no size, how big it is.
        id = ImageRef.Qualify(StoryKey, id);
        var info = _probe.Info(id);
        if (info is { Width: > 0, Height: > 0 } px && (width < 1f || height < 1f))
        {
            width = px.Width * 0.75f;
            height = px.Height * 0.75f;
        }

        if (width < 1f || height < 1f)
        {
            return InlineBox.Placeholder(format);
        }

        var limit = Math.Min(1f, MaximumBoxSize / Math.Max(width, height));
        width *= limit;
        height *= limit;
        if (height > TextAreaHeight)
        {
            // Taller than the text area: shrunk to it, aspect ratio kept (the width is scaled to the line width when the paragraph is laid out).
            width *= TextAreaHeight / height;
            height = TextAreaHeight;
        }

        return new InlineBox(width, height, info is null ? null : new ImageRef(id));
    }
}

/// <summary>A block the layout cannot draw yet: a marked box of fixed height.</summary>
internal sealed class PlaceholderPlan : BlockPlan
{
    private readonly float _height;

    public PlaceholderPlan(int bodyIndex, float height)
        : base(bodyIndex)
    {
        _height = height;
    }

    protected override IBlockFlow Make(float width) => new PlaceholderFlow(BodyIndex, width, _height);
}

/// <summary>The flow of a placeholder: one unsplittable box as wide as the column.</summary>
internal sealed class PlaceholderFlow : IBlockFlow
{
    private const float After = 8f;

    private readonly float _width;

    public PlaceholderFlow(int bodyIndex, float width, float height)
    {
        BodyIndex = bodyIndex;
        _width = width;
        Height = height;
    }

    public int BodyIndex { get; }

    public bool IsFirstPiece => true;

    public float Height { get; }

    public float SpaceBefore => 0f;

    public float SpaceAfter => After;

    public bool KeepNext => false;

    public bool PageBreakBefore => false;

    public string? ContextualGroup => null;

    public float MinimumHead => Height;

    public BreakKind? EndBreak => null;

    public IReadOnlyList<NoteRef> Notes => [];

    public IBlockFlow? AfterBreak(float width) => null;

    public FlowSplit Split(float available, bool mustProgress) =>
        Height <= available || mustProgress ? new FlowSplit(this, null) : new FlowSplit(null, this);

    public void Emit(PageBuilder page, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Add(new PlaceholderItem(x, y, _width, Height));
    }
}
