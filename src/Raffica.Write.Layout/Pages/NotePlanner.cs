using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>What a story is planned for: the fields it evaluates, the note references it numbers, the number a note shows for itself.</summary>
internal sealed class PlanContext
{
    /// <summary>Evaluates PAGE, NUMPAGES, SECTIONPAGES and SECTION; null: every field shows its stored result.</summary>
    public FieldContext? Fields { get; init; }

    /// <summary>Numbers the footnote and endnote references of the story (the body only); null: references draw nothing.</summary>
    public NotePlanner? Notes { get; init; }

    /// <summary>The number the note's own <c>w:footnoteRef</c> or <c>w:endnoteRef</c> shows (a note body only).</summary>
    public string? NoteMark { get; init; }
}

/// <summary>A footnote or endnote reference in the body, numbered in reading order.</summary>
internal sealed class NoteRef
{
    public NoteRef(bool isEndnote, int id, int number, string text)
    {
        IsEndnote = isEndnote;
        Id = id;
        Number = number;
        Text = text;
    }

    public bool IsEndnote { get; }

    public int Id { get; }

    /// <summary>Counted per document, in the order the references appear (not the note id).</summary>
    public int Number { get; }

    /// <summary>The number as shown, in the number format of the document settings.</summary>
    public string Text { get; }

    internal NoteBody? Body { get; set; }

    internal float BodyWidth { get; set; } = float.NaN;
}

/// <summary>A reference mark inside a paragraph: the item that draws the number, and the note it belongs to.</summary>
internal readonly record struct NoteMark(int ItemIndex, NoteRef Note);

/// <summary>The paragraphs of one note laid out at one width.</summary>
internal sealed class NoteBody
{
    public NoteBody(IReadOnlyList<IBlockFlow> flows)
    {
        Flows = flows;
        Height = NoteGroup.HeightOf(flows);
    }

    public IReadOnlyList<IBlockFlow> Flows { get; }

    public float Height { get; }
}

/// <summary>Flows that belong together on a page, one under the other with the usual space between paragraphs.</summary>
internal sealed class NoteGroup
{
    public NoteGroup(IReadOnlyList<IBlockFlow> flows)
    {
        Flows = flows;
        Height = HeightOf(flows);
    }

    public IReadOnlyList<IBlockFlow> Flows { get; }

    public float Height { get; }

    public static float HeightOf(IReadOnlyList<IBlockFlow> flows)
    {
        var height = 0f;
        for (var i = 0; i < flows.Count; i++)
        {
            height += flows[i].Height;
            if (i > 0)
            {
                height += flows[i - 1].SpaceAfter + flows[i].SpaceBefore;
            }
        }

        return height;
    }
}

