using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Sets or removes direct paragraph properties on every paragraph the selection touches, tables, cells and block content controls between
    /// its ends included. Nothing else of a paragraph changes. One history step.
    /// </summary>
    public EditResult ApplyParagraphChanges(TextRange selection, IReadOnlyList<PropertyChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return FormatParagraphs(selection, paragraph =>
        {
            foreach (var change in changes)
            {
                ApplyChange(paragraph.Properties, change);
            }
        });
    }

    /// <summary>
    /// Sets <c>w:pStyle</c> on every paragraph the selection touches (null removes it). Direct overrides stay; a style id the file does not define
    /// is written as given.
    /// </summary>
    public EditResult ApplyParagraphStyle(TextRange selection, string? styleId)
    {
        if (styleId is { Length: 0 })
        {
            throw new ArgumentException("A style id is not empty (null removes the style).", nameof(styleId));
        }

        return FormatParagraphs(selection, paragraph => paragraph.Properties.StyleId = styleId);
    }

    /// <summary>Sets <c>w:rStyle</c> on the runs the selection covers (null removes it), with the run splitting and merging of <see cref="ApplyCharacterChanges"/>.</summary>
    public EditResult ApplyCharacterStyle(TextRange selection, string? styleId)
    {
        if (styleId is { Length: 0 })
        {
            throw new ArgumentException("A style id is not empty (null removes the style).", nameof(styleId));
        }

        return FormatSelection(selection, _ => target => target.Bag.StyleId = styleId);
    }

    // The paragraph twin of FormatSelection: clone the touched top-level blocks, change every paragraph, one BlockChange, one history step.
    private EditResult FormatParagraphs(TextRange selection, Action<Paragraph> change)
    {
        var revision = History.BeginOperation();
        if (Resolve(selection) is not { } span)
        {
            return NoChange(selection.Start);
        }

        var old = CloneBlocks(span);
        for (var i = span.FirstIndex; i <= span.LastIndex; i++)
        {
            VisitWholeBlock(span.Container[i], change, 0);
        }

        _document.Edits.Raise();
        var count = span.LastIndex - span.FirstIndex + 1;
        var result = new EditResult(
            new TextPosition(span.Story, span.Last, span.LastOffset),
            [new BlockChange(span.Story, span.Container, span.FirstIndex, count, count, old)],
            KeepSelection: true);
        History.Record(result, new EditHistory.Point(span.FirstIndex, span.FirstOffset), revision, null);
        return result;
    }
}
