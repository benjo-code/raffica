using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>The switches of the character formatting that act on the effective value of the whole selection.</summary>
public enum CharacterToggle
{
    Bold,
    Italic,
    Underline,
    Strike,
    SmallCaps,
    AllCaps,
    Superscript,
    Subscript,
}

public sealed partial class TextEditor
{
    // A constant, not the W field: static initializers of the two partial files have no defined order.
    // Blocks nested deeper than this (tables in cells in controls ...) are left unformatted; the file hardening of 4.8 limits depth anyway.
    private const int MaxNesting = 64;

    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly Dictionary<CharacterToggle, ToggleDefinition> s_toggles = new()
    {
        [CharacterToggle.Bold] = new("b", r => r.Bold, null, "0"),
        [CharacterToggle.Italic] = new("i", r => r.Italic, null, "0"),
        [CharacterToggle.Underline] = new("u", r => r.Underline, "single", "none"),
        [CharacterToggle.Strike] = new("strike", r => r.Strike, null, "0"),
        [CharacterToggle.SmallCaps] = new("smallCaps", r => r.SmallCaps, null, "0"),
        [CharacterToggle.AllCaps] = new("caps", r => r.Caps, null, "0"),
        [CharacterToggle.Superscript] = new("vertAlign", r => r.Position == VerticalPosition.Superscript, "superscript", "baseline"),
        [CharacterToggle.Subscript] = new("vertAlign", r => r.Position == VerticalPosition.Subscript, "subscript", "baseline"),
    };

    // The attributes of a merged element that a new value makes stale: Word resolves a theme attribute before the plain one.
    private static readonly Dictionary<XName, Dictionary<string, string[]>> s_themeAttributes = new()
    {
        [XName.Get("rFonts", WordNamespace)] = new() { ["ascii"] = ["asciiTheme"], ["hAnsi"] = ["hAnsiTheme"], ["eastAsia"] = ["eastAsiaTheme"], ["cs"] = ["cstheme"] },
        [XName.Get("color", WordNamespace)] = new() { ["val"] = ["themeColor", "themeShade", "themeTint"] },

        // Paragraph attributes: a new value replaces the older spelling of the same thing, and first line and hanging exclude each other.
        [XName.Get("ind", WordNamespace)] = new() { ["left"] = ["start", "leftChars"], ["right"] = ["end", "rightChars"], ["firstLine"] = ["hanging", "firstLineChars", "hangingChars"], ["hanging"] = ["firstLine", "firstLineChars", "hangingChars"] },
        [XName.Get("spacing", WordNamespace)] = new() { ["before"] = ["beforeLines", "beforeAutospacing"], ["after"] = ["afterLines", "afterAutospacing"] },
    };

    /// <summary>
    /// Sets or removes the properties on the runs the selection covers (text that is not shown, such as deleted text, is never covered itself; inside a covered
    /// run it follows the content before it, see <c>ResolveHidden</c>, and anything outside the range stays as it is). Runs are split at the edges and the touched runs and their neighbours are merged again where they are equal
    /// (<see cref="RunMerger"/>). A paragraph without text in the selection gets the change on its paragraph mark. A collapsed selection in
    /// a paragraph with text changes nothing. One history step; the caret is the end of the selection.
    /// </summary>
    public EditResult ApplyCharacterChanges(TextRange selection, IReadOnlyList<PropertyChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return FormatSelection(selection, _ => target =>
        {
            foreach (var change in changes)
            {
                ApplyChange(target.Bag, change);
            }
        });
    }

