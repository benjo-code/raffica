using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Viewing;

/// <summary>
/// The caption command of the page view (the Caption card of the References tab): the insert is one <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> (one history step: undo takes the paragraph away and the
/// numbers follow with the next refresh). The caption labels what the user has selected: the cells of a table (the caption stands above or below the table), a picture (above or below its paragraph) or else the paragraph of the
/// caret. A command that finds nothing to do says why through <see cref="StructureNote"/>, never silently. A prefix document is refused (the editor cannot see that the body was cut, so the command passes the flag).
/// </summary>
internal sealed partial class PageView
{
    /// <summary>What the Caption card needs; read from the model when the card opens, never earlier.</summary>
    internal CaptionContext CaptionCardContext()
    {
        var model = _tab.Docx.Model;
        var onTable = _tab.Editor?.ObjectSelection is CellSelection;
        var ui = WriteStrings.Culture;
        return new CaptionContext(
            CaptionLabels.Known(model),
            onTable ? CaptionLabels.Table : CaptionLabels.Figure,
            HeadingNumbering.StateOf(model) == HeadingNumberingState.On,
            label => CaptionLabels.WordFor(model, label, ui));
    }

    /// <summary>Runs the answer of the Caption card; true when it was taken (also when it changed nothing), false for a released view or a request that is not well formed.</summary>
    internal bool RunCaptionCommand(CaptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_released || !request.IsValid)
        {
            return false;
        }

        var prefix = IsPrefixDocument;
        if (prefix)
        {
            StructureNote?.Invoke(WriteStrings.ReasonSectionPrefix);
            return true;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        var model = _tab.Docx.Model;
        var word = CaptionLabels.WordFor(model, request.Label, WriteStrings.Culture);
        var host = _tab.RefreshesFields ? _tab.EnsureFields().Host : null;
        var result = ApplyGuarded(session, ed =>
        {
            if (session.ObjectSelection is CellSelection cells)
            {
                return ed.InsertCaption(cells.Table, request.Label, request.Text, request.Place, request.ChapterNumber, word, host, prefix);
            }

            if (session.TargetPicture() is { } picture && ed.PositionAfterPicture(picture) is { } behind)
            {
                return ed.InsertCaption(behind, request.Label, request.Text, request.Place, request.ChapterNumber, word, host, prefix);
            }

            // Above the first paragraph of the selection, below the last one.
            return session.Selection?.Normalized() is { } selection
                ? ed.InsertCaption(request.Place == CaptionPlace.Above ? selection.Start : selection.End, request.Label, request.Text, request.Place, request.ChapterNumber, word, host, prefix)
                : null;
        });

        // The rules said yes but the editor had nothing to do: a visible note with the editor's reason, never silence. A table of contents has said so already (ApplyGuarded); a save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null && session.Editor.LastCaptionRefusal != CaptionRefusal.InsideToc)
        {
            StructureNote?.Invoke(RefusalWords(session.Editor.LastCaptionRefusal) ?? WriteStrings.NoteStructureFailed);
        }

        return true;
    }

    // The words for the editor's refusal of a caption, or null when it left none.
    private static string? RefusalWords(CaptionRefusal? refusal) => refusal switch
    {
        CaptionRefusal.Prefix => WriteStrings.ReasonSectionPrefix,
        CaptionRefusal.NotInBody or CaptionRefusal.NoPosition => WriteStrings.ReasonSectionNotInBody,
        CaptionRefusal.RestrictedControl => WriteStrings.ReasonCaptionRestricted,
        CaptionRefusal.SectionEnd => WriteStrings.NoteCaptionSectionEnd,
        CaptionRefusal.ChapterNeedsNumberedHeadings => WriteStrings.NoteCaptionChapter,
        _ => null,
    };
}
