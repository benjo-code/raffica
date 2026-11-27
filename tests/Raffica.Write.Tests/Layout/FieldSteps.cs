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

    /// <summary>A caption (Figure or Table, above or below) at a random paragraph, followed by a refresh: the layout must equal a full layout and every number must equal the independent count.</summary>
    InsertCaption,

    /// <summary>A caption with a chapter number (STYLEREF and SEQ with a restart per chapter); refused by a document with no numbered headings.</summary>
    InsertChapterCaption,

    /// <summary>The paragraph of a caption deleted (with its paragraph break), followed by a refresh.</summary>
    DeleteCaption,

    /// <summary>The paragraph of a caption moved to another place of the body, followed by a refresh.</summary>
    MoveCaption,

    /// <summary>A cross-reference of a random choice to a random target (heading, caption, note or bookmark) at a random place, after a refresh that makes every stored result right: the new field must store the independent count's answer before any refresh runs, the layout must equal a full layout and a refresh then finds nothing.</summary>
    InsertCrossReference,

    /// <summary>The text of a hidden reference bookmark deleted whole (the whole-range rule removes the bookmark): the fields that point at it keep their stored result.</summary>
    DeleteTarget,

    /// <summary>A table of contents (levels, page numbers, right alignment and leader chosen at random) at a random paragraph: before any refresh runs it must list what the independent walk expects; the refresh that follows finds the same.</summary>
    InsertToc,

    /// <summary>A list of figures or tables (a label of the document) at a random paragraph, judged like a table of contents.</summary>
    InsertFigureList,

    /// <summary>A table of contents removed whole (field, entries and the hidden bookmarks nothing points at).</summary>
    RemoveToc,

    /// <summary>A letter typed into a random heading, then a refresh over the touched blocks: the entries follow.</summary>
    EditHeading,

    /// <summary>A paragraph made a heading, then a refresh: a table of contents gains an entry (a structural change the newest step carries).</summary>
    AddHeading,

    /// <summary>A heading deleted with its paragraph break, then a refresh: a table of contents loses an entry.</summary>
    DeleteHeading,

    /// <summary>A heading and the blocks under it moved to another place of the body, then a refresh: the entries follow the new order.</summary>
    MoveChapter,

    /// <summary>A letter or a long text typed into a random paragraph after everything is settled: the page pass is skipped when nothing can have moved a page result, and the independent count must still hold (see <c>TypeSkippingPagesAsync</c>).</summary>
    TypeSkippingPages,
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
        FieldStepKind.InsertCaption,
        FieldStepKind.InsertCaption,
        FieldStepKind.InsertChapterCaption,
        FieldStepKind.MoveCaption,
        FieldStepKind.DeleteCaption,
        FieldStepKind.Undo,
        FieldStepKind.Redo,
        FieldStepKind.InsertCaption,
        FieldStepKind.InsertCrossReference,
        FieldStepKind.InsertCrossReference,
        FieldStepKind.InsertCrossReference,
        FieldStepKind.InsertCrossReference,
        FieldStepKind.DeleteTarget,
        FieldStepKind.Undo,
        FieldStepKind.Redo,
        FieldStepKind.InsertCrossReference,
        FieldStepKind.TypeInToc,
        FieldStepKind.InsertToc,
        FieldStepKind.Undo,
        FieldStepKind.Redo,
        FieldStepKind.AddHeading,
        FieldStepKind.Undo,
        FieldStepKind.Redo,
        FieldStepKind.EditHeading,
        FieldStepKind.MoveChapter,
        FieldStepKind.DeleteHeading,
        FieldStepKind.Undo,
        FieldStepKind.Redo,
        FieldStepKind.InsertFigureList,
        FieldStepKind.RemoveToc,
        FieldStepKind.Undo,
        FieldStepKind.Redo,
        FieldStepKind.InsertToc,
        FieldStepKind.AddHeading,
        FieldStepKind.TypeSkippingPages,
        FieldStepKind.TypeSkippingPages,
        FieldStepKind.TypeSkippingPages,
    ];

    private readonly WordDocument _model;
    private readonly DocumentLayout _layout;
    private readonly LayoutOptions _options;
    private FieldStepKind _kind;
    private int _names;

    // After a table of contents step (the table was right before it and a refresh followed it) the undo and the redo of that very step must put everything back: no structural refresh may follow either.
    private bool _strictUndo;
    private bool _strictRedo;

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

    /// <summary>The most passes of one refresh step (the runner stops at 3; the oracle allows more so that a document that needs them fails here with its label and not silently).</summary>
    public const int MaxSettlePasses = 6;

    /// <summary>How many refresh passes the steps ran in all.</summary>
    public int SettlePasses { get; private set; }

    /// <summary>How many typing steps ran without a page pass because nothing could have moved a page result.</summary>
    public int PagePassesSkipped { get; private set; }

    /// <summary>How many typing steps needed the page loop (the page table moved or a touched block carried something a page result points at).</summary>
    public int PagePassesRun { get; private set; }

    /// <summary>How many page results (PAGEREF fields and table of contents entries) were compared with the independent page count.</summary>
    public int PageResultsCompared { get; private set; }

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
        if (kind is not (FieldStepKind.Undo or FieldStepKind.Redo))
        {
            _strictUndo = false;
            _strictRedo = false;
        }

        return kind switch
        {
            FieldStepKind.Bookmark => await AddAsync(random, label),
            FieldStepKind.DeleteRange => await DeleteRangeAsync(random, label),
            FieldStepKind.DeleteBookmark => await DeleteBookmarkAsync(random, label),
            FieldStepKind.Refresh => await RefreshAsync(label, touchedOnly: false),
            FieldStepKind.RefreshTouched => await RefreshAsync(label, touchedOnly: true),
            FieldStepKind.TypeInToc => TypeInToc(random),
            FieldStepKind.InsertCaption => await InsertCaptionAsync(random, chapter: false, label),
            FieldStepKind.InsertChapterCaption => await InsertCaptionAsync(random, chapter: true, label),
            FieldStepKind.DeleteCaption => await DeleteCaptionAsync(random, label),
            FieldStepKind.MoveCaption => await MoveCaptionAsync(random, label),
            FieldStepKind.InsertCrossReference => await InsertCrossReferenceAsync(random, label),
            FieldStepKind.DeleteTarget => await DeleteTargetAsync(random, label),
            FieldStepKind.InsertToc => await InsertTocAsync(random, figures: false, label),
            FieldStepKind.InsertFigureList => await InsertTocAsync(random, figures: true, label),
            FieldStepKind.RemoveToc => await RemoveTocAsync(random, label),
            FieldStepKind.EditHeading => await EditHeadingAsync(random, label),
            FieldStepKind.AddHeading => await AddHeadingAsync(random, label),
            FieldStepKind.DeleteHeading => await DeleteHeadingAsync(random, label),
            FieldStepKind.MoveChapter => await MoveChapterAsync(random, label),
            FieldStepKind.TypeSkippingPages => await TypeSkippingPagesAsync(random, label),
            FieldStepKind.Undo => Editor.History.CanUndo ? await UndoRedoAsync(undo: true, label) : Refuse(),
            _ => Editor.History.CanRedo ? await UndoRedoAsync(undo: false, label) : Refuse(),
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

    // A caption at a random place: the number it carries must already be the one a refresh gives (the refresh that follows must find nothing wrong with it), and the layout must equal a full layout.
    private async Task<bool> InsertCaptionAsync(Random random, bool chapter, string label)
    {
        var paragraphs = Paragraphs();
        if (paragraphs.Count == 0)
        {
            return Refuse();
        }

        var paragraph = paragraphs[random.Next(paragraphs.Count)];
        var word = random.Next(3) == 0 ? "Table" : "Figure";
        var place = random.Next(2) == 0 ? CaptionPlace.Above : CaptionPlace.Below;
        var at = new TextPosition(StoryRef.Body, paragraph, random.Next(TextLength(paragraph) + 1));
        var host = new FakeFieldHost(_model);
        var result = Editor.InsertCaption(at, word, ": caption " + _names++, place, chapter, word, host);
        if (!await CheckAsync(result, label, required: false))
        {
            return false;
        }

        // Before the refresh heals anything: the new paragraph must already show the number the independent count gives.
        AssertNewCaptionIsRight(result!.Caret.Paragraph, host, label);
        return await RefreshAsync(label + " refresh", touchedOnly: true);
    }

    // Every field of the new caption that the independent count has an answer for stores that answer at once (a wrong number at insert time is not healed by the refresh that follows).
    private void AssertNewCaptionIsRight(Paragraph caption, FakeFieldHost host, string label)
    {
        var checkedFields = 0;
        foreach (var field in ReferenceFields.Expected(_model, host).Where(f => ReferenceEquals(f.Paragraph, caption) && f.Text is not null))
        {
            Assert.True(string.Equals(field.Text, ReferenceFields.Stored(caption, field.Ordinal), StringComparison.Ordinal), $"{label}: a new caption stores another result than the count gives (expected \"{field.Text}\", stored \"{ReferenceFields.Stored(caption, field.Ordinal)}\", {field.Instruction}, para \"{ParagraphProjection.Build(caption, ProjectionHost.Plain).Text}\")");
            checkedFields++;
        }

        Assert.True(checkedFields > 0, label + ": the independent count has no answer for the new caption");
    }

    // The paragraphs that hold a SEQ field of their own (a caption), with the list of blocks that holds each.
    private List<(Paragraph Paragraph, IList<Block> Container, int Index)> Captions()
    {
        var found = new List<(Paragraph, IList<Block>, int)>();
        foreach (var paragraph in Paragraphs())
        {
            if (FieldRefresh.HasFieldMarkup(paragraph) && FieldView.InParagraph(paragraph).Any(f => f.Parent is null && f.Support.Kind == FieldKind.Seq) && TextEditor.FindIn(_model.Body.Blocks, paragraph) is { } place)
            {
                found.Add((paragraph, place.Container, place.Index));
            }
        }

        return found;
    }

    // A cross-reference: the stored results are made right first (so the new field's number is judged against an exact count, not a stale one), the reference is made, and BEFORE the refresh heals anything every result the independent count
    // has an answer for in the paragraph of the new field must already be stored. A page reference has no answer in the count; it must be there and be a link.
    private async Task<bool> InsertCrossReferenceAsync(Random random, string label)
    {
        await RefreshAsync(label + " settle", touchedOnly: false);
        var host = new LayoutFieldHost(_model, _layout);
        var kinds = Enum.GetValues<CrossRefTargetKind>().Where(k => CrossReferences.Targets(_model, host, k).Count > 0).ToList();
        var paragraphs = Paragraphs();
        if (kinds.Count == 0 || paragraphs.Count == 0)
        {
            return Refuse();
        }

        var targets = CrossReferences.Targets(_model, host, kinds[random.Next(kinds.Count)]);
        var target = targets[random.Next(targets.Count)];
        var choices = CrossReferences.Offered(target.Kind);
        var choice = choices[random.Next(choices.Count)];
        var paragraph = paragraphs[random.Next(paragraphs.Count)];
        var at = new TextPosition(StoryRef.Body, paragraph, random.Next(TextLength(paragraph) + 1));
        var before = FieldView.InParagraph(paragraph).Count(f => f.Parent is null);
        var targetPage = target.Position.Paragraph is { } targetParagraph ? ReferencePageResults.PageTextOf(_layout, _model)(targetParagraph) : null;
        var result = Editor.InsertCrossReference(at, target, choice, host);
        if (!await CheckAsync(result, label, required: false))
        {
            return false;
        }

        var made = result!.Caret.Paragraph;
        var fields = FieldView.InParagraph(made).Where(f => f.Parent is null).ToList();
        Assert.True(fields.Count == before + 1, $"{label}: the paragraph of a new cross-reference holds {fields.Count} fields, not {before + 1}");
        Assert.True(fields.Any(f => f.Support.IsSupported && f.Instruction.Has("h") && f.Support.Kind is FieldKind.Ref or FieldKind.NoteRef or FieldKind.PageRef), label + ": no supported link field in the paragraph of the new cross-reference");
        var checkedFields = 0;
        foreach (var field in ReferenceFields.Expected(_model, host).Where(f => ReferenceEquals(f.Paragraph, made) && f.Text is not null))
        {
            Assert.True(string.Equals(field.Text, ReferenceFields.Stored(made, field.Ordinal), StringComparison.Ordinal), $"{label}: a new cross-reference stores another result than the count gives (expected \"{field.Text}\", stored \"{ReferenceFields.Stored(made, field.Ordinal)}\", {field.Instruction}, {choice} of {target.Kind})");
            checkedFields++;
        }

        if (choice != CrossRefKind.Page)
        {
            Assert.True(checkedFields > 0, $"{label}: the independent count has no answer for the new {choice} reference to a {target.Kind}");
        }
        else if (targetPage is not null && target.Position.Paragraph is { } pageTarget)
        {
            // The page the insert stored is the page the independent count gives for the layout the host saw (before the refresh that follows could heal anything).
            AssertInsertedPageIsRight(made, pageTarget, targetPage, label);
        }

        return await RefreshAsync(label + " refresh", touchedOnly: true);
    }

    // Every PAGEREF of the new paragraph that points at the target stores the expected page text.
    private void AssertInsertedPageIsRight(Paragraph made, Paragraph target, string expected, string label)
    {
        var starts = new Dictionary<string, Paragraph>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in Paragraphs())
        {
            foreach (var start in ModelWalk.Inlines(paragraph.Inlines).OfType<BookmarkStart>())
            {
                starts.TryAdd(start.Name, paragraph);
            }
        }

        var seen = 0;
        var fields = FieldView.InParagraph(made);
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Parent is null && fields[i].Support.Kind == FieldKind.PageRef && fields[i].Instruction.Argument is { } name && starts.TryGetValue(name, out var owner) && ReferenceEquals(owner, target))
            {
                Assert.True(string.Equals(expected, ReferenceFields.Stored(made, i), StringComparison.Ordinal), $"{label}: a new page cross-reference stores another page than the count gives (expected \"{expected}\", stored \"{ReferenceFields.Stored(made, i)}\")");
                seen++;
            }
        }

        Assert.True(seen > 0, label + ": the new page cross-reference is not in the paragraph the insert returned");
    }

    // The text of a hidden reference bookmark is deleted whole: the bookmark goes, and the fields that pointed at it keep the result they stored (the count has no answer for them, so the refresh must leave them alone).
    private async Task<bool> DeleteTargetAsync(Random random, string label)
    {
        var marks = BookmarkOperations.Scan(_model, (s, p) => _layout.ProjectionHost(s, p))
            .Where(f => f.Name.StartsWith("_Ref", StringComparison.Ordinal) && f.EndParagraph is not null && ReferenceEquals(f.StartParagraph, f.EndParagraph) && f.EndOffset > f.StartOffset)
            .ToList();
        if (marks.Count == 0)
        {
            return Refuse();
        }

        var mark = marks[random.Next(marks.Count)];
        var kept = new List<(Paragraph Paragraph, int Ordinal, string? Stored)>();
        foreach (var paragraph in Paragraphs())
        {
            var all = FieldView.InParagraph(paragraph);
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Parent is null && all[i].Support.Kind is FieldKind.Ref or FieldKind.NoteRef or FieldKind.PageRef && string.Equals(all[i].Instruction.Argument, mark.Name, StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add((paragraph, i, ReferenceFields.Stored(paragraph, i)));
                }
            }
        }

        var range = new TextRange(new TextPosition(StoryRef.Body, mark.StartParagraph, mark.StartOffset), new TextPosition(StoryRef.Body, mark.EndParagraph!, mark.EndOffset));
        if (!await CheckAsync(Editor.Delete(range), label, required: false))
        {
            return false;
        }

        Assert.DoesNotContain(mark.Name, BookmarkOperations.AllNames(_model));
        var done = await RefreshAsync(label + " refresh", touchedOnly: true);
        foreach (var (paragraph, ordinal, stored) in kept)
        {
            if (BookmarkOperations.ParagraphsOf(_model.Body.Blocks).Any(p => ReferenceEquals(p, paragraph)) && FieldView.InParagraph(paragraph).Count > ordinal && string.Equals(FieldView.InParagraph(paragraph)[ordinal].Instruction.Argument, mark.Name, StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(string.Equals(stored, ReferenceFields.Stored(paragraph, ordinal), StringComparison.Ordinal), label + ": a field whose bookmark was deleted did not keep its stored result");
            }
        }

        return done;
    }

    // A caption's paragraph deleted with its paragraph break (the range runs from the end of the paragraph before it); a caption that has no paragraph of the same list before it is emptied.
    private async Task<bool> DeleteCaptionAsync(Random random, string label)
    {
        var captions = Captions();
        if (captions.Count == 0)
        {
            return Refuse();
        }

        var (caption, container, index) = captions[random.Next(captions.Count)];
        var end = new TextPosition(StoryRef.Body, caption, TextLength(caption));
        var start = index > 0 && container[index - 1] is Paragraph before ? new TextPosition(StoryRef.Body, before, TextLength(before)) : new TextPosition(StoryRef.Body, caption, 0);
        return await CheckAsync(Editor.Delete(new TextRange(start, end)), label, required: false) && await RefreshAsync(label + " refresh", touchedOnly: true);
    }

    // A caption of the top level of the body moved to another place of it.
    private async Task<bool> MoveCaptionAsync(Random random, string label)
    {
        var body = _model.Body.Blocks;
        var captions = Captions().Where(c => ReferenceEquals(c.Container, body)).ToList();
        if (captions.Count == 0 || body.Count < 2)
        {
            return Refuse();
        }

        var (_, _, index) = captions[random.Next(captions.Count)];
        var target = random.Next(body.Count + 1);
        if (target == index || target == index + 1)
        {
            return Refuse();
        }

        return await CheckAsync(Editor.MoveBlocks(index, 1, target), label, required: false) && await RefreshAsync(label + " refresh", touchedOnly: true);
    }

    // ---- tables of contents ------------------------------------------------------------------------------------------------------------------

    // Whether the last refresh left a table of contents alone because a redo was pending: the lists are then as stale as they were, and the step after it proves nothing about an undo.
    private bool _lastRefreshDeferred;

    private HashSet<Paragraph> TocParagraphs()
    {
        var set = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var site in TocRefresh.Find(_model.Body.Blocks))
        {
            for (var i = site.First; i <= site.Last; i++)
            {
                if (site.Container[i] is Paragraph paragraph)
                {
                    set.Add(paragraph);
                }
            }
        }

        return set;
    }

    private List<Paragraph> Headings()
    {
        var inside = TocParagraphs();
        return [.. Paragraphs().Where(p => FieldRefresh.HeadingLevel(p) is not null && !inside.Contains(p))];
    }

    // Brings every list right first, so that the step after it is the only change; false when the refresh had to wait for a redo (then nothing about the step can be judged by an undo).
    private async Task<bool> SettleAsync(string label)
    {
        await RefreshAsync(label + " settle", touchedOnly: false);
        return !_lastRefreshDeferred;
    }

    // A table of contents at a random paragraph: before any refresh heals anything it must list what the independent walk expects.
    private async Task<bool> InsertTocAsync(Random random, bool figures, string label)
    {
        var fresh = await SettleAsync(label);
        var paragraphs = Paragraphs();
        if (paragraphs.Count == 0)
        {
            return Refuse();
        }

        var host = new FakeFieldHost(_model);
        var paragraph = paragraphs[random.Next(paragraphs.Count)];
        var at = new TextPosition(StoryRef.Body, paragraph, random.Next(TextLength(paragraph) + 1));
        var first = random.Next(1, 4);
        var last = Math.Max(first, random.Next(1, 10));
        var options = new TocOptions(first, last, random.Next(4) != 0, random.Next(3) != 0, (TocLeader)random.Next(4), figures ? (random.Next(2) == 0 ? "Figure" : "Table") : null);
        // When the settle had to wait for a redo the lists that were there before are stale by design (the refresh left them): only the new list is judged then.
        var stale = fresh ? null : TocRefresh.Find(_model.Body.Blocks).Select(s => s.Container[s.First]).ToHashSet();
        var result = Editor.InsertToc(at, options, host);
        if (!await CheckAsync(result, label, required: false))
        {
            return false;
        }

        ReferenceToc.AssertEqualToExpected(_model, host, label + " (a new list, before the refresh)", notJudged: stale);
        Assert.True(stale is null || TocRefresh.Find(_model.Body.Blocks).Any(s => !stale.Contains(s.Container[s.First])), label + ": the new list is not judged");
        var done = await RefreshAsync(label + " refresh", touchedOnly: false);
        _strictUndo = fresh;
        return done;
    }

    private async Task<bool> RemoveTocAsync(Random random, string label)
    {
        var fresh = await SettleAsync(label);
        var sites = TocRefresh.Find(_model.Body.Blocks);
        if (sites.Count == 0)
        {
            return Refuse();
        }

        var site = sites[random.Next(sites.Count)];
        var at = new TextPosition(StoryRef.Body, (Paragraph)site.Container[site.First], 0);
        if (!await CheckAsync(Editor.RemoveToc(at), label, required: false))
        {
            return false;
        }

        var done = await RefreshAsync(label + " refresh", touchedOnly: true);
        _strictUndo = fresh;
        return done;
    }

    private async Task<bool> EditHeadingAsync(Random random, string label)
    {
        var fresh = await SettleAsync(label);
        var headings = Headings();
        if (headings.Count == 0)
        {
            return Refuse();
        }

        var heading = headings[random.Next(headings.Count)];
        var at = new TextPosition(StoryRef.Body, heading, random.Next(TextLength(heading) + 1));
        if (!await CheckAsync(Editor.InsertText(new TextRange(at, at), "h" + _names++), label, required: false))
        {
            return false;
        }

        var done = await RefreshAsync(label + " refresh", touchedOnly: true);
        _strictUndo = fresh;
        return done;
    }

    // A paragraph with text, not in a table of contents, made a heading of level 1 to 3 (the style must be in the document).
    private async Task<bool> AddHeadingAsync(Random random, string label)
    {
        var fresh = await SettleAsync(label);
        var inside = TocParagraphs();
        var candidates = Paragraphs().Where(p => !inside.Contains(p) && FieldRefresh.HeadingLevel(p) is null && TextLength(p) > 0).ToList();
        var level = random.Next(1, 4);
        if (candidates.Count == 0 || _model.Styles?.Find("Heading" + level) is null)
        {
            return Refuse();
        }

        var paragraph = candidates[random.Next(candidates.Count)];
        var range = new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, TextLength(paragraph)));
        if (!await CheckAsync(Editor.ApplyParagraphStyle(range, "Heading" + level), label, required: false))
        {
            return false;
        }

        var done = await RefreshAsync(label + " refresh", touchedOnly: true);
        _strictUndo = fresh;
        return done;
    }

    // A heading deleted with its paragraph break (the range runs from the end of the paragraph before it, in the same list).
    private async Task<bool> DeleteHeadingAsync(Random random, string label)
    {
        var fresh = await SettleAsync(label);
        var headings = Headings();
        if (headings.Count == 0)
        {
            return Refuse();
        }

        var heading = headings[random.Next(headings.Count)];
        if (TextEditor.FindIn(_model.Body.Blocks, heading) is not { } place)
        {
            return Refuse();
        }

        var end = new TextPosition(StoryRef.Body, heading, TextLength(heading));
        var start = place.Index > 0 && place.Container[place.Index - 1] is Paragraph before ? new TextPosition(StoryRef.Body, before, TextLength(before)) : new TextPosition(StoryRef.Body, heading, 0);
        if (!await CheckAsync(Editor.Delete(new TextRange(start, end)), label, required: false))
        {
            return false;
        }

        var done = await RefreshAsync(label + " refresh", touchedOnly: true);
        _strictUndo = fresh;
        return done;
    }

    // A heading of the top level of the body and the blocks under it (up to the next heading, at most six) moved to another place of the body.
    private async Task<bool> MoveChapterAsync(Random random, string label)
    {
        var fresh = await SettleAsync(label);
        var body = _model.Body.Blocks;
        var starts = Enumerable.Range(0, body.Count).Where(i => body[i] is Paragraph p && FieldRefresh.HeadingLevel(p) is not null).ToList();
        if (starts.Count == 0 || body.Count < 3)
        {
            return Refuse();
        }

        var index = starts[random.Next(starts.Count)];
        var count = 1;
        while (index + count < body.Count && count < 6 && !(body[index + count] is Paragraph next && FieldRefresh.HeadingLevel(next) is not null))
        {
            count++;
        }

        var target = random.Next(body.Count + 1);
        if (target >= index && target <= index + count)
        {
            return Refuse();
        }

        if (!await CheckAsync(Editor.MoveBlocks(index, count, target), label, required: false))
        {
            return false;
        }

        var done = await RefreshAsync(label + " refresh", touchedOnly: true);
        _strictUndo = fresh;
        return done;
    }

    // Undo and redo are steps like the rest; right after a table of contents step (whose lists were right before it) the refresh that follows must find nothing structural to do: the record went back and came again with the step.
    private async Task<bool> UndoRedoAsync(bool undo, string label)
    {
        var strict = undo ? _strictUndo : _strictRedo;
        _strictUndo = false;
        _strictRedo = undo && strict;
        var result = undo ? Editor.History.Undo() : Editor.History.Redo();
        var ok = await CheckAsync(result, label, required: true);
        if (ok && strict)
        {
            await RefreshAsync(label + " refresh", touchedOnly: true, forbidStructural: true);
        }

        return ok;
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
            _pending.AddRange(result.ChangesInApplyOrder());
        }

        if (result.Style is not null || result.Sections is not null)
        {
            _layout.Restyle(2);
        }
        else
        {
            foreach (var change in result.ChangesInApplyOrder())
            {
                _layout.ApplyEdit(change, change.Container == _model.Body.Blocks ? Math.Max(1, (_layout.Locate(Math.Min(change.First, Math.Max(0, _model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1) : 2);
            }
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        AssertBookmarks(label);
        AssertTocMemo(label);
        Compared++;
        RanPerKind[(int)_kind]++;
        return true;
    }

    // A letter (or, now and then, a long text that pushes later blocks down) typed into a random paragraph once every result is settled, then what the runner does: when the page table is the settled one and the touched block carries nothing a
    // page result points at, the page pass is skipped, and the stored page results must still equal the independent count with no page pass run (and a forced whole-body pass must find nothing to write); otherwise the page loop runs and ends in the same state.
    private async Task<bool> TypeSkippingPagesAsync(Random random, string label)
    {
        await RefreshAsync(label + " settle", touchedOnly: false);
        var signature = new LayoutFieldHost(_model, _layout).PageSignature;
        var paragraphs = Paragraphs();
        if (paragraphs.Count == 0 || signature is null)
        {
            return Refuse();
        }

        var paragraph = paragraphs[random.Next(paragraphs.Count)];
        var text = random.Next(4) == 0 ? string.Join(' ', Enumerable.Repeat("filler", 120 + random.Next(400))) : "x";
        var at = new TextPosition(StoryRef.Body, paragraph, random.Next(TextLength(paragraph) + 1));
        if (!await CheckAsync(Editor.InsertText(new TextRange(at, at), text), label, required: false))
        {
            return false;
        }

        var touched = _pending.ToArray();
        var host = new LayoutFieldHost(_model, _layout);
        if (!Editor.PagePassCanBeSkipped(host, touched, signature))
        {
            PagePassesRun++;
            return await RefreshAsync(label + " page loop", touchedOnly: true);
        }

        PagePassesSkipped++;
        var result = Editor.RefreshFields(host, RefreshScope.OrderBasedPagesCurrent, touched);
        _pending.Clear();
        _everythingPending = false;
        foreach (var change in result.Content)
        {
            _layout.ApplyEdit(change, Math.Max(1, (_layout.Locate(Math.Min(change.First, Math.Max(0, _model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1));
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        if (result.Written > 0)
        {
            // The order-based results themselves changed (and may have moved a page): the page loop follows, as in the runner.
            Assert.False(result.Settled, label + ": a skipped pass that wrote something is not settled");
            return await RefreshAsync(label + " follow-up", touchedOnly: false);
        }

        Assert.True(result.Settled, label + ": a skipped pass that wrote nothing is settled");
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        var after = new LayoutFieldHost(_model, _layout);
        ReferenceFields.AssertStoredEqualsExpected(_model, after, label, includeTocs: false);
        PageResultsCompared += ReferencePageResults.AssertPageResults(_layout, _model, after, label);
        var forced = Editor.RefreshFields(after, RefreshScope.WithPages);
        Assert.Empty(forced.Content);
        Assert.True(forced.Settled, label + ": a whole pass after a skipped one finds the page results settled");
        RanPerKind[(int)_kind]++;
        return true;
    }

    // A refresh is brought into the layout as the session does, and repeated while a page result may still change (at most MaxSettlePasses: the runner's loop, the pages read from the complete layout of the step before); whether or not it wrote, the
    // layout must equal a full layout, every result the oracle has an answer for must be stored (the pages from the independent count), and one more refresh finds nothing.
    private async Task<bool> RefreshAsync(string label, bool touchedOnly, bool forbidStructural = false)
    {
        await _layout.LayOutRestAsync(null, CancellationToken.None);
        var touched = touchedOnly && !_everythingPending ? (IReadOnlyList<BlockChange>)[.. _pending] : null;
        _pending.Clear();
        _everythingPending = false;
        LayoutFieldHost host;
        FieldRefreshResult result;
        var passes = 0;
        var written = 0;
        do
        {
            host = new LayoutFieldHost(_model, _layout);
            result = Editor.RefreshFields(host, RefreshScope.WithPages, passes == 0 ? touched : null);
            passes++;
            written += result.Written;
            _lastRefreshDeferred = result.StructuralDeferred;

            // A structural result is carried by the newest step, as the session does it (nothing to protect with an empty history); a refresh that waited for a pending redo changed nothing and leaves the lists as they were.
            if (forbidStructural)
            {
                Assert.True(result.Structural is null, label + ": a structural refresh followed an undo or a redo that should have restored the table of contents");
            }

            if (result.Structural is { } record)
            {
                Assert.NotEqual(AttachOutcome.MustWait, Editor.History.AttachDerived(record));
            }
            else
            {
                Assert.False(result.StructuralDeferred && !Editor.History.CanRedo, label + ": a refresh waited for a redo that is not pending");
            }

            foreach (var change in result.Content)
            {
                _layout.ApplyEdit(change, Math.Max(1, (_layout.Locate(Math.Min(change.First, Math.Max(0, _model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1));
            }

            await _layout.LayOutRestAsync(null, CancellationToken.None);
        }
        while (!result.Settled && !result.StructuralDeferred && passes < MaxSettlePasses);

        Assert.True(result.Settled || result.StructuralDeferred, $"{label}: the page results did not settle in {MaxSettlePasses} passes");
        SettlePasses += passes;
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        AssertBookmarks(label);
        AssertTocMemo(label);
        host = new LayoutFieldHost(_model, _layout);
        ReferenceFields.AssertStoredEqualsExpected(_model, host, label, includeTocs: false);
        PageResultsCompared += ReferencePageResults.AssertPageResults(_layout, _model, host, label, includeTocs: !result.StructuralDeferred);
        var again = Editor.RefreshFields(host, RefreshScope.WithPages);
        Assert.Empty(again.Content);
        Assert.Null(again.Structural);
        Written += written;
        Compared++;
        RanPerKind[(int)_kind]++;
        return true;
    }

    // A refused edit inside a table of contents is a counted refusal like any other, and it must have left the document alone and named its reason.
    private bool TypeInToc(Random random)
    {
        var found = new List<(IList<Block> Container, TocRange Toc)>();
        CollectTocs(_model.Body.Blocks, found, 0);
        if (found.Count == 0)
        {
            return Refuse();
        }

        var (container, toc) = found[random.Next(found.Count)];
        var paragraph = (Paragraph)container[random.Next(toc.First, toc.Last + 1)];
        var at = new TextPosition(StoryRef.Body, paragraph, random.Next(TextLength(paragraph) + 1));
        var revision = _model.Edits.Revision;
        var result = Editor.InsertText(new TextRange(at, at), "x");
        Assert.Empty(result.Changes);
        Assert.Equal(revision, _model.Edits.Revision);
        Assert.Equal(FieldRefusalKind.InsideToc, Editor.LastFieldRefusal?.Kind);
        return Refuse();
    }

    // The tables of contents of every list of blocks of the body (the top level, block content controls and table cells).
    private static void CollectTocs(IList<Block> container, List<(IList<Block> Container, TocRange Toc)> found, int depth)
    {
        foreach (var toc in TocRanges.In(container) ?? [])
        {
            found.Add((container, toc));
        }

        if (depth > 8)
        {
            return;
        }

        foreach (var block in container)
        {
            switch (block)
            {
                case ContentControlBlock control:
                    CollectTocs(control.Content, found, depth + 1);
                    break;
                case Table table:
                    foreach (var cell in table.Rows.OfType<TableRow>().SelectMany(r => r.Items.OfType<TableCell>()))
                    {
                        CollectTocs(cell.Blocks, found, depth + 1);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    // The editor keeps its answer between edits: after every step it must equal a fresh search of the body (the invalidation is what is under test).
    private void AssertTocMemo(string label)
    {
        var kept = Editor.TocRangesOf(_model.Body.Blocks) ?? [];
        var fresh = TocRanges.In(_model.Body.Blocks) ?? [];
        Assert.True(kept.SequenceEqual(fresh), label + ": the kept tables of contents differ from a fresh search");
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
        foreach (var made in starts.Where(s => s.Name.StartsWith("Mark", StringComparison.Ordinal) || s.Name.StartsWith("_Ref", StringComparison.Ordinal)))
        {
            Assert.True(ends.Count(e => e.Id == made.Id) == 1, $"{label}: the bookmark {made.Name} has {ends.Count(e => e.Id == made.Id)} ends");
            Assert.True(starts.Count(s => s.Id == made.Id) == 1, $"{label}: the id {made.Id} is used by more than one bookmark");
        }
    }
}