    /// <summary>
    /// Turns a switch on when any of the selection is off and off when all of it is on, judged on the effective formatting. Turning off a value
    /// that a style supplies writes an explicit off; turning off a direct value removes the element. Superscript and subscript exclude each other.
    /// </summary>
    /// <exception cref="InvalidOperationException">The editor was made without an <see cref="IEffectiveFormat"/>.</exception>
    public EditResult ToggleCharacter(TextRange selection, CharacterToggle toggle)
    {
        var format = _format ?? throw new InvalidOperationException("Toggling a character format needs an IEffectiveFormat (the resolver of the effective formatting); the editor was made without one.");
        var definition = s_toggles[toggle];
        return FormatSelection(selection, targets =>
        {
            var allOn = targets.All(t => definition.Read(Effective(t, format)));
            return allOn
                ? target =>
                {
                    target.Bag.Remove(definition.Name);

                    // A paragraph mark's bag is only written back by Commit, and the effective format below reads the paragraph, so write it now.
                    target.Commit();
                    if (definition.Read(Effective(target, format)))
                    {
                        target.Bag.Set(definition.Name, definition.Element(definition.Off));
                    }
                }
            : target =>
            {
                if (!definition.Read(Effective(target, format)))
                {
                    target.Bag.Set(definition.Name, definition.Element(definition.On));
                }
            };
        });
    }

    /// <summary>
    /// <see cref="InsertText(TextRange, string)"/> where the new text also carries <paramref name="typingFormat"/>: the text first joins the
    /// neighbouring run as usual (so it has that run's properties), then the changes are applied to exactly the new characters and equal
    /// neighbours merge back. A text with a line break takes no typing format.
    /// </summary>
    public EditResult InsertText(TextRange selection, string text, IReadOnlyList<PropertyChange> typingFormat)
    {
        ArgumentNullException.ThrowIfNull(typingFormat);
        return InsertCore(selection, text, typingFormat);
    }

    private static EffectiveRun Effective(CharTarget target, IEffectiveFormat format) =>
        target.Run is null ? format.ForMark(target.Story, target.Paragraph) : format.ForRun(target.Story, target.Paragraph, target.Run);

    private static void ApplyChange(PropertyBag bag, PropertyChange change)
    {
        if (change.Value is null)
        {
            bag.Remove(change.Name);
            return;
        }

        var value = new XElement(change.Value);
        if (s_themeAttributes.TryGetValue(change.Name, out var stale) && bag.Get(change.Name) is { } existing)
        {
            var merged = new XElement(existing);
            foreach (var attribute in value.Attributes())
            {
                if (stale.TryGetValue(attribute.Name.LocalName, out var drop))
                {
                    foreach (var name in drop)
                    {
                        merged.Attribute(W + name)?.Remove();
                    }
                }

                merged.SetAttributeValue(attribute.Name, attribute.Value);
            }

            value = merged;
        }

        bag.Set(change.Name, value);
    }

    // The shared shape of every character operation: clone the touched blocks, split runs, change them, merge, one BlockChange, one history step.
    // The override operations pass: merge false (a removal never merges), a filter (only runs that carry the element are split and changed, so nothing
    // else of the document moves) and a paragraph action (paragraph rows and the mark; it sees every touched paragraph, also one without a covered run).
    private EditResult FormatSelection(
        TextRange selection,
        Func<IReadOnlyList<CharTarget>, Action<CharTarget>> plan,
        bool merge = true,
        Func<Paragraph, Run?, bool>? filter = null,
        Action<Paragraph, ParagraphCoverage>? paragraphAct = null)
    {
        var revision = History.BeginOperation();
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        var old = CloneBlocks(span);
        var targets = new List<CharTarget>();
        var touched = new HashSet<Run>(ReferenceEqualityComparer.Instance);
        var owners = new List<IList<Inline>>();
        WalkTouched(span, (map, start, end) =>
        {
            var length = map.Projection.Text.Length;
            paragraphAct?.Invoke(map.Paragraph, new ParagraphCoverage(span.Story, start == 0 && end == length, length == 0));
            CollectTargets(span.Story, map, start, end, targets, touched, owners, filter);
        });

        if (targets.Count == 0 && paragraphAct is null)
        {
            return NoChange(span.Story, span.Last, span.LastOffset);
        }

        var act = plan(targets);
        foreach (var target in targets)
        {
            act(target);
            target.Commit();
        }

        if (merge)
        {
            MergeTouched(owners, touched);
        }

        _document.Edits.Raise();
        var count = span.LastIndex - span.FirstIndex + 1;
        var result = new EditResult(
            new TextPosition(span.Story, span.Last, span.LastOffset),
            [new BlockChange(span.Story, span.Container, span.FirstIndex, count, count, old)]);
        History.Record(result, new EditHistory.Point(span.FirstIndex, span.FirstOffset), revision, null);
        return result;
    }

