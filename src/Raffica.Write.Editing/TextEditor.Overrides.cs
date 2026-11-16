using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// The formatting inspector read of a selection (see <see cref="FormattingInspector"/>), with this editor's projection hosts. Pure: no run is split,
    /// nothing is raised, no history step is made.
    /// </summary>
    public InspectorView Inspect(TextRange selection)
    {
        var touched = Scan(selection);
        return FormattingInspector.Build(
            [.. touched.Paragraphs.Select(p => p.Paragraph)],
            [.. touched.Paragraphs.Where(p => p.Empty).Select(p => p.Paragraph)],
            touched.Runs);
    }

    /// <summary>
    /// <see cref="Inspect"/> for a pane that is read while the user types and selects: a selection that touches more than <see cref="MaxFactBlocks"/> blocks is not read (the answer is
    /// null, so a select all of a long document costs a lookup, not a walk over every paragraph and run).
    /// </summary>
    public InspectorView? InspectBounded(TextRange selection) =>
        Resolve(selection) is { } span && span.LastIndex - span.FirstIndex + 1 > MaxFactBlocks ? null : Inspect(selection);

    /// <summary>
    /// Removes one override row: every direct child of that name of the touched paragraphs (scope <see cref="OverrideScope.Paragraph"/>), of the covered
    /// runs (<see cref="OverrideScope.Run"/>) or of the mark of a touched paragraph without text (<see cref="OverrideScope.ParagraphMark"/>), and nothing
    /// else: not the other rows, not the style, and no runs are merged. A run that the selection edge cuts is split, but only when it carries the element
    /// (the covered part loses it, the rest keeps it, and the pieces stay separate runs). A name that is not an override of the scope, or that nothing
    /// touched carries, changes nothing. One history step.
    /// </summary>
    public EditResult RemoveOverride(TextRange selection, OverrideScope scope, XName element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!FormattingInspector.IsOverride(scope, element) || !HasWork(Scan(selection), scope, element))
        {
            return NoChange(selection.Start);
        }

        return FormatSelection(
            selection,
            _ => target => RemoveAll(target.Bag, element),
            merge: false,
            filter: (_, run) => scope == OverrideScope.Run && run is not null && run.Properties.Get(element) is not null,
            paragraphAct: (paragraph, coverage) =>
            {
                if (scope == OverrideScope.Paragraph && paragraph.Properties.Get(element) is not null)
                {
                    RemoveAll(paragraph.Properties, element);
                }
                else if (scope == OverrideScope.ParagraphMark && coverage.Empty)
                {
                    ChangeMark(paragraph, mark => mark.Elements(element).Remove());
                }
            });
    }

    /// <summary>
    /// Removes every override of the touched paragraphs and the covered runs (every row of <see cref="Inspect"/>), keeping the paragraph and character
    /// style, the list (<c>w:numPr</c>), a section break and the tracked-change records. The mark's run properties are removed only from an empty
    /// paragraph and from a paragraph whose whole text the selection covers; otherwise they stay. No runs are merged. One history step.
    /// </summary>
    public EditResult ClearDirectFormatting(TextRange selection)
    {
        var touched = Scan(selection);
        var work = touched.Runs.Exists(r => FormattingInspector.OverridesOf(OverrideScope.Run, r.Properties.Children).Any())
            || touched.Paragraphs.Exists(p => FormattingInspector.OverridesOf(OverrideScope.Paragraph, p.Paragraph.Properties.Children).Any()
                || ((p.Covered || p.Empty) && FormattingInspector.OverridesOf(OverrideScope.ParagraphMark, p.Paragraph.Properties.Get(W + "rPr")?.Elements() ?? []).Any()));
        if (!work)
        {
            return NoChange(selection.Start);
        }

        return FormatSelection(
            selection,
            _ => target => RemoveWhere(target.Bag, OverrideScope.Run),
            merge: false,
            filter: (_, run) => run is not null && FormattingInspector.OverridesOf(OverrideScope.Run, run.Properties.Children).Any(),
            paragraphAct: (paragraph, coverage) =>
            {
                RemoveWhere(paragraph.Properties, OverrideScope.Paragraph);
                if ((coverage.Covered || coverage.Empty) && paragraph.Properties.Get(W + "rPr") is not null)
                {
                    ChangeMark(paragraph, mark => FormattingInspector.OverridesOf(OverrideScope.ParagraphMark, mark.Elements()).ToList().ForEach(e => e.Remove()));
                }
            });
    }

    // What a selection touches: its paragraphs (with whether their whole text is covered and whether they have none) and the runs with covered content,
    // read without changing anything. The walker and the covering rule are those of the writes.
    private Touched Scan(TextRange selection)
    {
        var paragraphs = new List<TouchedParagraph>();
        var runs = new List<Run>();
        if (Resolve(selection) is not { } span)
        {
            return new Touched(paragraphs, runs);
        }

        WalkTouched(span, (map, start, end) =>
        {
            var length = map.Projection.Text.Length;
            paragraphs.Add(new TouchedParagraph(map.Paragraph, start == 0 && end == length, length == 0));
            var targets = new List<CharTarget>();
            CollectTargets(span.Story, map, start, end, targets, [], [], filter: null, dryRun: true);
            runs.AddRange(targets.Select(t => t.Run!));
        });
        return new Touched(paragraphs, runs);
    }

    private static bool HasWork(Touched touched, OverrideScope scope, XName element) => scope switch
    {
        OverrideScope.Paragraph => touched.Paragraphs.Exists(p => p.Paragraph.Properties.Get(element) is not null),
        OverrideScope.Run => touched.Runs.Exists(r => r.Properties.Get(element) is not null),
        _ => touched.Paragraphs.Exists(p => p.Empty && p.Paragraph.Properties.Get(W + "rPr")?.Element(element) is not null),
    };

    // A direct child of a bag appears once in a valid file; a duplicate is the same row, so all of that name go.
    private static void RemoveAll(PropertyBag bag, XName name)
    {
        while (bag.Remove(name))
        {
        }
    }

    private static void RemoveWhere(PropertyBag bag, OverrideScope scope)
    {
        foreach (var name in bag.Children.Select(c => c.Name).Where(n => FormattingInspector.IsOverride(scope, n)).Distinct().ToList())
        {
            RemoveAll(bag, name);
        }
    }

    // The mark run properties are one element of the paragraph properties: its children are edited in place and an emptied element stays, as an emptied
    // run property element does (a removal is "before minus the element", nothing more).
    private static void ChangeMark(Paragraph paragraph, Action<XElement> change)
    {
        if (paragraph.Properties.Get(W + "rPr") is { } mark)
        {
            change(mark);
        }
    }

    private sealed record TouchedParagraph(Paragraph Paragraph, bool Covered, bool Empty);

    private sealed record Touched(List<TouchedParagraph> Paragraphs, List<Run> Runs);
}
