using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using WriteCorpus;
using Xunit;

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
    private int _kind;

    public FormattingSteps(WordDocument model, DocumentLayout layout, LayoutOptions options)
    {
        _model = model;
        _layout = layout;
        _options = options;
        _format = new RefreshableFormat(model);
        Editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p), format: _format);
    }

    public TextEditor Editor { get; }

    /// <summary>For each kind, how many of its steps changed something and were compared (a no-op step counts nowhere).</summary>
    public int[] RanPerKind { get; } = new int[Kinds];

    /// <summary>How many steps changed something and were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many of them were restyles.</summary>
    public int Restyles { get; private set; }

    /// <summary>How many steps used a range that reaches into a table or a block content control.</summary>
    public int ReachedInto { get; private set; }

    /// <summary>
    /// The corpus-wide guard, per view (every file but the thesis, which the per-file gates cover): every kind ran a step that changed something somewhere, and some
    /// step restyled the layout. A guard that only the per-file runs carry would stay green if a kind were skipped everywhere.
    /// </summary>
    public static async Task AssertCorpusAsync(string root, LayoutMode mode)
    {
        var totals = new int[Kinds];
        var restyles = 0;
        foreach (var entry in CorpusManifest.Load(root).Files.Where(f => f.Id != "thesis"))
        {
            using var file = DocxFile.Open(Path.Combine(root, entry.Id, entry.Id + ".docx"));
            var layout = DocumentLayout.Create(file.Model, PageTestKit.Fonts, new LayoutOptions(mode));
            await layout.LayOutRestAsync(null, CancellationToken.None);
            var steps = new FormattingSteps(file.Model, layout, new LayoutOptions(mode));
            await steps.RunAllAsync(entry.Id.Length * 7919 + (int)mode, $"{entry.Id} {mode}");
            for (var k = 0; k < Kinds; k++)
            {
                totals[k] += steps.RanPerKind[k];
            }

            restyles += steps.Restyles;
        }

        for (var k = 0; k < Kinds; k++)
        {
            Assert.True(totals[k] > 0, $"{mode}: formatting kind {k} never ran a step that changed something in the corpus");
        }

        Assert.True(restyles > 0, $"{mode}: no formatting step restyled the layout anywhere in the corpus");
    }

    /// <summary>Runs every kind once with a random generator seeded from <paramref name="seed"/> (the same picks for the same seed).</summary>
    public async Task RunAllAsync(int seed, string label)
    {
        var random = new Random(unchecked(seed * 31 + 17));
        for (var kind = 0; kind < Kinds; kind++)
        {
            await RunAsync(kind, random, $"{label} formatting kind {kind}");
        }
    }

    /// <summary>
    /// Fails when a kind never ran a step that changed something, or no step restyled. A file has what each kind needs when it has a paragraph, a paragraph style a
    /// paragraph uses, or a table or control to reach into (<see cref="ReachingCandidates"/>); for the others the guard asks for nothing.
    /// </summary>
    public void AssertEveryKindRan(string label)
    {
        if (TestEdit.Paragraphs(_model).Any(p => Len(p.Paragraph) > 0))
        {
            Assert.True(RanPerKind[0] > 0, label + ": no character toggle changed anything");
        }

        Assert.True(RanPerKind[1] > 0, label + ": no alignment changed anything");
        if (HasUsedParagraphStyle())
        {
            Assert.True(RanPerKind[2] > 0, label + ": no paragraph style changed anything");
            Assert.True(Restyles > 0, label + ": no update-style step ran and restyled");
        }

        if (ReachingCandidates(_model).Count > 0)
        {
            Assert.True(RanPerKind[4] > 0, label + ": no step reached into a table or content control");
        }
    }

    // A paragraph that uses a paragraph style the file defines (or the file's default one).
    private bool HasUsedParagraphStyle() =>
        _model.Styles is { } sheet && TestEdit.Paragraphs(_model).Any(p => UsedStyle(sheet, p.Paragraph) is not null);

    private static Style? UsedStyle(StyleSheet sheet, Paragraph paragraph) =>
        paragraph.Properties.StyleId is { } id ? sheet.Find(id) : sheet.Styles.FirstOrDefault(s => s.IsDefault && s.Type == "paragraph");

    /// <summary>Runs one formatting step: 0 toggle, 1 alignment, 2 paragraph style, 3 update style (and undo, redo), 4 formatting that reaches into tables and controls.</summary>
    public async Task RunAsync(int kind, Random random, string label)
    {
        if (TestEdit.Paragraphs(_model).Count == 0)
        {
            return;
        }

        _kind = kind;
        switch (kind)
        {
            case 0:
                await CheckAsync(Editor.ToggleCharacter(RandomRange(random), (CharacterToggle)random.Next(8)), label + " toggle");
                break;
            case 1:
                await AlignAsync(RandomRange(random), random, label + " alignment");
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

    // A range inside one list of blocks (the editor refuses a range across containers as no change), over paragraphs that have text when the file has any,
    // and never collapsed when it starts in text: a step that cannot change anything would prove nothing.
    private TextRange RandomRange(Random random)
    {
        var all = TestEdit.Paragraphs(_model);
        var withText = all.Where(p => Len(p.Paragraph) > 0).ToList();
        var pool = withText.Count > 0 ? withText : all;
        var (a, container, index) = pool[random.Next(pool.Count)];
        var lastIndex = random.Next(3) == 0 ? Math.Min(container.Count - 1, index + 1 + random.Next(4)) : index;
        while (lastIndex > index && container[lastIndex] is not Paragraph)
        {
            lastIndex--;
        }

        var b = (Paragraph)container[lastIndex];
        var length = Len(a);
        if (ReferenceEquals(a, b))
        {
            var from = length == 0 ? 0 : random.Next(length);
            return new TextRange(At(a, from), At(a, length == 0 ? 0 : Math.Min(length, from + 1 + random.Next(40))));
        }

        return new TextRange(At(a, random.Next(length + 1)), At(b, random.Next(Len(b) + 1)));
    }

    /// <summary>A paragraph alignment that differs from the current one: the next one is tried when an edit changes nothing.</summary>
    private async Task AlignAsync(TextRange range, Random random, string label)
    {
        var start = random.Next(4);
        for (var i = 0; i < 4; i++)
        {
            var result = Editor.ApplyParagraphChanges(range, ParagraphChanges.Alignment((ParagraphJustification)((start + i) % 4)));
            if (result.Changes.Count > 0)
            {
                await CheckAsync(result, label);
                return;
            }
        }
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
        var defined = _model.Styles is { } sheet ? all.Where(p => UsedStyle(sheet, p.Paragraph) is not null).ToList() : [];
        var paragraph = (defined.Count > 0 ? defined : all)[random.Next(defined.Count > 0 ? defined.Count : all.Count)].Paragraph;
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
        RanPerKind[_kind]++;
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