    // The one walker over the paragraphs a selection touches: the ends partly, the paragraphs between whole, and every paragraph of a table or
    // content control between the ends whole. Shared by the writes above and the pure read of the formatting inspector.
    private void WalkTouched(Span span, Action<ParagraphMap, int, int> visit)
    {
        for (var i = span.FirstIndex; i <= span.LastIndex; i++)
        {
            var block = span.Container[i];
            if (block is Paragraph paragraph)
            {
                var map = Map(span.Story, paragraph);
                var length = map.Projection.Text.Length;
                var start = i == span.FirstIndex ? Math.Clamp(span.FirstOffset, 0, length) : 0;
                var end = i == span.LastIndex ? Math.Clamp(span.LastOffset, 0, length) : length;
                visit(map, start, end);
            }
            else
            {
                // A table or content control between the ends: all of its text is covered (the snapshot of the caller holds the whole top-level block).
                VisitWholeBlock(block, inner =>
                {
                    var map = Map(span.Story, inner);
                    visit(map, 0, map.Projection.Text.Length);
                }, 0);
            }
        }
    }

    /// <summary>What a selection covers of one paragraph: its whole text (an empty paragraph always) and whether it has no text.</summary>
    private readonly record struct ParagraphCoverage(StoryRef Story, bool Covered, bool Empty);