/// <summary>
/// Numbers the note references of the body while it is planned, lays the notes out and makes the endnote blocks. One per layout; the
/// filler drives it from one thread. Footnote numbers count per document (restart rules are not applied); endnotes number separately.
/// </summary>
internal sealed class NotePlanner
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly WordDocument _document;
    private readonly Func<BlockPlanner> _planners;
    private readonly string? _footnoteFormat;
    private readonly string? _endnoteFormat;
    private readonly List<NoteRef> _endnotes = [];
    private int _footnotes;

    public NotePlanner(WordDocument document, Func<BlockPlanner> planners)
    {
        _document = document;
        _planners = planners;
        var settings = document.Settings?.Properties;
        _footnoteFormat = (string?)settings?.Get(W + "footnotePr")?.Element(W + "numFmt")?.Attribute(W + "val");
        // Word's default for endnotes is lowercase roman, for footnotes decimal.
        _endnoteFormat = (string?)settings?.Get(W + "endnotePr")?.Element(W + "numFmt")?.Attribute(W + "val") ?? "lowerRoman";
    }

    /// <summary>Numbers a reference. A reference to a note that does not exist is numbered like any other (it just has no text).</summary>
    public NoteRef Register(bool isEndnote, int id)
    {
        if (isEndnote)
        {
            var number = _endnotes.Count + 1;
            var reference = new NoteRef(true, id, number, PageNumbers.Format(number, _endnoteFormat));
            _endnotes.Add(reference);
            return reference;
        }

        _footnotes++;
        return new NoteRef(false, id, _footnotes, PageNumbers.Format(_footnotes, _footnoteFormat));
    }

    /// <summary>The note's blocks laid out at <paramref name="width"/> (empty for a missing note); the answer for the last width asked is kept.</summary>
    public NoteBody BodyOf(NoteRef reference, float width)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Body is { } known && reference.BodyWidth == width)
        {
            return known;
        }

        var collection = reference.IsEndnote ? _document.Endnotes : _document.Footnotes;
        var note = collection?.Find(reference.Id);
        NoteBody body;
        if (note is null || note.Kind != NoteKind.Normal)
        {
            body = new NoteBody([]);
        }
        else
        {
            var planner = _planners();
            planner.Context = new PlanContext { NoteMark = reference.Text };
            planner.StoryKey = reference.IsEndnote ? ImageRef.EndnotesStory : ImageRef.FootnotesStory;
            planner.Story = new StoryRef(StoryKind.Note, (reference.IsEndnote ? "endnote:" : string.Empty) + reference.Id.ToString(CultureInfo.InvariantCulture));
            planner.CanPlaceFloats = false;
            body = new NoteBody(planner.PlanStory(note.Content.Blocks, width));
        }

        reference.Body = body;
        reference.BodyWidth = width;
        return body;
    }

    /// <summary>The blocks that end the document: a separator and every endnote in order. Enumerate it only after the body is planned, so every reference is numbered.</summary>
    public IEnumerable<BlockPlan> EndnotePlans()
    {
        var any = false;
        foreach (var reference in _endnotes)
        {
            var note = _document.Endnotes?.Find(reference.Id);
            if (note is null || note.Kind != NoteKind.Normal)
            {
                continue;
            }

            if (!any)
            {
                any = true;
                yield return new LazyPlan(width => new RuleFlow(width, NoteLayout.ShortRule));
            }

            var count = FlatBlocks.From(note.Content.Blocks, -1).Count(f => !BlockPlanner.TakesNoRoom(f.Block));
            for (var i = 0; i < count; i++)
            {
                var index = i;
                var captured = reference;
                yield return new LazyPlan(width =>
                {
                    var flows = BodyOf(captured, width).Flows;
                    return index < flows.Count ? flows[index] : new RuleFlow(width, 0f, drawn: false);
                });
            }
        }
    }
}

/// <summary>The measures of the note area, in points.</summary>
internal static class NoteLayout
{
    /// <summary>The length of the rule above the footnotes (Word draws about two inches).</summary>
    public const float ShortRule = 144f;

    /// <summary>The room the separator takes above the first note: the rule sits <see cref="RuleOffset"/> below the top of it.</summary>
    public const float SeparatorHeight = 10f;

    public const float RuleOffset = 4f;

    /// <summary>The share of the body height that notes may take on one page; the rest of a longer note continues on the next page.</summary>
    public const float MaximumShare = 0.5f;
}

/// <summary>A block made when it is first laid out (the endnote blocks).</summary>
internal sealed class LazyPlan : BlockPlan
{
    private readonly Func<float, IBlockFlow> _make;

    public LazyPlan(Func<float, IBlockFlow> make)
        : base(-1)
    {
        _make = make;
    }

    protected override IBlockFlow Make(float width) => _make(width);
}

/// <summary>A rule of fixed length with the room of the note separator; draws nothing when it is not <paramref name="drawn"/> (a flow of no height).</summary>
internal sealed class RuleFlow : IBlockFlow
{
    private readonly float _length;
    private readonly bool _drawn;

    public RuleFlow(float width, float length, bool drawn = true)
    {
        _length = Math.Min(width, length);
        _drawn = drawn;
        Height = drawn ? NoteLayout.SeparatorHeight : 0f;
    }

    public int BodyIndex => -1;

    public bool IsFirstPiece => true;

    public float Height { get; }

    public float SpaceBefore => 0f;

    public float SpaceAfter => 0f;

    /// <summary>The separator stays with the first endnote.</summary>
    public bool KeepNext => _drawn;

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
        if (_drawn)
        {
            page.Add(new RuleItem(x, y + NoteLayout.RuleOffset, _length, 0.5f, 0xFF000000));
        }
    }
}

