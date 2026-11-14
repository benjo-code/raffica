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
    private const float PlaceholderHeight = 24f;
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

    public BlockPlanner(StyleResolver styles, NumberingCounter numbering, ParagraphLayouter layouter, DocumentSettings? settings)
    {
        _styles = styles;
        _numbering = numbering;
        _layouter = layouter;
        _autoHyphenation = styles.AutoHyphenation;
        _defaultTabStop = 36f;
        _expandShiftReturn = true;
        if (settings is not null)
        {
            var tab = (string?)settings.Properties.Get(W + "defaultTabStop")?.Attribute(W + "val");
            if (double.TryParse(tab, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) && twips >= 20)
            {
                _defaultTabStop = (float)(twips / 20.0);
            }

            _expandShiftReturn = settings.Properties.Get(W + "compat")?.Element(W + "doNotExpandShiftReturn") is null;
        }
    }

    /// <summary>The plan of a block, or null for a block that takes no room (a bookmark mark at block level).</summary>
    public BlockPlan? Plan(FlatBlock flat)
    {
        switch (flat.Block)
        {
            case Paragraph paragraph:
                return new ParagraphPlan(flat.BodyIndex, Input(paragraph), _layouter, isContinuation: false);
            case UnknownBlock unknown when s_invisibleBlocks.Contains(unknown.Content.Element.Name.LocalName):
                return null;
            default:
                // A table (until Task 10) or an element the layout cannot draw: a marked box, the model keeps the content.
                return new PlaceholderPlan(flat.BodyIndex, PlaceholderHeight);
        }
    }

    private ParagraphInput Input(Paragraph paragraph)
    {
        var resolved = _styles.ForParagraph(paragraph);
        var label = _numbering.Next(resolved);
        var items = new List<ParagraphItem>();
        var text = new StringBuilder();
        ResolvedRun? textRun = null;
        AddInlines(paragraph.Inlines, resolved, items, text, ref textRun);
        Flush(items, text, ref textRun);
        return new ParagraphInput(resolved, items, MarkRun(resolved), label)
        {
            DefaultTabStop = _defaultTabStop,
            ExpandShiftReturn = _expandShiftReturn,
            AutoHyphenation = _autoHyphenation,
        };
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
            switch (content)
            {
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

                    break;
                case UnknownRunContent unknown:
                    AddUnknown(unknown.Content.Element, format, items, text, ref textRun);
                    break;
                default:
                    break;
            }
        }
    }

    private static void AddUnknown(XElement element, ResolvedRun format, List<ParagraphItem> items, StringBuilder text, ref ResolvedRun? textRun)
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
            case "pict" or "object" or "AlternateContent":
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

    private static InlineBox Box(Picture picture, ResolvedRun format)
    {
        var width = picture.WidthEmu / EmuPerPoint;
        var height = picture.HeightEmu / EmuPerPoint;
        if (picture.EmbedRelationshipId is not { } id || width < 1f || height < 1f)
        {
            return InlineBox.Placeholder(format);
        }

        return new InlineBox(Math.Min(width, MaximumBoxSize), Math.Min(height, MaximumBoxSize), new ImageRef(id));
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

    public IBlockFlow? AfterBreak(float width) => null;

    public FlowSplit Split(float available, bool mustProgress) =>
        Height <= available || mustProgress ? new FlowSplit(this, null) : new FlowSplit(null, this);

    public void Emit(PageBuilder page, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Add(new PlaceholderItem(x, y, _width, Height));
    }
}
