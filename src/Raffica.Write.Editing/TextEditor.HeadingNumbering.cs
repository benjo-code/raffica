using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    private ListCatalog? _lists;

    /// <summary>
    /// The one list catalog of this editor, made on first use (never before). Every list command takes its definitions from it, so a run
    /// the catalog made is found again instead of the numbering part growing with each command.
    /// </summary>
    internal ListCatalog Lists => _lists ??= new ListCatalog(_document);

    /// <summary>
    /// Numbers the headings (<paramref name="on"/>) or stops numbering them: the nine styles Heading1 to Heading9 gain <c>w:numPr</c> of the heading
    /// list (<c>ilvl</c> from level 2 on) or lose it, in one history step. Switching on takes the list from <see cref="ListCatalog.HeadingList"/> and gives
    /// its levels the <c>w:pStyle</c> links Heading1 to Heading9 when they lack them; the definition and its <c>w:num</c> stay when the step is undone
    /// (a definition is never removed), so a redo and a later switch on find them. A style that already carries a list (or any <c>numPr</c> while
    /// switching off with no list) is not touched, and direct <c>numPr</c> of paragraphs never is. The step is a <see cref="StyleChange"/> with eight
    /// companions and no block change (<see cref="EditResult.Changes"/> is empty): the session restyles the layout.
    /// </summary>
    /// <param name="on">True to number the headings, false to remove the numbers.</param>
    /// <param name="caret">Where the caret is (the result and an undo put it back there); the start of the body when null.</param>
    /// <returns>Null, with nothing changed, when the state already is the wanted one or the file has no heading styles to switch.</returns>
    public EditResult? SetHeadingNumbering(bool on, TextPosition? caret = null)
    {
        var revision = History.BeginOperation();
        if (HeadingNumbering.StateOf(_document) != (on ? HeadingNumberingState.Off : HeadingNumberingState.On)
            || HeadingNumbering.HeadingStyles(_document) is not { } styles
            || _document.Styles is not { } sheet
            || (caret ?? StartOfBody()) is not { } position)
        {
            return null;
        }

        var numId = 0;
        if (on)
        {
            numId = Lists.HeadingList();
            if (_document.Numbering?.FindInstance(numId)?.AbstractNumId is { } abstractId && _document.Numbering.FindAbstract(abstractId) is { } definition)
            {
                HeadingNumbering.LinkLevels(definition);
            }
        }

        var changes = new List<StyleChange>();
        for (var i = 0; i < styles.Length; i++)
        {
            var original = styles[i];
            var carries = original.ParagraphProperties.NumberingId is > 0;
            if (carries == on)
            {
                continue;
            }

            var updated = original.Clone();
            if (on)
            {
                updated.ParagraphProperties.Set(W + "numPr", HeadingNumbering.NumPr(numId, i));
            }
            else
            {
                updated.ParagraphProperties.Remove(W + "numPr");
            }

            changes.Add(new StyleChange(original.Clone(), updated.Clone()));
            sheet.Replace(original, updated);
        }

        if (changes.Count == 0)
        {
            return null;
        }

        _document.Edits.Raise();
        var result = new EditResult(position, [], Style: changes[0] with { Companions = changes.Skip(1).ToList() }, KeepSelection: true);
        _document.Edits.Attribute(result.Changes);
        History.Record(result, new EditHistory.Point(Math.Max(0, position.Story.Kind == StoryKind.Body ? _document.Body.Blocks.IndexOf(position.Paragraph) : 0), position.Offset), revision, null);
        return result;
    }

    private TextPosition? StartOfBody() =>
        _document.Body.Blocks.OfType<Paragraph>().FirstOrDefault() is { } first ? new TextPosition(StoryRef.Body, first, 0) : null;
}