/// <summary>
/// The footnotes of one page: what continues from the page before, the notes of the references placed so far, and the room they
/// take at the bottom. The bands ask <see cref="ReservedWith"/> before they accept a piece with references, so the room is
/// reserved when the line is placed; <see cref="Emit"/> draws the area when the page ends and returns what did not fit.
/// </summary>
internal sealed class PageNotes
{
    private const float Tolerance = 0.05f;

    private readonly NotePlanner _planner;
    private readonly float _width;
    private readonly float _cap;
    private readonly IReadOnlyList<NoteGroup> _carry;
    private readonly float _carryHeight;
    private readonly List<NoteRef> _refs = [];
    private float _bodiesHeight;

    public PageNotes(NotePlanner planner, float width, float bodyHeight, float contentTop, IReadOnlyList<NoteGroup> carry)
    {
        _planner = planner;
        _width = width;
        _cap = Math.Max(0f, bodyHeight) * NoteLayout.MaximumShare;
        _carry = carry;
        foreach (var group in carry)
        {
            _carryHeight += group.Height;
        }

        ContentBottom = contentTop;
        Reserved = Area(0f);
    }

    /// <summary>The room the note area takes at the bottom of the page now.</summary>
    public float Reserved { get; private set; }

    /// <summary>The lowest bottom of body content placed on the page so far (any column of any band).</summary>
    public float ContentBottom { get; private set; }

    public bool HasContent => Reserved > 0f;

    /// <summary>What the area would take with the notes of <paramref name="added"/> as well.</summary>
    public float ReservedWith(IReadOnlyList<NoteRef> added)
    {
        var extra = 0f;
        foreach (var reference in added)
        {
            extra += _planner.BodyOf(reference, _width).Height;
        }

        return Area(extra);
    }

    public void Add(IReadOnlyList<NoteRef> added)
    {
        foreach (var reference in added)
        {
            _refs.Add(reference);
            _bodiesHeight += _planner.BodyOf(reference, _width).Height;
        }

        Reserved = Area(0f);
    }

    public void ContentReaches(float y) => ContentBottom = Math.Max(ContentBottom, y);

    private float Area(float extra)
    {
        var content = _carryHeight + _bodiesHeight + extra;
        return content <= 0f ? 0f : Math.Min(_cap, NoteLayout.SeparatorHeight + content);
    }

    /// <summary>Draws the area with its top at <paramref name="top"/>; returns the notes (or the rest of a note) that did not fit and continue on the next page.</summary>
    public List<NoteGroup> Emit(PageBuilder page, float x, float top, float bottom)
    {
        ArgumentNullException.ThrowIfNull(page);
        var overflow = new List<NoteGroup>();
        var rule = _carry.Count > 0 ? _width : NoteLayout.ShortRule;
        page.Add(new RuleItem(x, top + NoteLayout.RuleOffset, Math.Min(rule, _width), 0.5f, 0xFF000000));
        var y = top + NoteLayout.SeparatorHeight;
        var full = false;
        var placed = false;
        foreach (var group in AllGroups())
        {
            if (full)
            {
                overflow.Add(group);
                continue;
            }

            var flows = group.Flows;
            var previousAfter = 0f;
            for (var i = 0; i < flows.Count; i++)
            {
                var flow = flows[i];
                var gap = i == 0 ? 0f : previousAfter + flow.SpaceBefore;
                var split = flow.Split(bottom - y - gap + Tolerance, mustProgress: !placed);
                if (split.Head is not { } head)
                {
                    overflow.Add(new NoteGroup([.. flows.Skip(i)]));
                    full = true;
                    break;
                }

                head.Emit(page, x, y + gap);
                y += gap + head.Height;
                placed = true;
                previousAfter = head.SpaceAfter;
                if (split.Tail is { } tail)
                {
                    overflow.Add(new NoteGroup([tail, .. flows.Skip(i + 1)]));
                    full = true;
                    break;
                }
            }
        }

        return overflow;
    }

    private IEnumerable<NoteGroup> AllGroups()
    {
        foreach (var group in _carry)
        {
            yield return group;
        }

        foreach (var reference in _refs)
        {
            var body = _planner.BodyOf(reference, _width);
            if (body.Flows.Count > 0)
            {
                yield return new NoteGroup(body.Flows);
            }
        }
    }
}
