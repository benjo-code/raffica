using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>
/// Reads <see cref="ObjectCommandFacts"/> for the rules of the caption command of the References tab (the later field commands join this source): lazily and per question, nothing exists before the first question and
/// nothing is read for a tab without an editing session (a prefix document is a flag of the file and is answered without one). The answer is remembered for the model, its edit stamp (a refresh of a field result moves
/// it) and the selection. The asks come from the refresh of the ribbon, never from the key path.
/// </summary>
internal sealed class FieldFactsSource
{
    private ((WordDocument Model, EditStamp Stamp, TextRange Range) Key, ObjectBlock Value)? _known;

    /// <summary>How many times the place of the caret was worked out for the facts (tests; 0 before the first ask).</summary>
    public int Reads { get; private set; }

    /// <summary>The facts for the command <paramref name="id"/> of the tab.</summary>
    public ObjectCommandFacts Read(WriteTab? tab, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (tab is null || !WriteCommandIds.IsCaption(id))
        {
            return ObjectCommandFacts.Permissive;
        }

        if (tab.Docx.IsPrefix)
        {
            return new ObjectCommandFacts(ObjectBlock.PrefixDocument);
        }

        // A tab with no editing session has no caret: the command is offered until it exists (the operation decides when it runs).
        if (tab.Editor is not { BlockedReason: null } session || session.Selection is not { } range)
        {
            return ObjectCommandFacts.Permissive;
        }

        var model = tab.Docx.Model;
        var key = (model, model.Edits.Stamp, range);
        if (_known is not { } known || !known.Key.Equals(key))
        {
            try
            {
                Reads++;
                var refusal = session.Editor.RefusalForCaption(range.End, chapterNumber: false);
                known = (key, refusal is { } r ? ObjectWording.BlockOf(r) : ObjectBlock.None);
            }
            catch (StalePositionException)
            {
                // A selection that does not belong to the model any more has no facts; the next selection or edit reads again.
                return ObjectCommandFacts.Permissive;
            }

            _known = known;
        }

        return new ObjectCommandFacts(known.Value);
    }
}
