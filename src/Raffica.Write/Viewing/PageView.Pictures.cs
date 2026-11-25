using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// The picture commands and gestures of the page view (milestone 4.5a): the commands of the Picture group of the Insert tab end in <see cref="RunPictureCommand"/>; the eight handles of the selected picture resize it, a
/// floating picture is dragged to another paragraph, and the arrows nudge it. Every change is one <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> (one history step); a gesture changes the document
/// once, on release, and the overlay draws the outline of the result while the pointer is down.
/// </summary>
/// <remarks>
/// Rules. A command reads the picture from the object selection as it is when the operation runs and asks the editor again whether it may run, so a stale answer of the facts never changes the wrong picture; a command
/// that finds nothing to do says so through <see cref="StructureNote"/>, never silently. Pictures are selected in the page view only, so the commands that need one are disabled in the writing view with their reason
/// (Insert picture works in both views). A picture is inserted at the end of the text selection (a selected text stays) and is not selected afterwards. Resize: a corner keeps the proportions and Shift frees them, an edge
/// changes one side; each side stays between <see cref="PictureGestures.MinSizePt"/> and <see cref="PictureGestures.MaxSizePt"/>; the model changes once, on release, by <c>ResizePicture</c>. A turned picture has no
/// handles (its layout box is not resizable). Move: only a floating picture is dragged (a press on an inline picture selects it); the anchor goes to the paragraph under the pointer and the offsets are worked out so that
/// the picture's corner lands where the pointer has carried it (<see cref="PictureGestures.MoveOffsets"/>); over a table cell, a header, a footer, a note or the desk the cursor says "no" and a release changes nothing.
/// The arrows nudge a selected floating picture (1 point, Shift 10 points: one history step per second of nudges); for an inline picture they do what they do without a selection (the caret moves and the selection
/// ends). Nothing here reads a model on a pointer move except the object map of the page under the pointer and, while a picture is dragged, one text hit test.
/// </remarks>
internal sealed partial class PageView
{
    private PictureBox? _pictureBox;
    private int _picturePage;
    private (float X, float Y) _pictureStart;
    private KeyModifiers _pointerModifiers;
    private PictureDrop? _drop;

    private readonly record struct PictureDrop(TextPosition? Position, int Page, bool Allowed);

    // ---- commands -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a picture command; true when it was taken (also when it changed nothing), false for a released view, an unknown command or a payload that is not a value of the command (nothing changes then).
    /// <paramref name="payload"/> is a <see cref="PictureFile"/> for Insert picture, a wrap id for Text wrapping and a <see cref="PictureProperties"/> for Picture properties.
    /// </summary>
    internal bool RunPictureCommand(string id, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_released || !WriteCommandIds.IsPicture(id) || !PicturePayloads.IsValid(id, payload))
        {
            return false;
        }

        var prefix = IsPrefixDocument;
        if (prefix)
        {
            StructureNote?.Invoke(WriteStrings.ReasonPicturePrefixDocument);
            return true;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        PictureRefusal? refusal = null;
        var result = ApplyGuarded(session, ed => session.Selection is { } selection ? PictureOperation(ed, session, id, payload, selection, out refusal) : null);

        // The rules said yes but the editor had nothing to do: a visible note, never silence. A save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(refusal is { } reason ? PictureWording.ReasonText(reason) : WriteStrings.NotePictureFailed);
        }

