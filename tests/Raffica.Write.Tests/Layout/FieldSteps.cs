using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The kinds of field step of the oracle gate (milestone 4.5b); later tasks add the field kinds.</summary>
internal enum FieldStepKind
{
    /// <summary>A bookmark added over a random range of one paragraph.</summary>
    Bookmark,

    /// <summary>A delete of a range that is often exactly a bookmark's range (the whole-range rule removes its markers).</summary>
    DeleteRange,

    /// <summary>A bookmark removed through its name.</summary>
    DeleteBookmark,
    Undo,
    Redo,

    /// <summary>A field refresh over the whole body (SEQ, STYLEREF, REF, NOTEREF): the layout must equal a full layout and every result must equal the independent count.</summary>
    Refresh,

    /// <summary>A field refresh given only the block changes of the steps since the last refresh, as the session passes them (the whole body after a style or section step).</summary>
    RefreshTouched,

    /// <summary>Typing at a random place inside a table of contents (the multi-paragraph field of a document that has one): the editor must refuse with its reason and change nothing. Always a counted refusal; a document with no table of contents has nothing for it.</summary>
    TypeInToc,
}

/// <summary>
/// The field steps of the oracle gate: each step is made by the real <see cref="TextEditor"/>, brought into the layout as the session does (<see cref="DocumentLayout.ApplyEdit"/>) and compared, exactly, with a full layout; after
/// every step the bookmarks are checked (every start has exactly one end of its id, ids and names are unique). A step the editor refuses (null or no change) is a counted refusal and is compared nowhere, except an undo and a
/// redo with something on the stack, which must always change something.
/// </summary>
internal sealed class FieldSteps
{
    /// <summary>The number of kinds.</summary>
    public static readonly int Kinds = Enum.GetValues<FieldStepKind>().Length;

    private static readonly FieldStepKind[] s_forcedOrder =
    [
        FieldStepKind.Bookmark,
        FieldStepKind.Bookmark,
        FieldStepKind.Bookmark,
        FieldStepKind.Refresh,
        FieldStepKind.DeleteRange,
        FieldStepKind.RefreshTouched,
        FieldStepKind.Undo,
        FieldStepKind.RefreshTouched,
        FieldStepKind.Redo,
        FieldStepKind.RefreshTouched,
        FieldStepKind.DeleteBookmark,
        FieldStepKind.RefreshTouched,
        FieldStepKind.Undo,
        FieldStepKind.Undo,
        FieldStepKind.Refresh,
        FieldStepKind.Undo,
        FieldStepKind.TypeInToc,
    ];

    private readonly WordDocument _model;
    private readonly DocumentLayout _layout;
    private readonly LayoutOptions _options;
    private FieldStepKind _kind;
    private int _names;

    // The block changes of the steps since the last refresh; the whole body is pending at the start (the stored results are stale) and after a step that names no blocks (style, sections).
    private readonly List<BlockChange> _pending = [];
    private bool _everythingPending = true;

    public FieldSteps(WordDocument model, DocumentLayout layout, LayoutOptions options)
    {
        _model = model;
        _layout = layout;
        _options = options;
        Editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
    }

    public TextEditor Editor { get; }

    /// <summary>For each kind, how many of its steps changed something and were compared.</summary>
    public int[] RanPerKind { get; } = new int[Kinds];

    /// <summary>For each kind, how many attempts the editor refused (null or no change) or the document had nothing for.</summary>
    public int[] RefusedPerKind { get; } = new int[Kinds];

    /// <summary>How many steps were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many field results the refresh steps wrote.</summary>
    public int Written { get; private set; }

    /// <summary>How many bookmarks a whole-range delete took away with their text.</summary>
    public int RemovedByDelete { get; private set; }

