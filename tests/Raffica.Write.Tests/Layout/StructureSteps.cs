using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The kinds of structure step of the oracle gates.</summary>
internal enum StructureKind
{
    Move,
    Promote,
    Demote,
    DeleteHeading,
    DeleteRangeOverHeadings,
    InsertHeading,
    ToggleHeadingNumbering,
    ToggleList,
    ChangeLevel,
    Restart,
    Undo,
    Redo,
}

/// <summary>
/// The structure steps of the oracle gates (milestone 4.4a): a chapter move (a heading's subtree, or a random block range when the file has fewer than two headings), promote and demote,
/// deleting headings, inserting a heading, switching the numbering of the headings, list toggles, level changes, restarts, and undo and redo, each made by the real <see cref="TextEditor"/>,
/// brought into the layout exactly as the session does (a style change restyles, everything else is <see cref="DocumentLayout.ApplyEdit"/>) and compared with a full layout, exactly,
/// and the labels drawn on the pages with the independent <see cref="ReferenceNumbering"/>. A step the editor refuses (null, or no change) is counted as a refusal and compared nowhere,
/// except a move, an undo and a redo with something on the stack, which must always change something.
/// </summary>
internal sealed class StructureSteps
{
    /// <summary>The number of kinds.</summary>
    public static readonly int Kinds = Enum.GetValues<StructureKind>().Length;

    private readonly WordDocument _model;
    private readonly DocumentLayout _layout;
    private readonly LayoutOptions _options;
    private readonly FormattingSteps.RefreshableFormat _format;
    private StructureKind _kind;

