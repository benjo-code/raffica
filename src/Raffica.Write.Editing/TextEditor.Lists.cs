using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    private const int MaxListLevel = 8;

    // How far back ReadListFacts looks for the list a paragraph could continue; beyond it the answer is permissive and the operation decides.
    private const int ContinueLookBack = 500;

    /// <summary>
    /// Toggles a list of <paramref name="kind"/> on every paragraph the selection touches (tables, cells and block content controls between its ends included): when every
    /// one of them is already in a list of that kind the list is removed (a direct <c>w:numPr</c> goes; a list that comes from the style is switched off with <c>numId</c> 0),
    /// otherwise each paragraph that is not gets the list: the paragraphs of one call share one <c>w:num</c> (<see cref="ListCatalog.NewRun"/>, which numbers from 1: when other
    /// paragraphs of the document use the same definition the run gets a <c>w:startOverride</c> of 1), a paragraph that is in the other kind keeps its level, any other starts at level 0.
    /// A paragraph in the default style also gets the List Paragraph style when the file has it; any other style stays. One <see cref="BlockChange"/>, one history step;
    /// the <c>w:num</c> the call makes is not part of the step (an undo leaves it unused and a later toggle takes it back). A selection that leaves one list of blocks changes nothing.
    /// </summary>
    public EditResult ToggleList(TextRange selection, ListKind kind)
    {
        LastFieldRefusal = null;
        var revision = History.BeginOperation();
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        var paragraphs = ParagraphsOf(span);
        if (paragraphs.Count == 0)
        {
            return NoChange(span.Story, span.Last, span.LastOffset);
        }

        // The paragraphs of a table of contents keep their place and kind: a selection that takes some of them takes the others only, and one that takes nothing else is refused.
        var toc = TocParagraphsIn(span);
        if (toc is not null)
        {
            paragraphs = [.. paragraphs.Where(p => !toc.Contains(p))];
            if (paragraphs.Count == 0)
            {
                LastFieldRefusal = new FieldRefusal(span.IsCollapsed ? FieldRefusalKind.InsideToc : FieldRefusalKind.PartOfToc);
                return NoChange(span.Story, span.Last, span.LastOffset);
            }
        }

        var states = paragraphs.Select(p => (Paragraph: p, List: ListOf(span.Story, p))).ToList();
        var story = span.Story;
        if (states.All(s => IsKind(s.List, kind)))
        {
            return CommitParagraphEdit(span, revision, p =>
            {
                if (toc is null || !toc.Contains(p))
                {
                    RemoveList(story, p);
                }
            });
        }

        var targets = new Dictionary<Paragraph, int>(ReferenceEqualityComparer.Instance);
        foreach (var state in states.Where(s => !IsKind(s.List, kind)))
        {
            targets[state.Paragraph] = state.List.NumId > 0 ? state.List.Level : 0;
        }

        var numId = Lists.NewRun(kind);
        if (kind == ListKind.Numbered)
        {
            StartAtOneWhenShared(span.Story, numId, new HashSet<Paragraph>(targets.Keys, ReferenceEqualityComparer.Instance), targets.Values.Distinct().ToList());
        }

        var listStyle = ListParagraphStyleId();
        var defaultStyle = _document.Styles is { } sheet ? DefaultParagraphStyleId(sheet) : null;
        return CommitParagraphEdit(span, revision, p =>
        {
            if (!targets.TryGetValue(p, out var level))
            {
                return;
            }

            SetNumPr(p, numId, level);
            if (listStyle is not null && (p.Properties.StyleId is null || string.Equals(p.Properties.StyleId, defaultStyle, StringComparison.Ordinal)))
            {
                p.Properties.StyleId = listStyle;
            }
        });
    }

    /// <summary>
    /// Moves every list paragraph the selection touches <paramref name="delta"/> levels (clamped to 0 to 8); a paragraph whose list comes from its style gets a direct <c>w:numPr</c> with the same
    /// list and the new level. Null, with nothing changed, when no touched paragraph is in a list; an <see cref="EditResult"/> without changes when every list paragraph is at the edge
    /// already (so a caller never falls back to a tab character). One history step.
    /// </summary>
    public EditResult? ChangeListLevel(TextRange selection, int delta)
    {
        LastFieldRefusal = null;
        var revision = History.BeginOperation();
        if (Resolve(selection) is not { } span)
        {
            return null;
        }

        var toc = TocParagraphsIn(span);
        var listed = ParagraphsOf(span).Where(p => toc is null || !toc.Contains(p)).Select(p => (Paragraph: p, List: ListOf(span.Story, p))).Where(s => s.List.NumId > 0).ToList();
        if (listed.Count == 0)
        {
            if (toc is not null && ParagraphsOf(span).All(toc.Contains))
            {
                LastFieldRefusal = new FieldRefusal(span.IsCollapsed ? FieldRefusalKind.InsideToc : FieldRefusalKind.PartOfToc);
            }

            return null;
        }

        var moves = new Dictionary<Paragraph, (int NumId, int Level)>(ReferenceEqualityComparer.Instance);
        foreach (var (paragraph, list) in listed)
        {
            var level = (int)Math.Clamp((long)list.Level + delta, 0, MaxListLevel);
            if (level != list.Level)
            {
                moves[paragraph] = (list.NumId, level);
            }
        }

        if (moves.Count == 0)
        {
            return NoChange(span.Story, span.Last, span.LastOffset);
        }

        return CommitParagraphEdit(span, revision, p =>
        {
            if (moves.TryGetValue(p, out var move))
            {
                SetNumPr(p, move.NumId, move.Level);
            }
        });
    }

    /// <summary>
    /// Gives the paragraph at <paramref name="at"/> and the paragraphs of its list that follow it (up to the next paragraph of that list at a shallower level) a new <c>w:num</c> on the
    /// same definition with a <c>w:startOverride</c> of 1 for the paragraph's level. Null, with nothing changed, when the paragraph is not in a list. The new <c>w:num</c> is not part of the history step.
    /// </summary>
    public EditResult? RestartNumbering(TextPosition at)
    {
        if (TocRefusesAt(at))
        {
            return null;
        }

        var revision = History.BeginOperation();
        var (story, _, _) = Locate(at);
        var all = StoryParagraphs(story);
        var index = IndexOfReference(all, at.Paragraph);
        var list = ListOf(story, at.Paragraph);
        if (index < 0 || list.NumId <= 0 || _document.Numbering?.FindInstance(list.NumId) is null)
        {
            return null;
        }

        var affected = FollowingInList(story, all, index, list);
        var numId = Lists.Restart(list.NumId, list.Level);
        return CommitMoves(story, at, revision, affected, numId);
    }

    /// <summary>
    /// Points the paragraph at <paramref name="at"/> and the paragraphs of its list that follow it back at the <c>w:num</c> of the nearest earlier list paragraph that uses the same definition,
    /// so the numbering goes on from there (the undo of a restart). Null, with nothing changed, when the paragraph is not in a list, nothing before it uses the definition or it already
    /// continues that list.
    /// </summary>
    public EditResult? ContinueNumbering(TextPosition at)
    {
        if (TocRefusesAt(at))
        {
            return null;
        }

        var revision = History.BeginOperation();
        var (story, _, _) = Locate(at);
        var all = StoryParagraphs(story);
        var index = IndexOfReference(all, at.Paragraph);
        var list = ListOf(story, at.Paragraph);
        if (index < 0 || list.NumId <= 0 || AbstractOf(list.NumId) is not { } abstractId)
        {
            return null;
        }

        var (target, _) = ContinueTarget(story, all, index, list.NumId, abstractId, int.MaxValue);
        if (target <= 0)
        {
            return null;
        }

        var affected = FollowingInList(story, all, index, list);
        return CommitMoves(story, at, revision, affected, target);
    }

    /// <summary>
    /// What the list commands need to know about <paramref name="selection"/>. Pure: nothing changes and no history operation begins. Reads at most <see cref="MaxFactBlocks"/> top-level
    /// blocks of the selection (a larger one is read from its first paragraph, like a caret) and looks back at most 500 paragraphs for a list to continue (the answer is then permissive:
    /// the operation itself returns null when there is nothing to continue). Memoizing by revision and range is the caller's job, as for <see cref="ReadFormatFacts"/>.
    /// </summary>
    public ListFacts ReadListFacts(TextRange selection)
    {
        if (Resolve(selection) is not { } span)
        {
            const ListReason across = ListReason.SelectionAcrossContainers;
            return new ListFacts(false, null, across, false, across, false, across);
        }

        var large = span.LastIndex - span.FirstIndex + 1 > MaxFactBlocks;
        var paragraphs = large ? [span.First] : ParagraphsOf(span);
        var lists = paragraphs.Select(p => ListOf(span.Story, p)).ToList();
        var inList = lists.Any(l => l.NumId > 0);
        ListKind? kind = null;
        if (lists.Count > 0 && Lists.KindOf(lists[0].NumId, lists[0].Level) is { } firstKind && lists.All(l => Lists.KindOf(l.NumId, l.Level) == firstKind))
        {
            kind = firstKind;
        }

        var start = ListOf(span.Story, span.First);
        if (start.NumId <= 0)
        {
            return new ListFacts(inList, kind, ListReason.None, false, ListReason.NotInAList, false, ListReason.NotInAList);
        }

        var all = StoryParagraphs(span.Story);
        var index = IndexOfReference(all, span.First);
        var reason = index >= 0 && AbstractOf(start.NumId) is { } abstractId
            ? ContinueTarget(span.Story, all, index, start.NumId, abstractId, ContinueLookBack).Reason
            : ListReason.NothingBefore;
        return new ListFacts(inList, kind, ListReason.None, true, ListReason.None, reason == ListReason.None, reason);
    }

    // The list of a paragraph after every style layer: its list id (0 for none) and level. Without a format resolver only the direct numPr is known.
    private (int NumId, int Level) ListOf(StoryRef story, Paragraph paragraph)
    {
        if (_format is not null)
        {
            var effective = _format.ForParagraph(story, paragraph);
            return (Math.Max(0, effective.NumberingId), Math.Clamp(effective.NumberingLevel, 0, MaxListLevel));
        }

        return (Math.Max(0, paragraph.Properties.NumberingId ?? 0), Math.Clamp(paragraph.Properties.NumberingLevel ?? 0, 0, MaxListLevel));
    }

    private bool IsKind((int NumId, int Level) list, ListKind kind) => list.NumId > 0 && Lists.KindOf(list.NumId, list.Level) == kind;

    private int? AbstractOf(int numId) => _document.Numbering?.FindInstance(numId)?.AbstractNumId;

    private static List<Paragraph> ParagraphsOf(Span span)
    {
        var paragraphs = new List<Paragraph>();
        for (var i = span.FirstIndex; i <= span.LastIndex; i++)
        {
            VisitWholeBlock(span.Container[i], paragraphs.Add, 0);
        }

        return paragraphs;
    }

    private List<Paragraph> StoryParagraphs(StoryRef story)
    {
        var paragraphs = new List<Paragraph>();
        foreach (var block in StoryOf(story).Blocks)
        {
            VisitWholeBlock(block, paragraphs.Add, 0);
        }

        return paragraphs;
    }

    private static int IndexOfReference(List<Paragraph> paragraphs, Paragraph paragraph)
    {
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (ReferenceEquals(paragraphs[i], paragraph))
            {
                return i;
            }
        }

        return -1;
    }

    // The paragraph at index and the later ones of the same list, up to the first of that list at a shallower level.
    private List<Paragraph> FollowingInList(StoryRef story, List<Paragraph> all, int index, (int NumId, int Level) list)
    {
        var affected = new List<Paragraph> { all[index] };
        for (var i = index + 1; i < all.Count; i++)
        {
            var other = ListOf(story, all[i]);
            if (other.NumId != list.NumId)
            {
                continue;
            }

            if (other.Level < list.Level)
            {
                break;
            }

            affected.Add(all[i]);
        }

        return affected;
    }

    // The w:num an earlier list paragraph of the same definition has (0 when none, or when the nearest one is the list itself), and why. Looks at most lookBack paragraphs back; a miss
    // after that is permissive (None).
    private (int Target, ListReason Reason) ContinueTarget(StoryRef story, List<Paragraph> all, int index, int numId, int abstractId, int lookBack)
    {
        var stop = lookBack >= index ? 0 : index - lookBack;
        for (var i = index - 1; i >= stop; i--)
        {
            var other = ListOf(story, all[i]);
            if (other.NumId <= 0 || AbstractOf(other.NumId) != abstractId)
            {
                continue;
            }

            return other.NumId == numId ? (0, ListReason.AlreadyContinues) : (other.NumId, ListReason.None);
        }

        return stop > 0 ? (0, ListReason.None) : (0, ListReason.NothingBefore);
    }

    // A list that lies in one list of blocks (a cell, a content control, a note, the body) is changed there, exactly like ToggleList: container-relative indices, the caret in that container, so the step
    // is small and undo puts the caret back at the paragraph. Only a list that really crosses containers makes one BlockChange over the top-level blocks that hold it. The history needs a paragraph that
    // lies directly in that list of blocks for the caret: the position itself when it does, else the first one in the range (the range grows to the first of the story when it holds none), and its
    // index (not the index of a table or control that starts the range) is the point of the step.
    private EditResult? CommitMoves(StoryRef story, TextPosition at, int revision, List<Paragraph> affected, int numId)
    {
        var (_, own, ownIndex) = Locate(at);
        var levels = new Dictionary<Paragraph, int>(ReferenceEqualityComparer.Instance);
        foreach (var paragraph in affected)
        {
            levels[paragraph] = ListOf(story, paragraph).Level;
        }

        void Move(Paragraph p)
        {
            if (levels.TryGetValue(p, out var level))
            {
                SetNumPr(p, numId, level);
            }
        }

        var first = ownIndex;
        var last = ownIndex;
        var sameContainer = true;
        foreach (var paragraph in affected)
        {
            var index = -1;
            for (var i = 0; i < own.Count; i++)
            {
                if (ReferenceEquals(own[i], paragraph))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                sameContainer = false;
                break;
            }

            first = Math.Min(first, index);
            last = Math.Max(last, index);
        }

        if (sameContainer)
        {
            return CommitParagraphEdit(new Span(story, own, first, at.Paragraph, at.Offset, last, at.Paragraph, at.Offset), revision, Move, new EditHistory.Point(ownIndex, at.Offset));
        }

        return CommitMovesAcross(story, at, revision, affected, Move);
    }

    private EditResult? CommitMovesAcross(StoryRef story, TextPosition at, int revision, List<Paragraph> affected, Action<Paragraph> move)
    {
        var top = StoryOf(story).Blocks;
        var first = int.MaxValue;
        var last = -1;
        foreach (var paragraph in affected.Append(at.Paragraph))
        {
            var index = TextUnits.IndexUnder(top, paragraph);
            if (index < 0)
            {
                return null;
            }

            first = Math.Min(first, index);
            last = Math.Max(last, index);
        }

        var anchorIndex = -1;
        var inTop = TextUnits.IndexUnder(top, at.Paragraph);
        if (inTop >= 0 && ReferenceEquals(top[inTop], at.Paragraph))
        {
            anchorIndex = inTop;
        }
        else
        {
            for (var i = first; i <= last && anchorIndex < 0; i++)
            {
                if (top[i] is Paragraph)
                {
                    anchorIndex = i;
                }
            }

            if (anchorIndex < 0)
            {
                for (var i = 0; i < top.Count && anchorIndex < 0; i++)
                {
                    if (top[i] is Paragraph)
                    {
                        anchorIndex = i;
                    }
                }

                if (anchorIndex < 0)
                {
                    return null;
                }

                first = Math.Min(first, anchorIndex);
                last = Math.Max(last, anchorIndex);
            }
        }

        var anchor = (Paragraph)top[anchorIndex];
        var offset = ReferenceEquals(anchor, at.Paragraph) ? at.Offset : 0;
        var span = new Span(story, top, first, anchor, offset, last, anchor, offset);
        return CommitParagraphEdit(span, revision, move, new EditHistory.Point(anchorIndex, offset));
    }

    // Writes the direct list of a paragraph: ilvl and numId in schema order, other children of an existing numPr (revision marks) kept after them.
    private static void SetNumPr(Paragraph paragraph, int numId, int level)
    {
        var numPr = new XElement(
            W + "numPr",
            new XElement(W + "ilvl", new XAttribute(W + "val", level.ToString(CultureInfo.InvariantCulture))),
            new XElement(W + "numId", new XAttribute(W + "val", numId.ToString(CultureInfo.InvariantCulture))));
        if (paragraph.Properties.Get(W + "numPr") is { } existing)
        {
            foreach (var child in existing.Elements().Where(c => c.Name != W + "ilvl" && c.Name != W + "numId"))
            {
                numPr.Add(new XElement(child));
            }
        }

        paragraph.Properties.Set(W + "numPr", numPr);
    }

    // Takes the direct list away; a list that the style carries is switched off with numId 0 (the spelling Word uses for "no list").
    private void RemoveList(StoryRef story, Paragraph paragraph)
    {
        paragraph.Properties.Remove(W + "numPr");
        if (ListOf(story, paragraph).NumId > 0)
        {
            SetNumPr(paragraph, 0, 0);
        }
    }

    // A numbered run numbers from 1: when another paragraph of the story (outside the run, after every style layer) uses the same definition, the new w:num restarts it at every level the run uses.
    private void StartAtOneWhenShared(StoryRef story, int numId, HashSet<Paragraph> run, List<int> levels)
    {
        if (_document.Numbering is not { } numbering || numbering.FindInstance(numId) is not { AbstractNumId: { } abstractId } instance)
        {
            return;
        }

        var shared = false;
        foreach (var block in StoryOf(story).Blocks)
        {
            VisitWholeBlock(block, paragraph =>
            {
                if (shared || run.Contains(paragraph))
                {
                    return;
                }

                var other = ListOf(story, paragraph).NumId;
                shared = other > 0 && other != numId && AbstractOf(other) == abstractId;
            }, 0);
        }

        if (!shared)
        {
            return;
        }

        foreach (var level in levels)
        {
            instance.Overrides.Add(new LevelOverride { Level = level, StartOverride = 1 });
        }
    }

    private string? ListParagraphStyleId() =>
        _document.Styles?.Styles.FirstOrDefault(s =>
            (s.Type ?? "paragraph") == "paragraph"
            && (string.Equals(s.Id, "ListParagraph", StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, "List Paragraph", StringComparison.OrdinalIgnoreCase)))?.Id;

    // Enter on an empty list paragraph ends the list and keeps the paragraph; null when this is not that case (the caller splits as usual).
    private EditResult? EndEmptyListItem(TextPosition caret)
    {
        var (story, container, index) = Locate(caret);
        if (BreakRuleOf(story, container) != BreakRule.Paragraph
            || Map(story, caret.Paragraph).Projection.Text.Length != 0
            || ListOf(story, caret.Paragraph).NumId <= 0)
        {
            return null;
        }

        var revision = History.BeginOperation();
        return CommitParagraphEdit(new Span(story, container, index, caret.Paragraph, 0, index, caret.Paragraph, 0), revision, p => RemoveList(story, p));
    }

    // Backspace at the start of a list paragraph: the list goes first, the text stays; null when the paragraph is not in a list.
    private EditResult? RemoveListAtStart(StoryRef story, IList<Block> container, int index, Paragraph paragraph, int revision) =>
        ListOf(story, paragraph).NumId <= 0
            ? null
            : CommitParagraphEdit(new Span(story, container, index, paragraph, 0, index, paragraph, 0), revision, p => RemoveList(story, p));
}