    /// <summary>One of every kind in a fixed order, then <paramref name="steps"/> random steps from <paramref name="random"/>. Returns how many steps were compared.</summary>
    public async Task<int> RunAsync(Random random, int steps, string label)
    {
        var before = Compared;
        foreach (var kind in s_forcedOrder)
        {
            await RunKindAsync(kind, random, $"{label} forced {kind}");
        }

        for (var i = 0; i < steps; i++)
        {
            var kind = (FieldStepKind)random.Next(Kinds);
            await RunKindAsync(kind, random, $"{label} step {i} {kind}");
        }

        return Compared - before;
    }

    /// <summary>Runs one step of a kind. Returns true when it changed something and was compared.</summary>
    public async Task<bool> RunKindAsync(FieldStepKind kind, Random random, string label)
    {
        _kind = kind;
        return kind switch
        {
            FieldStepKind.Bookmark => await AddAsync(random, label),
            FieldStepKind.DeleteRange => await DeleteRangeAsync(random, label),
            FieldStepKind.DeleteBookmark => await DeleteBookmarkAsync(random, label),
            FieldStepKind.Refresh => await RefreshAsync(label, touchedOnly: false),
            FieldStepKind.RefreshTouched => await RefreshAsync(label, touchedOnly: true),
            FieldStepKind.TypeInToc => TypeInToc(random),
            FieldStepKind.Undo => Editor.History.CanUndo ? await CheckAsync(Editor.History.Undo(), label, required: true) : Refuse(),
            _ => Editor.History.CanRedo ? await CheckAsync(Editor.History.Redo(), label, required: true) : Refuse(),
        };
    }

    private List<Paragraph> Paragraphs() => [.. BookmarkOperations.ParagraphsOf(_model.Body.Blocks)];

