using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The formatting steps of the oracle gates (<c>CorpusIncrementalTests</c>, <c>RealEditorOracleTests</c>): a character toggle on a random range, a paragraph alignment, a
/// paragraph style, "Update style to match" followed by <see cref="DocumentLayout.Restyle"/> (with its undo and redo), and formatting whose range reaches into tables and block
/// content controls. Every step is made by the real <see cref="TextEditor"/>, brought into the layout the way the app does it (a style change restyles, everything else is
/// <see cref="DocumentLayout.ApplyEdit"/>) and then compared with a full layout, exactly. The picks come from the caller's seeded <see cref="Random"/>.
/// </summary>
internal sealed class FormattingSteps
{
    /// <summary>The kinds <see cref="RunAsync"/> knows.</summary>
    public const int Kinds = 5;

    private readonly WordDocument _model;
    private readonly DocumentLayout _layout;
    private readonly LayoutOptions _options;
    private readonly RefreshableFormat _format;

    public FormattingSteps(WordDocument model, DocumentLayout layout, LayoutOptions options)
    {
        _model = model;
        _layout = layout;
        _options = options;
        _format = new RefreshableFormat(model);
        Editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p), format: _format);
    }

    public TextEditor Editor { get; }

    /// <summary>How many steps changed something and were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many of them were restyles.</summary>
    public int Restyles { get; private set; }

    /// <summary>How many steps used a range that reaches into a table or a block content control.</summary>
    public int ReachedInto { get; private set; }

    /// <summary>Runs one formatting step: 0 toggle, 1 alignment, 2 paragraph style, 3 update style (and undo, redo), 4 formatting that reaches into tables and controls.</summary>
    public async Task RunAsync(int kind, Random random, string label)
    {
        if (TestEdit.Paragraphs(_model).Count == 0)
        {
            return;
        }

        switch (kind)
        {
            case 0:
                await CheckAsync(Editor.ToggleCharacter(RandomRange(random), (CharacterToggle)random.Next(8)), label + " toggle");
                break;
            case 1:
                await CheckAsync(Editor.ApplyParagraphChanges(RandomRange(random), ParagraphChanges.Alignment((ParagraphJustification)random.Next(4))), label + " alignment");
                break;
            case 2:
                if (_model.Styles?.Styles.Where(s => s.Type == "paragraph").ToList() is { Count: > 0 } styles)
                {
                    await CheckAsync(Editor.ApplyParagraphStyle(RandomRange(random), styles[random.Next(styles.Count)].Id), label + " paragraph style");
                }

                break;
            case 3:
                await UpdateStyleAsync(random, label);
                break;
            default:
                if (ReachingRange(random) is { } range)
                {
                    ReachedInto++;
                    await CheckAsync(Editor.ToggleCharacter(range, CharacterToggle.Bold), label + " reaching toggle");
                    await CheckAsync(Editor.ApplyParagraphChanges(range, ParagraphChanges.Alignment(ParagraphJustification.Center)), label + " reaching alignment");
                    await CheckAsync(Editor.ToggleCharacter(range, CharacterToggle.Italic), label + " reaching toggle 2");
                }

                break;
        }
    }

    /// <summary>The top-level indexes of the tables and block content controls that have a top-level paragraph on both sides and a paragraph inside.</summary>
    public static List<int> ReachingCandidates(WordDocument model)
    {
        var blocks = model.Body.Blocks;
        var candidates = new List<int>();
        for (var i = 1; i < blocks.Count - 1; i++)
        {
            if (blocks[i] is Table or ContentControlBlock && blocks[i - 1] is Paragraph && blocks[i + 1] is Paragraph && TestEdit.Paragraphs(blocks[i]).Any())
            {
                candidates.Add(i);
            }
        }

        return candidates;
    }

    private int Len(Paragraph paragraph) => ParagraphProjection.Build(paragraph, _layout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private TextRange RandomRange(Random random)
    {
        var all = TestEdit.Paragraphs(_model);
        var first = random.Next(all.Count);
        var last = random.Next(3) == 0 ? Math.Min(all.Count - 1, first + 1 + random.Next(4)) : first;
        var a = all[first].Paragraph;
        var b = all[last].Paragraph;
        var from = random.Next(Len(a) + 1);
        var to = ReferenceEquals(a, b) ? Math.Min(Len(a), from + random.Next(40)) : random.Next(Len(b) + 1);
        return new TextRange(At(a, from), At(b, to));
    }

    // From a top-level paragraph before a table or block content control to a top-level paragraph after it: the walker has to mutate paragraphs
    // nested inside a block that the one BlockChange snapshots whole.
    private TextRange? ReachingRange(Random random)
    {
        var blocks = _model.Body.Blocks;
        var candidates = ReachingCandidates(_model);
        if (candidates.Count == 0)
        {
            return null;
        }

        var at = candidates[random.Next(candidates.Count)];
        var span = Math.Min(blocks.Count - 1, at + 1 + random.Next(3));
        while (span > at && blocks[span] is not Paragraph)
        {
            span--;
        }

        var before = (Paragraph)blocks[at - 1];
        var after = (Paragraph)blocks[span];
        return new TextRange(At(before, random.Next(Len(before) + 1)), At(after, random.Next(Len(after) + 1)));
    }

    private async Task UpdateStyleAsync(Random random, string label)
    {
        var all = TestEdit.Paragraphs(_model);
        var paragraph = all[random.Next(all.Count)].Paragraph;
        var whole = new TextRange(At(paragraph, 0), At(paragraph, Len(paragraph)));

        // Give the paragraph direct overrides so that there is something to move into its style.
        await CheckAsync(Editor.ApplyParagraphChanges(whole, ParagraphChanges.Alignment((ParagraphJustification)(1 + random.Next(3)))), label + " (update) alignment");
        await CheckAsync(Editor.ToggleCharacter(whole, CharacterToggle.Bold), label + " (update) bold");
        whole = new TextRange(At(paragraph, 0), At(paragraph, Len(paragraph)));
        if (Editor.UpdateStyleToMatch(whole) is { } updated)
        {
            await CheckAsync(updated, label + " update style");
            await CheckAsync(Editor.History.Undo(), label + " undo update style");
            await CheckAsync(Editor.History.Redo(), label + " redo update style");
        }
    }

    private async Task CheckAsync(EditResult? result, string label)
    {
        if (result is null || (result.Changes.Count == 0 && result.Style is null))
        {
            return;
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
                _layout.ApplyEdit(change, 2);
            }
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        Compared++;
    }

    // The editor's format resolver snapshots the styles: made again after every style change.
    private sealed class RefreshableFormat(WordDocument model) : IEffectiveFormat
    {
        private LayoutEffectiveFormat _inner = new(model);

        public void Invalidate() => _inner = new LayoutEffectiveFormat(model);

        public EffectiveRun ForRun(StoryRef story, Paragraph paragraph, Run run) => _inner.ForRun(story, paragraph, run);

        public EffectiveRun ForMark(StoryRef story, Paragraph paragraph) => _inner.ForMark(story, paragraph);
    }
}
