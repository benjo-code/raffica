using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>Why a picture command cannot run at the caret or on the selected picture (<see cref="None"/>: it can).</summary>
internal enum PictureBlock
{
    None,

    /// <summary>Only the first blocks of the file are open: the editor cannot see that the body was cut, so every picture command is refused.</summary>
    PrefixDocument,

    /// <summary>The command acts on the selected picture and none is selected (a picture is selected in the page view only).</summary>
    NoSelection,

    /// <summary>The editor refuses the command; <see cref="PictureCommandFacts.Refusal"/> says why.</summary>
    Refused,
}

/// <summary>What the rules of the picture commands need to know; <paramref name="Block"/> is the reason the asked command cannot run.</summary>
internal sealed record PictureCommandFacts(PictureBlock Block, PictureRefusal? Refusal = null)
{
    /// <summary>The facts of a tab that has not been asked: every command is offered (the operation decides when it runs).</summary>
    public static PictureCommandFacts Permissive { get; } = new(PictureBlock.None);
}

/// <summary>The words for the reasons of the picture commands. They name neither a file, nor bytes, nor a description.</summary>
internal static class PictureWording
{
    /// <summary>The words for the facts; null when the command may run.</summary>
    public static string? ReasonText(PictureCommandFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Block switch
        {
            PictureBlock.PrefixDocument => WriteStrings.ReasonPicturePrefixDocument,
            PictureBlock.NoSelection => WriteStrings.ReasonPictureNoSelection,
            PictureBlock.Refused => facts.Refusal is { } refusal ? ReasonText(refusal) : WriteStrings.NotePictureFailed,
            _ => null,
        };
    }

    /// <summary>The words for a refusal of the editor (<c>ReasonPicture&lt;Name&gt;</c>; a guard test asks that every member has its words in both languages).</summary>
    public static string ReasonText(PictureRefusal refusal) => WriteStrings.Get(KeyOf(refusal));

    /// <summary>The string key of a refusal.</summary>
    public static string KeyOf(PictureRefusal refusal) => "ReasonPicture" + refusal;
}

/// <summary>
/// Reads <see cref="PictureCommandFacts"/> for the rules of the picture commands, lazily per command like <see cref="TableFactsSource"/>: nothing exists before the first question and nothing is read for a tab
/// without an editing session (a prefix document is answered from a flag). An answer is remembered for the model, its edit stamp, the text selection and the object selection, so a refresh of the ribbon asks the
/// editor once per command and change, never per key and never per frame. A picture is selected in the page view only, so in the writing view the commands that need one say so.
/// </summary>
internal sealed class PictureFactsSource
{
    private (WordDocument Model, EditStamp Stamp, TextRange Range, ObjectSelection? Objects)? _key;
    private readonly Dictionary<string, PictureCommandFacts> _known = [];

    /// <summary>How many times the editor was asked for the facts (tests; 0 before the first ask of a picture command).</summary>
    public int Reads { get; private set; }

    /// <summary>The facts for the command <paramref name="id"/> of the tab.</summary>
    public PictureCommandFacts Read(WriteTab? tab, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (tab is null)
        {
            return PictureCommandFacts.Permissive;
        }

        if (tab.Docx.IsPrefix)
        {
            return new PictureCommandFacts(PictureBlock.PrefixDocument);
        }

        if (tab.Editor is not { BlockedReason: null } session || session.Selection is not { } range)
        {
            return id == WriteCommandIds.PictureInsert ? PictureCommandFacts.Permissive : new PictureCommandFacts(PictureBlock.NoSelection);
        }

        var model = tab.Docx.Model;
        var key = (model, model.Edits.Stamp, range, session.ObjectSelection);
        if (_key is not { } known || !known.Equals(key))
        {
            _key = key;
            _known.Clear();
        }

        if (_known.TryGetValue(id, out var cached))
        {
            return cached;
        }

        try
        {
            Reads++;
            var facts = Evaluate(session, id, range);
            _known[id] = facts;
            return facts;
        }
        catch (StalePositionException)
        {
            // A selection that does not belong to the model any more has no facts; the next selection or edit reads again.
            return PictureCommandFacts.Permissive;
        }
    }

    /// <summary>Asks the editor whether the command may run (it changes nothing); the page view asks it again when the command runs, so a stale answer can never run a refused command.</summary>
    internal static PictureCommandFacts Evaluate(EditSession session, string id, TextRange selection)
    {
        var editor = session.Editor;
        if (id == WriteCommandIds.PictureInsert)
        {
            return Of(editor.RefusalForPicture(selection.End));
        }

        if (session.TargetPicture() is not { } picture)
        {
            return new PictureCommandFacts(PictureBlock.NoSelection);
        }

        switch (id)
        {
            case WriteCommandIds.PictureProperties:
                // The card can change a part the picture allows: only a picture that is no longer there is refused.
                return Of(editor.RefusalForDeletePicture(picture));
            case WriteCommandIds.PictureWrap:
                // Square is the probe: the wrap the picture already has is no reason to disable the picker.
                var wrap = editor.RefusalForPictureWrap(picture, PictureWrap.Square);
                return Of(wrap is PictureRefusal.NothingToChange ? null : wrap);
            case WriteCommandIds.PictureReset:
                return Of(editor.RefusalForResetPictureSize(picture));
            case WriteCommandIds.PictureDelete:
                return Of(editor.RefusalForDeletePicture(picture));
            default:
                return PictureCommandFacts.Permissive;
        }
    }

    private static PictureCommandFacts Of(PictureRefusal? refusal) => refusal is { } r ? new PictureCommandFacts(PictureBlock.Refused, r) : PictureCommandFacts.Permissive;
}