    private int TextLength(Paragraph paragraph) => ParagraphProjection.Build(paragraph, _layout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;

    private async Task<bool> AddAsync(Random random, string label)
    {
        var paragraphs = Paragraphs();
        if (paragraphs.Count == 0)
        {
            return Refuse();
        }

        var paragraph = paragraphs[random.Next(paragraphs.Count)];
        var length = TextLength(paragraph);
        var a = random.Next(length + 1);
        var b = random.Next(4) == 0 ? a : random.Next(length + 1);
        var range = new TextRange(new TextPosition(StoryRef.Body, paragraph, Math.Min(a, b)), new TextPosition(StoryRef.Body, paragraph, Math.Max(a, b)));
        return await CheckAsync(Editor.AddBookmark(range, "Mark" + _names++), label, required: false);
    }

    private async Task<bool> DeleteRangeAsync(Random random, string label)
    {
        var shown = Editor.VisibleBookmarks().Where(b => ReferenceEquals(b.Range.Start.Paragraph, b.Range.End.Paragraph) && b.Range.End.Offset > b.Range.Start.Offset).ToList();
        TextRange range;
        if (shown.Count > 0 && random.Next(3) != 0)
        {
            range = shown[random.Next(shown.Count)].Range;
        }
        else
        {
            var paragraphs = Paragraphs();
            if (paragraphs.Count == 0)
            {
                return Refuse();
            }

            var paragraph = paragraphs[random.Next(paragraphs.Count)];
            var length = TextLength(paragraph);
            var a = random.Next(length + 1);
            var b = random.Next(length + 1);
            range = new TextRange(new TextPosition(StoryRef.Body, paragraph, Math.Min(a, b)), new TextPosition(StoryRef.Body, paragraph, Math.Max(a, b)));
        }

        var count = Editor.VisibleBookmarks().Count;
        var done = await CheckAsync(Editor.Delete(range), label, required: false);
        if (done)
        {
            RemovedByDelete += Math.Max(0, count - Editor.VisibleBookmarks().Count);
        }

        return done;
    }

    private async Task<bool> DeleteBookmarkAsync(Random random, string label)
    {
        var shown = Editor.VisibleBookmarks();
        return shown.Count == 0 ? Refuse() : await CheckAsync(Editor.DeleteBookmark(shown[random.Next(shown.Count)].Name), label, required: false);
    }

    private async Task<bool> CheckAsync(EditResult? result, string label, bool required)
    {
        if (result is null || (result.Changes.Count == 0 && result.Style is null && result.Sections is null))
        {
            if (required)
            {
                Assert.Fail(label + ": the step returned no change");
            }

            return Refuse();
        }

        if (result.Style is not null || result.Sections is not null)
        {
            _everythingPending = true;
        }
        else
        {
            _pending.AddRange(result.Changes);
        }

        if (result.Style is not null || result.Sections is not null)
        {
            _layout.Restyle(2);
        }
        else
        {
            foreach (var change in result.Changes)
            {
                _layout.ApplyEdit(change, change.Container == _model.Body.Blocks ? Math.Max(1, (_layout.Locate(Math.Min(change.First, Math.Max(0, _model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1) : 2);
            }
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        AssertBookmarks(label);
        Compared++;
        RanPerKind[(int)_kind]++;
        return true;
    }

    // A refresh is brought into the layout as the session does; whether or not it wrote, the layout must equal a full layout and every result the oracle has an answer for must be stored; a second refresh finds nothing.
    private async Task<bool> RefreshAsync(string label, bool touchedOnly)
    {
        var host = new FakeFieldHost(_model);
        var result = Editor.RefreshFields(host, RefreshScope.OrderBased, touchedOnly && !_everythingPending ? [.. _pending] : null);
        _pending.Clear();
        _everythingPending = false;
        foreach (var change in result.Content)
        {
            _layout.ApplyEdit(change, Math.Max(1, (_layout.Locate(Math.Min(change.First, Math.Max(0, _model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1));
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        AssertBookmarks(label);
        ReferenceFields.AssertStoredEqualsExpected(_model, host, label);
        Assert.Empty(Editor.RefreshFields(host, RefreshScope.OrderBased).Content);
        Written += result.Written;
        Compared++;
        RanPerKind[(int)_kind]++;
        return true;
    }

    // A refused edit inside a table of contents is a counted refusal like any other, and it must have left the document alone and named its reason.
    private bool TypeInToc(Random random)
    {
        if (TocRanges.In(_model.Body.Blocks) is not { } ranges)
        {
            return Refuse();
        }

        var toc = ranges[random.Next(ranges.Count)];
        var paragraph = (Paragraph)_model.Body.Blocks[random.Next(toc.First, toc.Last + 1)];
        var at = new TextPosition(StoryRef.Body, paragraph, random.Next(TextLength(paragraph) + 1));
        var revision = _model.Edits.Revision;
        var result = Editor.InsertText(new TextRange(at, at), "x");
        Assert.Empty(result.Changes);
        Assert.Equal(revision, _model.Edits.Revision);
        Assert.Equal(FieldRefusalKind.InsideToc, Editor.LastFieldRefusal?.Kind);
        return Refuse();
    }

    private bool Refuse()
    {
        RefusedPerKind[(int)_kind]++;
        return false;
    }

    // Every bookmark start has exactly one end of its id and the ids and names of the ones Write made are unique.
    private void AssertBookmarks(string label)
    {
        var markers = BookmarkOperations.ParagraphsOf(_model.Body.Blocks).SelectMany(p => BookmarkOperations.InlinesOf(p.Inlines)).ToList();
        var starts = markers.OfType<BookmarkStart>().ToList();
        var ends = markers.OfType<BookmarkEnd>().ToList();
        foreach (var made in starts.Where(s => s.Name.StartsWith("Mark", StringComparison.Ordinal)))
        {
            Assert.True(ends.Count(e => e.Id == made.Id) == 1, $"{label}: the bookmark {made.Name} has {ends.Count(e => e.Id == made.Id)} ends");
            Assert.True(starts.Count(s => s.Id == made.Id) == 1, $"{label}: the id {made.Id} is used by more than one bookmark");
        }
    }
}