        return true;
    }

    // The operation, under the lease and on the model as it is now: the editor is asked first (a refusal ends the command with its reason), then it runs.
    private static EditResult? PictureOperation(TextEditor ed, EditSession session, string id, object? payload, TextRange selection, out PictureRefusal? refusal)
    {
        refusal = null;
        if (id == WriteCommandIds.PictureInsert)
        {
            if (payload is not PictureFile file)
            {
                return null;
            }

            refusal = ed.RefusalForInsertPicture(selection.End, file.Bytes, file.AltText);
            return refusal is null ? ed.InsertPicture(selection.End, file.Bytes, file.AltText) : null;
        }

        if (session.ObjectSelection is not PictureSelection picture)
        {
            refusal = PictureRefusal.NoPicture;
            return null;
        }

        var target = picture.Id;
        switch (id)
        {
            case WriteCommandIds.PictureProperties when payload is PictureProperties properties:
                refusal = ed.RefusalForSetPictureProperties(target, properties);
                return refusal is null ? ed.SetPictureProperties(target, properties) : null;
            case WriteCommandIds.PictureWrap when payload is string wrapId && PicturePayloads.TryWrap(wrapId, out var wrap):
                refusal = ed.RefusalForPictureWrap(target, wrap);
                return refusal is null ? ed.SetPictureWrap(target, wrap) : null;
            case WriteCommandIds.PictureReset:
                refusal = ed.RefusalForResetPictureSize(target);
                return refusal is null ? ed.ResetPictureSize(target) : null;
            case WriteCommandIds.PictureDelete:
                refusal = ed.RefusalForDeletePicture(target);
                return refusal is null ? ed.DeletePicture(target) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// What the Picture properties card shows: the size, description and wrap of the selected picture, read from the model now. Null, with <paramref name="note"/> naming why, when there is nothing to describe (no
    /// picture selected, a prefix document, a document that cannot be edited).
    /// </summary>
    internal PicturePropertiesContext? PicturePropertiesAtSelection(out string? note)
    {
        note = null;
        if (IsPrefixDocument)
        {
            note = WriteStrings.ReasonPicturePrefixDocument;
            return null;
        }

        if (_tab.Editor is not { ObjectSelection: PictureSelection picture, SelectedDrawing: { } drawing } session || PictureView.Of(drawing) is not { } info)
        {
            note = WriteStrings.ReasonPictureNoSelection;
            return null;
        }

        if (session.Editor.RefusalForDeletePicture(picture.Id) is { } refusal)
        {
            note = PictureWording.ReasonText(refusal);
            return null;
        }

        var canFloat = session.Editor.RefusalForPictureWrap(picture.Id, PictureWrap.Square) is not PictureRefusal.FloatInCell;
        return new PicturePropertiesContext(Twips(info.ExtentEmu.Width), Twips(info.ExtentEmu.Height), info.AltText ?? string.Empty, PicturePayloads.IdOf(info.Wrap), info.IsRotated, canFloat, MarginsInInches());
    }

    private static int Twips(long emu) => (int)Math.Clamp((emu + 317) / 635, 1, int.MaxValue);

    // ---- keys ---------------------------------------------------------------------------------------------------------------------

    // The arrows with a picture selected: a floating picture is nudged (1 point, Shift 10; a held key says why it cannot only once); an inline one is not claimed, so the key moves the caret and the selection ends. True when the key was claimed.
    private bool NudgeSelectedPicture(PictureSelection picture, Key key, KeyModifiers modifiers, bool repeat)
    {
        if (modifiers is not (KeyModifiers.None or KeyModifiers.Shift) || _tab.Editor is not { SelectedDrawing: { } drawing } session || PictureView.Of(drawing) is not { IsFloating: true })
        {
            return false;
        }

        var step = modifiers == KeyModifiers.Shift ? 10 : 1;
        var (dx, dy) = key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            _ => (0, step),
        };
        var prefix = IsPrefixDocument;
        PictureRefusal? refusal = null;
        var result = ApplyGuarded(session, ed =>
        {
            refusal = ed.RefusalForNudgePicture(picture.Id, dx, dy, prefix);
            return refusal is null ? ed.NudgePicture(picture.Id, dx, dy, prefix) : null;
        });
        if (result is null && session.BlockedReason is null && !repeat)
        {
            StructureNote?.Invoke(refusal is { } reason ? PictureWording.ReasonText(reason) : WriteStrings.NotePictureFailed);
        }

        return true;
    }

    // ---- press and move ---------------------------------------------------------------------------------------------------------

    // Remembers what a picture gesture needs of its press: the box as the page drew it, the page and the point on it in points.
    private void RememberPicturePress(PictureBox box, int page, float x, float y)
    {
        _pictureBox = box;
        _picturePage = page;
        _pictureStart = (x, y);
    }

    // The pointer moved while a picture is dragged: where it would land, and the cursor that says so.
    private void UpdatePictureDrop(Point point)
    {
        _drop = DropTargetAt(point);
        SetCursorType(_drop is { Allowed: true } ? StandardCursorType.SizeAll : StandardCursorType.No);
    }

    // The place under a point where a dragged picture may be dropped: a paragraph of the body that is not in a table cell. Anything else (a cell, a header, a footer, a note, the desk) is no drop.
    private PictureDrop? DropTargetAt(Point point)
    {
        if (_mode != WriteView.Page || ActiveLayout() is not { } layout || !TryMapPoint(point, out var page, out var x, out var y) || !layout.TryGetPage(page, out var laid) || laid is null)
        {
            return null;
        }

        if (x < 0 || y < 0 || x > laid.Width || y > laid.Height)
        {
            return new PictureDrop(null, page, false);
        }

        foreach (var grid in laid.Objects.Tables)
        {
            if (TableGestures.CellAt(grid, x, y) is not null)
            {
                return new PictureDrop(null, page, false);
            }
        }

        if (layout.HitTestHeaderFooter(page, x, y) is not null || layout.HitTest(page, x, y) is not { } hit || hit.Position.Story.Kind != StoryKind.Body)
        {
            return new PictureDrop(null, page, false);
        }

        return new PictureDrop(hit.Position, page, true);
    }

    // A point of the view in points of a page (the page may be another than the pointer is over).
    private (double X, double Y)? PagePoint(int page, Point point)
    {
        if (_scroller is not { } scroller)
        {
            return null;
        }

        var rect = scroller.RectOf(page).Translate(new Vector(LeftShift(scroller), -_offsetY));
        return ((point.X - rect.X) / scroller.Scale, (point.Y - rect.Y) / scroller.Scale);
    }

    // ---- commit -------------------------------------------------------------------------------------------------------------------

    // The commit of a gesture on a picture, called once on release. True when the document changed.
    private bool CommitPictureDrag(ObjectDragKind kind, ObjectDragEnd end)
    {
        if (_released || IsPrefixDocument || _tab.Editor is not { IsSaving: false } session || _scroller is null || _pictureBox is not { } box || session.ObjectSelection is not PictureSelection selected)
        {
            return false;
        }

        return kind switch
        {
            PictureResize resize => CommitResize(session, selected, box, resize.Slot, end),
            PictureMove => CommitMove(session, selected, box, end),
            _ => false,
        };
    }

    private bool CommitResize(EditSession session, PictureSelection selected, PictureBox box, HandleSlot slot, ObjectDragEnd end)
    {
        if (PagePoint(_picturePage, end.End) is not { } point)
        {
            return false;
        }

        var frame = PictureGestures.Resize(new Rect(box.X, box.Y, box.Width, box.Height), slot, point.X - _pictureStart.X, point.Y - _pictureStart.Y, _pointerModifiers.HasFlag(KeyModifiers.Shift));
        var (width, height) = (ToEmu(frame.Width), ToEmu(frame.Height));
        var prefix = IsPrefixDocument;
        PictureRefusal? refusal = null;
        var result = ApplyGuarded(session, ed =>
        {
            refusal = ed.RefusalForResizePicture(selected.Id, width, height, prefix);
            return refusal is null ? ed.ResizePicture(selected.Id, width, height, prefix) : null;
        });
        return Finished(session, result, refusal);
    }

    private static long ToEmu(double points) => Math.Clamp((long)Math.Round(points * PictureRules.EmuPerPoint), 1, PictureRules.MaxExtentEmu);

    private bool CommitMove(EditSession session, PictureSelection selected, PictureBox box, ObjectDragEnd end)
    {
        // Where the pointer released says where the picture goes; over anything that is no drop the cursor said so and nothing happens.
        if (DropTargetAt(end.End) is not { Allowed: true, Position: { } target } drop || ActiveLayout() is not { } layout || session.SelectedDrawing is not { } drawing
            || PictureView.Of(drawing) is not { Anchor: { } anchor } || PagePoint(drop.Page, end.End) is not { } point)
        {
            return false;
        }

        var anchorPosition = new TextPosition(target.Story, target.Paragraph, 0);
        if (layout.Caret(anchorPosition, drop.Page) is not { } paragraph)
        {
            StructureNote?.Invoke(WriteStrings.NotePictureFailed);
            return false;
        }

        // The corner of the picture goes where the pointer has carried it (the grab point stays under the pointer). A paragraph whose first line is on another page than the pointer is over keeps its line's top.
        var desiredX = point.X - (_pictureStart.X - box.X);
        var desiredY = paragraph.Page == drop.Page ? point.Y - (_pictureStart.Y - box.Y) : paragraph.Top;
        var (dx, dy) = PictureGestures.MoveOffsets(anchor, new Rect(box.X, box.Y, box.Width, box.Height), desiredX, desiredY, paragraph.X, paragraph.Top);
        var prefix = IsPrefixDocument;
        PictureRefusal? refusal = null;
        var result = ApplyGuarded(session, ed =>
        {
            refusal = ed.RefusalForMovePicture(selected.Id, anchorPosition, prefix);
            return refusal is null ? ed.MovePicture(selected.Id, anchorPosition, dx, dy, prefix) : null;
        });

        // The editor ends the object selection of a move (the picture is in another paragraph now): select it again in the same pass, so the handles do not blink.
        if (result?.Caret is { } caret && session.Editor.PictureAt(caret) is { } moved)
        {
            _ = session.SetObjectSelection(new PictureSelection(moved));
        }

        return Finished(session, result, refusal);
    }

    // The end of a gesture that wrote: a refusal is a note (a refusal that says "nothing to change" and a save that queued the write say nothing).
    private bool Finished(EditSession session, EditResult? result, PictureRefusal? refusal)
    {
        if (result is null && session.BlockedReason is null && refusal is not PictureRefusal.NothingToChange)
        {
            StructureNote?.Invoke(refusal is { } reason ? PictureWording.ReasonText(reason) : WriteStrings.NotePictureFailed);
        }

        return result is not null;
    }

    // ---- cursor and drawing ---------------------------------------------------------------------------------------------------

    // The cursor of the selected picture under the pointer: the resize arrow over a handle, the move cross over a floating picture; null for anything else. Only the page under the pointer is hit-tested and the id is never read.
    private static StandardCursorType? PictureCursorAt(DocumentLayout layout, EditSession session, int page, float x, float y, float tolerance)
    {
        if (session.SelectedDrawing is not { } drawing)
        {
            return null;
        }

        return layout.HitObject(page, x, y, tolerance, drawing, false) switch
        {
            ObjectHit.Handle handle => PictureGestures.CursorOf(handle.Slot),
            ObjectHit.Picture { Box: { Floating: true } box } when box.Shows(drawing) => StandardCursorType.SizeAll,
            _ => null,
        };
    }

    // The tint of the paragraph a dragged picture would be anchored to (the first line of it), over the page like the handles; nothing is made when no picture is dragged.
    private void DrawPictureDrop(DrawingContext context, PageScroller scroller, DocumentLayout layout, IBrush tint)
    {
        if (_objectDrag is not { Phase: ObjectDragPhase.Dragging, Kind: PictureMove } || _drop is not { Allowed: true, Position: { } position } drop)
        {
            return;
        }

        var start = new TextPosition(position.Story, position.Paragraph, 0);
        var page = scroller.RectOf(drop.Page).Translate(new Vector(LeftShift(scroller), -_offsetY));
        using var clip = context.PushClip(page);
        using var opacity = context.PushOpacity(SelectionOpacity);
        var end = start with { Offset = ProjectionOf(layout, start).Text.Length };
        var boxes = layout.SelectionBoxes(new TextRange(start, end), drop.Page);
        if (boxes.Count > 0)
        {
            var first = boxes[0];
            context.FillRectangle(tint, new Rect(page.X + (first.X * scroller.Scale), page.Y + (first.Y * scroller.Scale), first.Width * scroller.Scale, first.Height * scroller.Scale));
        }
        else if (layout.Caret(start, drop.Page) is { } caret)
        {
            context.FillRectangle(tint, new Rect(page.X + (caret.X * scroller.Scale), page.Y + (caret.Top * scroller.Scale), 40 * scroller.Scale, caret.Height * scroller.Scale));
        }
    }
}

