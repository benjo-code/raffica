using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The structure steps of the oracle gates (milestone 4.4a): a chapter move (a heading's subtree, or a random block range when the file has fewer than two headings) made by the
/// real <see cref="TextEditor"/>, brought into the layout with <see cref="DocumentLayout.ApplyEdit"/> exactly as the session does and compared with a full layout, exactly; with its
/// undo and redo.
/// </summary>
internal sealed class StructureSteps
{
    private readonly WordDocument _model;
    private readonly DocumentLayout _layout;
    private readonly LayoutOptions _options;

    public StructureSteps(WordDocument model, DocumentLayout layout, LayoutOptions options)
    {
        _model = model;
        _layout = layout;
        _options = options;
        Editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
    }

    public TextEditor Editor { get; }

    /// <summary>Replaces the page the incremental layout is brought through (default: the page of the change's first block, plus one).</summary>
    public Func<DocumentLayout, BlockChange, int>? ThroughPage { get; set; }

    /// <summary>How many moves (undo and redo not counted) changed something and were compared.</summary>
    public int Moves { get; private set; }

    /// <summary>How many moves used a subtree of a heading.</summary>
    public int SubtreeMoves { get; private set; }

    /// <summary>How many steps (moves, undos and redos) were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many moves spanned a table.</summary>
    public int MovedTables { get; private set; }

    /// <summary>How many moves spanned a section-break paragraph.</summary>
    public int MovedSections { get; private set; }

    public static int Headings(WordDocument model)
    {
        var format = new LayoutEffectiveFormat(model);
        return model.Body.Blocks.OfType<Paragraph>().Count(p => format.ForParagraph(StoryRef.Body, p).OutlineLevel is not null);
    }

    /// <summary>One random legal move on the body, then undo and redo, each compared with a full layout. Returns false when the document is too small to move anything.</summary>
    public async Task<bool> MoveAsync(Random random, string label)
    {
        var blocks = _model.Body.Blocks;
        var (first, count, subtree) = Pick(random);
        if (count == 0)
        {
            return false;
        }

        var targets = new List<int>();
        for (var t = 0; t <= blocks.Count; t++)
        {
            if (t < first || t > first + count)
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

        var result = Editor.MoveBlocks(first, count, target);
        if (result is null)
        {
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

        await CheckAsync(result, label + " move");
        Moves++;
        if (subtree)
        {
            SubtreeMoves++;
        }

        await CheckAsync(Editor.History.Undo(), label + " undo move");
        await CheckAsync(Editor.History.Redo(), label + " redo move");
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

        // A subtree that is the whole body has no legal target; try a few headings.
        if (headings.Count >= 2)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var first = headings[random.Next(headings.Count)];
                var end = first + 1;
                while (end < blocks.Count && !(levels[end] is { } l && l <= levels[first]))
                {
                    end++;
                }

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

    private async Task CheckAsync(EditResult? result, string label)
    {
        // A move, its undo and its redo always change blocks: nothing compared would prove nothing.
        if (result is not { Changes.Count: > 0 })
        {
            Assert.Fail(label + ": the step returned no change");
            return;
        }

        foreach (var change in result.Changes)
        {
            var through = ThroughPage?.Invoke(_layout, change)
                ?? Math.Max(1, (_layout.Locate(Math.Min(change.First, _model.Body.Blocks.Count - 1))?.Page ?? 0) + 1);
            _layout.ApplyEdit(change, through);
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        Compared++;
    }
}
