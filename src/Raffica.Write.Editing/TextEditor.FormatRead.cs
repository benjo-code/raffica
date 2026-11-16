using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>A run, or the mark of a paragraph without text (<see cref="Run"/> null), whose formatting a selection shows.</summary>
public readonly record struct CoveredTarget(StoryRef Story, Paragraph Paragraph, Run? Run);

/// <summary>A paragraph a selection touches.</summary>
public readonly record struct CoveredParagraph(StoryRef Story, Paragraph Paragraph);

/// <summary>
/// What a selection covers, read for the formatting display of the ribbon (<see cref="TextEditor.ReadCovered"/>): the runs (or marks) whose character formatting it shows and the paragraphs
/// whose paragraph formatting it shows. <see cref="Truncated"/> is true when the selection was too large to read whole; the lists then hold the position at the start of the selection only.
/// </summary>
public sealed record CoveredFormat(IReadOnlyList<CoveredTarget> Targets, IReadOnlyList<CoveredParagraph> Paragraphs, bool Truncated);

public sealed partial class TextEditor
{
    /// <summary>
    /// The runs and paragraphs <paramref name="selection"/> covers, for showing their formatting (nothing changes, no history operation begins). A caret covers the run before it (the one after it at the
    /// start of a paragraph, the mark in a paragraph without text), the formatting typed text takes. A selection that touches more than <paramref name="maxBlocks"/> blocks is not walked: the
    /// answer is the position at its start and <see cref="CoveredFormat.Truncated"/> is true, so a select all of a long document costs a lookup.
    /// </summary>
    public CoveredFormat ReadCovered(TextRange selection, int maxBlocks = MaxFactBlocks)
    {
        if (Resolve(selection) is not { } span)
        {
            return new CoveredFormat([], [], false);
        }

        if (selection.IsEmpty || span.LastIndex - span.FirstIndex + 1 > maxBlocks)
        {
            var truncated = !selection.IsEmpty;
            var map = Map(span.Story, span.First);
            var run = RunAt(map, span.FirstOffset, before: selection.IsEmpty);
            return new CoveredFormat([new CoveredTarget(span.Story, span.First, run)], [new CoveredParagraph(span.Story, span.First)], truncated);
        }

        var targets = new List<CoveredTarget>();
        var paragraphs = new List<CoveredParagraph>();
        WalkTouched(span, (map, start, end) =>
        {
            paragraphs.Add(new CoveredParagraph(span.Story, map.Paragraph));
            if (map.Projection.Text.Length == 0)
            {
                targets.Add(new CoveredTarget(span.Story, map.Paragraph, null));
                return;
            }

            var found = new List<CharTarget>();
            CollectTargets(span.Story, map, start, end, found, [], [], filter: null, dryRun: true);
            targets.AddRange(found.Select(t => new CoveredTarget(span.Story, t.Paragraph, t.Run)));
        });
        return new CoveredFormat(targets, paragraphs, false);
    }
}
