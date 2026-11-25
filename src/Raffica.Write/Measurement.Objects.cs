using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using SkiaSharp;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write;

/// <summary>
/// The objects run (<c>--measure-objects</c>), the gesture run (<c>--measure-gesture</c>) and the typing run at objects (<c>--measure-typing-objects</c>) of <see cref="Measurement"/>: the table, picture and note
/// commands of milestone 4.5a through the page view, as the Insert tab, the Table tab or a key would run them, with the time the typing run judges (the handler, the render pass that shows the result and the page
/// draws of that frame; the frame latency is recorded). Rows <c>objects case=&lt;case&gt; view=page n=… median-ms=… p95-ms=… max-ms=… handler-median-ms=… frame-median-ms=… …</c> (the words of a typing row) for the
/// cases of <see cref="ObjectPlaces.Names"/>: a 5 by 5 table inserted, a row added, two cells merged, borders set on them, a picture inserted (the card's operation), pasted from a clipboard that holds a picture,
/// resized and moved (a floating picture to the paragraph two blocks on), a footnote inserted and the reference of one deleted; after each case one <c>objects-refresh case=… view=page n=1 ms=…</c> row (the debounced
/// refresh of the ribbon states that follows the last change). Every run is undone outside the timing; what a case needs before its command (the caret, the cell selection, the inserted and selected picture, the
/// wrapped picture) is made in frames of its own, as the structure run places itself, because a caret set in the same frame as the command is the one the last undo left. The judged metric <c>objects</c> of
/// <c>measure.cs</c> is the worst median of the ten rows against a fixed limit (see <c>CheckCommand.ObjectsBudget</c>).
/// <para>
/// The gesture run presses at the object of each kind of <see cref="GesturePlaces.Names"/> (a column border, the right edge of a table, a row handle, a cell, the corner handle of a selected picture and a floating
/// picture), then raises one <c>PointerMoved</c> per animation frame along a seeded path, as a hand drags; a move is judged as a key is (the handler, the render pass of the overlay layer that draws the guide line, the
/// outline or the cell tint, and any page draw of that frame). The gesture is cancelled at the end of its kind (Escape), so no gesture changes the document; the preparation (an inserted picture) is undone. Rows
/// <c>gesture case=&lt;kind&gt; view=page n=… median-ms=… p95-ms=… …</c>; the metric <c>gesture</c> is the worst p95 of the six against 16 ms.
/// </para>
/// <para>
/// The typing run at objects types at the end of a picture in a paragraph of the thesis (a picture is inserted there first, outside the timing) and in a table cell, in both views; its rows are the rows of a typing
/// run (case <c>picture-paragraph</c> and <c>table-cell</c>) and count into the judged metric <c>typing</c>.
/// </para>
/// </summary>
internal sealed partial class Measurement
{
    /// <summary>The runs per case when the count is the default one.</summary>
    public const int ObjectsRuns = 20;

    /// <summary>The pointer moves per drag kind when the count is the default one.</summary>
    public const int GestureMoves = 100;

    private readonly bool _objectsRun;
    private readonly bool _gestureRun;
    private readonly bool _typingObjectsRun;
    private int _objectsStage;
    private int _objectsOffset;
    private bool _gestureDown;
    private Point[] _gesturePath = [];
    private readonly Pointer _pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

    // A 64 by 48 picture of one colour, the bytes the picture card and the clipboard hand over.
    private static byte[] ObjectPicture()
    {
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(new SKColor(0x33, 0x66, 0x99));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static readonly byte[] s_picture = ObjectPicture();

    // The clipboard of the paste case: one picture, handed over at once (the run does not read the system clipboard).
    private sealed class PictureClipboard : IEditClipboard
    {
        public Task<ClipboardContent?> ReadAsync() => Task.FromResult<ClipboardContent?>(new ClipboardContent(string.Empty, null, Image: s_picture));

        public Task<bool> WriteAsync(ClipboardContent content) => Task.FromResult(false);
    }

    // ---- places -------------------------------------------------------------------------------------------------------------------

    private static Paragraph? FirstCellParagraph(Table table, int cell)
    {
        foreach (var row in table.Rows)
        {
            if (row is TableRow tableRow && tableRow.Items.Count > cell && tableRow.Items[cell] is TableCell found)
            {
                return found.Blocks.OfType<Paragraph>().FirstOrDefault();
            }
        }

        return null;
    }

    // The table nearest the middle of the body whose first two cells merge and that has at least three columns (a border to drag, an edge to move): its first cell's paragraph is the place.
    private static TypingPlace? TablePlace(PageView view, bool needsBorders)
    {
        var model = view.Tab.Docx.Model;
        var blocks = model.Body.Blocks;
        var editor = view.Tab.EnsureEditor().Editor;
        foreach (var i in Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Table).OrderBy(i => Math.Abs(i - (blocks.Count / 2))))
        {
            var table = (Table)blocks[i];
            var paragraph = FirstCellParagraph(table, 0);
            if (paragraph is null || editor.RefusalForMerge(new CellRange(new TableRef(StoryRef.Body, i), 0, 0, 0, 1)) is not null)
            {
                continue;
            }

            if (needsBorders && table.Rows.OfType<TableRow>().FirstOrDefault()?.Items.OfType<TableCell>().Count() is not >= 3)
            {
                continue;
            }

            return new TypingPlace("table", StoryRef.Body, paragraph, 0, i);
        }

        return null;
    }