    public StructureSteps(WordDocument model, DocumentLayout layout, LayoutOptions options)
    {
        _model = model;
        _layout = layout;
        _options = options;
        _format = new FormattingSteps.RefreshableFormat(model);
        Editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p), format: _format);
    }

    public TextEditor Editor { get; }

    /// <summary>What reading the drawn labels skipped (text-box pages) and collapsed (repeated header rows), over every step.</summary>
    public LabelTally Tally { get; } = new();

    /// <summary>Replaces the page the incremental layout is brought through (default: the page of the change's first block, plus one).</summary>
    public Func<DocumentLayout, BlockChange, int>? ThroughPage { get; set; }

    /// <summary>For each kind, how many of its steps changed something and were compared.</summary>
    public int[] RanPerKind { get; } = new int[Kinds];

    /// <summary>For each kind, how many attempts the editor refused (null or no change).</summary>
    public int[] RefusedPerKind { get; } = new int[Kinds];

    /// <summary>How many moves (undo and redo not counted) changed something and were compared.</summary>
    public int Moves { get; private set; }

    /// <summary>How many moves used a subtree of a heading.</summary>
    public int SubtreeMoves { get; private set; }

    /// <summary>How many steps (moves, undos and redos included) were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many moves spanned a table.</summary>
    public int MovedTables { get; private set; }

    /// <summary>How many moves spanned a section-break paragraph.</summary>
    public int MovedSections { get; private set; }

    /// <summary>How many labels were compared with the reference, summed over the steps.</summary>
    public long LabelsCompared { get; private set; }

    /// <summary>How many steps were restyles.</summary>
    public int Restyles { get; private set; }

    public static int Headings(WordDocument model)
    {
        var format = new LayoutEffectiveFormat(model);
        return model.Body.Blocks.OfType<Paragraph>().Count(p => format.ForParagraph(StoryRef.Body, p).OutlineLevel is not null);
    }

    /// <summary>
    /// One of every kind in a fixed order (numbering on first), then <paramref name="steps"/> random steps, all from <paramref name="random"/>. Returns how many steps were compared.
    /// </summary>
    public async Task<int> RunAsync(Random random, int steps, string label)
    {
        var before = Compared;
        for (var kind = 0; kind < Kinds; kind++)
        {
            // The order starts with numbering on and puts undo and redo after the editing kinds.
            var order = (StructureKind)((kind + (int)StructureKind.ToggleHeadingNumbering) % Kinds);
            await RunKindAsync(order, random, $"{label} forced {order}");
        }

        for (var i = 0; i < steps; i++)
        {
            var kind = (StructureKind)random.Next(Kinds);
            await RunKindAsync(kind, random, $"{label} step {i} {kind}");
        }

        return Compared - before;
    }

    /// <summary>Runs one step of a kind. Returns true when it changed something and was compared; false when the editor refused it or the document has nothing for it.</summary>
    public async Task<bool> RunKindAsync(StructureKind kind, Random random, string label)
    {
        _kind = kind;
        switch (kind)
        {
            case StructureKind.Move:
                return await MoveAsync(random, label);
            case StructureKind.Promote:
            case StructureKind.Demote:
                return await ShiftAsync(random, kind == StructureKind.Promote ? -1 : 1, label);
            case StructureKind.DeleteHeading:
                return await DeleteHeadingAsync(random, label);
            case StructureKind.DeleteRangeOverHeadings:
                return await DeleteRangeAsync(random, label);
            case StructureKind.InsertHeading:
                return await InsertHeadingAsync(random, label);
            case StructureKind.ToggleHeadingNumbering:
                return await ToggleNumberingAsync(label);
            case StructureKind.ToggleList:
                return await ToggleListAsync(random, label);
            case StructureKind.ChangeLevel:
                return await ChangeLevelAsync(random, label);
            case StructureKind.Restart:
                return await RestartAsync(random, label);
            case StructureKind.Undo:
                if (!Editor.History.CanUndo)
                {
                    return false;
                }

                return await CheckAsync(Editor.History.Undo(), label, required: true);
            default:
                if (!Editor.History.CanRedo)
                {
                    return false;
                }

                return await CheckAsync(Editor.History.Redo(), label, required: true);
        }
    }

    /// <summary>One random legal move on the body, then undo and redo, each compared with a full layout. Returns false when the document is too small to move anything.</summary>
    public async Task<bool> MoveAsync(Random random, string label)
    {
        var blocks = _model.Body.Blocks;

        // A table of contents moves whole or not at all and nothing lands inside one (the editor refuses the rest): the picks are the legal ones, the oracle is about the moves the editor accepts.
        var tocs = TocRanges.In(blocks) ?? [];
        var (first, count, subtree) = Pick(random);
        for (var attempt = 0; attempt < 64 && count > 0 && tocs.Any(t => first + count - 1 >= t.First && first <= t.Last && !(first <= t.First && first + count - 1 >= t.Last)); attempt++)
        {
            (first, count, subtree) = Pick(random);
        }

        if (count == 0)
        {
            return false;
        }

        var targets = new List<int>();
        for (var t = 0; t <= blocks.Count; t++)
        {
            if ((t < first || t > first + count) && !tocs.Any(r => t > r.First && t <= r.Last))
            {
                targets.Add(t);
            }
        }

        if (targets.Count == 0)
        {
            return false;
        }

        return await MoveToAsync(first, count, targets[random.Next(targets.Count)], subtree, label);
    }

    /// <summary>Moves a given range (the caller picks it) and checks it, its undo and its redo.</summary>
    public async Task<bool> MoveToAsync(int first, int count, int target, bool subtree, string label)
    {
        var blocks = _model.Body.Blocks;
        var (spanFirst, spanCount) = (Math.Min(first, target), Math.Max(first + count, target) - Math.Min(first, target));

        _kind = StructureKind.Move;
        var result = Editor.MoveBlocks(first, count, target);
        if (result is null)
        {
            RefusedPerKind[(int)StructureKind.Move]++;
            return false;
        }

        if (Enumerable.Range(spanFirst, spanCount).Any(i => blocks[i] is Table))
        {
            MovedTables++;
        }

        if (Enumerable.Range(spanFirst, spanCount).Any(i => blocks[i] is Paragraph { SectionBreak: not null }))
        {
            MovedSections++;
        }

        await CheckAsync(result, label + " move", required: true);
        Moves++;
        if (subtree)
        {
            SubtreeMoves++;
        }

        _kind = StructureKind.Undo;
        await CheckAsync(Editor.History.Undo(), label + " undo move", required: true);
        _kind = StructureKind.Redo;
        await CheckAsync(Editor.History.Redo(), label + " redo move", required: true);
        _kind = StructureKind.Move;
        return true;
    }

    // A heading paragraph and the blocks up to the next heading of the same or a higher level; a random range when the file has fewer than two headings.
    private (int First, int Count, bool Subtree) Pick(Random random)
    {
        var blocks = _model.Body.Blocks;
        if (blocks.Count < 2)
        {
            return (0, 0, false);
        }

        var (levels, headings) = HeadingsOf();

        // A subtree that is the whole body has no legal target; try a few headings.
        if (headings.Count >= 2)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var first = headings[random.Next(headings.Count)];
                var end = SubtreeEnd(levels, first);
                if (end - first < blocks.Count)
                {
                    return (first, end - first, true);
                }
            }
        }

        var start = random.Next(blocks.Count);
        var length = 1 + random.Next(Math.Min(6, blocks.Count - start));
        if (length >= blocks.Count)
        {
            length = blocks.Count - 1;
        }

        return (start, length, false);
    }

    private (int?[] Levels, List<int> Headings) HeadingsOf()
    {
        var blocks = _model.Body.Blocks;
        var format = new LayoutEffectiveFormat(_model);
        var levels = new int?[blocks.Count];
        var headings = new List<int>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p && format.ForParagraph(StoryRef.Body, p).OutlineLevel is { } level)
            {
                levels[i] = level;
                headings.Add(i);
            }
        }

        return (levels, headings);
    }

    private static int SubtreeEnd(int?[] levels, int first)
    {
        var end = first + 1;
        while (end < levels.Length && !(levels[end] is { } l && l <= levels[first]))
        {
            end++;
        }

        return end;
    }

    private int Len(Paragraph paragraph) => ParagraphProjection.Build(paragraph, _layout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private async Task<bool> ShiftAsync(Random random, int delta, string label)
    {
        var (levels, headings) = HeadingsOf();
        if (headings.Count == 0)
        {
            return false;
        }

        var first = headings[random.Next(headings.Count)];
        var end = SubtreeEnd(levels, first);
        return await CheckAsync(Editor.ShiftOutlineLevels(first, end - 1, delta), label, required: false);
    }

    // The heading and its paragraph break: the range from the start of the heading to the start of the next paragraph.
    private async Task<bool> DeleteHeadingAsync(Random random, string label)
    {
        var (_, headings) = HeadingsOf();
        if (headings.Count == 0)
        {
            return false;
        }

        var blocks = _model.Body.Blocks;
        var at = headings[random.Next(headings.Count)];
        var heading = (Paragraph)blocks[at];
        var range = at + 1 < blocks.Count && blocks[at + 1] is Paragraph next
            ? new TextRange(At(heading, 0), At(next, 0))
            : new TextRange(At(heading, 0), At(heading, Len(heading)));
        return await CheckAsync(Editor.Delete(range), label, required: false);
    }

    // From the middle of one heading to the middle of a later heading (a second heading always lies in the range, its text partly and its break inside the delete).
    private async Task<bool> DeleteRangeAsync(Random random, string label)
    {
        var (_, headings) = HeadingsOf();
        if (headings.Count < 2)
        {
            return false;
        }

        var blocks = _model.Body.Blocks;
        var from = random.Next(headings.Count - 1);
        var start = headings[from];
        var stop = headings[Math.Min(headings.Count - 1, from + 1 + random.Next(3))];
        var a = (Paragraph)blocks[start];
        var b = (Paragraph)blocks[stop];
        var range = new TextRange(At(a, Len(a) / 2), At(b, Len(b) / 2));
        return await CheckAsync(Editor.Delete(range), label, required: false);
    }

    // A paragraph split in two, text typed into the new one and a heading style applied to it.
    private async Task<bool> InsertHeadingAsync(Random random, string label)
    {
        var paragraphs = _model.Body.Blocks.OfType<Paragraph>().ToList();
        if (paragraphs.Count == 0 || _model.Styles is not { } sheet)
        {
            return false;
        }

        var styleId = "Heading" + (1 + random.Next(3));
        if (sheet.Find(styleId) is null)
        {
            return false;
        }

        var source = paragraphs[random.Next(paragraphs.Count)];
        var end = At(source, Len(source));
        var split = Editor.SplitParagraph(new TextRange(end, end));
        if (!await CheckAsync(split, label + " split", required: false))
        {
            return false;
        }

        var caret = split.Caret;
        await CheckAsync(Editor.InsertText(new TextRange(caret, caret), "New heading " + random.Next(100)), label + " text", required: false);
        var whole = new TextRange(At(caret.Paragraph, 0), At(caret.Paragraph, Len(caret.Paragraph)));
        return await CheckAsync(Editor.ApplyParagraphStyle(whole, styleId), label + " style", required: false);
    }

    private async Task<bool> ToggleNumberingAsync(string label)
    {
        var first = _model.Body.Blocks.OfType<Paragraph>().FirstOrDefault();
        var state = HeadingNumbering.StateOf(_model);
        if (first is null || state == HeadingNumberingState.Unavailable)
        {
            return false;
        }

        return await CheckAsync(Editor.SetHeadingNumbering(state != HeadingNumberingState.On, At(first, 0)), label, required: false);
    }

    private async Task<bool> ToggleListAsync(Random random, string label)
    {
        var blocks = _model.Body.Blocks;
        var paragraphs = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph).ToList();
        if (paragraphs.Count == 0)
        {
            return false;
        }

        var first = paragraphs[random.Next(paragraphs.Count)];
        var last = first;
        for (var extra = random.Next(3); extra > 0 && last + 1 < blocks.Count && blocks[last + 1] is Paragraph; extra--)
        {
            last++;
        }

        var a = (Paragraph)blocks[first];
        var b = (Paragraph)blocks[last];
        var kind = random.Next(2) == 0 ? ListKind.Bullet : ListKind.Numbered;
        return await CheckAsync(Editor.ToggleList(new TextRange(At(a, 0), At(b, Len(b))), kind), label, required: false);
    }

    // A paragraph that is in a list (counted by the independent reference), else any top-level paragraph.
    private Paragraph? ListParagraph(Random random)
    {
        var labelled = ReferenceNumbering.Labels(_model);
        if (labelled.Count > 0)
        {
            return labelled[random.Next(labelled.Count)].Paragraph;
        }

        var paragraphs = _model.Body.Blocks.OfType<Paragraph>().ToList();
        return paragraphs.Count == 0 ? null : paragraphs[random.Next(paragraphs.Count)];
    }

    private async Task<bool> ChangeLevelAsync(Random random, string label)
    {
        if (ListParagraph(random) is not { } paragraph)
        {
            return false;
        }

        var range = new TextRange(At(paragraph, 0), At(paragraph, 0));
        return await CheckAsync(Editor.ChangeListLevel(range, random.Next(2) == 0 ? -1 : 1), label, required: false);
    }

    private async Task<bool> RestartAsync(Random random, string label)
    {
        if (ListParagraph(random) is not { } paragraph)
        {
            return false;
        }

        var result = random.Next(3) == 0 ? Editor.ContinueNumbering(At(paragraph, 0)) : Editor.RestartNumbering(At(paragraph, 0));
        return await CheckAsync(result, label, required: false);
    }

    // Brings an edit into the layout the way the session does, compares the layout with a full one and the drawn labels with the reference.
    private async Task<bool> CheckAsync(EditResult? result, string label, bool required)
    {
        if (result is null || (result.Changes.Count == 0 && result.Style is null))
        {
            if (required)
            {
                // A move, an undo and a redo with something on the stack always change blocks or styles: nothing compared would prove nothing.
                Assert.Fail(label + ": the step returned no change");
            }

            RefusedPerKind[(int)_kind]++;
            return false;
        }

        if (result.Style is not null)
        {
            // The style sheet changed: the resolver of the layout (and of the editor) is stale; the model already holds the block changes too.
            _format.Invalidate();
            _layout.Restyle(2);
            Restyles++;
        }
        else
        {
            foreach (var change in result.Changes)
            {
                var through = ThroughPage?.Invoke(_layout, change)
                    ?? Math.Max(1, (_layout.Locate(Math.Min(change.First, _model.Body.Blocks.Count - 1))?.Page ?? 0) + 1);
                _layout.ApplyEdit(change, through);
            }
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        LabelsCompared += DrawnLabels.AssertEqualToReference(_layout, _model, label, Tally).Count;
        Compared++;
        RanPerKind[(int)_kind]++;
        return true;
    }

    // The files whose drawn labels could not all be read because a page's text does not line up with its lines (a text box), and the most pages and paragraphs one reading skipped. The bound is what
    // the seeded runs showed; any other file, and every generated document, must read every page.
    private static readonly Dictionary<string, (int Pages, int Paragraphs)> s_unreadable = new(StringComparer.Ordinal)
    {
        ["poi-comment"] = (1, 0),
        ["poi-shapes-with-text"] = (1, 3),
    };

    /// <summary>Fails when a reading skipped more pages or paragraphs than the file's allowance (none for a file that is not listed).</summary>
    public static void AssertReadable(string id, LabelTally tally)
    {
        var (pages, paragraphs) = s_unreadable.TryGetValue(id, out var allowed) ? allowed : (0, 0);
        Assert.True(
            tally.MaxUnreadablePages <= pages && tally.MaxUnreadableParagraphs <= paragraphs,
            $"{id}: labels were not readable on some pages ({tally}), allowed {pages} pages and {paragraphs} paragraphs");
    }
}