    // Every paragraph inside a table (rows, cells, nested tables) or a block content control, whole. Row items the model does not understand are kept as they are.
    // Shared by the character and the paragraph operations: the visitor gets each paragraph once.
    private static void VisitWholeBlock(Block block, Action<Paragraph> visit, int depth)
    {
        if (depth > MaxNesting)
        {
            return;
        }

        switch (block)
        {
            case Paragraph paragraph:
                visit(paragraph);
                break;
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    VisitWholeBlock(inner, visit, depth + 1);
                }

                break;
            case Table table:
                foreach (var row in table.Rows)
                {
                    VisitWholeRowItem(row, visit, depth + 1);
                }

                break;
            default:
                break;
        }
    }

    private static void VisitWholeRowItem(RowItem item, Action<Paragraph> visit, int depth)
    {
        if (depth > MaxNesting)
        {
            return;
        }

        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    VisitWholeRowItem(child, visit, depth + 1);
                }

                break;
            case TableCell cell:
                foreach (var inner in cell.Blocks)
                {
                    VisitWholeBlock(inner, visit, depth + 1);
                }

                break;
            default:
                break;
        }
    }

    // Applies changes to the characters [start, end) of a paragraph that was just edited (the typing format of an insert).
    private static void FormatRange(StoryRef story, ParagraphMap map, int start, int end, IReadOnlyList<PropertyChange> changes)
    {
        var targets = new List<CharTarget>();
        var touched = new HashSet<Run>(ReferenceEqualityComparer.Instance);
        var owners = new List<IList<Inline>>();
        CollectTargets(story, map, start, end, targets, touched, owners);
        foreach (var target in targets)
        {
            foreach (var change in changes)
            {
                ApplyChange(target.Bag, change);
            }

            target.Commit();
        }

        MergeTouched(owners, touched);
    }

    // Runs are split here, not with InlineSplitter: that one cuts the containers it passes through (it would split a hyperlink or a tracked-change wrapper in two).
    // Splits the runs at the edges of [start, end) and adds a target for every run that is covered (for a paragraph without text, its mark).
    // With a filter, a run (or the mark, run null) it refuses is neither split nor a target. With dryRun nothing is changed: every run that has covered
    // content is a target as it is (the pure read of the formatting inspector), and a paragraph without text has none (its mark is read from the paragraph).
    private static void CollectTargets(
        StoryRef story,
        ParagraphMap map,
        int start,
        int end,
        List<CharTarget> targets,
        HashSet<Run> touched,
        List<IList<Inline>> owners,
        Func<Paragraph, Run?, bool>? filter = null,
        bool dryRun = false)
    {
        var projection = map.Projection;
        if (projection.Text.Length == 0)
        {
            if (!dryRun && (filter is null || filter(map.Paragraph, null)))
            {
                targets.Add(new CharTarget(story, map.Paragraph, null));
            }

            return;
        }

        var whole = new HashSet<RunContent>(ReferenceEqualityComparer.Instance);
        var partial = new Dictionary<RunContent, (int From, int To)>(ReferenceEqualityComparer.Instance);
        var wholeRuns = new HashSet<Run>(ReferenceEqualityComparer.Instance);
        var visible = new HashSet<RunContent>(ReferenceEqualityComparer.Instance);
        foreach (var piece in projection.Pieces)
        {
            if (piece.Length == 0)
            {
                continue;
            }

            if (piece.Content is { } shown)
            {
                visible.Add(shown);
            }

            if (piece.End <= start || piece.Start >= end)
            {
                continue;
            }

            if (piece.Content is null)
            {
                if (piece.Run is { } owner)
                {
                    wholeRuns.Add(owner);
                }
            }
            else if (piece is TextPiece && piece.Content is TextContent)
            {
                var from = Math.Max(start, piece.Start) - piece.Start + piece.ContentOffset;
                var to = Math.Min(end, piece.End) - piece.Start + piece.ContentOffset;
                partial[piece.Content] = partial.TryGetValue(piece.Content, out var seen) ? (Math.Min(seen.From, from), Math.Max(seen.To, to)) : (from, to);
            }
            else
            {
                whole.Add(piece.Content);
            }
        }

        var runs = new List<Run>();
        var items = new Dictionary<Run, List<FlatItem>>(ReferenceEqualityComparer.Instance);
        foreach (var item in map.Items)
        {
            if (!items.TryGetValue(item.Run, out var list))
            {
                items[item.Run] = list = [];
                runs.Add(item.Run);
            }

            list.Add(item);
        }

        foreach (var run in runs)
        {
            if (filter is not null && !filter(map.Paragraph, run))
            {
                continue;
            }

            if (dryRun)
            {
                if (wholeRuns.Contains(run) || items[run].Exists(i => whole.Contains(i.Content) || (partial.TryGetValue(i.Content, out var cut) && cut.To > cut.From)))
                {
                    targets.Add(new CharTarget(story, map.Paragraph, run));
                }

                continue;
            }

            var sequence = Sequence(run, items[run], whole, partial, wholeRuns, visible);
            if (!sequence.Exists(s => s.State == true))
            {
                continue;
            }

            ResolveHidden(sequence);
            var owner = items[run][0].Owner;
            var pieces = new List<(bool State, List<RunContent> Contents)>();
            foreach (var (content, state) in sequence)
            {
                if (pieces.Count > 0 && pieces[^1].State == state!.Value)
                {
                    pieces[^1].Contents.Add(content);
                }
                else
                {
                    pieces.Add((state!.Value, [content]));
                }
            }

            var at = owner.IndexOf(run);
            for (var k = 0; k < pieces.Count; k++)
            {
                var target = k == 0 ? run : run.Clone();
                target.Content.Clear();
                if (k > 0)
                {
                    owner.Insert(at + k, target);
                }

                foreach (var content in pieces[k].Contents)
                {
                    target.Content.Add(content);
                }

                if (pieces[k].State)
                {
                    targets.Add(new CharTarget(story, map.Paragraph, target));
                    touched.Add(target);
                }
            }

            if (!owners.Exists(o => ReferenceEquals(o, owner)))
            {
                owners.Add(owner);
            }
        }
    }

    // The contents of a run in order, each with: true covered, false shown but not covered, null not shown at all. A partly covered text is cut in up to three.
    private static List<(RunContent Content, bool? State)> Sequence(
        Run run,
        List<FlatItem> items,
        HashSet<RunContent> whole,
        Dictionary<RunContent, (int From, int To)> partial,
        HashSet<Run> wholeRuns,
        HashSet<RunContent> visible)
    {
        var sequence = new List<(RunContent, bool?)>();
        var allCovered = wholeRuns.Contains(run);
        foreach (var item in items)
        {
            var content = item.Content;
            if (allCovered || whole.Contains(content))
            {
                sequence.Add((content, true));
            }
            else if (content is TextContent text && partial.TryGetValue(content, out var cut))
            {
                var from = Math.Clamp(cut.From, 0, text.Text.Length);
                var to = Math.Clamp(cut.To, from, text.Text.Length);
                string[] texts = [text.Text[..from], text.Text[from..to], text.Text[to..]];
                bool[] states = [false, true, false];
                var reused = false;
                for (var k = 0; k < 3; k++)
                {
                    if (texts[k].Length == 0)
                    {
                        continue;
                    }

                    var part = reused ? text.Clone() : text;
                    reused = true;
                    part.Text = texts[k];
                    sequence.Add((part, states[k]));
                }
            }
            else
            {
                sequence.Add((content, visible.Contains(content) ? false : null));
            }
        }

        return sequence;
    }

    // Contents that show nothing follow the content before them (the one after them at the start of a run).
    private static void ResolveHidden(List<(RunContent Content, bool? State)> sequence)
    {
        bool? last = null;
        for (var i = 0; i < sequence.Count; i++)
        {
            if (sequence[i].State is null)
            {
                sequence[i] = (sequence[i].Content, last);
            }

            last = sequence[i].State ?? last;
        }

        bool? next = null;
        for (var i = sequence.Count - 1; i >= 0; i--)
        {
            if (sequence[i].State is null)
            {
                sequence[i] = (sequence[i].Content, next ?? false);
            }

            next = sequence[i].State;
        }
    }

    private static void MergeTouched(List<IList<Inline>> owners, HashSet<Run> touched)
    {
        foreach (var owner in owners)
        {
            var eligible = new HashSet<Inline>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < owner.Count; i++)
            {
                if (owner[i] is Run run && touched.Contains(run))
                {
                    eligible.Add(run);
                    if (i > 0)
                    {
                        eligible.Add(owner[i - 1]);
                    }

                    if (i + 1 < owner.Count)
                    {
                        eligible.Add(owner[i + 1]);
                    }
                }
            }

            var index = 0;
            while (index + 1 < owner.Count)
            {
                if (owner[index] is Run a && owner[index + 1] is Run b && eligible.Contains(a) && eligible.Contains(b) && RunMerger.CanMerge(a, b))
                {
                    RunMerger.Merge(a, b);
                    owner.RemoveAt(index + 1);
                }
                else
                {
                    index++;
                }
            }
        }
    }

    private sealed record ToggleDefinition(string Local, Func<EffectiveRun, bool> Read, string? On, string Off)
    {
        public XName Name { get; } = XName.Get(Local, WordNamespace);

        public XElement Element(string? value) => value is null ? new XElement(Name) : new XElement(Name, new XAttribute(W + "val", value));
    }

    // A run, or the mark of a paragraph without text, that a character operation changes. The mark's run properties live as one element in the
    // paragraph's properties; a bag is made from them and written back by Commit.
    private sealed class CharTarget
    {
        public CharTarget(StoryRef story, Paragraph paragraph, Run? run)
        {
            Story = story;
            Paragraph = paragraph;
            Run = run;
            if (run is not null)
            {
                Bag = run.Properties;
            }
            else
            {
                Bag = new PropertyBag(PropertyKind.Run);
                if (paragraph.Properties.Get(W + "rPr") is { } mark)
                {
                    foreach (var child in mark.Elements())
                    {
                        Bag.AddLoaded(child);
                    }
                }
            }
        }

        public StoryRef Story { get; }

        public Paragraph Paragraph { get; }

        public Run? Run { get; }

        public PropertyBag Bag { get; }

        public void Commit()
        {
            if (Run is not null)
            {
                return;
            }

            var mark = Paragraph.Properties.Get(W + "rPr");
            if (Bag.Children.Count == 0)
            {
                if (mark is not null)
                {
                    Paragraph.Properties.Remove(W + "rPr");
                }

                return;
            }

            var element = mark ?? new XElement(W + "rPr");
            element.RemoveNodes();
            foreach (var child in Bag.Children)
            {
                element.Add(new XElement(child));
            }

            if (mark is null)
            {
                Paragraph.Properties.Set(W + "rPr", element);
            }
        }
    }
}