    // The paragraph nearest the middle of the body with a footnote reference, at the start of the reference.
    private static TypingPlace? NotePlace(PageView view)
    {
        var model = view.Tab.Docx.Model;
        var blocks = model.Body.Blocks;
        var layout = view.Runner.PageLayout;
        foreach (var i in Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph).OrderBy(i => Math.Abs(i - (blocks.Count / 2))))
        {
            var paragraph = (Paragraph)blocks[i];
            if (ReferenceOf(paragraph, layout) is { } piece)
            {
                return new TypingPlace("footnote-delete", StoryRef.Body, paragraph, piece.Start, i);
            }
        }

        return null;
    }

    private static AtomicPiece? ReferenceOf(Paragraph paragraph, DocumentLayout layout) =>
        ParagraphProjection.Build(paragraph, layout.ProjectionHost(StoryRef.Body, paragraph)).Pieces.OfType<AtomicPiece>().FirstOrDefault(p => p.Atomic == AtomicKind.NoteReference);

    private List<Job> PlanObjects(PageView view)
    {
        var places = TypingPlaces.Pick(view.Tab.Docx.Model, view.Runner.PageLayout);
        var runs = _typingKeys == TypingKeys ? ObjectsRuns : _typingKeys;
        var middle = places.FirstOrDefault(p => p.Name == "middle");
        var table = TablePlace(view, needsBorders: false);
        var note = NotePlace(view);
        var jobs = new List<Job>();
        foreach (var name in ObjectPlaces.Names)
        {
            var place = name switch
            {
                "table-add-row" or "table-merge" or "table-border-set" => table,
                "footnote-delete" => note,
                _ => middle,
            };
            if (place is null)
            {
                _error.WriteLine($"error: the document has no place for '{name}'");
                continue;
            }

            jobs.Add(new Job(name, WriteView.Page, null, place, runs, "objects-" + name));
        }

        return jobs;
    }

    // ---- the objects run ----------------------------------------------------------------------------------------------------------

    // The place of a case as the model has it now (an undo may replace paragraph objects, so it is read again from the body block for every run).
    private TextPosition? ObjectCaret(PageView view, string kind)
    {
        var model = view.Tab.Docx.Model;
        var place = _job!.Place;
        if (place.HintBlock >= model.Body.Blocks.Count)
        {
            return null;
        }

        switch (model.Body.Blocks[place.HintBlock])
        {
            case Table table when FirstCellParagraph(table, 0) is { } cell:
                return new TextPosition(StoryRef.Body, cell, 0);
            case Paragraph paragraph:
                var layout = view.Runner.PageLayout;
                if (kind == "footnote-delete")
                {
                    return ReferenceOf(paragraph, layout) is { } piece ? new TextPosition(StoryRef.Body, paragraph, piece.Start) : null;
                }

                return new TextPosition(StoryRef.Body, paragraph, Math.Min(place.Offset, ParagraphProjection.Build(paragraph, layout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length));
            default:
                return null;
        }
    }

    private static bool NeedsCells(string kind) => kind is "table-merge" or "table-border-set";

    private static bool NeedsPicture(string kind) => kind is "picture-resize" or "picture-float-move";

    // The steps before the timed command, each in a frame of its own: the caret and the view, the caret into view, then what the case needs. A step returns false when it ended the run.
    private List<Func<bool>> ObjectSteps(PageView view, EditSession session, string kind)
    {
        var steps = new List<Func<bool>>
        {
            () =>
            {
                if (ObjectCaret(view, kind) is not { } caret)
                {
                    FailTyping($"the place of the case has changed ({_job?.Case})");
                    return false;
                }

                _objectsOffset = caret.Offset;
                session.SetSelection(new TextRange(caret, caret));
                view.ScrollToBlock(_job!.Place.HintBlock);
                return true;
            },
            () =>
            {
                view.EnsureCaretVisible();
                return true;
            },
        };

        if (NeedsCells(kind))
        {
            steps.Add(() => SelectFirstCells(view, session));
        }
        else if (kind == "picture-paste")
        {
            steps.Add(() =>
            {
                view.EditClipboard ??= new PictureClipboard();
                return true;
            });
        }
        else if (kind == "footnote-delete")
        {
            steps.Add(() =>
            {
                if (ObjectCaret(view, kind) is not { } caret || ReferenceOf(caret.Paragraph, view.Runner.PageLayout) is not { } piece)
                {
                    FailTyping("the footnote reference of the case has gone");
                    return false;
                }

                session.SetSelection(new TextRange(caret, caret with { Offset = piece.Start + piece.Length }));
                return true;
            });
        }
        else if (NeedsPicture(kind))
        {
            steps.Add(() => InsertPicture(view, session));
            steps.Add(() => SelectPicture(view, session));
            if (kind == "picture-float-move")
            {
                steps.Add(() =>
                {
                    var before = session.Editor.History.UndoCount;
                    view.RunPictureCommand(WriteCommandIds.PictureWrap, "square");
                    return CountedStep(session, before, "the picture could not be made floating");
                });
                steps.Add(() => SelectPicture(view, session));
            }
        }

        return steps;
    }

    // A preparation step that changed the document is one more step to undo; one that changed nothing ends the run.
    private bool CountedStep(EditSession session, int undoCountBefore, string failure)
    {
        if (session.Editor.History.UndoCount <= undoCountBefore)
        {
            FailTyping(failure + $" ({_job?.Case})");
            return false;
        }

        _structureUndos++;
        return true;
    }

    private bool SelectFirstCells(PageView view, EditSession session)
    {
        var model = view.Tab.Docx.Model;
        if (model.Body.Blocks[_job!.Place.HintBlock] is not Table || !session.SelectCells(new TableRef(StoryRef.Body, _job.Place.HintBlock), (0, 0), (0, 1)))
        {
            FailTyping($"the first two cells of the table could not be selected ({_job?.Case})");
            return false;
        }

        return true;
    }

    private bool InsertPicture(PageView view, EditSession session)
    {
        var before = session.Editor.History.UndoCount;
        view.RunPictureCommand(WriteCommandIds.PictureInsert, new PictureFile(s_picture, "measure"));
        return CountedStep(session, before, "the picture could not be inserted");
    }

    // The picture that stands at the caret of the case, selected (the way a press on it would).
    private bool SelectPicture(PageView view, EditSession session)
    {
        var model = view.Tab.Docx.Model;
        if (model.Body.Blocks[_job!.Place.HintBlock] is not Paragraph paragraph || session.Editor.PictureAt(new TextPosition(StoryRef.Body, paragraph, _objectsOffset)) is not { } id || !session.SetObjectSelection(new PictureSelection(id)))
        {
            FailTyping($"the inserted picture could not be selected ({_job?.Case})");
            return false;
        }

        return true;
    }

    private void SendObjects(PageView view, string kind)
    {
        var session = view.Tab.EnsureEditor();
        var model = view.Tab.Docx.Model;
        var steps = ObjectSteps(view, session, kind);
        if (_objectsStage < steps.Count)
        {
            if (steps[_objectsStage++]())
            {
                NextTypingFrame();
            }

            return;
        }

        Func<bool> run;
        switch (kind)
        {
            case "table-insert":
                run = () => view.RunTableCommand(WriteCommandIds.TableInsert, new TableSize(5, 5));
                break;
            case "table-add-row":
                run = () => view.RunTableCommand(WriteCommandIds.TableRowBelow);
                break;
            case "table-merge":
                run = () => view.RunTableCommand(WriteCommandIds.TableMerge);
                break;
            case "table-border-set":
                run = () => view.RunTableCommand(WriteCommandIds.TableBorders, "all");
                break;
            case "picture-insert":
                run = () => view.RunPictureCommand(WriteCommandIds.PictureInsert, new PictureFile(s_picture, "measure"));
                break;
            case "picture-paste":
                run = () =>
                {
                    var pasted = view.RunEditCommandAsync(WriteCommandIds.Paste);
                    return pasted.IsCompletedSuccessfully;
                };
                break;
            case "picture-resize":
                {
                    if (session.ObjectSelection is not PictureSelection picture)
                    {
                        FailTyping("no picture is selected for the resize");
                        return;
                    }

                    run = () => view.ApplyOperation(ed => ed.ResizePicture(picture.Id, 1905000, 1428750)) is not null;
                    break;
                }

            case "picture-float-move":
                {
                    var target = _job!.Place.HintBlock + 2;
                    if (session.ObjectSelection is not PictureSelection picture || target >= model.Body.Blocks.Count || model.Body.Blocks[target] is not Paragraph into)
                    {
                        FailTyping("no floating picture and paragraph to move it to");
                        return;
                    }

                    run = () => view.ApplyOperation(ed => ed.MovePicture(picture.Id, new TextPosition(StoryRef.Body, into, 0), 127000, 127000)) is not null;
                    break;
                }

            case "footnote-insert":
                run = () => view.RunNoteCommand(WriteCommandIds.NoteFootnote);
                break;
            default:
                run = () => view.ApplyOperation(ed => session.Selection is { } selection ? ed.Delete(selection) : null) is not null;
                break;
        }

        var revision = model.Edits.Revision;
        _objectsStage = 0;
        _keyIncomplete = false;
        _renderSeen = false;
        _renderStart = 0;
        _awaiting = true;
        session.Phases = _phases;
        _phases.Reset();
        BeginKeyProbe();
        _keyAt = Stopwatch.GetTimestamp();
        var taken = run();
        _handlerEnd = Stopwatch.GetTimestamp();
        EndKeyProbe();
        if (!taken || model.Edits.Revision == revision)
        {
            _awaiting = false;
            FailTyping($"the objects command changed nothing ({_job?.Case})");
            return;
        }

        // One step of history per command, and one per preparation (counted when it ran); RestoreStructure undoes what was counted.
        _structureUndos++;
        NextTypingFrame();
    }

    // At the end of a case: the refresh of the ribbon states that follows the last change (the timer would run it 100 ms after it), timed apart from the runs. Recorded, never judged.
    private void WriteObjectsRefresh()
    {
        var start = Stopwatch.GetTimestamp();
        _window.ViewStatesRefresh?.Flush();
        _window.FormatRefresh?.Flush();
        Write(string.Create(CultureInfo.InvariantCulture, $"objects-refresh case={_job!.Case} view=page n=1 ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.00}"));
    }

    // ---- the typing run at objects ------------------------------------------------------------------------------------------------

    // The two places of the run: the end of an inline picture in the paragraph the typing run calls "middle" (the picture is inserted here, outside the timing, and stays), and a table cell.
    private static List<TypingPlace> PickTypingObjects(PageView view)
    {
        var found = new List<TypingPlace>();
        var model = view.Tab.Docx.Model;
        var places = TypingPlaces.Pick(model, view.Runner.PageLayout);
        if (places.FirstOrDefault(p => p.Name == "middle") is { } middle)
        {
            var session = view.Tab.EnsureEditor();
            // The first command attaches the session to the view (and resets the selection): do that before the caret is placed.
            view.ApplyOperation(_ => null);
            var at = new TextPosition(StoryRef.Body, middle.Paragraph, middle.Offset);
            session.SetSelection(new TextRange(at, at));
            view.RunPictureCommand(WriteCommandIds.PictureInsert, new PictureFile(s_picture, "measure"));
            if (model.Body.Blocks[middle.HintBlock] is Paragraph changed && session.Editor.PictureAt(new TextPosition(StoryRef.Body, changed, middle.Offset)) is not null)
            {
                found.Add(new TypingPlace("picture-paragraph", StoryRef.Body, changed, middle.Offset + 1, middle.HintBlock));
            }
        }

        if (places.FirstOrDefault(p => p.Name == "table-cell") is { } cell)
        {
            found.Add(cell);
        }

        return found;
    }

    // ---- the gesture run ----------------------------------------------------------------------------------------------------------

    private List<Job> PlanGesture(PageView view)
    {
        var places = TypingPlaces.Pick(view.Tab.Docx.Model, view.Runner.PageLayout);
        var runs = _typingKeys == TypingKeys ? GestureMoves : _typingKeys;
        var middle = places.FirstOrDefault(p => p.Name == "middle");
        var table = TablePlace(view, needsBorders: true);
        var jobs = new List<Job>();
        foreach (var name in GesturePlaces.Names)
        {
            var place = name.StartsWith("picture-", StringComparison.Ordinal) ? middle : table;
            if (place is null)
            {
                _error.WriteLine($"error: the document has no place for '{name}'");
                continue;
            }

            jobs.Add(new Job(name, WriteView.Page, null, place, runs, "gesture-" + name));
        }

        return jobs;
    }

    private static Point ViewPoint(PageView view, int page, double x, double y)
    {
        var rect = view.PageBounds(page)!.Value;
        var scale = view.Zoom * 96 / 72;
        return new Point(rect.X + (x * scale), rect.Y + (y * scale));
    }

    // The page and the grid of the table of the job on the page that holds its block.
    private static (int Page, TableGrid Grid)? GridOf(PageView view, int block)
    {
        var layout = view.Runner.PageLayout;
        if (layout.Locate(block) is not { } at)
        {
            return null;
        }

        for (var page = at.Page; page <= at.Page + 1 && layout.TryGetPage(page, out var laid) && laid is not null; page++)
        {
            if (laid.Objects.Tables.FirstOrDefault(t => t.BlockIndex == block && t.Story == StoryRef.Body) is { } grid)
            {
                return (page, grid);
            }
        }

        return null;
    }

    // The box of the picture that is selected, on the page of its paragraph or the next one.
    private (int Page, PictureBox Box)? SelectedBox(PageView view, EditSession session)
    {
        if (session.ObjectSelection is not PictureSelection picture)
        {
            return null;
        }

        var layout = view.Runner.PageLayout;
        if (layout.Locate(_job!.Place.HintBlock) is not { } at)
        {
            return null;
        }

        for (var page = at.Page; page <= at.Page + 1 && layout.TryGetPage(page, out var laid) && laid is not null; page++)
        {
            if (laid.Objects.Pictures.FirstOrDefault(p => p.Id == picture.Id) is { } box)
            {
                return (page, box);
            }
        }

        return null;
    }

    // A triangle wave between 0 and the reach, one step per move: every move differs from the one before (a move that changes nothing renders nothing, and the run waits for a frame), and the first one clears the 4 px of the drag threshold.
    private static double Wave(int index, double step, double reach)
    {
        var along = (index + 1) * step % (2 * reach);
        return Math.Round(along <= reach ? along : (2 * reach) - along);
    }

    // The path of one kind: the point the press is at, then a seeded, repeatable walk around it (a hand's path, never leaving the page).
    private Point[]? GesturePath(PageView view, EditSession session, string kind, int moves, out Point press)
    {
        press = default;
        var scale = view.Zoom * 96 / 72;
        var tolerance = ObjectHandles.TolerancePoints((float)scale);
        var path = new Point[moves];
        switch (kind)
        {
            case "column-border" or "table-edge" or "row" or "cell-range":
                {
                    if (GridOf(view, _job!.Place.HintBlock) is not { } found)
                    {
                        return null;
                    }

                    var (page, grid) = found;
                    var first = grid.Cells.First(c => c.Row == 0);
                    switch (kind)
                    {
                        case "column-border" or "table-edge":
                            {
                                var border = kind == "table-edge" ? grid.ColumnBorders[^1] : grid.ColumnBorders[1];
                                press = ViewPoint(view, page, border, first.Y + (first.Height / 2));
                                for (var i = 0; i < moves; i++)
                                {
                                    path[i] = press + new Vector(Wave(i, 6, 30), 0);
                                }

                                return path;
                            }

                        case "row":
                            {
                                var rows = grid.Cells.Where(c => c.Column == 0).OrderBy(c => c.Row).ToList();
                                var row = rows[Math.Min(1, rows.Count - 1)];
                                press = ViewPoint(view, page, grid.X - (2 * tolerance), row.Y + (row.Height / 2));
                                var low = ViewPoint(view, page, grid.X - (2 * tolerance), rows[0].Y + (rows[0].Height / 2)).Y;
                                var high = ViewPoint(view, page, grid.X - (2 * tolerance), rows[^1].Y + (rows[^1].Height / 2)).Y;
                                for (var i = 0; i < moves; i++)
                                {
                                    var step = Math.Max(6.0, Math.Abs(high - low) / 12);
                                    path[i] = new Point(press.X, Math.Min(low, high) + Wave(i, step, Math.Abs(high - low)));
                                }

                                return path;
                            }

                        default:
                            {
                                var cells = grid.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column).Take(12).ToList();
                                press = ViewPoint(view, page, cells[0].X + (cells[0].Width / 2), cells[0].Y + (cells[0].Height / 2));
                                for (var i = 0; i < moves; i++)
                                {
                                    var cell = cells[(i + 1) % cells.Count];
                                    path[i] = ViewPoint(view, page, cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
                                }

                                return path;
                            }
                    }
                }

            default:
                {
                    if (SelectedBox(view, session) is not { } found)
                    {
                        return null;
                    }

                    var (page, box) = found;
                    var from = kind == "picture-resize" ? box.HandleCentre(HandleSlot.BottomRight) : (X: box.X + (box.Width / 2), Y: box.Y + (box.Height / 2));
                    press = ViewPoint(view, page, from.X, from.Y);
                    var reach = kind == "picture-resize" ? 40.0 : 60.0;
                    for (var i = 0; i < moves; i++)
                    {
                        path[i] = press + new Vector(Wave(i, 6, reach), Wave(i, 5, reach));
                    }

                    return path;
                }
        }
    }

    private void SendGesture(PageView view, string kind)
    {
        var session = view.Tab.EnsureEditor();
        if (!_gestureDown)
        {
            var steps = ObjectSteps(view, session, kind);
            if (_objectsStage < steps.Count)
            {
                if (steps[_objectsStage++]())
                {
                    NextTypingFrame();
                }

                return;
            }

            if (GesturePath(view, session, kind, _job!.Keys, out var press) is not { } path)
            {
                FailTyping($"the object of the gesture is not on the page ({_job.Case})");
                return;
            }

            // The press is the one of the pointer: the object under it is taken, the gesture starts. It is not timed (the moves are what a drag costs).
            view.HandlePress(press, 1, KeyModifiers.None, rightButton: false);
            if (view.ObjectGesture is not { IsActive: true })
            {
                FailTyping($"the press started no gesture ({_job.Case})");
                return;
            }

            _gesturePath = path;
            _gestureDown = true;
            NextTypingFrame();
            return;
        }

        var point = _gesturePath[_keyInJob % _gesturePath.Length];
        _keyIncomplete = false;
        _renderSeen = false;
        _renderStart = 0;
        _awaiting = true;
        _phases.Reset();
        BeginKeyProbe();
        _keyAt = Stopwatch.GetTimestamp();
        view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, _pointer, view, point, (ulong)Stopwatch.GetTimestamp(), new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
        _handlerEnd = Stopwatch.GetTimestamp();
        EndKeyProbe();
        if (view.ObjectGesture is not { IsActive: true })
        {
            _awaiting = false;
            FailTyping($"the gesture ended while the pointer moved ({_job?.Case})");
            return;
        }

        NextTypingFrame();
    }

    // The end of a kind: the drag is cancelled (it changes nothing) and what the preparation did is undone.
    private void EndGestureJob(PageView view)
    {
        view.CancelObjectDrag(ObjectDragCancel.Escape);
        _gestureDown = false;
        _objectsStage = 0;
        RestoreStructure(view);
    }
}

/// <summary>The cases of <c>--measure-objects</c> (<c>ObjectCases.Names</c> of the bench and <c>WriteRun.ObjectsNames</c> of <c>measure.cs</c> are near copies: change the three together).</summary>
internal static class ObjectPlaces
{
    public static readonly string[] Names = ["table-insert", "table-add-row", "table-merge", "table-border-set", "picture-insert", "picture-paste", "picture-resize", "picture-float-move", "footnote-insert", "footnote-delete"];
}

/// <summary>The drag kinds of <c>--measure-gesture</c> (<c>WriteRun.GestureNames</c> of <c>measure.cs</c> is a near copy: change both together).</summary>
internal static class GesturePlaces
{
    public static readonly string[] Names = ["column-border", "table-edge", "row", "cell-range", "picture-resize", "picture-float-move"];
}
