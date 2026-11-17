using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// "Update style to match": the formatting of the first paragraph of the selection becomes the style's. Its direct paragraph overrides and the
    /// character overrides that every run of the paragraph carries with an equal value are written into the style's paragraph and run properties
    /// (merged into an element the style already has, as a direct value would merge) and removed from the paragraph and its runs. <c>basedOn</c>,
    /// <c>next</c>, <c>link</c>, the other properties of the style and every other paragraph stay as they are. One history step holds the style swap
    /// and the block change; <see cref="EditResult.Style"/> carries the snapshots. A paragraph without overrides to move changes nothing (an
    /// <see cref="EditResult"/> without changes).
    /// </summary>
    /// <returns>
    /// Null, with nothing changed, when the selection touches paragraphs of different styles, when the style is not defined in the file (a paragraph
    /// without <c>w:pStyle</c> uses the file's default paragraph style) or when the file has no styles part.
    /// </returns>
    public EditResult? UpdateStyleToMatch(TextRange selection)
    {
        var revision = History.BeginOperation();
        if (_document.Styles is not { } sheet || Resolve(selection) is not { } span || PlanUpdate(sheet, selection, span) is not { } plan)
        {
            return null;
        }

        var (original, source, runs, paragraphRows, runRows) = plan;
        if (paragraphRows.Count == 0 && runRows.Count == 0)
        {
            return NoChange(span.Story, span.Last, span.LastOffset);
        }

        var old = CloneBlocks(span);
        var updated = original.Clone();
        foreach (var row in paragraphRows)
        {
            ApplyChange(updated.ParagraphProperties, new PropertyChange(row.Name, row));
            RemoveAll(source.Properties, row.Name);
        }

        foreach (var row in runRows)
        {
            ApplyChange(updated.RunProperties, new PropertyChange(row.Name, row));
            foreach (var run in runs)
            {
                RemoveAll(run.Properties, row.Name);
            }
        }

        var change = new StyleChange(original.Clone(), updated.Clone());
        sheet.Replace(original, updated);
        _document.Edits.Raise();
        var count = span.LastIndex - span.FirstIndex + 1;
        var result = new EditResult(
            new TextPosition(span.Story, span.Last, span.LastOffset),
            [new BlockChange(span.Story, span.Container, span.FirstIndex, count, count, old)],
            Style: change,
            KeepSelection: true);
        _document.Edits.Attribute(result.Changes);
        History.Record(result, new EditHistory.Point(span.FirstIndex, span.FirstOffset), revision, null);
        return result;
    }

    /// <summary>
    /// Whether <see cref="UpdateStyleToMatch"/> would move something into the style: the selection touches paragraphs of one style that the file defines and the first paragraph
    /// has an override to move. Pure: nothing changes and no history operation begins. The commands read it, so the UI never relies on the empty result of the operation.
    /// </summary>
    public bool CanUpdateStyleToMatch(TextRange selection)
    {
        return _document.Styles is { } sheet && Resolve(selection) is { } span && PlanUpdate(sheet, selection, span) is { } plan && (plan.ParagraphRows.Count > 0 || plan.RunRows.Count > 0);
    }

    /// <summary>
    /// The most blocks (top-level paragraphs, tables, content controls) a selection may touch for <see cref="ReadFormatFacts"/> to read the expensive facts: a read walks every touched paragraph
    /// with a projection, so a larger selection (a select all of a long document) is not read.
    /// </summary>
    public const int MaxFactBlocks = 200;

    /// <summary>
    /// The facts the rules of the formatting commands need about <paramref name="selection"/> (see <see cref="FormatFacts"/>). Pure: nothing changes and no history operation begins.
    /// A selection that touches more than <see cref="MaxFactBlocks"/> blocks is not read: the facts are the permissive ones (the style is taken as defined and not mixed, there is
    /// direct formatting to clear and something to update), so the commands stay usable and the operations themselves still refuse what cannot be done (Update style to match
    /// returns null for paragraphs of different styles). <see cref="FormatFacts.HasStylesPart"/> is always read.
    /// </summary>
    public FormatFacts ReadFormatFacts(TextRange selection)
    {
        var sheet = _document.Styles;
        if (Resolve(selection) is { } span && span.LastIndex - span.FirstIndex + 1 > MaxFactBlocks)
        {
            return FormatFacts.Unknown with { HasStylesPart = sheet is not null };
        }

        var view = Inspect(selection);
        var hasOverrides = view.Rows.Count > 0;
        if (sheet is null)
        {
            return new FormatFacts(false, false, false, hasOverrides, false);
        }

        var id = view.ParagraphStyleId ?? DefaultParagraphStyleId(sheet);
        var defined = !view.ParagraphStyleMixed && id is not null && sheet.Find(id) is not null;
        return new FormatFacts(true, defined, view.ParagraphStyleMixed, hasOverrides, defined && CanUpdateStyleToMatch(selection));
    }

    // The paragraph style a paragraph uses: its own, or the default paragraph style of the file when it names none (null when the file has no default).
    private static string? EffectiveParagraphStyleId(StyleSheet sheet, Paragraph paragraph) => paragraph.Properties.StyleId ?? DefaultParagraphStyleId(sheet);

    private static string? DefaultParagraphStyleId(StyleSheet sheet) => sheet.Styles.FirstOrDefault(s => s.IsDefault && s.Type == "paragraph")?.Id;

    private sealed record UpdatePlan(Style Original, Paragraph Source, List<Run> Runs, List<XElement> ParagraphRows, List<XElement> RunRows);

    // What an update would move, or null when the style cannot be updated (different styles, a style the file does not define, no paragraph).
    private UpdatePlan? PlanUpdate(StyleSheet sheet, TextRange selection, Span span)
    {
        var paragraphs = Scan(selection).Paragraphs.Select(p => p.Paragraph).ToList();
        if (paragraphs.Count == 0)
        {
            return null;
        }

        var ids = paragraphs.Select(p => EffectiveParagraphStyleId(sheet, p)).Distinct().ToList();
        if (ids.Count != 1 || ids[0] is not { } id || sheet.Find(id) is not { } original)
        {
            return null;
        }

        var source = paragraphs[0];
        var runs = Map(span.Story, source).Items.Select(i => i.Run).Distinct().ToList();
        var paragraphRows = FormattingInspector.OverridesOf(OverrideScope.Paragraph, source.Properties.Children).ToList();
        var runRows = runs.Count == 0
            ? []
            : FormattingInspector.OverridesOf(OverrideScope.Run, runs[0].Properties.Children)
                .Where(row => runs.All(r => r.Properties.Get(row.Name) is { } other && XNode.DeepEquals(other, row)))
                .ToList();
        return new UpdatePlan(original, source, runs, paragraphRows, runRows);
    }
}
